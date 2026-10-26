using System.Diagnostics;

namespace DecisionCouncil.Core.Providers;

/// <summary>
/// Simulated provider for demos and UI work. Returns canned answers in the real output format after a
/// realistic delay. It never starts a process or touches the network, so it costs nothing.
/// </summary>
public sealed class DemoProvider(double speed = 1.0) : IModelProvider
{
    public string Name => "Demo (simulated)";
    public bool IsSimulated => true;

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var seconds = request.Round switch
        {
            DebateRound.Round1 => 2.0 + Random.Shared.NextDouble() * 4.0,
            DebateRound.Round2 => 1.5 + Random.Shared.NextDouble() * 3.0,
            _ => 4.0,
        };
        await Task.Delay(TimeSpan.FromSeconds(seconds / Math.Max(0.1, speed)), ct);

        var text = DemoScript.Answer(request);
        sw.Stop();
        return new ModelResult(
            text,
            request.Model + " (simulated)",
            InputTokens: (request.SystemPrompt.Length + request.UserPrompt.Length) / 4,
            OutputTokens: text.Length / 4,
            CostUsd: 0m,
            sw.Elapsed,
            RawOutput: null);
    }

    public Task<ProviderCheck> CheckAsync(CancellationToken ct) =>
        Task.FromResult(new ProviderCheck(true, "Demo provider: answers are simulated, no model is called and no credit is used."));
}
