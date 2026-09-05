using System.Text;
using Phoenix.Core;

namespace Phoenix.Archaeology;

public sealed class ForensicSnapshot
{
    private readonly ILogger _log;

    public ForensicSnapshot(ILogger log) => _log = log;

    public string Create(string sourcePath, string forensicRoot)
    {
        var projectName = new DirectoryInfo(sourcePath).Name;
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var snapDir = Path.Combine(forensicRoot, projectName, "snapshot-" + stamp);
        Directory.CreateDirectory(snapDir);

        _log.Info($"Creating immutable forensic copy: {snapDir}");
        CopyTree(sourcePath, snapDir);

        // content-address every file and write a manifest of hashes
        var manifestPath = Path.Combine(snapDir, "FORENSIC_MANIFEST.sha256");
        var sb = new StringBuilder();
        foreach (var file in EnumerateFiles(snapDir))
        {
            var rel = Path.GetRelativePath(snapDir, file).Replace('\\', '/');
            var hash = Crypto.Sha256File(file);
            sb.AppendLine($"{hash}  {rel}");
        }
        File.WriteAllText(manifestPath, sb.ToString());

        var manifestHash = Crypto.Sha256File(manifestPath);
        File.WriteAllText(Path.Combine(snapDir, "FORENSIC_MANIFEST.sha256.sum"), manifestHash);

        _log.Info($"Snapshot complete. {CountFiles(snapDir)} files, manifest hash {manifestHash[..12]}");
        return snapDir;
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("FORENSIC_MANIFEST.sha256", StringComparison.OrdinalIgnoreCase)
                        && !f.EndsWith("FORENSIC_MANIFEST.sha256.sum", StringComparison.OrdinalIgnoreCase));
    }

    private static int CountFiles(string root) => EnumerateFiles(root).Count();

    private static void CopyTree(string source, string dest)
    {
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            // Preserve .git/.svn as evidence; only skip regenerable build output & vendor caches.
            if (name is "node_modules" or "bin" or "obj" or "__pycache__" or ".venv" or "vendor")
                continue;
            var child = Path.Combine(dest, name);
            Directory.CreateDirectory(child);
            CopyTree(dir, child);
        }
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }
    }
}

