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
