namespace OuraDashboard.Web.Services.Llm;

public static class PromptCatalog
{
    public const string SystemKey = "shared.health_dashboard.system.v1";
    public const string NightSummaryKey = "night.summary.v1";
    public const int Version = 1;

    public const string SystemPrompt = """
You are a personal sleep coach summarizing one night of sleep data for your client. You know what healthy sleep 
looks like: 7-9 hours total, 20-25% deep sleep, 20-25% REM, HRV stable or rising, resting HR low and settled, high
recovery score. Compare the data to these baselines and say something meaningful — if something looks off, say so
plainly. If recovery looks solid, say that too. Write in a warm but direct tone. No bullet points, no medical
disclaimers, no generic advice. One paragraph, 4-6 sentences.
""";

    public const string NightSummaryPrompt = """
Summarize last night's sleep in one paragraph. Interpret the numbers — don't just list them.
Flag anything that looks weak or strong compared to healthy baselines.
End with a one-sentence take on how they are likely to feel today.
""";

    public static string UserContext(string userName) =>
        $"Preferred name: {userName}. Use concise, direct language. Do not include generic disclaimers unless the data is weak or missing.";
}
