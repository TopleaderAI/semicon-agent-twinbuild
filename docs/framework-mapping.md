# 프레임워크 개념 매핑 — LangGraph × MAF 1.0

> `semicon-agent-twinbuild` 트윈 빌드에서 발견한 개념 대응을 실코드와 함께 기록한다.
> 출퇴근 20분 학습 자료 겸용. `PROJECT_KNOWLEDGE.md` §5의 확장판.
>
> - **v0.1** (2026-07-10): 툴 정의 / 툴 실행 루프 비교 (Week 1)
> - **v0.2** (2026-07-13): 상태 지속(Checkpointer ↔ AgentSession) / HITL / 조건부 분기 (Week 2)

---

## 0. 검증 환경

| | LangGraph 트랙 | MAF 트랙 |
|---|---|---|
| 언어/런타임 | Python 3.12 (uv) | .NET (net10.0) |
| 코어 패키지 | `langgraph` v1.x | `Microsoft.Agents.AI` 1.13.0 (stable), `Microsoft.Agents.AI.Workflows` 1.13.0 (**stable**) |
| LLM 커넥터 | `langchain-openrouter` (기본) / `langchain-anthropic` | OpenAI 커넥터 + Endpoint 오버라이드 (기본) / `Microsoft.Agents.AI.Anthropic` (**prerelease**) |
| LLM | OpenRouter `deepseek/deepseek-chat-v3.1` (개발) / Claude (크레딧 복구 시) | 동일 |
| MAF API 검증 기준 | — | 공식 리포 `dotnet-1.13.0` 태그 sparse-clone |

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
- `Microsoft.Agents.AI.Workflows`: **stable** (1.13.0) — §7의 Workflow 분기도 stable 표면 안
- `Microsoft.Agents.AI.Anthropic`: **prerelease** — LLM 커넥터 계층에 격리
- 커넥터 API 표면이 아직 흔들림 (예: `APIKey` / `ApiKey` 프로퍼티 표기가
  문서 소스마다 상이) — 컴파일 에러 시 IntelliSense 기준으로 수정

### 3.3 MAF: 1.0 GA 리네임 함정 (dotnet-1.13.0 태그 소스로 검증)

`create_react_agent` deprecation과 같은 "구 튜토리얼 함정" 계열.
2025년 블로그/튜토리얼 대부분이 구 API를 사용한다.

| 구 API (2025 문서) | 현행 API (1.0 GA / 1.13.0) |
|---|---|
| `AgentThread` | `AgentSession` |
| `agent.GetNewThread()` | `await agent.CreateSessionAsync()` |
| `RunAsync(..., thread)` | `RunAsync(..., session)` |
| `FunctionApprovalRequestContent` | `ToolApprovalRequestContent` |

---

## 4. 환경 구축 gotchas (Windows)

| 증상 | 원인 | 해결 |
|---|---|---|
| `uv add langgraph` 실패 (self-dependency) | uv 프로젝트명이 폴더명(`langgraph`)을 따라가 PyPI 패키지명과 충돌 | `pyproject.toml`의 `name`을 `semicon-langgraph`로 변경. .NET과 달리 프로젝트명이 패키지 네임스페이스와 충돌 검사됨 |
| PowerShell 명령이 cmd에서 실패 | 셸 문법 차이 | 터미널을 PowerShell로 통일 |
| `git add` 시 LF/CRLF 경고 | uv 생성 파일(LF) vs Windows Git 기본(CRLF) | 리포 루트 `.gitattributes`: `* text=auto eol=lf` + `git add --renormalize .` (Mac 병행 개발 대비) |

---

## 5. 상태 지속 — Checkpointer ↔ AgentSession 직렬화

**핵심 비대칭: 내장 vs opt-in.** 양쪽 모두 "세션 키로 대화 상태를 프로세스 재시작
너머로 지속"이 가능하다. 다른 것은 **저장을 누가, 언제 하는가**.

### LangGraph — compile 시점에 주입하면 이후는 자동

```python
conn = sqlite3.connect("checkpoints.sqlite", check_same_thread=False)
graph = builder.compile(checkpointer=SqliteSaver(conn))

config = {"configurable": {"thread_id": thread_id}}   # thread_id = 세션 키
graph.invoke({"messages": [("user", user_input)]}, config)
# 매 super-step마다 SqliteSaver가 자동 스냅샷 — 호출측 저장 코드 없음
```

- 저장 시점을 프레임워크가 소유: 그래프의 **매 super-step**마다 기록
- 복원도 암묵적: 같은 `thread_id`로 invoke하면 이전 상태 위에서 이어짐
- 부산물: checkpointer DB가 곧 **사후 트레이스 자산** (재실행 없이 디버깅)

### MAF — 직렬화 시점을 호출자가 소유

```csharp
// 복원: 파일이 있으면 Deserialize, 없으면 새 세션
session = File.Exists(sessionPath)
    ? await agent.DeserializeSessionAsync(state)
    : await agent.CreateSessionAsync();

AgentResponse response = await agent.RunAsync(input, session);

// 저장: 매 턴 직후 '호출자가' 스냅샷 — 프레임워크는 저장하지 않는다
JsonElement serialized = await agent.SerializeSessionAsync(session);
File.WriteAllText(sessionPath, JsonSerializer.Serialize(serialized));
```

- 직렬화 결과는 툴 호출/결과 쌍까지 포함한 JSON (복원 후 메시지 수로 검증)
- 저장소 선택 자유 (파일/DB/Redis) — 대신 저장 누락도 호출자 책임

### 대응표

| 관점 | LangGraph | MAF |
|---|---|---|
| 세션 키 | `thread_id` (config) | 직렬화 저장 키 (파일명 등, 호출자 정의) |
| 저장 시점 | 매 super-step 자동 | 호출자가 명시적으로 (`SerializeSessionAsync`) |
| 복원 | 같은 thread_id로 invoke | `DeserializeSessionAsync` 명시 호출 |
| 저장소 | Saver 구현체 (SQLite/Postgres/...) | 임의 (JSON을 어디 두든 자유) |
| .NET 감각 | EF SaveChanges 자동 호출에 가까움 | `ISession` + 수동 커밋 |

---

## 6. HITL — interrupt ↔ ApprovalRequiredAIFunction

**핵심 비대칭: 승인의 '모양'이 다르다.** §2의 "제어 흐름 소유권" 차이가 그대로
기능 구현 방식 차이로 이어진 첫 사례 (Week 1 가설 검증 완료).

- LangGraph: HITL이 **그래프 노드 모양** — 임의 지점에 `interrupt()`를 꽂는다
- MAF: HITL이 **툴 모양** — "승인 필요 툴"로 모델링. 임의 지점 승인이 필요하면
  그 지점을 툴로 승격해야 한다 (`SubmitSignalJudgment` 패턴)

### LangGraph — 노드 안의 interrupt

```python
def approval_node(state: MessagesState) -> dict:
    decision = interrupt({"question": "승인하시겠습니까?", "analysis": ...})
    # Command(resume=값) 재개 시, 그 값이 interrupt()의 반환값이 된다
    ...

# 호출측
result = graph.invoke({...}, config)          # interrupt에서 멈춤
pending = result["__interrupt__"][0].value    # payload 수신
result = graph.invoke(Command(resume=answer), config)  # 재개
```

- **interrupt는 checkpointer가 전제조건** — 멈춘 상태를 저장할 곳이 필요하므로.
  즉 LangGraph HITL은 "영속화의 응용 기능"이고, 대기 상태 영속화가 공짜
- **재개는 노드 단위 replay**: 멈춘 줄이 아니라 노드 처음부터 재실행된다.
  interrupt 이전 코드는 멱등 필수 — .NET async/await식 지점 재개가 아님
- payload는 자유 구성 (dict 아무거나)

### MAF — 승인 필요 툴 + 응답 컨텐츠

```csharp
AIFunction submitTool = new ApprovalRequiredAIFunction(
    AIFunctionFactory.Create(SubmitSignalJudgment));

AgentResponse response = await agent.RunAsync(query, session);
// 응답 Contents에 ToolApprovalRequestContent 포함 = interrupt payload 대응

var reply = new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]);
response = await agent.RunAsync([reply], session);   // 같은 세션으로 재개
```

- 승인 대상 = **툴 호출 인자** (payload를 자유 구성하는 게 아니라 함수 시그니처가 스키마)
- 대기 상태 영속화는 §5와 같은 opt-in: 승인 대기 중 세션을 직렬화→복원→재개 가능
  (과거 이슈 #1318의 NotSupportedException은 1.13.0에서 해소 확인).
  즉 비대칭은 "가능/불가능"이 아니라 **"내장/opt-in"**
- 루프 재개 방식이 다르므로 멱등성 이슈 자체가 없음 — 대기·재개가
  메시지 교환(요청 컨텐츠 ↔ 응답 컨텐츠)으로 표현된다

### 대응표

| 관점 | LangGraph | MAF |
|---|---|---|
| 승인 지점 | 임의 노드 (`interrupt()`) | 승인 필요 툴 (지점을 툴로 승격) |
| payload | 자유 구성 dict | 툴 호출 인자 (함수 시그니처 = 스키마) |
| 대기 신호 | `result["__interrupt__"]` | `ToolApprovalRequestContent` in Contents |
| 재개 | `Command(resume=값)` → interrupt 반환값 | `CreateResponse(bool)` 유저 메시지 회신 |
| 대기 영속화 | checkpointer 내장 (공짜) | 세션 직렬화 opt-in (호출자 소유) |
| 재개 단위 | 노드 replay (멱등 필수) | 메시지 교환 (replay 없음) |

---

## 7. 조건부 분기 — conditional edges ↔ Workflow AddSwitch

3-way(강/중/약) 시그널 라우팅. MAF 쪽은 **Workflow 첫 대면** — §2에서 예고한
"StateGraph의 진짜 대응물"이며, `Microsoft.Agents.AI.Workflows`가 **stable**
패키지라 §7(안정성 경계) 규칙 안에서 사용 가능함을 확인.

### LangGraph — 라우터 함수가 state를 읽는다

```python
builder.add_conditional_edges(
    "judge",
    route_by_strength,   # (state) -> "strong" | "medium" | "weak"
    {"strong": "strong", "medium": "medium", "weak": "weak"},
)

def route_by_strength(state: SignalState) -> Literal["strong", "medium", "weak"]:
    s = state["strength"]
    return s if s in ("strong", "medium", "weak") else "medium"  # else = 코드 컨벤션
```

### MAF — 조건이 '직전 executor의 반환 메시지'를 받는다

```csharp
var workflow = new WorkflowBuilder(judge)
    .AddSwitch(judge, sw => sw
        .AddCase(Is(SignalStrength.Strong), strong)
        .AddCase(Is(SignalStrength.Weak), weak)
        .WithDefault(medium))            // default가 빌더 API 수준에서 강제됨
    .WithOutputFrom(strong, medium, weak)
    .Build();

static Func<object?, bool> Is(SignalStrength expected) =>
    msg => msg is SignalJudgment j && j.Strength == expected;
```

### 대응표

| 관점 | LangGraph | MAF |
|---|---|---|
| 분기 API | `add_conditional_edges(라우터, path_map)` | `AddSwitch(sw => AddCase/WithDefault)` |
| 조건의 입력 | **누적 state** (그래프 전체 공유) | **직전 executor의 반환 메시지** (typed) |
| 상태 공유 | 기본값 (모든 노드가 state 공유) | opt-in (`QueueStateUpdateAsync`/`ReadStateAsync`, scoped) |
| default 분기 | 라우터 함수의 else (코드 컨벤션) | `WithDefault` (빌더 API가 구조적으로 강제) |
| 노드 단위 | 함수 (`(state) -> dict`) | `Executor<TIn, TOut>` 클래스 (`HandleAsync` 오버라이드) |
| 실행 | `graph.invoke(...)` | `InProcessExecution.RunStreamingAsync` + 이벤트 스트림 |
| .NET 감각 | switch문을 함수로 뺀 것 | TPL Dataflow의 typed 메시지 파이프라인에 가까움 |

**패턴 노트 (양쪽 공통 채택)**: 공식 MAF 샘플은 분기 판정에 structured output
(`ChatResponseFormat.ForJsonSchema<T>()`)을 쓰지만 Azure OpenAI 전제.
OpenRouter+deepseek 경로에서는 json_schema strict 모드 통과가 불확실하므로
**프롬프트 JSON 강제 + 방어적 파싱 + 파싱 실패 시 default 분기**로 통일 —
LangGraph 라우터의 else ↔ MAF `WithDefault`가 정확히 대칭이 되는 부수 효과.
(실측: deepseek/deepseek-chat-v3.1, temperature=0에서 양쪽 모두 1회차에
유효 JSON 반환. 툴 호출 비일관성과 달리 JSON 출력은 안정적 경향 — n 작음, 관찰 지속)

---

## 8. §2 정밀화 — MAF 툴 루프의 실소유자

Week 2 스택 트레이스 실증: MAF 에이전트의 툴 반복 루프를 실제로 소유하는 것은
MAF 자체가 아니라 **MEAI(`Microsoft.Extensions.AI`) 계층의
`FunctionInvokingChatClient`** 데코레이터다.

- §2의 "루프를 런타임이 소유"를 "MEAI 데코레이터 체인이 소유"로 정밀화
- 시사점: `AIAgent`를 안 쓰고 MEAI `IChatClient`만 조립해도 같은 툴 루프를 얻는다.
  MAF가 더하는 것은 세션/승인/워크플로우 계층
- 루프 개입 지점: LangGraph는 state 검사 + `tool_choice` 강제(코드 수준 보장),
  MAF는 Middleware에서 `ChatOptions` 조작 (동일 목적의 다른 레이어)

---

## v0.3 예정 (Week 3+)

- [ ] MCP 툴 연결: `langchain-mcp-adapters` ↔ MAF 네이티브 MCP
- [ ] Workflow 자체 checkpoint (`CheckpointWithHumanInTheLoop` 샘플 존재 확인)와
      AgentSession 직렬화의 관계 — Week 6 착수 시 검증
- [ ] 관측: LangSmith ↔ OpenTelemetry