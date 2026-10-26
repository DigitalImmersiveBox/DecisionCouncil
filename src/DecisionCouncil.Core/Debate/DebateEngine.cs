using System.Diagnostics;
using DecisionCouncil.Core.Agents;
using DecisionCouncil.Core.Providers;
using DecisionCouncil.Core.Storage;
using DecisionCouncil.Core.Usage;

namespace DecisionCouncil.Core.Debate;

public abstract record DebateEvent;
public sealed record DebateCreated(long DebateId) : DebateEvent;
public sealed record PhaseStarted(DebateRound Round, IReadOnlyList<string> AgentIds) : DebateEvent;
public sealed record CallStarted(string AgentId, DebateRound Round) : DebateEvent;
public sealed record CallRetrying(string AgentId, DebateRound Round, int Attempt, string Reason) : DebateEvent;
public sealed record CallFinished(AgentRun Run) : DebateEvent;
public sealed record PhaseSkipped(DebateRound Round, string Reason) : DebateEvent;
public sealed record DebateFinished(long DebateId, DebateStatus Status, string? Message) : DebateEvent;

public sealed class DebateOptions
{
    public string AgentModel { get; init; } = "sonnet";
    public string SynthesizerModel { get; init; } = "opus";
    public string Language { get; init; } = "English";
    public int MaxConcurrency { get; init; } = 3;
    public int MaxRetries { get; init; } = 2;
}

public sealed class DebateRequest
{
    public required Idea Idea { get; init; }
    public required IReadOnlyList<AgentDefinition> Agents { get; init; }
    public bool Round2 { get; init; } = true;
}

/// <summary>
/// Runs one debate: isolated round 1 in parallel, optional cross-examination, then synthesis.
/// Every finished call is saved immediately, so a cancel, a crash or an exhausted credit keeps the debate so far.
/// </summary>
public sealed class DebateEngine(IModelProvider provider, CouncilStore store, PromptBuilder prompts, UsageGuard usage, DebateOptions options)
{
    public const string SynthesizerId = "synthesizer";
    public const string SynthesizerName = "Synthesis";

    /// <summary>Model calls a debate will make: agents in round 1, again in round 2, plus one synthesis.</summary>
    public static int CallCount(int agents, bool round2) => agents == 0 ? 0 : agents * (round2 ? 2 : 1) + 1;

    public async Task<long> RunAsync(DebateRequest request, IProgress<DebateEvent>? progress, CancellationToken ct)
    {
        if (request.Agents.Count == 0) throw new ArgumentException("Pick at least one agent.");

        var idea = request.Idea;
        var debateId = store.CreateDebate(idea, request.Agents.Select(a => a.Id), request.Round2, provider.Name, options.Language);
        progress?.Report(new DebateCreated(debateId));

        var run = new RunContext(debateId, idea, progress, ct, Math.Max(1, options.MaxConcurrency));
        DebateStatus status;
        string? message = null;
        try
        {
            // Round 1: every agent alone. No answer is visible to any other agent yet.
            progress?.Report(new PhaseStarted(DebateRound.Round1, request.Agents.Select(a => a.Id).ToList()));
            var round1Runs = await Task.WhenAll(request.Agents.Select(agent =>
            {
                var (system, user) = prompts.Round1(agent, idea);
                return CallAsync(run, agent.Id, agent.Name, DebateRound.Round1, system, user, options.AgentModel);
            }));
            run.ThrowIfStopped();

            var round1 = request.Agents.Zip(round1Runs)
                .Where(p => p.Second is { Status: RunStatus.Done })
                .Select(p => new AgentAnswer(p.First, p.Second!.Answer!))
                .ToList();
            if (round1.Count == 0) throw new StopDebate(DebateStatus.Failed, "No agent answered in round 1.");

            // Round 2: each agent sees the others' round-1 answers and cross-examines them.
            var round2 = new List<AgentAnswer>();
            if (!request.Round2)
            {
                progress?.Report(new PhaseSkipped(DebateRound.Round2, "Round 2 is switched off for this debate."));
            }
            else if (round1.Count < 2)
            {
                progress?.Report(new PhaseSkipped(DebateRound.Round2, "Round 2 needs at least two round-1 answers."));
            }
            else
            {
                progress?.Report(new PhaseStarted(DebateRound.Round2, round1.Select(a => a.Agent.Id).ToList()));
                var round2Runs = await Task.WhenAll(round1.Select(own =>
                {
                    var others = round1.Where(o => o.Agent.Id != own.Agent.Id).ToList();
                    var (system, user) = prompts.Round2(own.Agent, idea, own.Answer, others);
                    return CallAsync(run, own.Agent.Id, own.Agent.Name, DebateRound.Round2, system, user, options.AgentModel);
                }));
                run.ThrowIfStopped();
                round2 = round1.Zip(round2Runs)
                    .Where(p => p.Second is { Status: RunStatus.Done })
                    .Select(p => new AgentAnswer(p.First.Agent, p.Second!.Answer!))
                    .ToList();
            }

            // Synthesis: keeps disagreement visible, gives no verdict.
            progress?.Report(new PhaseStarted(DebateRound.Synthesis, [SynthesizerId]));
            var (sSystem, sUser) = prompts.Synthesis(idea, round1, round2);
            var synthesis = await CallAsync(run, SynthesizerId, SynthesizerName, DebateRound.Synthesis, sSystem, sUser, options.SynthesizerModel);
            run.ThrowIfStopped();

            status = run.AnyFailed || synthesis?.Status != RunStatus.Done ? DebateStatus.CompletedWithErrors : DebateStatus.Completed;
            if (status == DebateStatus.CompletedWithErrors) message = "Some calls failed. Their errors are shown in the agent columns.";
        }
        catch (StopDebate stop)
        {
            status = stop.Status;
            message = stop.Message;
        }

        store.FinishDebate(debateId, status, message);
        progress?.Report(new DebateFinished(debateId, status, message));
        return debateId;
    }

    /// <summary>One agent, round 1, outside any debate. Used by "Test run" on the Agents screen.</summary>
    public async Task<AgentRun> TestAgentAsync(AgentDefinition agent, Idea idea, CancellationToken ct)
    {
        var (system, user) = prompts.Round1(agent, idea);
        // No debate id: a test run belongs to no debate, but a real one is still saved so it counts towards usage.
        var context = new RunContext(null, idea, null, ct, 1);
        var run = await CallAsync(context, agent.Id, agent.Name, DebateRound.Round1, system, user, options.AgentModel);
        context.ThrowIfStopped();
        return run ?? throw new OperationCanceledException(ct);
    }

    private async Task<AgentRun?> CallAsync(RunContext ctx, string agentId, string agentName, DebateRound round,
        string system, string user, string model)
    {
        var token = ctx.Token;
        try
        {
            await ctx.Gate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return null; // never started
        }

        var record = new AgentRun
        {
            DebateId = ctx.DebateId,
            AgentId = agentId,
            AgentName = agentName,
            Round = round,
            SystemPrompt = system,
            UserPrompt = user,
            Model = model,
            Simulated = provider.IsSimulated,
        };
        var sw = Stopwatch.StartNew();
        try
        {
            ctx.Progress?.Report(new CallStarted(agentId, round));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    usage.EnsureCanCall(provider.IsSimulated);
                    var result = await provider.CompleteAsync(new ModelRequest(system, user, model, agentId, round, ctx.Idea.Text), token);
                    record.Answer = result.Text;
                    record.Model = result.Model;
                    record.InputTokens = result.InputTokens;
                    record.OutputTokens = result.OutputTokens;
                    record.CostUsd = result.CostUsd;
                    record.Status = RunStatus.Done;
                    break;
                }
                catch (ProviderException ex) when (ex.IsTransient && attempt < options.MaxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1) + Random.Shared.NextDouble());
                    ctx.Progress?.Report(new CallRetrying(agentId, round, attempt + 1, ex.Message));
                    await Task.Delay(delay, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            record.Status = RunStatus.Cancelled;
            record.Error = ctx.StopReason?.Message ?? "Cancelled.";
        }
        catch (ProviderException ex) when (ex.IsFatal)
        {
            record.Status = RunStatus.Failed;
            record.Error = ex.Message;
            ctx.Stop(new StopDebate(ex.Kind == ProviderErrorKind.CreditExhausted ? DebateStatus.StoppedAtLimit : DebateStatus.Failed, ex.Message));
        }
        catch (UsageLimitReachedException ex)
        {
            record.Status = RunStatus.Failed;
            record.Error = ex.Message;
            ctx.Stop(new StopDebate(DebateStatus.StoppedAtLimit, ex.Message));
        }
        catch (Exception ex)
        {
            record.Status = RunStatus.Failed;
            record.Error = ex.Message;
        }
        finally
        {
            ctx.Gate.Release();
        }

        sw.Stop();
        record.DurationMs = (long)sw.Elapsed.TotalMilliseconds;
        if (record.Status != RunStatus.Done) ctx.AnyFailed = true;
        // Simulated test runs are not worth keeping; real ones are, because they used credit.
        // The call is already paid for, so a failed save must never cost the caller the answer.
        if (ctx.DebateId is not null || !record.Simulated)
        {
            try
            {
                store.SaveRun(record);
            }
            catch (Exception ex)
            {
                record.Error ??= "The answer could not be saved: " + ex.Message;
            }
        }
        ctx.Progress?.Report(new CallFinished(record));
        return record;
    }

    private sealed class StopDebate(DebateStatus status, string message) : Exception(message)
    {
        public DebateStatus Status { get; } = status;
    }

    private sealed class RunContext(long? debateId, Idea idea, IProgress<DebateEvent>? progress, CancellationToken userToken, int concurrency)
    {
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(userToken);

        public long? DebateId { get; } = debateId;
        public Idea Idea { get; } = idea;
        public IProgress<DebateEvent>? Progress { get; } = progress;
        public SemaphoreSlim Gate { get; } = new(concurrency);
        public CancellationToken Token => _stop.Token;
        public StopDebate? StopReason { get; private set; }
        public bool AnyFailed { get; set; }

        public void Stop(StopDebate reason)
        {
            lock (this) StopReason ??= reason;
            _stop.Cancel();
        }

        public void ThrowIfStopped()
        {
            if (StopReason is { } reason) throw reason;
            if (userToken.IsCancellationRequested) throw new StopDebate(DebateStatus.Cancelled, "Cancelled by you. Answers that finished are kept.");
        }
    }
}
