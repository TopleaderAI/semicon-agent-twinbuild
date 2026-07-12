"""Week 2 Step 2: interrupt로 Human-in-the-Loop 승인 게이트.

시그널 판정 후 approval 노드에서 interrupt() 호출 → 그래프가 그 지점에서 멈추고
상태가 checkpointer에 저장된다. 사람이 응답하면 Command(resume=...)로 재개.

- interrupt는 checkpointer 없이는 동작하지 않는다 (멈춘 상태를 어딘가에 저장해야 하므로)
- 실행:  uv run hitl_agent.py
"""

import os
import sqlite3
import uuid
from typing import Literal

from dotenv import load_dotenv
from langchain_core.messages import AIMessage
from langchain_core.tools import tool
from langgraph.checkpoint.sqlite import SqliteSaver
from langgraph.graph import END, START, MessagesState, StateGraph
from langgraph.prebuilt import ToolNode
from langgraph.types import Command, interrupt

load_dotenv()

# --- Tool (Week 1과 동일) ---

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
    "뉴스가 필요하면 반드시 fetch_sk_hynix_news 툴을 먼저 호출한다. "
    "툴을 호출하지 않은 채 '데이터를 가져올 수 없다'고 답하는 것은 금지된다. "
    "툴 결과를 받으면 추가 확인 없이 그 응답 안에서 "
    "각 뉴스 한 줄 요약과 시그널(긍정/중립/부정) 판정을 완료하라."
)

from llm_factory import build_llm

llm = build_llm()
from langchain_core.messages import ToolMessage

llm_forced = llm.bind_tools(TOOLS, tool_choice="any")   # 툴 호출 강제

def agent_node(state: MessagesState) -> dict:
    messages = [("system", SYSTEM_PROMPT)] + state["messages"]
    # 아직 툴 결과가 없으면 = 첫 턴 → 툴 호출을 강제
    has_tool_result = any(isinstance(m, ToolMessage) for m in state["messages"])
    model = llm_forced if has_tool_result else llm_forced
    return {"messages": [model.invoke(messages)]}


def route_after_agent(state: MessagesState) -> Literal["tools", "approval"]:
    """툴 호출이 남았으면 tools, 판정이 끝났으면 approval 게이트로."""
    last = state["messages"][-1]
    if isinstance(last, AIMessage) and last.tool_calls:
        return "tools"
    return "approval"


def approval_node(state: MessagesState) -> dict:
    """Week 2 핵심: interrupt로 사람 승인 대기.

    interrupt() 호출 시점에 그래프 실행이 '여기서' 멈추고 payload가 호출자에게
    반환된다. Command(resume=값)으로 재개하면 그 값이 interrupt()의 반환값이 된다.
    주의: 재개 시 이 노드는 '처음부터' 다시 실행된다 (interrupt 이전 코드는
    멱등해야 함) — .NET의 async/await 같은 지점 재개가 아니라 노드 단위 재실행.
    """
    analysis = state["messages"][-1].content
    decision = interrupt(
        {
            "question": "시그널 판정 결과입니다. 승인하시겠습니까? (approve/reject)",
            "analysis": analysis,
        }
    )
    if decision == "approve":
        note = "[HITL] 사람이 시그널 판정을 승인함. 리포트 파이프라인으로 전달 가능."
    else:
        note = f"[HITL] 사람이 거부함 (사유: {decision}). 판정 보류."
    return {"messages": [("assistant", note)]}


builder = StateGraph(MessagesState)
builder.add_node("agent", agent_node)
builder.add_node("tools", ToolNode(TOOLS))
builder.add_node("approval", approval_node)
builder.add_edge(START, "agent")
builder.add_conditional_edges("agent", route_after_agent)
builder.add_edge("tools", "agent")
builder.add_edge("approval", END)

conn = sqlite3.connect("hitl_checkpoints.sqlite", check_same_thread=False)
graph = builder.compile(checkpointer=SqliteSaver(conn))

# --- Run: 승인 대기 → 재개 데모 ---

if __name__ == "__main__":
    config = {"configurable": {"thread_id": str(uuid.uuid4())}}

    # 1단계: interrupt 지점까지 실행 → 멈춤
    result = graph.invoke(
        {"messages": [("user", "오늘 SK하이닉스 뉴스 요약하고 시그널 판정해줘")]},
        config,
    )

    # interrupt로 멈추면 결과에 __interrupt__ 키가 담긴다
    pending = result["__interrupt__"][0].value
    print("=== 승인 대기 ===")
    print(pending["analysis"])
    print()
    answer = input(f"{pending['question']} > ").strip() or "approve"

    # 2단계: Command(resume=...)로 멈춘 지점부터 재개
    result = graph.invoke(Command(resume=answer), config)
    print()
    print(result["messages"][-1].content)