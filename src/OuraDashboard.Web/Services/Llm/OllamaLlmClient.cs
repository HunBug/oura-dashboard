using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OuraDashboard.Web.Services.Llm;

public sealed class OllamaLlmClient(
    HttpClient http,
    IOptions<LlmOptions> options,
    LlmConcurrencyLimiter limiter,
    ILogger<OllamaLlmClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public async Task<LlmChatResponse> CompleteChatAsync(LlmChatRequest request, CancellationToken ct)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            throw new LlmClientException("disabled", "LLM generation is disabled.");

        var payload = new OllamaChatRequest(
            request.Model,
            false,
            request.KeepAlive,
            opts.Think,
            request.Messages.Select(x => new OllamaMessage(x.Role, x.Content)).ToList(),
            new OllamaOptions(
                request.Parameters.Temperature,
                request.Parameters.TopP,
                request.Parameters.TopK,
                request.Parameters.RepeatPenalty,
                request.Parameters.NumPredict,
                request.Parameters.NumCtx));

        var rawRequest = JsonSerializer.Serialize(payload, JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds)));

        var sw = Stopwatch.StartNew();
        try
        {
            using var lease = await limiter.WaitAsync(timeout.Token);
            using var response = await http.PostAsJsonAsync("/api/chat", payload, JsonOptions, timeout.Token);
            var rawResponse = await response.Content.ReadAsStringAsync(timeout.Token);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var code = $"http_{(int)response.StatusCode}";
                var body = rawResponse.Length > 500 ? rawResponse[..500] : rawResponse;
                throw new LlmClientException(code, $"Ollama returned {(int)response.StatusCode}: {body}", rawRequest, rawResponse);
            }

            var parsed = JsonSerializer.Deserialize<OllamaChatResponse>(rawResponse, JsonOptions);
            var text = parsed?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                var doneReason = parsed?.DoneReason;
                var thinking = parsed?.Message?.Thinking?.Trim();
                var msg = doneReason == "length"
                    ? $"Token limit hit (done_reason=length, eval_count={parsed?.EvalCount}): model ran out of tokens before writing a response. Increase NumPredict (currently {opts.NumPredict}) — thinking models need 2000+."
                    : !string.IsNullOrWhiteSpace(thinking)
                        ? $"Ollama returned empty content but non-empty thinking (done_reason={doneReason ?? "?"}). Model may need more tokens."
                        : $"Ollama returned an empty response (done_reason={doneReason ?? "?"}).";
                throw new LlmClientException("empty_response", msg, rawRequest, rawResponse);
            }

            if (text.Length > opts.MaxResponseChars)
                text = text[..opts.MaxResponseChars];

            return new LlmChatResponse(
                text,
                rawRequest,
                rawResponse,
                (int)sw.ElapsedMilliseconds,
                parsed?.PromptEvalCount,
                parsed?.EvalCount);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            throw new LlmClientException("timeout", $"Ollama request timed out after {opts.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Ollama request failed.");
            throw new LlmClientException("connect_failed", ex.Message);
        }
        catch (JsonException ex)
        {
            sw.Stop();
            throw new LlmClientException("parse_failed", ex.Message, rawRequest);
        }
    }

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Think,
        IReadOnlyList<OllamaMessage> Messages,
        OllamaOptions Options);

    private sealed record OllamaMessage(
        string Role,
        string Content,
        string? Thinking = null);

    private sealed record OllamaOptions(
        double Temperature,
        [property: JsonPropertyName("top_p")] double TopP,
        [property: JsonPropertyName("top_k")] int TopK,
        [property: JsonPropertyName("repeat_penalty")] double RepeatPenalty,
        [property: JsonPropertyName("num_predict")] int NumPredict,
        [property: JsonPropertyName("num_ctx")] int NumCtx);

    private sealed record OllamaChatResponse(
        OllamaMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);
}
