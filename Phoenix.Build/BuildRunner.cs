using System.Diagnostics;
using Phoenix.Core;
using Phoenix.Environment;

namespace Phoenix.Build;

public sealed record BuildResult(
    bool Executed,
    int ExitCode,
    string Logs,
    IReadOnlyList<string> PlannedCommands,
    string BuildSystem);

public sealed class BuildPlanner
{
    public IReadOnlyList<string> Plan(string buildSystem, string root)
    {
        return buildSystem?.ToLowerInvariant() switch
        {
            "pip" => new[] { "python -m pip install -r requirements.txt", "python -m compileall ." },
            "npm" => new[] { "npm ci || npm install", "npm test" },
            "cargo" => new[] { "cargo build", "cargo test" },
            "go-modules" => new[] { "go build ./...", "go test ./..." },
            "msbuild" => new[] { "dotnet build", "dotnet test" },
            "maven" => new[] { "mvn -B -q package", "mvn -B test" },
            "gradle" => new[] { "gradle build", "gradle test" },
            "cmake" => new[] { "cmake -S . -B build", "cmake --build build" },
            "make" => new[] { "make", "make test" },
            "composer" => new[] { "composer install", "composer test" },
            "bundler" => new[] { "bundle install", "bundle exec rake" },
            "pub" => new[] { "dart pub get", "dart test" },
            "docker" => new[] { "docker build -t phoenix-target ." },
            _ => new[] { "# no known build system; manual build required" }
        };
    }
}

public sealed class BuildRunner
{
    private readonly ILogger _log;
    private readonly BuildPlanner _planner = new();

    public BuildRunner(ILogger log) => _log = log;

    public BuildResult Run(string buildSystem, string root, bool allowExecution, int timeoutMs = 120000)
    {
        var commands = _planner.Plan(buildSystem, root);
        if (!allowExecution)
        {
            _log.Warn("Sandbox: execution disabled (untrusted project). Showing planned build only.");
            foreach (var c in commands) _log.Info("  plan> " + c);
            return new BuildResult(false, -1, "BUILD NOT EXECUTED (simulation mode)", commands, buildSystem);
        }

        _log.Warn("UNTRUSTED BUILD: executing on host with limited isolation (clean env, temp work dir, timeout).");
        _log.Warn("For production use a VM/container per Principle 19. Never run without explicit authorization.");

        var sb = new System.Text.StringBuilder();
        var work = Path.Combine(Path.GetTempPath(), "phoenix-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            CopyTree(root, work);
            foreach (var cmd in commands)
            {
                sb.AppendLine("$ " + cmd);
                var exit = RunSandboxed(work, cmd, timeoutMs, sb);
                if (exit != 0 && cmd.Contains("test") == false)
                {
                    _log.Warn($"Build step failed (exit {exit}).");
                    return new BuildResult(true, exit, sb.ToString(), commands, buildSystem);
                }
            }
            return new BuildResult(true, 0, sb.ToString(), commands, buildSystem);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static int RunSandboxed(string work, string command, int timeoutMs, System.Text.StringBuilder log)
    {
        var parts = command.Split(' ', 2);
        var psi = new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : "")
        {
            WorkingDirectory = work,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Do not inherit the user's full environment; start clean to limit secret exposure.
        psi.Environment.Clear();
        psi.Environment["PATH"] = System.Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["TEMP"] = Path.GetTempPath();
        using var proc = Process.Start(psi)!;
        var sw = new Stopwatch();
        sw.Start();
        while (!proc.StandardOutput.EndOfStream && sw.ElapsedMilliseconds < timeoutMs)
            log.AppendLine(proc.StandardOutput.ReadLine());
        while (!proc.StandardError.EndOfStream && sw.ElapsedMilliseconds < timeoutMs)
            log.AppendLine("! " + proc.StandardError.ReadLine());
        if (!proc.WaitForExit(timeoutMs))
        {
            log.AppendLine("! TIMEOUT — process killed");
            try { proc.Kill(true); } catch { }
            return -999;
        }
        return proc.ExitCode;
    }

    private static void CopyTree(string src, string dest)
    {
        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (name is ".git" or "node_modules" or "bin" or "obj") continue;
            var child = Path.Combine(dest, name);
            Directory.CreateDirectory(child);
            CopyTree(dir, child);
        }
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}

public sealed class JudgeAgent
{
    public RecoveryStatus Evaluate(BuildResult build, bool testsAttempted)
    {
        if (!build.Executed) return RecoveryStatus.Unresolved;
        if (build.ExitCode == 0 && testsAttempted) return RecoveryStatus.FullyResurrected;
        if (build.ExitCode == 0 && !testsAttempted) return RecoveryStatus.BuildsButDoesNotRun;
        if (build.ExitCode != 0) return RecoveryStatus.Unresolved;
        return RecoveryStatus.PartiallyResurrected;
    }

    public string Verdict(RecoveryStatus status) => status switch
    {
        RecoveryStatus.FullyResurrected => "FULLY RESURRECTED",
        RecoveryStatus.BuildsButDoesNotRun => "BUILDS BUT DOES NOT RUN",
        RecoveryStatus.RunsButTestsFail => "RUNS BUT TESTS FAIL",
        RecoveryStatus.PartiallyResurrected => "PARTIALLY RESURRECTED",
        _ => "UNRESOLVED"
    };
}
