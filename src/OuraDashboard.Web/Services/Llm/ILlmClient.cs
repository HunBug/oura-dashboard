namespace OuraDashboard.Web.Services.Llm;

public interface ILlmClient
{
    Task<LlmChatResponse> CompleteChatAsync(LlmChatRequest request, CancellationToken ct);
}
