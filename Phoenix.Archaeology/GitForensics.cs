using System.Text;
using Phoenix.Core;

namespace Phoenix.Archaeology;

public sealed class GitForensics
{
    public bool IsGitRepository(string root)
        => Directory.Exists(Path.Combine(root, ".git"));

    public GitProfile? Analyze(string root)
    {
        if (!IsGitRepository(root)) return null;

        var info = new GitProfile
        {
            HasGit = true,
            RemoteUrls = RunLines(root, "config --get-all remote.origin.url"),
            Branches = RunLines(root, "branch --list"),
            Tags = RunLines(root, "tag --list"),
            CommitCount = ParseCount(Run(root, "rev-list --all --count")),
            Authors = RunLines(root, "shortlog -sne --all")
        };

        var first = Run(root, "rev-list --max-parents=0 --format=%ci HEAD");
        var last = Run(root, "log -1 --format=%ci");
        info.FirstCommit = ParseDate(first);
        info.LastCommit = ParseDate(last);
        return info;
    }

    public IReadOnlyList<string> HistoricalManifest(string root, string relativeManifest)
    {
        var outLines = new List<string>();
        var gitPath = relativeManifest.Replace('\\', '/');
        var result = Run(root, $"log --all --follow --format=%H %ci -- \"{gitPath}\"");
        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            outLines.Add(line.Trim());
        return outLines;
    }

    private static string Run(string root, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var sb = new StringBuilder();
            while (!proc.StandardOutput.EndOfStream)
                sb.AppendLine(proc.StandardOutput.ReadLine());
            proc.WaitForExit(15000);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> RunLines(string root, string args)
        => Run(root, args).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    private static DateTime? ParseDate(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.Length >= 10 && char.IsDigit(l[0]));
        if (line == null) return null;
        var parts = line.Split(' ', 2);
        if (DateTime.TryParse(parts[0], out var d)) return d;
        return null;
    }

    private static int ParseCount(string text)
        => int.TryParse(text.Trim(), out var n) ? n : 0;
}

public sealed class GitProfile
{
    public bool HasGit { get; set; }
    public IReadOnlyList<string> RemoteUrls { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Branches { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Authors { get; set; } = Array.Empty<string>();
    public int CommitCount { get; set; }
    public DateTime? FirstCommit { get; set; }
    public DateTime? LastCommit { get; set; }
}
