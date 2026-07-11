# semicon-agent-twinbuild

> 동일한 멀티에이전트 시스템을 **LangGraph (Python)** 와 **Microsoft Agent Framework 1.0 (.NET)** 으로
> 두 번 구현하는 트윈 빌드 프로젝트. 도메인은 메모리 반도체 투자 리서치.

## 왜 트윈 빌드인가

새 도메인과 새 프레임워크를 동시에 배우면 변수가 두 개가 된다.
이미 깊이 이해하는 도메인(HBM 수요 시그널 분석)을 고정하면 **프레임워크 차이에만 집중**할 수 있다.

같은 스펙을 두 번 구현하며 얻는 것:

- 두 프레임워크의 설계 철학 차이를 실코드 수준에서 비교 ([docs/framework-mapping.md](docs/framework-mapping.md))
- MCP 툴 서버를 **한 번 만들어 양쪽에서 재사용** — 툴 레이어 분리 검증
- 최종적으로 이종 프레임워크 에이전트 간 A2A 상호운용 실험

## 대상 시스템

반도체 투자 리서치 멀티에이전트 (4 에이전트):

| 에이전트 | 역할 |
|---|---|
| Orchestrator | 질의 분해, 라우팅, 응답 조립 |
| Data Collector | 뉴스/공시/가격 수집 (MCP 툴 호출) |
| Analyst | 시그널 판정 + Kelly 포지션 사이징 |
| Reporter | 일일/이벤트 리포트 생성 |

핵심 기능: 장기 실행 상태 유지(checkpointer ↔ AgentThread), Human-in-the-Loop 승인,
시그널 강도별 조건부 분기, 관측(LangSmith ↔ OpenTelemetry).

## 리포 구조

```
semicon-agent-twinbuild/
├── langgraph/    # Python 3.12 + uv, LangGraph 구현
├── maf/          # .NET, Microsoft.Agents.AI 1.0 구현
├── mcp-server/   # 공유 MCP 툴 서버 (Week 3 예정)
└── docs/         # 프레임워크 개념 매핑 문서
```

## 실행 방법

두 트랙 모두 `ANTHROPIC_API_KEY` 환경변수가 필요하다.
`AGENT_MODEL`로 모델을 전환한다 (기본: `claude-haiku-4-5`, 시연: sonnet).

### LangGraph (Python)

```powershell
cd langgraph
uv sync
uv run python hello_agent.py          # 기본 실행
uv run python hello_agent.py --trace  # 전체 메시지 트레이스
```

### MAF (.NET)

```powershell
cd maf/HelloAgent
dotnet run
```

두 구현 모두 동일 스펙: mock 뉴스 3건 → 툴 호출 → 한 줄 요약 + HBM 수요 관점 시그널(긍정/중립/부정) 판정.

## 진행 상황

- [x] **Week 1** — 환경 구축 + 양쪽 Hello Agent (툴 정의/실행 루프 비교 → [매핑 문서 v0.1](docs/framework-mapping.md))
- [ ] **Week 2** — 상태 지속(checkpointer ↔ AgentThread), HITL, 조건부 분기
- [ ] **Week 3~5** — MCP 툴 서버 + LangGraph 본 구현
- [ ] **Week 6~7** — MAF 포팅 + A2A 하이브리드 실험
- [ ] **Week 8** — 프레임워크 비교 회고 + 서비스화 시나리오

## 안정성 경계 (MAF)

코어는 `Microsoft.Agents.AI` **1.0 stable** 표면만 사용한다.
Anthropic 커넥터(`Microsoft.Agents.AI.Anthropic`)는 prerelease로, 커넥터 계층에만 격리한다.

## Disclaimer

이 프로젝트의 시그널 로직은 프레임워크 학습을 위한 예제이며, 투자 조언이 아니다.