using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DecisionCouncil.Core.Storage;

/// <summary>
/// Local SQLite database: ideas, debates, agent runs and decisions.
/// Calls are serialised with a lock because round 1 saves runs from several threads at once.
/// </summary>
public sealed class CouncilStore
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public string DatabasePath { get; }

    public CouncilStore(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = true }.ToString();
        Migrate();
    }

    private void Migrate()
    {
        Exec("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS ideas (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                text TEXT NOT NULL,
                extra_context TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS debates (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                idea_id INTEGER NOT NULL REFERENCES ideas(id),
                started_at TEXT NOT NULL,
                finished_at TEXT,
                status TEXT NOT NULL,
                status_message TEXT,
                round2_enabled INTEGER NOT NULL,
                provider TEXT NOT NULL,
                language TEXT NOT NULL,
                agent_ids TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS agent_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                debate_id INTEGER REFERENCES debates(id),
                agent_id TEXT NOT NULL,
                agent_name TEXT NOT NULL,
                round INTEGER NOT NULL,
                system_prompt TEXT NOT NULL,
                user_prompt TEXT NOT NULL,
                answer TEXT,
                model TEXT NOT NULL,
                input_tokens INTEGER,
                output_tokens INTEGER,
                cost_usd TEXT,
                duration_ms INTEGER NOT NULL,
                status TEXT NOT NULL,
                error TEXT,
                simulated INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_runs_debate ON agent_runs(debate_id);
            CREATE INDEX IF NOT EXISTS ix_runs_created ON agent_runs(created_at);
            CREATE TABLE IF NOT EXISTS decisions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                debate_id INTEGER NOT NULL UNIQUE REFERENCES debates(id),
                choice TEXT NOT NULL,
                reason TEXT NOT NULL,
                decided_at TEXT NOT NULL,
                revisit_on TEXT,
                outcome TEXT,
                warnings_came_true TEXT NOT NULL DEFAULT '',
                outcome_logged_at TEXT
            );
            """);
        AllowRunsWithoutDebate();
    }

    /// <summary>
    /// Databases from 1.0.x declared agent_runs.debate_id NOT NULL, so a test run (no debate) could not be saved.
    /// SQLite cannot drop a NOT NULL constraint in place, so the table is rebuilt once with the same columns.
    /// </summary>
    private void AllowRunsWithoutDebate()
    {
        lock (_gate)
        {
            using var c = Open();
            using (var info = c.CreateCommand())
            {
                info.CommandText = "SELECT \"notnull\" FROM pragma_table_info('agent_runs') WHERE name = 'debate_id'";
                if (Convert.ToInt64(info.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return;
            }

            using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE agent_runs_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    debate_id INTEGER REFERENCES debates(id),
                    agent_id TEXT NOT NULL,
                    agent_name TEXT NOT NULL,
                    round INTEGER NOT NULL,
                    system_prompt TEXT NOT NULL,
                    user_prompt TEXT NOT NULL,
                    answer TEXT,
                    model TEXT NOT NULL,
                    input_tokens INTEGER,
                    output_tokens INTEGER,
                    cost_usd TEXT,
                    duration_ms INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    error TEXT,
                    simulated INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );
                INSERT INTO agent_runs_new (id, debate_id, agent_id, agent_name, round, system_prompt, user_prompt, answer, model,
                    input_tokens, output_tokens, cost_usd, duration_ms, status, error, simulated, created_at)
                SELECT id, NULLIF(debate_id, 0), agent_id, agent_name, round, system_prompt, user_prompt, answer, model,
                    input_tokens, output_tokens, cost_usd, duration_ms, status, error, simulated, created_at
                FROM agent_runs;
                DROP TABLE agent_runs;
                ALTER TABLE agent_runs_new RENAME TO agent_runs;
                CREATE INDEX IF NOT EXISTS ix_runs_debate ON agent_runs(debate_id);
                CREATE INDEX IF NOT EXISTS ix_runs_created ON agent_runs(created_at);
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    // ---- writes ----

    public long CreateDebate(Idea idea, IEnumerable<string> agentIds, bool round2, string provider, string language)
    {
        lock (_gate)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            idea.CreatedAt = DateTime.Now;
            idea.Id = Scalar(c, tx, "INSERT INTO ideas (title, text, extra_context, created_at) VALUES ($t, $x, $e, $c); SELECT last_insert_rowid();",
                ("$t", idea.Title), ("$x", idea.Text), ("$e", idea.ExtraContext), ("$c", Iso(idea.CreatedAt)));
            var id = Scalar(c, tx, """
                INSERT INTO debates (idea_id, started_at, status, round2_enabled, provider, language, agent_ids)
                VALUES ($i, $s, $st, $r2, $p, $l, $a); SELECT last_insert_rowid();
                """,
                ("$i", idea.Id), ("$s", Iso(DateTime.Now)), ("$st", DebateStatus.Running.ToString()), ("$r2", round2 ? 1 : 0),
                ("$p", provider), ("$l", language), ("$a", string.Join(',', agentIds)));
            tx.Commit();
            return id;
        }
    }

    public void SaveRun(AgentRun run)
    {
        lock (_gate)
        {
            using var c = Open();
            run.CreatedAt = DateTime.Now;
            run.Id = Scalar(c, null, """
                INSERT INTO agent_runs (debate_id, agent_id, agent_name, round, system_prompt, user_prompt, answer, model,
                    input_tokens, output_tokens, cost_usd, duration_ms, status, error, simulated, created_at)
                VALUES ($d, $a, $n, $r, $sp, $up, $ans, $m, $it, $ot, $cost, $dur, $st, $err, $sim, $c);
                SELECT last_insert_rowid();
                """,
                ("$d", run.DebateId), ("$a", run.AgentId), ("$n", run.AgentName), ("$r", (int)run.Round),
                ("$sp", run.SystemPrompt), ("$up", run.UserPrompt), ("$ans", run.Answer), ("$m", run.Model),
                ("$it", run.InputTokens), ("$ot", run.OutputTokens),
                ("$cost", run.CostUsd?.ToString(CultureInfo.InvariantCulture)), ("$dur", run.DurationMs),
                ("$st", run.Status.ToString()), ("$err", run.Error), ("$sim", run.Simulated ? 1 : 0), ("$c", Iso(run.CreatedAt)));
        }
    }

    public void FinishDebate(long debateId, DebateStatus status, string? message)
    {
        lock (_gate)
        {
            using var c = Open();
            Scalar(c, null, "UPDATE debates SET status = $s, status_message = $m, finished_at = $f WHERE id = $id; SELECT 0;",
                ("$s", status.ToString()), ("$m", message), ("$f", Iso(DateTime.Now)), ("$id", debateId));
        }
    }

    public void SaveDecision(Decision d)
    {
        lock (_gate)
        {
            using var c = Open();
            d.Id = Scalar(c, null, """
                INSERT INTO decisions (debate_id, choice, reason, decided_at, revisit_on)
                VALUES ($d, $c, $r, $at, $rv)
                ON CONFLICT(debate_id) DO UPDATE SET choice = excluded.choice, reason = excluded.reason,
                    decided_at = excluded.decided_at, revisit_on = excluded.revisit_on;
                SELECT id FROM decisions WHERE debate_id = $d;
                """,
                ("$d", d.DebateId), ("$c", d.Choice), ("$r", d.Reason), ("$at", Iso(d.DecidedAt)),
                ("$rv", d.RevisitOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
    }

    public void LogOutcome(long debateId, string outcome, IEnumerable<string> warningsCameTrue)
    {
        lock (_gate)
        {
            using var c = Open();
            Scalar(c, null, "UPDATE decisions SET outcome = $o, warnings_came_true = $w, outcome_logged_at = $at WHERE debate_id = $d; SELECT 0;",
                ("$o", outcome), ("$w", string.Join(',', warningsCameTrue)), ("$at", Iso(DateTime.Now)), ("$d", debateId));
        }
    }

    /// <summary>Debates left "Running" by a crash or a closed window are marked cancelled on start-up.</summary>
    public int CloseAbandonedDebates()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE debates SET status = $s, status_message = 'The app closed before the debate finished.', finished_at = $f WHERE status = $r";
            cmd.Parameters.AddWithValue("$s", DebateStatus.Cancelled.ToString());
            cmd.Parameters.AddWithValue("$f", Iso(DateTime.Now));
            cmd.Parameters.AddWithValue("$r", DebateStatus.Running.ToString());
            return cmd.ExecuteNonQuery();
        }
    }

    // ---- reads ----

    public List<DebateSummary> ListDebates()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                SELECT d.id, i.title, d.started_at, d.status, d.provider, d.agent_ids,
                       x.id, x.choice, x.reason, x.decided_at, x.revisit_on, x.outcome, x.warnings_came_true, x.outcome_logged_at
                FROM debates d JOIN ideas i ON i.id = d.idea_id
                LEFT JOIN decisions x ON x.debate_id = d.id
                ORDER BY d.started_at DESC
                """;
            using var r = cmd.ExecuteReader();
            var list = new List<DebateSummary>();
            while (r.Read())
            {
                list.Add(new DebateSummary
                {
                    DebateId = r.GetInt64(0),
                    IdeaTitle = r.GetString(1),
                    StartedAt = ParseIso(r.GetString(2)),
                    Status = Enum.Parse<DebateStatus>(r.GetString(3)),
                    Provider = r.GetString(4),
                    AgentCount = r.GetString(5).Split(',', StringSplitOptions.RemoveEmptyEntries).Length,
                    Decision = r.IsDBNull(6) ? null : ReadDecision(r, 6, r.GetInt64(0)),
                });
            }
            return list;
        }
    }

    public DebateDetail? GetDebate(long debateId)
    {
        lock (_gate)
        {
            using var c = Open();
            DebateDetail? d = null;
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT d.id, d.started_at, d.finished_at, d.status, d.status_message, d.round2_enabled, d.provider, d.language, d.agent_ids,
                           i.id, i.title, i.text, i.extra_context, i.created_at,
                           x.id, x.choice, x.reason, x.decided_at, x.revisit_on, x.outcome, x.warnings_came_true, x.outcome_logged_at
                    FROM debates d JOIN ideas i ON i.id = d.idea_id
                    LEFT JOIN decisions x ON x.debate_id = d.id
                    WHERE d.id = $id
                    """;
                cmd.Parameters.AddWithValue("$id", debateId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                d = new DebateDetail
                {
                    Id = r.GetInt64(0),
                    StartedAt = ParseIso(r.GetString(1)),
                    FinishedAt = r.IsDBNull(2) ? null : ParseIso(r.GetString(2)),
                    Status = Enum.Parse<DebateStatus>(r.GetString(3)),
                    StatusMessage = r.IsDBNull(4) ? null : r.GetString(4),
                    Round2Enabled = r.GetInt64(5) == 1,
                    Provider = r.GetString(6),
                    Language = r.GetString(7),
                    AgentIds = r.GetString(8).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Idea = new Idea
                    {
                        Id = r.GetInt64(9),
                        Title = r.GetString(10),
                        Text = r.GetString(11),
                        ExtraContext = r.GetString(12),
                        CreatedAt = ParseIso(r.GetString(13)),
                    },
                    Decision = r.IsDBNull(14) ? null : ReadDecision(r, 14, debateId),
                };
            }

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT id, agent_id, agent_name, round, system_prompt, user_prompt, answer, model, input_tokens, output_tokens,
                           cost_usd, duration_ms, status, error, simulated, created_at
                    FROM agent_runs WHERE debate_id = $id ORDER BY round, id
                    """;
                cmd.Parameters.AddWithValue("$id", debateId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    d.Runs.Add(new AgentRun
                    {
                        Id = r.GetInt64(0),
                        DebateId = debateId,
                        AgentId = r.GetString(1),
                        AgentName = r.GetString(2),
                        Round = (DebateRound)r.GetInt32(3),
                        SystemPrompt = r.GetString(4),
                        UserPrompt = r.GetString(5),
                        Answer = r.IsDBNull(6) ? null : r.GetString(6),
                        Model = r.GetString(7),
                        InputTokens = r.IsDBNull(8) ? null : r.GetInt32(8),
                        OutputTokens = r.IsDBNull(9) ? null : r.GetInt32(9),
                        CostUsd = r.IsDBNull(10) ? null : decimal.Parse(r.GetString(10), CultureInfo.InvariantCulture),
                        DurationMs = r.GetInt64(11),
                        Status = Enum.Parse<RunStatus>(r.GetString(12)),
                        Error = r.IsDBNull(13) ? null : r.GetString(13),
                        Simulated = r.GetInt64(14) == 1,
                        CreatedAt = ParseIso(r.GetString(15)),
                    });
                }
            }
            return d;
        }
    }

    /// <summary>Real (non-simulated) usage in the calendar month that contains <paramref name="when"/>.</summary>
    public UsageTotals MonthlyUsage(DateTime when)
    {
        var from = new DateTime(when.Year, when.Month, 1);
        var to = from.AddMonths(1);
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                SELECT input_tokens, output_tokens, cost_usd FROM agent_runs
                WHERE simulated = 0 AND created_at >= $f AND created_at < $t
                """;
            cmd.Parameters.AddWithValue("$f", Iso(from));
            cmd.Parameters.AddWithValue("$t", Iso(to));
            using var r = cmd.ExecuteReader();
            int calls = 0; long tokens = 0; decimal cost = 0;
            while (r.Read())
            {
                calls++;
                tokens += (r.IsDBNull(0) ? 0 : r.GetInt64(0)) + (r.IsDBNull(1) ? 0 : r.GetInt64(1));
                if (!r.IsDBNull(2)) cost += decimal.Parse(r.GetString(2), CultureInfo.InvariantCulture);
            }
            return new UsageTotals(calls, tokens, cost);
        }
    }

    // ---- helpers ----

    private static Decision ReadDecision(SqliteDataReader r, int o, long debateId) => new()
    {
        Id = r.GetInt64(o),
        DebateId = debateId,
        Choice = r.GetString(o + 1),
        Reason = r.GetString(o + 2),
        DecidedAt = ParseIso(r.GetString(o + 3)),
        RevisitOn = r.IsDBNull(o + 4) ? null : DateOnly.ParseExact(r.GetString(o + 4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Outcome = r.IsDBNull(o + 5) ? null : r.GetString(o + 5),
        WarningsCameTrue = r.GetString(o + 6).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        OutcomeLoggedAt = r.IsDBNull(o + 7) ? null : ParseIso(r.GetString(o + 7)),
    };

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Exec(string sql)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static long Scalar(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static string Iso(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
    private static DateTime ParseIso(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture);
}
