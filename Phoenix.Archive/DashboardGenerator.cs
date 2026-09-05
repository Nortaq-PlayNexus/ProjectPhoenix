using System.Text;
using Phoenix.Core;

namespace Phoenix.Archive;

/// <summary>Spec #44/#45 — aggregate a static HTML operations dashboard from the database.</summary>
public static class DashboardGenerator
{
    public static string Generate(PhoenixDb db, string outputPath)
    {
        var total = db.CountProjects();
        var fully = db.CountProjectsByStatus("FULLY RESURRECTED");
        var buildable = db.CountProjectsByStatus("BUILDS BUT DOES NOT RUN") + fully;
        var deps = db.CountDependencies();
        var recoverable = db.CountDependenciesWithVersion();
        var vuln = db.CountDependenciesByState(DependencyState.Vulnerable);
        var tested = db.CountRecoveryTestsByResult("PASS");

        var b = new StringBuilder();
        b.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>Phoenix Dashboard</title>");
        b.AppendLine("<style>body{font-family:Segoe UI,Arial;margin:2rem;color:#222}h1{color:#0a4d8c}.cards{display:flex;gap:1rem;flex-wrap:wrap;margin:1rem 0}.card{background:#eef4fb;border:1px solid #cfe0f3;border-radius:.5rem;padding:1rem 1.4rem;min-width:120px}.card .n{font-size:1.8rem;font-weight:700;color:#0a4d8c}.card .l{color:#555;font-size:.85rem}table{border-collapse:collapse;width:100%;margin-top:1rem}td,th{border:1px solid #ccc;padding:.4rem .6rem;text-align:left}th{background:#eef4fb}code{background:#f4f4f4;padding:0 .3rem}.warn{color:#b00}</style>");
        b.AppendLine("</head><body>");
        b.AppendLine("<h1>Phoenix — Resurrection Dashboard</h1>");
        b.AppendLine("<div class='cards'>");
        Card(b, total, "Projects tracked");
        Card(b, fully, "Fully resurrected");
        Card(b, buildable, "Buildable");
        Card(b, deps, "Dependencies");
        Card(b, recoverable, "With version pin");
        Card(b, vuln, "Vulnerable");
        Card(b, tested, "Recovery tests passed");
        b.AppendLine("</div>");

        b.AppendLine("<h2>Projects</h2><table><tr><th>ID</th><th>Name</th><th>Status</th><th>Snapshot</th><th>Created</th></tr>");
        foreach (var p in db.GetProjects())
            b.AppendLine($"<tr><td>{p.Id}</td><td>{Escape(p.Name)}</td><td>{Escape(p.Status ?? "—")}</td><td><code>{Escape((p.SnapshotHash ?? "").Substring(0, Math.Min(12, (p.SnapshotHash ?? "").Length)))}</code></td><td>{Escape(p.CreatedAt)}</td></tr>");
        b.AppendLine("</table>");

        var vulnDeps = db.GetDependenciesByState(DependencyState.Vulnerable);
        if (vulnDeps.Count > 0)
        {
            b.AppendLine("<h2 class='warn'>Vulnerable dependencies</h2><table><tr><th>Package</th><th>Version</th><th>Ecosystem</th></tr>");
            foreach (var d in vulnDeps)
                b.AppendLine($"<tr><td>{Escape(d.Name)}</td><td>{Escape(d.Version ?? "(any)")}</td><td>{Escape(d.Ecosystem)}</td></tr>");
            b.AppendLine("</table>");
        }
        b.AppendLine($"<p><small>Generated {DateTime.UtcNow:o}</small></p>");
        b.AppendLine("</body></html>");

        File.WriteAllText(outputPath, b.ToString());
        return outputPath;
    }

    private static void Card(StringBuilder b, int n, string label)
        => b.AppendLine($"<div class='card'><div class='n'>{n}</div><div class='l'>{Escape(label)}</div></div>");

    private static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
