// branch_agent.py 대응물 — WorkflowBuilder.AddSwitch로 3-way 분기.
// API 검증: dotnet-1.13.0 태그 samples/03-workflows/ConditionalEdges/02_SwitchCase
//
// LangGraph와의 대칭:
//   add_conditional_edges(router)  ↔  AddSwitch(sw => AddCase/WithDefault)
//   라우터는 state를 읽는다        ↔  조건은 "이전 executor의 반환 메시지"를 받는다
//   미인식 값 → medium (else)      ↔  WithDefault(medium)

using System.Text.Json;
using System.Text.Json.Serialization;
using Maf.Shared;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

const string JudgePrompt =
    "너는 메모리 반도체 투자 리서치 어시스턴트다. " +
    "아래 뉴스들을 HBM 수요 관점에서 종합해 시그널 강도를 판정하라. " +
    "반드시 JSON 객체 하나만 출력하라: {\"strength\": \"strong|medium|weak\", \"reason\": \"근거 한 줄\"} " +
    "코드블록/설명 등 다른 텍스트는 금지.";

string[] mockNews =
[
    "SK하이닉스, HBM4 샘플 주요 고객사 공급 개시... 양산 일정 앞당길 듯",
    "마이크론, HBM 증설 투자 발표에 업계 공급과잉 우려 제기",
    "빅테크 A사, 차기 데이터센터 capex 가이던스 상향... AI 인프라 투자 지속",
];

AIAgent judgeAgent = LlmFactory.BuildAgent("SignalJudge", JudgePrompt);

var judge = new SignalJudgeExecutor(judgeAgent);
var strong = new StrongSignalExecutor();
var medium = new MediumSignalExecutor();
var weak = new WeakSignalExecutor();

var workflow = new WorkflowBuilder(judge)
    .AddSwitch(judge, sw => sw
        .AddCase(Is(SignalStrength.Strong), strong)
        .AddCase(Is(SignalStrength.Weak), weak)
        .WithDefault(medium))   // medium + 파싱 실패(uncertain) 모두 여기로
    .WithOutputFrom(strong, medium, weak)
    .Build();

string newsBlock = string.Join("\n", mockNews.Select(n => $"- {n}"));

await using StreamingRun run =
    await InProcessExecution.RunStreamingAsync(workflow, new ChatMessage(ChatRole.User, newsBlock));
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent output)
    {
        Console.WriteLine(output);
    }
    else if (evt is ExecutorFailedEvent failed)
    {
        Console.Error.WriteLine($"Executor '{failed.ExecutorId}' failed: {failed.Data}");
    }
}

static Func<object?, bool> Is(SignalStrength expected) =>
    msg => msg is SignalJudgment j && j.Strength == expected;

public enum SignalStrength { Strong, Medium, Weak, Uncertain }

public sealed class SignalJudgment
{
    [JsonPropertyName("strength")]
    [JsonConverter(typeof(JsonStringEnumConverter))]   // "strong" 소문자 역직렬화 허용
    public SignalStrength Strength { get; set; } = SignalStrength.Uncertain;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class SignalJudgeExecutor(AIAgent agent)
    : Executor<ChatMessage, SignalJudgment>("SignalJudge")
{
    public override async ValueTask<SignalJudgment> HandleAsync(
        ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var response = await agent.RunAsync(message, cancellationToken: cancellationToken);
        string raw = response.Text.Trim();
        if (raw.StartsWith("```")) // deepseek 코드블록 방어
        {
            raw = raw.Trim('`').Replace("json", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        try
        {
            return JsonSerializer.Deserialize<SignalJudgment>(raw)
                ?? new SignalJudgment { Reason = "역직렬화 null" };
        }
        catch (JsonException)
        {
            return new SignalJudgment { Reason = $"판정 파싱 실패: {response.Text[..Math.Min(80, response.Text.Length)]}" };
        }
    }
}

[YieldsOutput(typeof(string))]
internal sealed class StrongSignalExecutor() : Executor<SignalJudgment>("StrongSignal")
{
    public override async ValueTask HandleAsync(SignalJudgment j, IWorkflowContext context, CancellationToken ct = default) =>
        await context.YieldOutputAsync($"[강] 액션 후보 — HITL 승인 게이트 대상 (Week 6 연결 예정). 근거: {j.Reason}", ct);
}

[YieldsOutput(typeof(string))]
internal sealed class MediumSignalExecutor() : Executor<SignalJudgment>("MediumSignal")
{
    public override async ValueTask HandleAsync(SignalJudgment j, IWorkflowContext context, CancellationToken ct = default) =>
        await context.YieldOutputAsync($"[중/불확실] 관찰 지속. 근거: {j.Reason}", ct);
}

[YieldsOutput(typeof(string))]
internal sealed class WeakSignalExecutor() : Executor<SignalJudgment>("WeakSignal")
{
    public override async ValueTask HandleAsync(SignalJudgment j, IWorkflowContext context, CancellationToken ct = default) =>
        await context.YieldOutputAsync($"[약] 노이즈 판단 — 리포트 생략. 근거: {j.Reason}", ct);
}