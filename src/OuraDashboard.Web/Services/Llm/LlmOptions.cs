namespace OuraDashboard.Web.Services.Llm;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public bool Enabled { get; set; }
    public bool SandboxMode { get; set; }
    // null = let Ollama decide; false = disable thinking (recommended for small GPUs)
    public bool? Think { get; set; }
    public string BaseUrl { get; set; } = "http://neolinux:11434";
    public string Model { get; set; } = "llama3.1:8b";
    public int TimeoutSeconds { get; set; } = 60;
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int MaxConcurrentRequests { get; set; } = 1;
    public int MaxPromptChars { get; set; } = 24_000;
    public int MaxResponseChars { get; set; } = 8_000;
    public int RequestCacheTtlHours { get; set; } = 168;
    public string KeepAlive { get; set; } = "10m";
    public double Temperature { get; set; } = 0.2;
    public double TopP { get; set; } = 0.9;
    public int TopK { get; set; } = 40;
    public double RepeatPenalty { get; set; } = 1.1;
    public int NumPredict { get; set; } = 700;
    public int NumCtx { get; set; } = 8192;
}
