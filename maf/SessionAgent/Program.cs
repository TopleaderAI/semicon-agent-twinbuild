// Week 2 Step 1 (MAF): AgentSession 직렬화로 대화 상태 지속.
//
// checkpoint_agent.py 대응물. LangGraph는 checkpointer가 매 super-step마다
// 자동 저장하지만, MAF는 세션 영속화가 opt-in이다:
//   SerializeSessionAsync() → JSON → 저장소  /  DeserializeSessionAsync() → 복원
// 세션 키(파일명)가 LangGraph의 thread_id에 대응한다.
//
// [버전 주의] Microsoft.Agents.AI 1.13.0 기준.
//   구 문서/블로그의 AgentThread / GetNewThread() / thread.Serialize() 는
//   1.0 GA에서 AgentSession / CreateSessionAsync() / agent.SerializeSessionAsync() 로
//   전면 리네임됨. RunAsync의 파라미터명도 thread → session.
//
// 실행:  dotnet run [sessionKey]   (생략 시 "demo")
//        재실행해서 "아까 내가 뭐 물어봤지?"로 지속성 확인
using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using Maf.Shared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;


// LLM 공급자는 LlmFactory가 결정 (AGENT_PROVIDER: openrouter 기본 | anthropic)
AIAgent agent = LlmFactory.BuildAgent(
    name: "SessionAgent",
    instructions: """
        너는 메모리 반도체 투자 리서치 어시스턴트다.
        뉴스 요청이 오면 툴을 호출해 가져온 뒤 요약과 시그널(긍정/중립/부정)을 판정하라.
        이전 대화 내용을 기억하고 있으므로, 과거 질문에 대한 후속 질문에도 답하라.
        """,
    tools: [AIFunctionFactory.Create(FetchSkHynixNews, name: nameof(FetchSkHynixNews))]);

// --- 세션 복원: 파일이 있으면 Deserialize, 없으면 새로 생성 ---
// .NET 감각: ISession + 영속 스토어. 단, ASP.NET 세션과 달리 프레임워크가
// 자동 저장하지 않는다 — 저장 시점(아래 루프의 매 턴 직후)은 호출자 책임.

string sessionKey = args.Length > 0 ? args[0] : "demo";
Directory.CreateDirectory("sessions");
string sessionPath = Path.Combine("sessions", $"{sessionKey}.json");

AgentSession session;
if (File.Exists(sessionPath))
{
    JsonElement state = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(sessionPath));
    session = await agent.DeserializeSessionAsync(state);
}
else
{
    session = await agent.CreateSessionAsync();
}

// 재시작 시 이전 상태가 복원되는지 확인용 출력 (checkpoint_agent.py의 snapshot 검사 대응)
int restored = session.TryGetInMemoryChatHistory(out List<ChatMessage>? history)
    ? history.Count
    : 0;
Console.WriteLine($"[session={sessionKey}] 복원된 메시지 수: {restored}");
Console.WriteLine("질문 입력 (빈 줄로 종료):");

// --- 대화형 루프 ---

while (true)
{
    Console.Write("> ");
    string? input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input))
    {
        break;
    }

    AgentResponse response;
    try
    {
        response = await agent.RunAsync(input, session);
    }
    catch (ClientResultException ex)
    {
        Console.WriteLine($"[HTTP {ex.Status}] {ex.GetRawResponse()?.Content}");
        throw;
    }
    Console.WriteLine(response);
    Console.WriteLine();

    // LangGraph checkpointer가 매 super-step마다 저장하듯, 매 턴 직후 스냅샷 저장.
    JsonElement serialized = await agent.SerializeSessionAsync(session);
    File.WriteAllText(sessionPath, JsonSerializer.Serialize(serialized));
}

// --- Tool (Week 1과 동일, Week 3에서 MCP 서버로 이전 예정) ---

[Description("오늘의 SK하이닉스 관련 뉴스 헤드라인 목록을 반환한다.")]
static string[] FetchSkHynixNews() =>
[
    "SK하이닉스, HBM4 샘플 주요 고객사 공급 개시... 양산 일정 앞당길 듯",
    "마이크론, HBM 증설 투자 발표에 업계 공급과잉 우려 제기",
    "빅테크 A사, 차기 데이터센터 capex 가이던스 상향... AI 인프라 투자 지속",
];