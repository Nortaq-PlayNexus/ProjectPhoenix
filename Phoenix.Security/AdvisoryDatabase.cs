using Phoenix.Core;

namespace Phoenix.Security;

/// <summary>
/// Offline known-vulnerability advisories (a minimal, bundled stand-in for OSV/etc.).
/// Each advisory flags a version range known to be vulnerable and the fixed version.
/// </summary>
public sealed record Advisory(string Package, string Ecosystem, string VulnerableRange, string FixedIn, string Id, string Title);

public static class AdvisoryDatabase
{
    // Deliberately small but real historical examples so offline scanning has signal.
    public static readonly IReadOnlyList<Advisory> Advisories = new List<Advisory>
    {
        new("django", "python", "<2.2.28", "2.2.28", "CVE-2022-28346", "SQL injection in Django <2.2.28"),
        new("django", "python", "<3.2.13", "3.2.13", "CVE-2022-28346", "SQL injection in Django <3.2.13"),
        new("urllib3", "python", "<1.26.5", "1.26.5", "CVE-2021-33503", "Denial of service in urllib3 <1.26.5"),
        new("requests", "python", "<2.20.0", "2.20.0", "CVE-2018-18074", "Credentials leaked via redirect in requests <2.20.0"),
        new("lodash", "node", "<4.17.21", "4.17.21", "CVE-2020-8203", "Prototype pollution in lodash <4.17.21"),
        new("minimist", "node", "<1.2.6", "1.2.6", "CVE-2021-44906", "Prototype pollution in minimist <1.2.6"),
        new("flask", "python", "<2.2.5", "2.2.5", "CVE-2023-30861", "Cookie deserialization in Flask <2.2.5"),
        new("serde", "rust", "<1.0.118", "1.0.118", "RUSTSEC-2020-0031", "RCE via flatc false dependency in serde <1.0.118")
    };

    public static bool IsVulnerable(string version, string vulnerableRange)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        if (!vulnerableRange.StartsWith('<')) return false;
        var upper = vulnerableRange[1..].Trim();
        // Major-version gate: a range like <3.2.13 only applies within the 3.x line.
        if (Major(version) != Major(upper)) return false;
        return Compare(version, upper) < 0;
    }

    public static int Major(string v)
    {
        var clean = new string(v.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        var first = clean.Split('.')[0];
        return int.TryParse(first, out var m) ? m : -1;
    }

    private static int Compare(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] Parse(string v)
    {
        var clean = new string(v.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return clean.Split('.').Where(s => s.Length > 0).Select(int.Parse).ToArray();
    }
}
