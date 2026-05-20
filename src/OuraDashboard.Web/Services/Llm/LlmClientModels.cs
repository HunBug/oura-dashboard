namespace OuraDashboard.Web.Services.Llm;

public sealed record LlmMessage(string Role, string Content);

public sealed record LlmParameters(
    double Temperature,
    double TopP,
    int TopK,
    double RepeatPenalty,
    int NumPredict,
    int NumCtx);

public sealed record LlmChatRequest(
    string Model,
    IReadOnlyList<LlmMessage> Messages,
    LlmParameters Parameters,
    string KeepAlive);

public sealed record LlmChatResponse(
    string Text,
    string RawRequestJson,
    string RawResponseJson,
    int LatencyMs,
    int? PromptTokens,
    int? CompletionTokens);

public sealed class LlmClientException(
    string code,
    string message,
    string? rawRequestJson = null,
    string? rawResponseJson = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? RawRequestJson { get; } = rawRequestJson;
    public string? RawResponseJson { get; } = rawResponseJson;
}
