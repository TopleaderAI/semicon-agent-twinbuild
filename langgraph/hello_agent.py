"""Week 1 Hello Agent: SK하이닉스 뉴스 요약 + 시그널 판정 (mock data)."""

import os
import sys
from typing import Literal

from dotenv import load_dotenv
from langchain_anthropic import ChatAnthropic
from langchain_core.messages import AIMessage
from langchain_core.tools import tool
from langgraph.graph import END, START, MessagesState, StateGraph
from langgraph.prebuilt import ToolNode


load_dotenv()

# --- Tool (Week 1은 mock, Week 3에서 MCP 서버로 이전 예정) ---

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

MODEL = os.getenv("AGENT_MODEL", "claude-haiku-4-5")

SYSTEM_PROMPT = (
    "너는 메모리 반도체 투자 리서치 어시스턴트다. "
    "뉴스 툴을 호출해 오늘의 SK하이닉스 뉴스를 가져온 뒤, "
    "각 뉴스를 한 줄로 요약하고 HBM 수요 관점에서 "
    "시그널(긍정/중립/부정)과 근거를 판정하라. "
    "툴 결과를 받으면 추가 확인 없이 그 응답 안에서 "
    "요약과 판정을 모두 완료하라."
)

llm = ChatAnthropic(model=MODEL, max_tokens=1024)
llm_with_tools = llm.bind_tools(TOOLS)

# --- Nodes ---

def agent_node(state: MessagesState) -> dict:
    """LLM 호출 노드. 툴 호출이 필요하면 tool_calls가 담긴 AIMessage 반환."""
    messages = [("system", SYSTEM_PROMPT)] + state["messages"]
    return {"messages" : [llm_with_tools.invoke(messages)]}

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
graph = builder.compile()

# --- Run ---

if __name__ == "__main__":
    result = graph.invoke({
        "messages":[("user", "오늘 SK하이닉스 뉴스 요약하고 시그널 판정해줘")]
    })
    if "--trace" in sys.argv:
        for m in result["messages"]:
            m.pretty_print()
    else:
        print(result["messages"][-1].content)
