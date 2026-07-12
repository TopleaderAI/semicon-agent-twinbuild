"""Week 2 Step 1: SQLite checkpointer로 대화 상태 지속.

hello_agent.py의 그래프를 그대로 쓰되, compile 시 checkpointer를 주입한다.
- thread_id가 곧 세션 키: 같은 thread_id면 프로세스를 껐다 켜도 이전 대화가 복원된다
- 실행:  uv run checkpoint_agent.py [thread_id]
  (thread_id 생략 시 "demo" — 재실행해서 "아까 내가 뭐 물어봤지?"로 지속성 확인)
"""

import os
import sqlite3
import sys
from typing import Literal

from dotenv import load_dotenv
from langchain_core.messages import AIMessage
from langchain_core.tools import tool
from langgraph.checkpoint.sqlite import SqliteSaver
from langgraph.graph import END, START, MessagesState, StateGraph
from langgraph.prebuilt import ToolNode

load_dotenv()

# --- Tool (Week 1과 동일, Week 3에서 MCP 서버로 이전 예정) ---

MOCK_NEWS: list[str] = [
    "SK하이닉스, HBM4 샘플 주요 고객사 공급 개시... 양산 일정 앞당길 듯",
    "마이크론, HBM 증설 투자 발표에 업계 공급과잉 우려 제기",
    "빅테크 A사, 차기 데이터센터 capex 가이던스 상향... AI 인프라 투자 지속",
]


@tool
def fetch_sk_hynix_news() -> list[str]:
    """오늘의 SK하이닉스 관련 뉴스 헤드라인 목록을 반환한다."""
    return MOCK_NEWS


TOOLS = [fetch_sk_hynix_news]

# --- Model ---

SYSTEM_PROMPT = (
    "너는 메모리 반도체 투자 리서치 어시스턴트다. "
    "뉴스 요청이 오면 툴을 호출해 가져온 뒤 요약과 시그널(긍정/중립/부정)을 판정하라. "
    "이전 대화 내용을 기억하고 있으므로, 과거 질문에 대한 후속 질문에도 답하라."
)

from llm_factory import build_llm

llm = build_llm()
llm_with_tools = llm.bind_tools(TOOLS)

# --- Nodes (Week 1과 동일) ---


def agent_node(state: MessagesState) -> dict:
    """LLM 호출 노드."""
    messages = [("system", SYSTEM_PROMPT)] + state["messages"]
    return {"messages": [llm_with_tools.invoke(messages)]}


def route_after_agent(state: MessagesState) -> Literal["tools", "__end__"]:
    """조건부 엣지: 툴 호출 요청이 있으면 tools로, 없으면 종료."""
    last = state["messages"][-1]
    if isinstance(last, AIMessage) and last.tool_calls:
        return "tools"
    return END


builder = StateGraph(MessagesState)
builder.add_node("agent", agent_node)
builder.add_node("tools", ToolNode(TOOLS))
builder.add_edge(START, "agent")
builder.add_conditional_edges("agent", route_after_agent)
builder.add_edge("tools", "agent")

# --- Week 2 변경점: checkpointer 주입 ---
# SqliteSaver가 매 super-step마다 상태 스냅샷을 checkpoints.sqlite에 기록한다.
# .NET 감각: ISession + 영속 스토어. thread_id = 세션 키.

conn = sqlite3.connect("checkpoints.sqlite", check_same_thread=False)
checkpointer = SqliteSaver(conn)
graph = builder.compile(checkpointer=checkpointer)

# --- Run: 대화형 루프 ---

if __name__ == "__main__":
    thread_id = sys.argv[1] if len(sys.argv) > 1 else "demo"
    config = {"configurable": {"thread_id": thread_id}}

    # 재시작 시 이전 상태가 복원되는지 확인용 출력
    snapshot = graph.get_state(config)
    restored = len(snapshot.values.get("messages", []))
    print(f"[thread={thread_id}] 복원된 메시지 수: {restored}")
    print("질문 입력 (빈 줄로 종료):")

    while True:
        user_input = input("> ").strip()
        if not user_input:
            break
        result = graph.invoke({"messages": [("user", user_input)]}, config)
        print(result["messages"][-1].content)
        print()