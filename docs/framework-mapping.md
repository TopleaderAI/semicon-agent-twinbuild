# 프레임워크 개념 매핑 — LangGraph × MAF 1.0

> `semicon-agent-twinbuild` 트윈 빌드에서 발견한 개념 대응을 실코드와 함께 기록한다.
> 출퇴근 20분 학습 자료 겸용. `PROJECT_KNOWLEDGE.md` §5의 확장판.
>
> - **v0.1** (2026-07-10): 툴 정의 / 툴 실행 루프 비교 (Week 1)
> - v0.2 (예정): 상태 지속(checkpointer ↔ AgentThread) / HITL / 조건부 분기 (Week 2)

---

## 0. 검증 환경

| | LangGraph 트랙 | MAF 트랙 |
|---|---|---|
| 언어/런타임 | Python 3.12 (uv) | .NET 8 |
| 코어 패키지 | `langgraph` v1.x | `Microsoft.Agents.AI` 1.x (stable) |
| LLM 커넥터 | `langchain-anthropic` | `Microsoft.Agents.AI.Anthropic` (**prerelease**) |
| LLM | Claude API (개발: haiku / 시연: sonnet) | 동일 |

---

## 1. 툴 정의 방식

**공통 원리**: 양쪽 모두 "일반 함수 + 메타데이터 → LLM용 툴 스키마 자동 생성".
스키마를 손으로 쓰지 않는다는 점은 같고, **메타데이터의 출처**가 다르다.

### LangGraph — `@tool` 데코레이터

```python
from langchain_core.tools import tool

@tool
def fetch_sk_hynix_news() -> list[str]:
    """오늘의 SK하이닉스 관련 뉴스 헤드라인 목록을 반환한다."""
    return MOCK_NEWS
```

- **docstring이 곧 툴 설명** → LLM에게 전달되는 스키마의 일부
- 타입 힌트에서 파라미터/반환 스키마 추출
- 데코레이터가 함수를 `BaseTool` 객체로 래핑 (함수가 다른 타입이 됨)

### MAF — `[Description]` + `AIFunctionFactory.Create()`

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

[Description("오늘의 SK하이닉스 관련 뉴스 헤드라인 목록을 반환한다.")]
static string[] FetchSkHynixNews() => MOCK_NEWS;

// 등록 시점에 래핑
tools: [AIFunctionFactory.Create(FetchSkHynixNews)]
```

- **어트리뷰트가 툴 설명** → 리플렉션으로 시그니처 + 어트리뷰트를 읽어 스키마 생성
- 함수 자체는 순수한 C# 메서드로 유지, 래핑은 등록 시점에 발생
- `AIFunction`은 MAF 고유가 아니라 `Microsoft.Extensions.AI` 표준 —
  Semantic Kernel 등 다른 MEAI 기반 라이브러리와 호환

### 대응표

| 관점 | LangGraph | MAF |
|---|---|---|
| 설명 출처 | docstring | `[Description]` 어트리뷰트 |
| 스키마 추출 | 타입 힌트 (런타임 인스펙션) | 리플렉션 (시그니처 + 어트리뷰트) |
| 래핑 시점 | 정의 시점 (데코레이터) | 등록 시점 (`Create()` 호출) |
| 결과 타입 | `BaseTool` | `AIFunction` (MEAI 표준) |
| .NET 감각 | — | DI 등록 시 팩토리로 래핑하는 것과 유사 |

---

## 2. 툴 실행 루프의 소유권 — 가장 큰 철학 차이

동일 스펙(mock 뉴스 툴 1개 + 요약/판정)인데 코드량이 **~60줄 vs ~30줄**.
차이의 원인은 "LLM ↔ 툴 반복 루프를 누가 소유하는가".

### LangGraph — 루프를 그래프에 명시

```python
builder = StateGraph(MessagesState)
builder.add_node("agent", agent_node)          # LLM 호출
builder.add_node("tools", ToolNode(TOOLS))     # 툴 실행
builder.add_edge(START, "agent")
builder.add_conditional_edges("agent", route_after_agent)  # tool_calls 유무로 분기
builder.add_edge("tools", "agent")             # 툴 결과 들고 다시 LLM으로
graph = builder.compile()
```

- ReAct 루프(`agent → tools → agent → ... → END`)를 **엣지로 직접 조립**
- 제어 흐름이 코드에 보인다 = 아무 지점이나 노드를 끼워넣을 수 있다
- 이 장황함은 비용이 아니라 투자: Week 2의 checkpointer/interrupt가
  바로 이 그래프 구조 위에 꽂힌다

### MAF — 루프를 런타임이 소유

```csharp
AIAgent agent = client.AsAIAgent(model, name, instructions, tools);
Console.WriteLine(await agent.RunAsync(userInput));
```

- `RunAsync()` 한 번이 전체 루프 처리:
  프롬프트 전송 → tool_calls 수신 → 로컬 함수 실행 → 결과 회신 → 최종 응답
- 루프 내부 개입은 **Middleware Pipeline**으로 (ASP.NET Core 미들웨어와 동일한 감각)
- 명시적 제어 흐름이 필요하면 별도의 **Workflow** (graph-based)로 승격 —
  LangGraph의 StateGraph에 대응하는 것은 Agent가 아니라 이쪽

### 대응표

| 관점 | LangGraph | MAF |
|---|---|---|
| 기본 단위 | 그래프 (루프도 그래프의 일부) | 에이전트 (루프는 런타임 내장) |
| 루프 개입 | 노드/엣지 삽입 | Middleware |
| 명시적 오케스트레이션 | StateGraph 그 자체 | Workflow (별도 레이어) |
| 올바른 §5 대응 | `StateGraph` ↔ | `Workflow` (≠ `AIAgent`) |
| .NET 감각 | 미들웨어 체인을 손으로 조립 | `app.Run()` 뒤에 숨은 호스팅 루프 |

**예상 비대칭 (Week 2에서 검증)**: HITL을 붙일 때 LangGraph는 그래프에
`interrupt`를 꽂고, MAF는 프레임워크 제공 승인 플로우를 탄다.
"제어 흐름 소유권" 차이가 기능 구현 방식 차이로 이어지는 첫 사례가 될 것.

---

## 3. 버전 주의사항 (2026-07 기준)

### 3.1 LangGraph v1.0: `create_react_agent` deprecated

- `langgraph.prebuilt.create_react_agent`는 **v1.0에서 deprecated, v2.0에서 제거 예정**
- 공식 대체: `langchain.agents.create_agent` (별도 `langchain` 패키지)
- **2023~2024년 튜토리얼 대부분이 구 패턴** — 복붙 시 deprecation 경고 또는 v2에서 파손
- 본 프로젝트는 manual StateGraph 채택:
  1. Week 2+ checkpointer/interrupt는 그래프 직접 조립이 전제
  2. §5 매핑의 비교 대상이 StateGraph ↔ Workflow라 고수준 팩토리는 학습 목적에 부적합

### 3.2 MAF: 커넥터 안정성 경계

- 코어 `Microsoft.Agents.AI`: **1.0 GA (stable)** — 여기까지만 코어 의존
- `Microsoft.Agents.AI.Anthropic`: **prerelease** — LLM 커넥터 계층에 격리
- 커넥터 API 표면이 아직 흔들림 (예: `APIKey` / `ApiKey` 프로퍼티 표기가
  문서 소스마다 상이) — 컴파일 에러 시 IntelliSense 기준으로 수정

---

## 4. 환경 구축 gotchas (Windows)

| 증상 | 원인 | 해결 |
|---|---|---|
| `uv add langgraph` 실패 (self-dependency) | uv 프로젝트명이 폴더명(`langgraph`)을 따라가 PyPI 패키지명과 충돌 | `pyproject.toml`의 `name`을 `semicon-langgraph`로 변경. .NET과 달리 프로젝트명이 패키지 네임스페이스와 충돌 검사됨 |
| PowerShell 명령이 cmd에서 실패 | 셸 문법 차이 | 터미널을 PowerShell로 통일 |
| `git add` 시 LF/CRLF 경고 | uv 생성 파일(LF) vs Windows Git 기본(CRLF) | 리포 루트 `.gitattributes`: `* text=auto eol=lf` + `git add --renormalize .` (Mac 병행 개발 대비) |

---

## v0.2 예정 (Week 2)

- [ ] 상태 지속: `Checkpointer`(SQLite) ↔ `AgentThread` 직렬화
- [ ] HITL: `interrupt` ↔ MAF 승인 플로우 — §2의 "소유권 비대칭" 가설 검증
- [ ] 조건부 분기: conditional edges ↔ Workflow 분기 (시그널 강/중/약 3-way)