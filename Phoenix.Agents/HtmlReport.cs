using System.Text;
using Phoenix.Build;

namespace Phoenix.Agents;

/// <summary>Spec #50/#51 — also emit machine-friendly HTML resurrection reports.</summary>
public static class HtmlReport
{
    public static void Write(ResurrectionContext ctx)
    {
        if (ctx.CapsulePath == null || ctx.Profile == null) return;
        var meta = System.IO.Path.Combine(ctx.CapsulePath, "metadata");
        var verdict = new JudgeAgent().Verdict(ctx.Recovery);

        var f = new StringBuilder();
        f.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>Project Forensics</title>");
        f.AppendLine("<style>body{font-family:Segoe UI,Arial;margin:2rem;color:#222}h1{color:#0a4d8c}table{border-collapse:collapse;width:100%;margin-top:1rem}td,th{border:1px solid #ccc;padding:.4rem .6rem;text-align:left}th{background:#eef4fb}code{background:#f4f4f4;padding:0 .3rem}</style>");
        f.AppendLine("</head><body>");
        f.AppendLine($"<h1>Project Forensics — {Escape(ctx.Profile.ProjectName)}</h1>");
        f.AppendLine("<table>");
        Row(f, "Detected language", ctx.Profile.DetectedLanguage ?? "unknown");
        Row(f, "Build system", ctx.Profile.BuildSystem ?? "unknown");
        Row(f, "Ecosystems", string.Join(", ", ctx.Profile.Ecosystems));
        Row(f, "Confidence", $"{ctx.Profile.Confidence:P0}");
        Row(f, "Files inventoried", ctx.Profile.FileCount.ToString());
        Row(f, "Evidence window", $"{ctx.Profile.EarliestTimestamp:yyyy-MM-dd} → {ctx.Profile.LatestTimestamp:yyyy-MM-dd}");
        f.AppendLine("</table>");
        f.AppendLine("<h2>Dependencies</h2><table><tr><th>Name</th><th>Version</th><th>Ecosystem</th><th>State</th></tr>");
        foreach (var d in ctx.Dependencies)
            f.AppendLine($"<tr><td>{Escape(d.Name)}</td><td>{Escape(d.Version ?? "(any)")}</td><td>{Escape(d.Ecosystem)}</td><td>{d.State}</td></tr>");
        f.AppendLine("</table>");
        if (ctx.Profile.Notes.Count > 0)
        {
            f.AppendLine("<h2>Notes</h2><ul>");
            foreach (var n in ctx.Profile.Notes) f.AppendLine($"<li>{Escape(n)}</li>");
            f.AppendLine("</ul>");
        }
        f.AppendLine("</body></html>");
        System.IO.File.WriteAllText(System.IO.Path.Combine(meta, "PROJECT_FORENSICS.html"), f.ToString());

        var r = new StringBuilder();
        r.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>Resurrection Report</title><style>body{font-family:Segoe UI,Arial;margin:2rem;color:#222}h1{color:#0a4d8c}.pill{display:inline-block;padding:.2rem .6rem;border-radius:1rem;background:#0a4d8c;color:#fff;font-weight:600}table{border-collapse:collapse;width:100%;margin-top:1rem}td,th{border:1px solid #ccc;padding:.4rem .6rem;text-align:left}th{background:#eef4fb}.high{color:#b00}.low{color:#888}</style></head><body>");
        r.AppendLine($"<h1>Resurrection Report — {Escape(ctx.Profile.ProjectName)}</h1>");
        r.AppendLine($"<p>Recovery status: <span class='pill'>{Escape(verdict)}</span></p>");
        r.AppendLine("<table>");
        Row(r, "Environment findings", (ctx.Environment?.Findings.Count ?? 0).ToString());
        Row(r, "Dependencies recovered", ctx.Dependencies.Count.ToString());
        Row(r, "Security findings", ctx.Security.Count.ToString());
        r.AppendLine("</table>");
        if (ctx.Security.Count > 0)
        {
            r.AppendLine("<h2>Problems discovered</h2><table><tr><th>Sev</th><th>Kind</th><th>Target</th><th>Detail</th></tr>");
            foreach (var s in ctx.Security)
                r.AppendLine($"<tr><td class='{s.Severity.ToLowerInvariant()}'>{Escape(s.Severity)}</td><td>{Escape(s.Kind)}</td><td>{Escape(s.DependencyName)}</td><td>{Escape(s.Detail)}</td></tr>");
            r.AppendLine("</table>");
        }
        if (ctx.Environment != null)
        {
            r.AppendLine("<h2>Environment reconstructed</h2><table><tr><th>Kind</th><th>Value</th><th>Conf</th><th>Evidence</th></tr>");
            foreach (var e in ctx.Environment.Findings)
                r.AppendLine($"<tr><td>{Escape(e.Kind)}</td><td>{Escape(e.Value)}</td><td>{e.Confidence:P0}</td><td>{Escape(e.Evidence)}</td></tr>");
            r.AppendLine("</table>");
        }
        if (!string.IsNullOrEmpty(ctx.RepairAttemptPath))
            r.AppendLine($"<p>Repair branch: <code>{Escape(ctx.RepairAttemptPath)}</code></p>");
        r.AppendLine("</body></html>");
        System.IO.File.WriteAllText(System.IO.Path.Combine(meta, "RESURRECTION_REPORT.html"), r.ToString());
    }

    private static void Row(StringBuilder b, string k, string v)
        => b.AppendLine($"<tr><td><strong>{Escape(k)}</strong></td><td>{Escape(v)}</td></tr>");

    private static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
