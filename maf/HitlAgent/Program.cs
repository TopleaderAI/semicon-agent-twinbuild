// Week 2 Step 2 (MAF): ApprovalRequiredAIFunction으로 Human-in-the-Loop 승인 게이트.
//
// hitl_agent.py 대응물. §2 "소유권 비대칭" 가설의 검증 지점:
//   - LangGraph: HITL이 그래프 노드 모양 — 임의 지점에 interrupt()를 꽂는다.
//   - MAF: HITL이 툴 모양 — "승인 필요 툴"(ApprovalRequiredAIFunction)로 모델링한다.
//     임의 지점 승인이 필요하면 그 지점을 툴로 승격해야 한다.
//     여기서는 "판정 제출"을 SubmitSignalJudgment 툴로 만들어 승인을 건다.
//
// 실행 흐름:
//   RunAsync → 응답에 ToolApprovalRequestContent 포함(= interrupt payload 대응)
//   → 사람 Y/n → CreateResponse(bool)를 유저 메시지로 회신(= Command(resume=) 대응)
//   → 같은 session으로 RunAsync 재개
//
// [버전 주의] Microsoft.Agents.AI 1.13.0 기준.
//   구 문서의 FunctionApprovalRequestContent는 ToolApprovalRequestContent로 리네임됨
//   (공식 1.13.0 샘플 Agent_Step04_UsingFunctionToolsWithApprovals 기준).
//
// 실행:  dotnet run

using System.ComponentModel;
using System.Text.Json;
using Maf.Shared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// 뉴스 수집은 자동 실행, 판정 제출만 승인 필요 툴로 래핑
AIFunction fetchTool = AIFunctionFactory.Create(FetchSkHynixNews, name: nameof(FetchSkHynixNews));
AIFunction submitTool = new ApprovalRequiredAIFunction(
    AIFunctionFactory.Create(SubmitSignalJudgment, name: nameof(SubmitSignalJudgment)));

// LLM 공급자는 LlmFactory가 결정 (AGENT_PROVIDER: openrouter 기본 | anthropic)
AIAgent agent = LlmFactory.BuildAgent(
    name: "HitlAgent",
    instructions: """
        너는 메모리 반도체 투자 리서치 어시스턴트다.
        뉴스가 필요하면 반드시 FetchSkHynixNews 툴을 먼저 호출한다.
        툴 결과를 받으면 각 뉴스 한 줄 요약과 시그널(긍정/중립/부정) 판정을 작성한 뒤,
        반드시 SubmitSignalJudgment 툴을 호출해 판정 전문을 제출하는 것으로 마무리하라.
        판정을 제출하지 않은 채 답변을 끝내는 것은 금지된다.
        """,
    tools: [fetchTool, submitTool]);

JsonSerializerOptions jsonOptions = new()
{
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

AgentSession session = await agent.CreateSessionAsync();

// 1단계: 승인 요청 지점까지 실행 → 응답에 승인 요청이 담겨 돌아온다
AgentResponse response = await agent.RunAsync(
    "오늘 SK하이닉스 뉴스 요약하고 시그널 판정해줘", session);

List<ToolApprovalRequestContent> approvalRequests = [.. response.Messages
    .SelectMany(m => m.Contents)
    .OfType<ToolApprovalRequestContent>()];

while (approvalRequests.Count > 0)
{
    // --- §2 검증 실험: 승인 대기 상태를 직렬화 → 복원 후 재개 ---
    // LangGraph에서 interrupt는 checkpointer가 전제(멈춘 상태의 영속화가 내장).
    // MAF에서 동일 효과를 내려면 대기 중인 세션을 직접 직렬화해야 한다.
    // 과거 이슈(#1318): 승인 요청이 담긴 상태의 직렬화가 NotSupportedException을
    // 던졌음 — 1.13.0에서 해소됐는지 여부 자체가 매핑 문서 기록 대상.
    try
    {
        JsonElement paused = await agent.SerializeSessionAsync(session);
        File.WriteAllText("hitl_pending.json", JsonSerializer.Serialize(paused));
        JsonElement reloaded = JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText("hitl_pending.json"));
        session = await agent.DeserializeSessionAsync(reloaded);
        Console.WriteLine("[persist] 승인 대기 상태 직렬화/복원 성공 — interrupt+checkpointer 등가 확인");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[persist] 승인 대기 상태 직렬화 실패 ({ex.GetType().Name}: {ex.Message})");
        Console.WriteLine("[persist] 인메모리 세션으로 계속 — 매핑 문서에 비대칭으로 기록할 것");
    }

    // --- 2단계: 사람 승인 (Command(resume=...) 대응) ---
    List<ChatMessage> userInputMessages = approvalRequests.ConvertAll(request =>
    {
        var call = (FunctionCallContent)request.ToolCall;
        Console.WriteLine();
        Console.WriteLine("=== 승인 대기 ===");
        Console.WriteLine($"함수: {call.Name}");
        Console.WriteLine($"인자: {JsonSerializer.Serialize(call.Arguments, jsonOptions)}");
        Console.Write("시그널 판정 결과입니다. 승인하시겠습니까? (Y/n) > ");
        bool approved = !string.Equals(
            Console.ReadLine()?.Trim(), "n", StringComparison.OrdinalIgnoreCase);
        return new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]);
    });

    // 3단계: 같은 세션으로 재개 (거부 시에도 모델이 거부 사실을 받아 마무리 응답 생성)
    response = await agent.RunAsync(userInputMessages, session);
    approvalRequests = [.. response.Messages
        .SelectMany(m => m.Contents)
        .OfType<ToolApprovalRequestContent>()];
}

Console.WriteLine();
Console.WriteLine($"Agent: {response}");

// --- Tools ---

[Description("오늘의 SK하이닉스 관련 뉴스 헤드라인 목록을 반환한다.")]
static string[] FetchSkHynixNews() =>
[
    "SK하이닉스, HBM4 샘플 주요 고객사 공급 개시... 양산 일정 앞당길 듯",
    "마이크론, HBM 증설 투자 발표에 업계 공급과잉 우려 제기",
    "빅테크 A사, 차기 데이터센터 capex 가이던스 상향... AI 인프라 투자 지속",
];

[Description("시그널 판정 결과를 제출한다. 사람 승인 후에만 실행된다.")]
static string SubmitSignalJudgment(
    [Description("뉴스별 한 줄 요약과 시그널(긍정/중립/부정) 판정 전문")] string analysis) =>
    "[HITL] 사람이 시그널 판정을 승인함. 리포트 파이프라인으로 전달 가능.";