using System.Text.Json;
using Phoenix.Core;

namespace Phoenix.Archive;

public sealed record DependencyLockEntry(string Name, string? Version, string Ecosystem, string Fingerprint);

public sealed class LockVerifier
{
    public IReadOnlyList<DependencyLockEntry> ReadLock(string capsulePath)
    {
        var path = Path.Combine(capsulePath, "phoenix.lock.json");
        if (!File.Exists(path)) return Array.Empty<DependencyLockEntry>();
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<DependencyLockEntry>();
        if (doc.RootElement.TryGetProperty("dependencies", out var arr))
            foreach (var e in arr.EnumerateArray())
                list.Add(new DependencyLockEntry(
                    e.GetProperty("name").GetString()!,
                    e.TryGetProperty("version", out var v) ? v.GetString() : null,
                    e.GetProperty("ecosystem").GetString()!,
                    e.GetProperty("fingerprint").GetString()!));
        return list;
    }

    public IReadOnlyList<string> DetectDrift(string capsulePath, IReadOnlyList<(string Name, string? Version, string Ecosystem)> current)
    {
        var locked = ReadLock(capsulePath).ToDictionary(k => k.Name + "|" + k.Ecosystem, v => v);
        var drift = new List<string>();
        foreach (var (name, version, eco) in current)
        {
            var key = name + "|" + eco;
            if (!locked.TryGetValue(key, out var entry))
            {
                drift.Add($"NEW: {name} {version ?? "(any)"} [{eco}]");
                continue;
            }
            var fp = Crypto.Sha256(name + "|" + (version ?? "") + "|" + eco);
            if (fp != entry.Fingerprint)
                drift.Add($"DRIFT: {name} {entry.Version ?? "(any)"} -> {version ?? "(any)"} [{eco}]");
        }
        return drift;
    }
}
