using Phoenix.Core;

namespace Phoenix.Archaeology;

public sealed class FileSystemForensics
{
    private static readonly HashSet<string> SecretExtensions = new()
    {
        ".pem", ".key", ".pfx", ".p12", ".keystore", ".jks"
    };

    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", "id_rsa", "id_dsa", "credentials.json",
        "secrets.json", "service-account.json"
    };

    public IReadOnlyList<(string RelativePath, string AbsolutePath, long Size, string? Classification)> Scan(string root)
    {
        var results = new List<(string, string, long, string?)>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("FORENSIC_MANIFEST", StringComparison.OrdinalIgnoreCase)) continue;
            long size = new FileInfo(file).Length;
            results.Add((rel, file, size, Classify(name)));
        }
        return results;
    }

    private static string? Classify(string name)
    {
        if (SecretNames.Contains(name)) return "secret";
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (SecretExtensions.Contains(ext)) return "secret";
        if (ext is ".cs" or ".fs" or ".vb") return "source-dotnet";
        if (ext is ".py") return "source-python";
        if (ext is ".js" or ".jsx" or ".ts" or ".tsx" or ".mjs" or ".cjs") return "source-js";
        if (ext is ".rs") return "source-rust";
        if (ext is ".go") return "source-go";
        if (ext is ".java" or ".kt" or ".kts") return "source-jvm";
        if (ext is ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp") return "source-cpp";
        if (ext is ".php") return "source-php";
        if (ext is ".rb") return "source-ruby";
        if (ext is ".dart") return "source-dart";
        if (ext is ".swift") return "source-swift";
        if (ext is ".lock" or ".sha256" or ".sum") return "lockfile-aux";
        if (ext is ".md" or ".rst" or ".txt") return "documentation";
        if (name is "Dockerfile" or "Containerfile" or "docker-compose.yml" or "compose.yaml") return "container";
        return null;
    }

    public (DateTime? Earliest, DateTime? Latest) TimestampRange(string root)
    {
        DateTime? earliest = null, latest = null;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var wt = File.GetLastWriteTimeUtc(file);
            if (earliest == null || wt < earliest) earliest = wt;
            if (latest == null || wt > latest) latest = wt;
        }
        return (earliest, latest);
    }
}
