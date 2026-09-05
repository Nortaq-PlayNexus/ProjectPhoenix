using System.Text.Json;
using System.Text.RegularExpressions;
using Phoenix.Core;
using Phoenix.Environment;
using Phoenix.Security;

namespace Phoenix.Repair;

/// <summary>Spec #24 — every proposed/actual modification carries provenance.</summary>
public sealed record RepairProposal(
    string Id,
    string Target,
    string Reason,
    string SuggestedAction,
    string Status);

public sealed record FileModification(string Path, string Package, string FromVersion, string ToVersion, string Hash);

public sealed class RepairEngine
{
    public IReadOnlyList<RepairProposal> Propose(
        string snapshotPath,
        string? language,
        string? buildSystem,
        IReadOnlyList<SecurityFinding> security,
        IReadOnlyList<EnvironmentFinding> environment)
    {
        var list = new List<RepairProposal>();
        var n = 0;
        string Id() => "R" + (++n).ToString("D3");

        // 1) Known vulnerabilities -> propose upgrade to fixed version.
        foreach (var s in security.Where(s => s.Kind == "vulnerability"))
        {
            var m = Regex.Match(s.Detail, @"fixed in (?<v>[\d][\d.]*\d)");
            var fixedIn = m.Success ? m.Groups["v"].Value : "latest";
            list.Add(new RepairProposal(Id(), s.DependencyName, s.Detail,
                $"Upgrade {s.DependencyName} to {fixedIn} (removes {s.DependencyName} advisory).", "Proposed"));
        }

        // 2) Missing build entrypoint for detected build system.
        if (buildSystem == "pip" && !File.Exists(Path.Combine(snapshotPath, "setup.py"))
            && !File.Exists(Path.Combine(snapshotPath, "pyproject.toml")))
        {
            list.Add(new RepairProposal(Id(), "(environment)", "No Python build entrypoint found",
                "Add pyproject.toml or setup.py; for resurrection, restore via 'pip install -r requirements.txt'.", "Proposed"));
        }
        if (buildSystem is "npm" or "node" && !File.Exists(Path.Combine(snapshotPath, "package-lock.json"))
            && !File.Exists(Path.Combine(snapshotPath, "package.json")))
        {
            list.Add(new RepairProposal(Id(), "(environment)", "No Node manifest", "Provide package.json to enable install.", "Proposed"));
        }

        // 3) Missing/uncertain runtime -> propose environment reconstruction first (Minimal-Change Principle #28).
        foreach (var e in environment.Where(e => e.Kind == "runtime"))
        {
            list.Add(new RepairProposal(Id(), "(environment)", $"Reconstruct original runtime: {e.Value}",
                $"Provision {e.Value} (conf {e.Confidence:P0}) before any code modernization.", "Proposed"));
        }

        if (list.Count == 0)
            list.Add(new RepairProposal(Id(), "(none)", "No automatic repairs required",
                "Project appears internally consistent; proceed to build/verify.", "NoActionNeeded"));

        return list;
    }

    /// <summary>Spec #24 — autonomously apply version-upgrade proposals into an isolated branch.</summary>
    public IReadOnlyList<FileModification> Apply(string attemptDir, string snapshotPath, IReadOnlyList<RepairProposal> proposals)
    {
        var work = Path.Combine(attemptDir, "source");
        CopyTree(snapshotPath, work);
        var mods = new List<FileModification>();
        foreach (var p in proposals.Where(x => x.Status == "Proposed"))
        {
            var m = Regex.Match(p.SuggestedAction, @"Upgrade (?<pkg>\S+) to (?<ver>[\d][\d.]*\d)");
            if (!m.Success) continue;
            var pkg = m.Groups["pkg"].Value;
            var to = m.Groups["ver"].Value;
            foreach (var f in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f).ToLowerInvariant();
                if (name is "requirements.txt" or "pyproject.toml")
                    PatchRequirements(f, pkg, to, mods);
                else if (name == "package.json")
                    PatchPackageJson(f, pkg, to, mods);
            }
        }
        return mods;
    }

    private static void PatchRequirements(string file, string pkg, string to, List<FileModification> mods)
    {
        var lines = File.ReadAllLines(file);
        var outLines = new List<string>();
        var changed = false;
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @$"^\s*{Regex.Escape(pkg)}\s*(?<op>==|>=|<=|~=|!=)?\s*(?<ver>[\d][\d.]*\d)");
            if (m.Success && !string.IsNullOrEmpty(m.Groups["ver"].Value))
            {
                var from = m.Groups["ver"].Value;
                outLines.Add($"{pkg}=={to}");
                mods.Add(new FileModification(file, pkg, from, to, Crypto.Sha256File(file)));
                changed = true;
            }
            else outLines.Add(line);
        }
        if (changed) File.WriteAllLines(file, outLines);
    }

    private static void PatchPackageJson(string file, string pkg, string to, List<FileModification> mods)
    {
        var text = File.ReadAllText(file);
        var m = Regex.Match(text, @$"""{Regex.Escape(pkg)}""\s*:\s*""(?<ver>[\d][\d.]*\d)""");
        if (!m.Success) return;
        var from = m.Groups["ver"].Value;
        var replaced = text.Replace(m.Value, $"\"{pkg}\": \"{to}\"");
        File.WriteAllText(file, replaced);
        mods.Add(new FileModification(file, pkg, from, to, Crypto.Sha256File(file)));
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}

/// <summary>Spec #27 — create an isolated resurrection-attempt branch and a provenance log.</summary>
public sealed class RepairStore
{
    public string CreateAttempt(string recoveryRoot, string projectName, string snapshotPath, IReadOnlyList<RepairProposal> proposals)
    {
        var baseDir = Path.Combine(recoveryRoot, Sanitize(projectName));
        Directory.CreateDirectory(baseDir);
        var attempt = 1;
        while (Directory.Exists(Path.Combine(baseDir, $"resurrection-attempt-{attempt:D3}"))) attempt++;
        var attemptDir = Path.Combine(baseDir, $"resurrection-attempt-{attempt:D3}");
        Directory.CreateDirectory(attemptDir);

        var originalHash = Crypto.Sha256Directory(snapshotPath);
        var log = new Dictionary<string, object?>
        {
            ["repair_id"] = "REPAIR-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ["attempt"] = attempt,
            ["original_snapshot_hash"] = originalHash,
            ["attempt_dir"] = attemptDir,
            ["timestamp_utc"] = DateTime.UtcNow.ToString("o"),
            ["agent"] = "Repair Engineer",
            ["model"] = "rule-based (offline); AI disabled",
            ["proposals"] = proposals.Select(p => new Dictionary<string, object?>
            {
                ["id"] = p.Id, ["target"] = p.Target, ["reason"] = p.Reason,
                ["action"] = p.SuggestedAction, ["status"] = p.Status,
                ["modified_hash"] = p.Status == "NoActionNeeded" ? originalHash : "(pending apply)"
            }).ToList()
        };
        File.WriteAllText(Path.Combine(attemptDir, "REPAIR_LOG.json"),
            JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
        return attemptDir;
    }

    /// <summary>Record applied file modifications back into the provenance log.</summary>
    public void RecordModifications(string attemptDir, IReadOnlyList<RepairProposal> proposals, IReadOnlyList<FileModification> mods)
    {
        var logPath = Path.Combine(attemptDir, "REPAIR_LOG.json");
        if (!File.Exists(logPath)) return;
        var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(logPath))!.AsObject();
        doc["modifications"] = JsonSerializer.SerializeToNode(mods, new JsonSerializerOptions { WriteIndented = true });
        foreach (var p in proposals)
        {
            var m = Regex.Match(p.SuggestedAction, @"Upgrade (?<pkg>\S+) to (?<ver>[\d][\d.]*\d)");
            if (!m.Success) continue;
            var hit = mods.FirstOrDefault(x => x.Package.Equals(m.Groups["pkg"].Value, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                var node = doc["proposals"]!.AsArray()
                    .OfType<System.Text.Json.Nodes.JsonObject>()
                    .FirstOrDefault(n => n["id"]?.GetValue<string>() == p.Id);
                if (node != null) { node["status"] = "Applied"; node["modified_hash"] = hit.Hash; }
            }
        }
        File.WriteAllText(logPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Sanitize(string name)
        => new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
}
