using System.Text.Json;
using Phoenix.Core;

namespace Phoenix.Archive;

public sealed class CapsuleBuilder
{
    public sealed record CapsuleInput(
        long ProjectId,
        string ProjectName,
        string SourceRoot,
        string? Ecosystem,
        string? BuildSystem,
        string? DetectedLanguage,
        IReadOnlyList<DependencyRecord> Dependencies,
        IReadOnlyList<string> Notes,
        string RecoveryStatus,
        double Confidence,
        DateTime? Earliest,
        DateTime? Latest);

    public string Build(CapsuleInput input, string capsuleRoot)
    {
        var dir = Path.Combine(capsuleRoot, $"{Sanitize(input.ProjectName)}.phoenix");
        Directory.CreateDirectory(dir);
        Sub(dir, "source"); Sub(dir, "git"); Sub(dir, "dependencies"); Sub(dir, "environment");
        Sub(dir, "metadata"); Sub(dir, "tests"); Sub(dir, "manifests"); Sub(dir, "checksums"); Sub(dir, "build"); Sub(dir, "documentation"); Sub(dir, "recovery");

        // copy source (mirrors forensic copy minus hash manifests)
        CopySource(input.SourceRoot, Path.Combine(dir, "source"));

        // manifest
        var fingerprint = Crypto.Sha256(input.ProjectName + "|" + input.SourceRoot);
        var manifest = new Dictionary<string, object?>
        {
            ["phoenix_version"] = "0.1.0",
            ["project"] = input.ProjectName,
            ["project_fingerprint"] = fingerprint,
            ["source_hash"] = Crypto.Sha256Directory(input.SourceRoot),
            ["environment"] = new Dictionary<string, object?>
            {
                ["os"] = Environment.OSVersion.Platform.ToString(),
                ["architecture"] = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                ["runtime"] = input.DetectedLanguage,
                ["compiler"] = input.BuildSystem
            },
            ["dependencies"] = input.Dependencies.Select(d => new Dictionary<string, object?>
            {
                ["name"] = d.Name, ["version"] = d.Version, ["ecosystem"] = d.Ecosystem,
                ["scope"] = d.Scope, ["state"] = d.State.ToString(), ["risk"] = d.RiskScore
            }).ToList(),
            ["artifacts"] = Array.Empty<object>(),
            ["tests"] = Array.Empty<object>(),
            ["recovery_status"] = input.RecoveryStatus,
            ["confidence"] = input.Confidence,
            ["timeline"] = new Dictionary<string, object?> { ["earliest"] = input.Earliest, ["latest"] = input.Latest }
        };

        var manifestPath = Path.Combine(dir, "metadata", "phoenix.manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        var manifestHash = Crypto.Sha256File(manifestPath);

        File.WriteAllText(Path.Combine(dir, "metadata", "RECOVERY.md"),
            string.Join("\n", input.Notes.Prepend($"# Recovery notes for {input.ProjectName}")));

        File.WriteAllText(Path.Combine(dir, "BUILD_RECIPE.md"), BuildRecipe(input));
        File.WriteAllText(Path.Combine(dir, "phoenix.yaml"),
            "project: " + input.ProjectName + "\nrecovery_status: " + input.RecoveryStatus + "\n");

        File.WriteAllText(Path.Combine(dir, "CAPSULE.sha256"),
            manifestHash + "  metadata/phoenix.manifest.json");

        WriteLock(dir, input.Dependencies);
        return dir;
    }

    private static void WriteLock(string dir, IReadOnlyList<DependencyRecord> deps)
    {
        var entries = deps
            .Where(d => d.Scope != "system")
            .Select(d => new Dictionary<string, object?>
            {
                ["name"] = d.Name,
                ["version"] = d.Version,
                ["ecosystem"] = d.Ecosystem,
                ["fingerprint"] = Crypto.Sha256(d.Name + "|" + (d.Version ?? "") + "|" + d.Ecosystem)
            }).ToList();
        File.WriteAllText(Path.Combine(dir, "phoenix.lock.json"),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["generated_utc"] = DateTime.UtcNow.ToString("o"),
                ["dependencies"] = entries
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Sub(string dir, string name) => Directory.CreateDirectory(Path.Combine(dir, name));

    private static string BuildRecipe(CapsuleInput input)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# BUILD_RECIPE — {input.ProjectName}");
        sb.AppendLine();
        sb.AppendLine($"Ecosystem: {input.Ecosystem ?? "unknown"}");
        sb.AppendLine($"Build system: {input.BuildSystem ?? "unknown"}");
        sb.AppendLine($"Confidence: {input.Confidence:P0}");
        sb.AppendLine();
        sb.AppendLine("## Recovery steps");
        sb.AppendLine("1. Install required runtime / toolchain.");
        sb.AppendLine("2. Restore dependencies (see metadata/phoenix.manifest.json).");
        sb.AppendLine("3. Apply environment variables if documented.");
        sb.AppendLine("4. Build using the detected build system.");
        sb.AppendLine("5. Run tests if present.");
        sb.AppendLine("6. Verify with Phoenix (`phoenix verify`).");
        return sb.ToString();
    }

    private static void CopySource(string src, string dest)
    {
        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (name is ".git" or "node_modules" or "bin" or "obj") continue;
            var child = Path.Combine(dest, name);
            Directory.CreateDirectory(child);
            CopySource(dir, child);
        }
        foreach (var file in Directory.EnumerateFiles(src))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            if (destFile.EndsWith("FORENSIC_MANIFEST.sha256", StringComparison.OrdinalIgnoreCase)
                || destFile.EndsWith("FORENSIC_MANIFEST.sha256.sum", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsSecretFile(file)) continue; // Principle: never archive secrets in plaintext (spec #53)
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static bool IsSecretFile(string path)
    {
        var name = Path.GetFileName(path);
        if (SecretNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return SecretExtensions.Contains(ext);
    }

    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", "id_rsa", "id_dsa", "credentials.json",
        "secrets.json", "service-account.json"
    };
    private static readonly HashSet<string> SecretExtensions = new()
    {
        ".pem", ".key", ".pfx", ".p12", ".keystore", ".jks"
    };

    private static string Sanitize(string name)
        => new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
}
