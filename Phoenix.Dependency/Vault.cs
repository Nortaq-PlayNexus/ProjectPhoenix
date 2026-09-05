using System.Text.Json;
using Phoenix.Core;

namespace Phoenix.Dependency;

public sealed class DependencyVault
{
    private readonly string _root;

    public DependencyVault(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
        foreach (var eco in new[] { "python", "npm", "cargo", "go", "nuget", "maven", "gradle", "ruby", "php", "docker", "metadata" })
            Directory.CreateDirectory(Path.Combine(root, eco));
    }

    public string StoreArtifact(string ecosystem, string name, string? version, byte[] content, string sha256)
    {
        var ecoDir = Path.Combine(_root, SanitizeEco(ecosystem));
        Directory.CreateDirectory(ecoDir);
        var key = $"{SanitizeName(name)}@{version ?? "unknown"}@{sha256[..16]}";
        var path = Path.Combine(ecoDir, key);
        File.WriteAllBytes(path, content);

        var meta = new Dictionary<string, object?>
        {
            ["name"] = name, ["version"] = version, ["ecosystem"] = ecosystem,
            ["sha256"] = sha256, ["size"] = content.Length,
            ["retrieved_utc"] = DateTime.UtcNow.ToString("o"), ["source"] = "local-vault"
        };
        File.WriteAllText(Path.Combine(_root, "metadata", key + ".json"),
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

        return key;
    }

    public bool HasArtifact(string sha256)
        => Directory.EnumerateFiles(Path.Combine(_root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Any(p =>
            {
                try { return JsonDocument.Parse(File.ReadAllText(p)).RootElement.GetProperty("sha256").GetString() == sha256; }
                catch { return false; }
            });

    public InsuranceStatus ComputeInsurance(IReadOnlyList<DependencyRecord> deps)
    {
        if (deps.Count == 0) return InsuranceStatus.Uninsured;
        var insured = 0;
        foreach (var d in deps)
        {
            if (d.State is DependencyState.Missing or DependencyState.Unavailable or DependencyState.Corrupted)
                continue;
            if (!string.IsNullOrEmpty(d.Version) || d.ArchiveStatus == "stored" || d.LocalCacheStatus == "stored")
                insured++;
        }
        var ratio = (double)insured / deps.Count;
        return ratio switch
        {
            >= 0.999 => InsuranceStatus.Insured,
            >= 0.5 => InsuranceStatus.PartiallyInsured,
            > 0 => InsuranceStatus.AtRisk,
            _ => InsuranceStatus.Uninsured
        };
    }

    private static string SanitizeEco(string e) => string.IsNullOrWhiteSpace(e) ? "generic" : e;
    private static string SanitizeName(string n) => new string(n.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_').ToArray());
}
