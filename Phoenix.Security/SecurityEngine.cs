using Phoenix.Core;

namespace Phoenix.Security;

public sealed record SecurityFinding(
    string DependencyName,
    string Ecosystem,
    string Kind,
    string Severity,
    string Detail);

public sealed class SecurityEngine
{
    // Small curated allowlists used as typosquat baselines. In production these
    // are sourced from registry intelligence feeds (OSV, etc.).
    private static readonly Dictionary<string, HashSet<string>> Popular = new()
    {
        ["python"] = new() { "django", "flask", "requests", "numpy", "pandas", "pytest", "sqlalchemy", "click", "urllib3", "boto3" },
        ["node"] = new() { "react", "vue", "axios", "lodash", "express", "webpack", "jest", "chalk", "commander", "moment" },
        ["rust"] = new() { "serde", "tokio", "rand", "clap", "log", "regex", "hyper", "anyhow" },
        ["go"] = new() { "gin", "cobra", "uuid", "viper", "zap" },
        ["php"] = new() { "monolog", "symfony", "laravel", "guzzle" },
        ["ruby"] = new() { "rails", "rspec", "devise", "nokogiri" }
    };

    public IReadOnlyList<SecurityFinding> Analyze(IReadOnlyList<DependencyRecord> deps)
    {
        var allPopular = new HashSet<string>(Popular.Values.SelectMany(x => x), StringComparer.OrdinalIgnoreCase);
        var findings = new List<SecurityFinding>();
        foreach (var d in deps)
        {
            if (d.Scope == "system") continue;

            // Known-vulnerability advisories (offline bundle; stand-in for OSV).
            // Checked for every package, including popular ones.
            foreach (var adv in AdvisoryDatabase.Advisories)
            {
                if (!adv.Package.Equals(d.Name, StringComparison.OrdinalIgnoreCase)
                    || adv.Ecosystem != d.Ecosystem
                    || string.IsNullOrEmpty(d.Version))
                    continue;
                if (AdvisoryDatabase.IsVulnerable(d.Version, adv.VulnerableRange))
                    findings.Add(new SecurityFinding(d.Name, d.Ecosystem, "vulnerability", "HIGH",
                        $"{adv.Id}: {adv.Title} (fixed in {adv.FixedIn})."));
            }

            // Typosquat heuristic: skip known-popular packages, then flag small
            // edit-distance names to ANY popular package (cross-ecosystem mimicry).
            if (Popular.TryGetValue(d.Ecosystem, out var known) && known.Contains(d.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            foreach (var popular in allPopular)
            {
                var dist = Levenshtein(d.Name.ToLowerInvariant(), popular);
                if (dist is >= 1 and <= 2 && d.Name.Length >= 4)
                {
                    findings.Add(new SecurityFinding(d.Name, d.Ecosystem, "typosquat", "HIGH",
                        $"Name is {dist} edit(s) from popular package '{popular}'."));
                    break;
                }
            }

            if (d.License != null && d.License.Contains("GPL", StringComparison.OrdinalIgnoreCase) && d.Ecosystem == "node")
                findings.Add(new SecurityFinding(d.Name, d.Ecosystem, "license", "LOW",
                    $"Copyleft license detected: {d.License}."));
        }
        return findings;
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length; var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[n, m];
    }
}
