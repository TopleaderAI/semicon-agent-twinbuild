### 2026-07-10 (Week 1 — 환경 구축 + LangGraph Hello Agent)

- **완료**:
  - 모노리포 생성 (`/langgraph`, `/maf`, `/mcp-server`, `/docs`) + uv/Python 3.12 환경
  - LangGraph Hello Agent 동작 (manual StateGraph, 툴 1개, mock 뉴스 → 요약 + 시그널 판정)
  - 개발 비용 최적화: AGENT_MODEL env 스위치 (개발 haiku / 시연 sonnet), --trace 디버깅 플래그
- **결정**:
  - `create_react_agent` 대신 manual StateGraph 채택 (LangGraph v1.0에서 deprecated
    + Week 2 checkpointer/interrupt 확장성 + §5 매핑 학습 목적)
  - uv 프로젝트명은 `semicon-langgraph` (패키지명 `langgraph`와 충돌 회피)
  - `.gitattributes`로 LF 통일 (Windows/Mac 크로스 플랫폼 대비)
- **다음 할 일**:
  - [ ] Sonnet으로 DoD 캡처 1회 (스크린샷/로그 저장)
  - [ ] .NET 프로젝트 스캐폴드 + Microsoft.Agents.AI 1.0 NuGet
  - [ ] MAF Hello Agent (동일 스펙) → Week 1 DoD 완료
  - [ ] 개념 매핑 문서 v0.1: 툴 정의 방식 비교 (@tool vs AIFunctionFactory)
    + create_react_agent deprecation 노트

### 2026-07-10 (Week 1 — MAF Hello Agent, DoD 달성)

- **완료**:
  - .NET 콘솔 프로젝트 + Microsoft.Agents.AI (stable) + Microsoft.Agents.AI.Anthropic (prerelease 커넥터)
  - MAF Hello Agent 동작: AnthropicClient.AsAIAgent + AIFunctionFactory.Create 툴 1개,
    LangGraph 버전과 동일 스펙 (mock 뉴스 → 요약 + 시그널 판정)
  - **Week 1 DoD 달성**: 양쪽 프레임워크에서 E2E 동작 확인 (출력 캡처 보관)
- **결정**:
  - Anthropic 커넥터는 prerelease지만 코어(Microsoft.Agents.AI)는 stable 유지 —
    커넥터 계층만 preview 허용, §7 경계 원칙 준수
- **발견 (§5 매핑 반영 대기)**:
  - 툴 정의: @tool 데코레이터 + docstring ↔ [Description] + AIFunctionFactory.Create() 리플렉션
  - 툴 루프 소유권: LangGraph는 그래프에 명시(agent→tools→agent 엣지),
    MAF는 RunAsync()가 내부 처리 — 동일 스펙 코드량 차이(~60줄 vs ~30줄)의 원인
- **다음 할 일**:
  - [ ] 개념 매핑 문서 v0.1 작성 (docs/) — Week 1 마지막 항목
  - [ ] Week 2 착수: LangGraph checkpointer(SQLite) + interrupt

### 2026-07-12 (Week 2 — LangGraph 트랙: checkpointer + HITL 완료)

- **완료**:
  - `checkpoint_agent.py`: SqliteSaver 주입, thread_id 세션 키로 프로세스 재시작 후
    대화 상태 복원 확인 (재실행 → 복원 메시지 수 검증)
  - `hitl_agent.py`: interrupt 승인 게이트 — 승인 대기 → 사람 응답 → Command(resume=) 재개
    E2E 동작 (mock 뉴스 3건 요약 + 시그널 판정 → approve 플로우)
  - `llm_factory.py`: AGENT_PROVIDER 스위치 (Anthropic 직결 / OpenRouter 경유),
    그래프 코드는 BaseChatModel 인터페이스 의존이라 무변경
  - 진단 스크립트 2종: `inspect_last_run.py` (checkpointer DB 트레이스 덤프),
    `tool_probe.py` (모델 tool calling 1회 프로브)
  - `.gitignore`에 `*.sqlite` 추가, 커밋 6개 분리 푸시 (하나의 커밋 = 하나의 의도)
- **결정**:
  - Anthropic 크레딧 급소진 → 개발 LLM을 OpenRouter(`deepseek/deepseek-chat-v3.1`)로 전환.
    공식 `langchain-openrouter` 패키지 채택 (ChatOpenAI base_url 우회법 대신)
  - deepseek의 툴 호출 비일관성 대응: temperature=0 + 프롬프트 강화(soft)에 더해
    첫 턴 `tool_choice="any"` 강제(hard) — ToolMessage 유무로 강제/일반 모델 분기
    (무한 툴 루프 방지)
  - 크레딧 소진 원인은 Console Usage에서 추후 확인 필요 (키 로테이션 검토 항목 유지)
- **발견 (§5 매핑 v0.2 반영 대기)**:
  - interrupt는 checkpointer가 전제조건 — LangGraph HITL은 "영속화의 응용 기능"
  - 재개 시 노드는 처음부터 재실행 (노드 단위 replay) — interrupt 이전 코드는 멱등 필수,
    .NET async/await식 지점 재개가 아님
  - 프롬프트 유도 vs 구조 강제: 루프를 그래프가 소유하므로 "첫 턴은 반드시 툴" 같은
    규칙을 상태 검사 + tool_choice로 코드 수준에서 보장 가능.
    MAF 대응물은 Middleware에서 ChatOptions 조작으로 추정 (Week 2 MAF에서 검증)
  - checkpointer DB가 곧 디버깅 자산 — 재실행(과금) 없이 사후 트레이스 분석 가능
- **백로그**:
  - HITL 게이트 진입 조건 강화 — 시그널 판정 유무 검증 후 interrupt (Week 5)
  - Analyst 판정 출력을 structured output 스키마로 승격 (뉴스별 판정 + 종합)
  - Ollama provider 스위치 추가 (Phase 2 실데이터 반복 실행 시 비용 절감용)
- **다음 할 일**:
  - [ ] MAF: AgentThread 세션 상태 지속 (checkpoint_agent 대응물) — 착수 시 API 공식 문서 검증 선행
  - [ ] MAF: 승인(HITL) 플로우 — §2 "소유권 비대칭" 가설 검증
  - [ ] 조건부 분기 3-way (시그널 강/중/약) 양쪽 구현
  - [ ] 매핑 문서 v0.2 작성 (상태 지속 / HITL / 분기 + 위 발견 3건)
  - [ ] Anthropic Console Usage에서 크레딧 소진 원인 확인 + 필요 시 키 로테이션

### 2026-07-12 (Week 2 — MAF 트랙: AgentSession 지속 + HITL 완료)

- **완료**:
  - `maf/SessionAgent`: AgentSession 직렬화/복원으로 프로세스 재시작 후 대화 상태 지속
    (checkpoint_agent.py 대응). 세션 키 = 파일명, 매 턴 직후 SerializeSessionAsync → JSON 저장.
    재실행 시 "복원된 메시지 수: 4" 확인 — 툴 호출/결과 쌍까지 통째 직렬화됨
  - `maf/HitlAgent`: ApprovalRequiredAIFunction 승인 게이트 E2E (hitl_agent.py 대응).
    승인 대기 → Y/n → CreateResponse(bool) 회신 → 같은 세션으로 재개
  - `maf/Shared/LlmFactory`: AGENT_PROVIDER 스위치 (openrouter 기본 / anthropic) —
    llm_factory.py 대응. OpenRouter는 OpenAI 커넥터 + Endpoint 오버라이드
    (공식 1.13.0 샘플 Agent_With_AzureFoundryModel 패턴)
  - **Week 2 DoD 코드 파트 달성**: "승인 대기 → 사람 응답 → 재개"가 양쪽 프레임워크에서 동작
    (문서 파트인 매핑 문서 v0.2는 잔여 — 완성 시 Week 2 DoD 최종 충족)
- **결정**:
  - Anthropic 크레딧 소진으로 MAF 트랙도 OpenRouter 전환 (LangGraph 트랙과 대칭 구조)
  - 디버그용 ClientResultException 래퍼(GetRawResponse로 400 body 노출)는 상시 유지 —
    tool_probe.py ↔ "PowerShell 프로브 + GetRawResponse" 격리 패턴 대응
  - `.gitignore`에 `maf/**/sessions/`, `maf/**/hitl_pending.json` 추가 (*.sqlite 대응물)
- **발견 (§5 매핑 v0.2 반영 대기)**:
  - **API 리네임 2건 (dotnet-1.13.0 태그 소스로 검증)**: AgentThread → AgentSession
    (GetNewThread → CreateSessionAsync, RunAsync 파라미터 thread → session, 1.0 GA),
    FunctionApprovalRequestContent → ToolApprovalRequestContent (공식 샘플
    Agent_Step04 기준). 2025년 블로그/튜토리얼 대부분 구 API — create_react_agent
    deprecation과 같은 "구 튜토리얼 함정" 계열
  - HITL 영속화 비대칭은 "가능/불가능"이 아니라 **"내장/opt-in"**: LangGraph는
    interrupt가 checkpointer 전제라 대기 상태 영속화가 공짜, MAF는 승인 대기 중
    세션 직렬화→복원→재개가 가능하되(이슈 #1318은 1.13.0에서 해소 확인) 직렬화
    시점을 호출자가 소유
  - 승인의 형태 차이: LangGraph는 interrupt payload 자유 구성(그래프 노드 모양),
    MAF는 툴 호출 인자가 곧 승인 대상(툴 모양) — 임의 지점 승인은 그 지점을
    "승인 필요 툴"로 승격해야 함 (SubmitSignalJudgment 패턴)
  - MAF 툴 루프의 실소유자는 MEAI 계층의 FunctionInvokingChatClient (스택 트레이스
    실증) — §2 "런타임 내장"을 "MEAI 데코레이터 체인"으로 정밀화
  - deepseek이 MAF+OpenAI 호환 경로에서는 프롬프트만으로 툴 2개 순차 호출 성공
    (LangGraph에서는 tool_choice="any" 강제 필요했음). 단 n=1이라 결론 유보 —
    재발 시 ChatOptions.ToolMode 미들웨어 실험
  - 운영 풋건: AGENT_MODEL을 두 공급자가 공유해 공급자 전환 시 모델 네임스페이스
    불일치로 400 발생 (잔존 세션 env var가 원인이었음)
- **백로그**:
  - LlmFactory에 provider/model 네임스페이스 가드 추가 (openrouter인데 '/' 없는
    모델명이면 요청 전 fail-fast) — LangGraph llm_factory.py에도 대칭 적용
- **다음 할 일**:
  - [ ] 커밋 4개 분리 푸시: ① chore: ignore MAF runtime session state
        ② feat(maf): LlmFactory ③ feat(maf): SessionAgent ④ feat(maf): HitlAgent
  - [ ] 조건부 분기 3-way (시그널 강/중/약): LangGraph conditional edges ↔
        MAF **Workflow** 첫 대면 (착수 시 Workflow API 공식 문서/태그 소스 검증 선행)
  - [ ] 매핑 문서 v0.2 작성: 상태 지속 / HITL / 분기 + 위 발견 반영
        (§5 표의 "AgentThread" 표기도 AgentSession으로 수정)