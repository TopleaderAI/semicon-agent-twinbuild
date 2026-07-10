using System.ComponentModel;
using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY");
var model = Environment.GetEnvironmentVariable("AGENT_MODEL") ?? "claude-haiku-4-5";

AnthropicClient client = new() { ApiKey = apiKey };

AIAgent agent = client.AsAIAgent(
    model: model,
    name: "HelloAgent",
    instructions: """
        너는 메모리 반도체 투자 리서치 어시스턴트다.
        뉴스 툴을 호출해 오늘의 SK하이닉스 뉴스를 가져온 뒤,
        각 뉴스를 한 줄로 요약하고 HBM 수요 관점에서
        시그널(긍정/중립/부정)과 근거를 판정하라.
        툴 결과를 받으면 추가 확인 없이 그 응답 안에서
        요약과 판정을 모두 완료하라.
        """,
    tools: [AIFunctionFactory.Create(FetchSkHynixNews)]);

Console.WriteLine(await agent.RunAsync("오늘 SK하이닉스 뉴스 요약하고 시그널 판정해줘"));

// --- Tool (Week 1은 mock, Week 3에서 MCP 서버로 이전 예정) ---

[Description("오늘의 SK하이닉스 관련 뉴스 헤드라인 목록을 반환한다.")]
static string[] FetchSkHynixNews() =>
[
    "SK하이닉스, HBM4 샘플 주요 고객사 공급 개시... 양산 일정 앞당길 듯",
    "마이크론, HBM 증설 투자 발표에 업계 공급과잉 우려 제기",
    "빅테크 A사, 차기 데이터센터 capex 가이던스 상향... AI 인프라 투자 지속",
];