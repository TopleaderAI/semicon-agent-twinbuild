# PROJECT_KNOWLEDGE.md — Twin-Build: 반도체 투자 리서치 멀티에이전트

> LangGraph × Microsoft Agent Framework(MAF) 1.0 트윈 빌드 학습 프로젝트
> 최종 수정: 2026-07-13

---

## 1. 프로젝트 목적

동일한 멀티에이전트 시스템을 **LangGraph(Python)** 와 **MAF 1.0(.NET)** 으로 두 번 구현하여,
두 프레임워크의 개념·설계 철학·운영 방식을 실전 수준으로 체득한다.

**왜 트윈 빌드인가**
- 새 도메인 + 새 프레임워크를 동시에 배우면 변수가 두 개라 학습 효율이 떨어진다.
- 이미 깊이 이해하는 도메인(메모리 반도체 투자 시그널 분석)을 재료로 쓰면
  프레임워크 차이에만 집중할 수 있다.
- 같은 스펙을 두 번 구현하면 "프레임워크를 아는 사람"을 넘어
  **"이종 에이전트 시스템을 통합할 수 있는 사람"** 이 된다 (A2A 상호운용).

**최종 목표와의 연결**
- AI Engineering 전문가 전환의 핵심 포트폴리오.
- MAF .NET 구현은 기존 개인사업 고객사(레거시 .NET + Oracle/MS-SQL 스택)에
  AI 에이전트 레이어를 얹는 수익화 시나리오와 직결된다.

---

## 2. 학습자 배경 (컨텍스트)

- C# 시니어 개발자, 약 20년 경력 (.NET/WinForms, Oracle, MS-SQL, 한국 SME 시장)
- Python / AI 엔지니어링 전환 중
- 수강 완료/진행: LangGraph 멀티 AI Agent 과정, 하네스 엔지니어링 과정
  (Agent = Model + Harness + Environment 프레임 습득)
- 기존 자산:
  - HBM 수요 시그널 모니터링 로직 (하이퍼스케일러 capex, CXMT 뉴스 빈도,
    DRAM/HBM 가격 역전비, capex 자금조달 품질)
  - Kelly Criterion 기반 포지션 사이징 프레임워크
  - `ptrade` 백테스트 패키지 (PurgedKFold, 벡터화 백테스팅)
- 개발 환경: Windows PC (주력) + MacBook (uv, Ollama, Claude Code 구성 완료)

---

## 3. 대상 시스템 스펙 (양쪽 공통)

**"반도체 투자 리서치 멀티에이전트"** — 기존 모니터링 로직의 에이전트 승격판

### 에이전트 구성 (4개)

| 에이전트 | 역할 |
|---|---|
| Orchestrator | 사용자 질의 분해, 에이전트 라우팅, 최종 응답 조립 |
| Data Collector | 뉴스/공시/가격 데이터 수집 (MCP 툴 호출 전담) |
| Analyst | 시그널 판정 + Kelly 기준 포지션 사이징 산출 |
| Reporter | 일일/이벤트 리포트 생성 (Markdown) |

### 필수 기능 요구사항

1. **장기 실행 상태 유지** — 세션을 넘어 모니터링 상태 지속
   (LangGraph: checkpointer / MAF: AgentSession 직렬화)
2. **Human-in-the-Loop** — 매수/매도 시그널 발생 시 사람 승인 대기
   (LangGraph: interrupt / MAF: 승인 플로우)
3. **조건부 분기** — 시그널 강도별 라우팅 (조건부 엣지 / 그래프 워크플로우)
4. **툴 레이어 분리** — 데이터 수집 툴은 **MCP 서버로 독립 구현**하여
   양쪽 프레임워크에서 재사용 (한 번 만들고 두 번 쓴다)
5. **관측 가능성** — LangSmith(LangGraph) vs OpenTelemetry+DevUI(MAF) 비교

### 검증 시나리오 (실전 게이트)

- SK하이닉스 실적 발표 이벤트를 트리거로 한 E2E 시나리오
- 미국 빅테크 실적(실제 capex 수치) 반영 시나리오

---

## 4. 아키텍처 개요

```
┌─────────────────────────────────────────────────┐
│                 사용자 / 스케줄러                  │
└──────────────┬──────────────────────────────────┘
               │
   ┌───────────▼───────────┐        A2A (Phase 3)
   │      Orchestrator      │◄──────────────────────┐
   └───┬────────┬───────┬──┘                        │
       │        │       │                           │
  ┌────▼───┐ ┌──▼────┐ ┌▼────────┐        ┌────────┴────────┐
  │  Data  │ │Analyst│ │Reporter │        │ (이종 프레임워크  │
  │Collector│ │       │ │         │        │  Analyst 재사용) │
  └────┬───┘ └───────┘ └─────────┘        └─────────────────┘
       │
  ┌────▼──────────────────────────┐
  │   MCP Tool Server (공유 자산)   │
  │  - 뉴스 검색  - 공시 조회        │
  │  - 가격 데이터 - 시그널 지표 계산 │
  └───────────────────────────────┘
```

- Phase 2: 전체를 LangGraph로 구현
- Phase 3: 전체를 MAF(.NET)로 재구현 + LangGraph Analyst를 A2A로 호출하는
  하이브리드 구성 실험

---

## 5. 프레임워크 개념 매핑 (작업하며 지속 업데이트)

| 개념 | LangGraph | MAF 1.0 |
|---|---|---|
| 그래프 정의 | StateGraph / Node / Edge | Workflow (graph-based) |
| 상태 지속 | Checkpointer (내장, 매 super-step 자동 저장) | AgentSession 직렬화 (opt-in, 호출자가 저장 시점 소유) |
| HITL | interrupt (그래프 노드 모양) | ApprovalRequiredAIFunction (툴 모양) |
| 멀티에이전트 패턴 | Supervisor / Swarm | Sequential / Concurrent / Handoff / Group Chat / Magentic-One |
| 툴 정의 | @tool 데코레이터 | AIFunctionFactory.Create() (일반 C# 메서드 래핑) |
| MCP | 어댑터 (langchain-mcp-adapters) | 네이티브 지원 |
| 관측 | LangSmith | OpenTelemetry (네이티브) + DevUI (preview) |
| 미들웨어 | - (콜백/훅) | Middleware Pipeline (1.0 stable) |

---

## 6. 기술 스택

**LangGraph 트랙 (Python)**
- Python 3.12 (3.14 호환성 이슈 회피), uv 패키지 관리
- langgraph, langchain-mcp-adapters, LangSmith
- LLM: Claude API (필요 시 Ollama 로컬 모델 병행)

**MAF 트랙 (.NET)**
- .NET 8+ / C#, Microsoft.Agents.AI (1.0 GA)
- MCP 네이티브 연동, OpenTelemetry
- LLM: MAF 커넥터 경유 (Anthropic Claude / Azure OpenAI / Ollama 중 선택)

**공유**
- MCP Tool Server: Python (FastMCP 또는 공식 SDK)
- Git/GitHub 공개 리포 (포트폴리오 목적)

---

## 7. MAF 1.0 안정성 경계 (중요)

프로젝트 코어는 **1.0 stable 표면 위에만** 올린다.

- **Stable (1.0 GA)**: 코어 에이전트 추상화, 서비스 커넥터, 미들웨어,
  메모리/컨텍스트 프로바이더, 그래프 워크플로우, 오케스트레이션 패턴
  (sequential, concurrent, handoff, group chat, Magentic-One), MCP 지원
- **Preview (실험 허용, 코어 의존 금지)**: DevUI, Hosted Agents,
  CodeAct(hyperlight), Agent Harness 신기능 일부
- A2A는 채택 전 현재 지원 상태를 확인하고 진행한다.

---

## 8. 산출물 체크리스트

- [ ] 개념 매핑 문서 (§5를 실코드 예시와 함께 확장) — 출퇴근 20분 학습 자료 겸용
- [ ] MCP Tool Server 1개 (재사용 가능 독립 자산)
- [ ] LangGraph 구현 (동일 스펙)
- [ ] MAF .NET 구현 (동일 스펙)
- [ ] A2A 상호운용 데모 (하이브리드 구성)
- [ ] 프레임워크 비교 회고 글 (블로그/포트폴리오)
- [ ] 고객사 적용 시나리오 1-pager (레거시 .NET + AI 에이전트 레이어)

---

## 9. 관련 문서

- `PLAN.md` — 8주 주차별 실행 계획
- `CLAUDE.md` — 이 프로젝트에서 Claude의 역할과 응답 규칙
- `PROGRESS.md` — 진행 상황 로그 (세션마다 업데이트)