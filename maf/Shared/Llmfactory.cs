// llm_factory.py 대응물 (MAF 트랙).
//
// AGENT_PROVIDER 환경변수로 LLM 공급자를 스위치한다:
//   - "openrouter" (기본): OpenAI 호환 API — Microsoft.Agents.AI.OpenAI 커넥터 +
//     Endpoint를 https://openrouter.ai/api/v1 로 오버라이드 (공식 샘플
//     Agent_With_AzureFoundryModel과 동일 패턴, 1.13.0 태그 검증)
//   - "anthropic": Anthropic 직결 — Microsoft.Agents.AI.Anthropic (prerelease 커넥터)
//
// LangGraph 쪽과 대칭:
//   build_llm() → BaseChatModel  ↔  BuildAgent() → AIAgent
//   그래프 코드가 BaseChatModel에만 의존하듯, 호출측은 AIAgent 추상화에만 의존한다.
//   (커넥터별 확장 메서드 차이 — Anthropic은 AsAIAgent(model,...), OpenAI는
//    GetChatClient(model).AsAIAgent(...) — 를 이 파일 안에 격리)

using System.ClientModel;
using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Maf.Shared;

public static class LlmFactory
{
    /// <summary>공급자/모델을 환경변수에서 읽어 AIAgent를 조립한다.</summary>
    /// <remarks>
    /// AGENT_PROVIDER: openrouter(기본) | anthropic
    /// AGENT_MODEL: 공급자별 기본값 — openrouter: deepseek/deepseek-chat-v3.1,
    ///              anthropic: claude-haiku-4-5
    /// OPENROUTER_API_KEY / ANTHROPIC_API_KEY: 공급자별 키
    /// </remarks>
    public static AIAgent BuildAgent(string name, string instructions, IList<AITool>? tools = null)
    {
        string provider = (Environment.GetEnvironmentVariable("AGENT_PROVIDER") ?? "openrouter")
            .Trim().ToLowerInvariant();

        return provider switch
        {
            "openrouter" => BuildOpenRouterAgent(name, instructions, tools),
            "anthropic" => BuildAnthropicAgent(name, instructions, tools),
            _ => throw new InvalidOperationException(
                $"Unknown AGENT_PROVIDER '{provider}' (expected: openrouter | anthropic)"),
        };
    }

    private static AIAgent BuildOpenRouterAgent(string name, string instructions, IList<AITool>? tools)
    {
        string apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException("Set OPENROUTER_API_KEY");
        string model = Environment.GetEnvironmentVariable("AGENT_MODEL")
            ?? "deepseek/deepseek-chat-v3.1";

        // OpenRouter는 OpenAI 호환 API — Endpoint만 오버라이드하면 OpenAI SDK 그대로 사용
        OpenAIClientOptions clientOptions = new() { Endpoint = new Uri("https://openrouter.ai/api/v1") };
        OpenAIClient client = new(new ApiKeyCredential(apiKey), clientOptions);

        return client.GetChatClient(model).AsAIAgent(
            instructions: instructions,
            name: name,
            tools: tools);
    }

    private static AIAgent BuildAnthropicAgent(string name, string instructions, IList<AITool>? tools)
    {
        string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY");
        string model = Environment.GetEnvironmentVariable("AGENT_MODEL") ?? "claude-haiku-4-5";

        AnthropicClient client = new() { ApiKey = apiKey };

        return client.AsAIAgent(
            model: model,
            name: name,
            instructions: instructions,
            tools: tools);
    }
}