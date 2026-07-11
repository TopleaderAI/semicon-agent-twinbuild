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