using Phoenix.Archive;
using Phoenix.Archaeology;
using Phoenix.Build;
using Phoenix.Core;
using Phoenix.Dependency;
using Phoenix.Environment;
using Phoenix.Repair;
using Phoenix.Security;

namespace Phoenix.Agents;

public sealed record ResurrectionOutcome(
    long ProjectId,
    string ProjectName,
    string? Language,
    string? BuildSystem,
    IReadOnlyList<string> Ecosystems,
    int DependencyCount,
    int SecurityFindingCount,
    IReadOnlyList<EnvironmentFinding> Environment,
    InsuranceStatus Insurance,
    RecoveryStatus Recovery,
    string CapsulePath,
    string SnapshotPath,
    double Confidence);

public sealed class ResurrectionContext
{
    public long ProjectId;
    public string SourcePath = "";
    public string SnapshotPath = "";
    public string SnapshotHash = "";
    public ArchaeologyProfile? Profile;
    public List<DependencyRecord> Dependencies = new();
    public ReconstructedEnvironment? Environment;
    public InsuranceStatus Insurance = InsuranceStatus.Uninsured;
    public List<SecurityFinding> Security = new();
    public RecoveryStatus Recovery = RecoveryStatus.Unresolved;
    public BuildResult? LastBuild;
    public string? CapsulePath;
    public bool AllowBuild;
    public string? RepairAttemptPath;
    public PhoenixDb Db = null!;
    public ILogger Log = null!;
}

public interface IAgent
{
    string Role { get; }
    string Name { get; }
    void Execute(ResurrectionContext ctx);
}

public sealed class Coordinator
{
    private readonly List<IAgent> _agents = new();

    public Coordinator Add(IAgent agent) { _agents.Add(agent); return this; }

    public ResurrectionContext Run(ResurrectionContext ctx)
    {
        foreach (var agent in _agents)
        {
            ctx.Log.Info($"[AGENT] {agent.Role} :: {agent.Name}");
            try { agent.Execute(ctx); }
            catch (Exception ex)
            {
                ctx.Log.Warn($"Agent {agent.Name} failed: {ex.Message}");
                ctx.Db.InsertEvent(ctx.ProjectId, "agent-error", agent.Role, agent.Name, null, null, "error: " + ex.Message);
            }
        }
        return ctx;
    }

    public static ResurrectionOutcome Summarize(ResurrectionContext ctx) => new(
        ctx.ProjectId, ctx.Profile?.ProjectName ?? Path.GetFileName(ctx.SnapshotPath),
        ctx.Profile?.DetectedLanguage, ctx.Profile?.BuildSystem,
        ctx.Profile?.Ecosystems ?? Array.Empty<string>(), ctx.Dependencies.Count, ctx.Security.Count,
        ctx.Environment?.Findings ?? Array.Empty<EnvironmentFinding>(), ctx.Insurance, ctx.Recovery,
        ctx.CapsulePath ?? "", ctx.SnapshotPath, ctx.Profile?.Confidence ?? 0);
}

// ---- Specialized agents (spec #25) ----

public sealed class ArchaeologistAgent : IAgent
{
    public string Role => "Archaeologist";
    public string Name => "forensic-identify";
    public void Execute(ResurrectionContext ctx)
    {
        ctx.Profile = new ArchaeologyEngine().Analyze(ctx.SnapshotPath);
        if (ctx.Profile != null && !string.IsNullOrEmpty(ctx.SourcePath))
            ctx.Profile = ctx.Profile with { ProjectName = Path.GetFileName(ctx.SourcePath.TrimEnd(Path.DirectorySeparatorChar)) };
        ctx.Db.InsertEvent(ctx.ProjectId, "identify", Role, Name, ctx.SnapshotHash, null, "complete");
    }
}

public sealed class DependencyAnalystAgent : IAgent
{
    public string Role => "Dependency Analyst";
    public string Name => "map-graph";
    public void Execute(ResurrectionContext ctx)
    {
        if (ctx.Profile == null) return;
        ctx.Db.DeleteDependencies(ctx.ProjectId); // idempotent: clear prior mapping for this project
        foreach (var m in ctx.Profile.Manifests)
            foreach (var dep in m.Dependencies)
            {
                var risk = Score(dep, m.Ecosystem);
                ctx.Db.InsertDependency(ctx.ProjectId, dep.Name, dep.Version, dep.Ecosystem, dep.Scope,
                    DependencyState.Unknown, null, null, null, risk, "pending", "pending");
            }
        ctx.Dependencies = ctx.Db.GetDependencies(ctx.ProjectId).ToList();
    }
    private static int Score(RawDependency dep, string eco)
    {
        var s = 30;
        if (dep.Version == null) s += 20;
        if (dep.Scope == "system") s += 10;
        if (eco is "python" or "node") s += 5;
        return Math.Min(100, s);
    }
}

public sealed class EnvironmentEngineerAgent : IAgent
{
    public string Role => "Environment Engineer";
    public string Name => "reconstruct";
    public void Execute(ResurrectionContext ctx)
    {
        if (ctx.Profile == null) return;
        ctx.Environment = new EnvironmentReconstructionEngine().Reconstruct(ctx.SnapshotPath);
        var e = ctx.Environment;
        ctx.Db.InsertEnvironment(ctx.ProjectId, e.RecommendedOs, e.RecommendedArchitecture, e.RecommendedRuntime,
            e.RecommendedCompiler, null, ctx.Profile.BuildSystem, e.RecommendedRuntime);
    }
}

public sealed class SecurityEngineerAgent : IAgent
{
    public string Role => "Security Engineer";
    public string Name => "supply-chain-scan";
    public void Execute(ResurrectionContext ctx)
    {
        var findings = new SecurityEngine().Analyze(ctx.Dependencies);
        ctx.Security = findings.ToList();
        foreach (var f in findings.Where(f => f.Severity == "HIGH"))
        {
            var hit = ctx.Dependencies.FirstOrDefault(d => d.Name == f.DependencyName && d.Ecosystem == f.Ecosystem);
            if (hit != null) ctx.Db.UpdateDependencyState(hit.Id, DependencyState.Vulnerable, 90);
        }
        ctx.Db.InsertEvent(ctx.ProjectId, "security-scan", Role, Name, ctx.SnapshotHash, null,
            findings.Any(f => f.Severity == "HIGH") ? "suspicious" : "clean");
    }
}

public sealed class BuildEngineerAgent : IAgent
{
    public string Role => "Build Engineer";
    public string Name => "sandboxed-build";
    public void Execute(ResurrectionContext ctx)
    {
        if (!ctx.AllowBuild || ctx.Profile == null) return;
        var runner = new BuildRunner(ctx.Log);
        var result = runner.Run(ctx.Profile.BuildSystem ?? "generic", ctx.SnapshotPath, allowExecution: true);
        ctx.LastBuild = result;
        ctx.Db.InsertBuild(ctx.ProjectId, 1, DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks,
            result.Executed ? result.ExitCode : -1, result.Logs);
        ctx.Db.InsertEvent(ctx.ProjectId, "sandboxed-build", Role, Name, ctx.SnapshotHash,
            Crypto.Sha256(result.Logs), result.Executed ? "executed" : "simulated");
        ctx.Db.InsertRecoveryTest(ctx.ProjectId, null, "build",
            result.Executed ? (result.ExitCode == 0 ? "PASS" : "FAIL") : "SKIPPED");
    }
}

public sealed class JudgeAgentWrapper : IAgent
{
    public string Role => "Judge";
    public string Name => "independent-evaluation";
    public void Execute(ResurrectionContext ctx)
    {
        // The Judge runs AFTER build; its verdict is authoritative and cannot be
        // overridden by the repair/build agents (spec #26).
        var buildRan = ctx.AllowBuild && ctx.LastBuild != null;
        var status = buildRan
            ? new JudgeAgent().Evaluate(ctx.LastBuild!, ctx.LastBuild!.PlannedCommands.Any(c => c.Contains("test")))
            : RecoveryStatus.Unresolved;
        ctx.Recovery = status;
        ctx.Db.UpdateProjectStatus(ctx.ProjectId, new JudgeAgent().Verdict(status));
    }
}

public sealed class RepairEngineerAgent : IAgent
{
    public string Role => "Repair Engineer";
    public string Name => "propose-repairs";
    public void Execute(ResurrectionContext ctx)
    {
        if (ctx.Profile == null) return;
        var proposals = new RepairEngine().Propose(ctx.SnapshotPath, ctx.Profile.DetectedLanguage,
            ctx.Profile.BuildSystem, ctx.Security, ctx.Environment?.Findings ?? Array.Empty<EnvironmentFinding>());
        var recoveryRoot = System.Environment.GetEnvironmentVariable("PHOENIX_RECOVERY_ROOT")
            ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "Phoenix", "Recovery");
        ctx.RepairAttemptPath = new RepairStore().CreateAttempt(recoveryRoot, ctx.Profile.ProjectName, ctx.SnapshotPath, proposals);
        var mods = new RepairEngine().Apply(ctx.RepairAttemptPath, ctx.SnapshotPath, proposals);
        new RepairStore().RecordModifications(ctx.RepairAttemptPath, proposals, mods);
        foreach (var p in proposals)
            ctx.Log.Info($"[REPAIR] {p.Id}: {p.Reason} -> {p.SuggestedAction}");
        if (mods.Count > 0)
            ctx.Log.Info($"[REPAIR] applied {mods.Count} file modification(s) in {ctx.RepairAttemptPath}");
        ctx.Db.InsertEvent(ctx.ProjectId, "repair", Role, Name, ctx.SnapshotHash, null,
            proposals.Any(p => p.Status == "Proposed") ? "applied" : "no-action");
    }
}

public sealed class ArchivistAgent : IAgent
{
    public string Role => "Archivist";
    public string Name => "capsule";
    public void Execute(ResurrectionContext ctx)
    {
        if (ctx.Profile == null) return;
        var input = new CapsuleBuilder.CapsuleInput(
            ctx.ProjectId, ctx.Profile.ProjectName, ctx.SnapshotPath, ctx.Profile.Ecosystems.FirstOrDefault(),
            ctx.Profile.BuildSystem, ctx.Profile.DetectedLanguage, ctx.Dependencies, ctx.Profile.Notes,
            new JudgeAgent().Verdict(ctx.Recovery), ctx.Profile.Confidence, ctx.Profile.EarliestTimestamp, ctx.Profile.LatestTimestamp);
        var capsuleRoot = System.Environment.GetEnvironmentVariable("PHOENIX_CAPSULE_ROOT")
            ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "Phoenix", "Capsules");
        ctx.CapsulePath = new CapsuleBuilder().Build(input, capsuleRoot);
        ctx.Db.InsertCapsule(ctx.ProjectId, ctx.CapsulePath,
            Crypto.Sha256File(Path.Combine(ctx.CapsulePath, "metadata", "phoenix.manifest.json")),
            new JudgeAgent().Verdict(ctx.Recovery));
        WriteReports(ctx);
        ctx.Db.InsertEvent(ctx.ProjectId, "capsule", Role, Name, ctx.SnapshotHash, null, "complete");
    }

    private static void WriteReports(ResurrectionContext ctx)
    {
        if (ctx.CapsulePath == null || ctx.Profile == null) return;
        var meta = Path.Combine(ctx.CapsulePath, "metadata");
        var verdict = new JudgeAgent().Verdict(ctx.Recovery);

        var forensics = new System.Text.StringBuilder();
        forensics.AppendLine($"# PROJECT FORENSICS — {ctx.Profile.ProjectName}");
        forensics.AppendLine();
        forensics.AppendLine($"- Detected language: {ctx.Profile.DetectedLanguage ?? "unknown"}");
        forensics.AppendLine($"- Build system: {ctx.Profile.BuildSystem ?? "unknown"}");
        forensics.AppendLine($"- Ecosystems: {string.Join(", ", ctx.Profile.Ecosystems)}");
        forensics.AppendLine($"- Confidence: {ctx.Profile.Confidence:P0}");
        forensics.AppendLine($"- Files inventoried: {ctx.Profile.FileCount}");
        forensics.AppendLine($"- Earliest evidence: {ctx.Profile.EarliestTimestamp:yyyy-MM-dd}");
        forensics.AppendLine($"- Latest evidence: {ctx.Profile.LatestTimestamp:yyyy-MM-dd}");
        forensics.AppendLine();
        forensics.AppendLine("## Dependencies");
        foreach (var d in ctx.Dependencies)
            forensics.AppendLine($"- {d.Name} {d.Version ?? "(any)"} [{d.Ecosystem}] state={d.State}");
        forensics.AppendLine();
        forensics.AppendLine("## Notes");
        foreach (var n in ctx.Profile.Notes) forensics.AppendLine($"- {n}");
        File.WriteAllText(Path.Combine(meta, "PROJECT_FORENSICS.md"), forensics.ToString());

        var report = new System.Text.StringBuilder();
        report.AppendLine($"# RESURRECTION REPORT — {ctx.Profile.ProjectName}");
        report.AppendLine();
        report.AppendLine($"- Recovery status: **{verdict}**");
        report.AppendLine($"- Environment reconstructed: {ctx.Environment?.Findings.Count ?? 0} finding(s)");
        report.AppendLine($"- Dependencies recovered: {ctx.Dependencies.Count}");
        report.AppendLine($"- Security findings: {ctx.Security.Count}");
        if (ctx.Security.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Problems discovered");
            foreach (var s in ctx.Security) report.AppendLine($"- [{s.Severity}] {s.Kind}: {s.DependencyName} — {s.Detail}");
        }
        if (ctx.Environment != null)
        {
            report.AppendLine();
            report.AppendLine("## Environment reconstructed");
            foreach (var f in ctx.Environment.Findings) report.AppendLine($"- {f.Kind}: {f.Value} (conf {f.Confidence:P0}) [{f.Evidence}]");
        }
        File.WriteAllText(Path.Combine(meta, "RESURRECTION_REPORT.md"), report.ToString());
        HtmlReport.Write(ctx);
    }
}
