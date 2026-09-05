using Phoenix.Core;

namespace Phoenix.Recovery;

public sealed record ScenarioResult(string Scenario, double SurvivalPct, int Recoverable, int Total);

public sealed class DisasterSimulator
{
    public IReadOnlyList<ScenarioResult> Simulate(IReadOnlyList<DependencyRecord> deps, InsuranceStatus insurance)
    {
        var results = new List<ScenarioResult>();
        var total = deps.Count == 0 ? 1 : deps.Count;

        bool IsRecoverable(DependencyRecord d) =>
            d.Scope == "system"
            || !string.IsNullOrEmpty(d.Version)
            || d.State is DependencyState.Healthy or DependencyState.Aging
            || d.LocalCacheStatus == "stored" || d.ArchiveStatus == "stored";

        // A dependency is "lost" only if it is unavailable offline (not cached/archived
        // and not a system dependency that ships with the environment).
        int Recoverable(Func<DependencyRecord, bool> lost) =>
            deps.Count(d => !lost(d) && IsRecoverable(d));

        results.Add(Scenario("Everything available", _ => false));
        results.Add(Scenario("Internet / all registries down", d => d.Scope != "system"
            && d.LocalCacheStatus != "stored" && d.ArchiveStatus != "stored"));
        results.Add(Scenario("Local source destroyed", _ => false));
        foreach (var eco in deps.Select(d => d.Ecosystem).Distinct())
            results.Add(Scenario($"Registry down: {eco}", d => d.Ecosystem == eco && d.Scope != "system"
                && d.LocalCacheStatus != "stored" && d.ArchiveStatus != "stored"));

        ScenarioResult Scenario(string name, Func<DependencyRecord, bool> lost)
            => new ScenarioResult(name, Math.Round(100.0 * Recoverable(lost) / total, 1), Recoverable(lost), total);
        return results;
    }
}

public sealed record CascadeResult(string Dependency, int AffectedProjects, IReadOnlyList<string> Projects);

public sealed class CascadeSimulator
{
    private readonly PhoenixDb _db;
    public CascadeSimulator(PhoenixDb db) => _db = db;

    public CascadeResult Simulate(string dependencyName)
    {
        var rows = _db.GetAllDependenciesWithProject();
        var affected = rows.Where(r => r.Dependency.Equals(dependencyName, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Project).Distinct().ToList();
        return new CascadeResult(dependencyName, affected.Count, affected);
    }
}
