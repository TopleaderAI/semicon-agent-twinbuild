"""Week 2 조건부 분기: 시그널 강도(강/중/약) 3-way 라우팅 (mock data).

hello/hitl_agent의 라우팅은 2-way(tool_calls 유무)였다. 여기서는 LLM 판정을
커스텀 상태 필드에 올리고, 그 값으로 3-way 분기한다.
MAF 대응물: maf/BranchAgent (WorkflowBuilder.AddSwitch)
"""

import json
import sys
from typing import Literal

from dotenv import load_dotenv
from langgraph.graph import END, START, MessagesState, StateGraph

from llm_factory import build_llm

load_dotenv()

MOCK_NEWS: list[str] = [
    "SK하이닉스, HBM4 샘플 주요 고객사 공급 개시... 양산 일정 앞당길 듯",
    "마이크론, HBM 증설 투자 발표에 업계 공급과잉 우려 제기",
    "빅테크 A사, 차기 데이터센터 capex 가이던스 상향... AI 인프라 투자 지속",
]

JUDGE_PROMPT = (
    "너는 메모리 반도체 투자 리서치 어시스턴트다. "
    "아래 뉴스들을 HBM 수요 관점에서 종합해 시그널 강도를 판정하라. "
    '반드시 JSON 객체 하나만 출력하라: {"strength": "strong|medium|weak", "reason": "근거 한 줄"} '
    "코드블록/설명 등 다른 텍스트는 금지."
)


class SignalState(MessagesState):
    """MessagesState + 분기 키. 라우터가 읽는 값은 messages가 아니라 이 필드다."""
    strength: str
    reason: str


llm = build_llm()


def judge_node(state: SignalState) -> dict:
    """뉴스 종합 판정. 파싱 실패 시 strength=''로 두어 default 분기로 보낸다."""
    news = "\n".join(f"- {n}" for n in MOCK_NEWS)
    resp = llm.invoke([("system", JUDGE_PROMPT), ("user", news)])
    strength, reason = "", "판정 파싱 실패"
    try:
        raw = resp.content.strip().removeprefix("```json").removesuffix("```").strip()
        data = json.loads(raw)
        strength = str(data.get("strength", "")).lower()
        reason = str(data.get("reason", reason))
    except (json.JSONDecodeError, AttributeError):
        pass
    return {"messages": [resp], "strength": strength, "reason": reason}


def route_by_strength(state: SignalState) -> Literal["strong", "medium", "weak"]:
    """3-way 라우터. 미인식 값은 medium — MAF WithDefault 대응."""
    s = state["strength"]
    return s if s in ("strong", "medium", "weak") else "medium"


def strong_node(state: SignalState) -> dict:
    return {"messages": [("assistant",
        f"[강] 액션 후보 — HITL 승인 게이트 대상 (Week 5 연결 예정). 근거: {state['reason']}")]}


def medium_node(state: SignalState) -> dict:
    return {"messages": [("assistant",
        f"[중] 관찰 지속 — 지표 모니터링 유지. 근거: {state['reason']}")]}


def weak_node(state: SignalState) -> dict:
    return {"messages": [("assistant",
        f"[약] 노이즈 판단 — 리포트 생략. 근거: {state['reason']}")]}


builder = StateGraph(SignalState)
builder.add_node("judge", judge_node)
builder.add_node("strong", strong_node)
builder.add_node("medium", medium_node)
builder.add_node("weak", weak_node)
builder.add_edge(START, "judge")
builder.add_conditional_edges(
    "judge",
    route_by_strength,
    {"strong": "strong", "medium": "medium", "weak": "weak"},
)
builder.add_edge("strong", END)
builder.add_edge("medium", END)
builder.add_edge("weak", END)
graph = builder.compile()


if __name__ == "__main__":
    result = graph.invoke({"messages": [("user", "시그널 강도 판정 및 라우팅")]})
    if "--trace" in sys.argv:
        for m in result["messages"]:
            m.pretty_print()
    else:
        print(f"strength={result['strength']}")
        print(result["messages"][-1].content)