using Phoenix.Agents;
using Phoenix.Archaeology;
using Phoenix.Archive;
using Phoenix.Core;
using Phoenix.Dependency;
using Phoenix.Environment;
using Phoenix.Build;
using Phoenix.Security;
using Phoenix.Recovery;
using Phoenix.Providers;

var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Phoenix");
var dbPath = Path.Combine(home, "Database", "phoenix.db");
var forensicRoot = Path.Combine(home, "Forensics");
var vaultRoot = Path.Combine(home, "Dependencies", "PhoenixDependencyVault");
foreach (var d in new[] { forensicRoot, vaultRoot, Path.GetDirectoryName(dbPath)! })
    Directory.CreateDirectory(d);

var db = new PhoenixDb(dbPath);
var log = new ConsoleLogger();
var vault = new DependencyVault(vaultRoot);

if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
{
    Console.WriteLine("PROJECT PHOENIX — Autonomous Software Resurrection & Dependency Insurance");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  phoenix resurrect <path> [--build]   Full resurrection pipeline (add --build to attempt sandboxed build)");
    Console.WriteLine("  phoenix insure <path>               Map dependencies and compute insurance status");
    Console.WriteLine("  phoenix simulate <path>             Run the disaster simulator (DOOMSDAY) for a project");
    Console.WriteLine("  phoenix cascade <depName>           Show blast radius if a dependency disappears (all projects)");
    Console.WriteLine("  phoenix report                      Print the project health dashboard");
    Console.WriteLine("  phoenix dashboard                   Generate static HTML dashboard (PhoenixDashboard.html)");
    Console.WriteLine("  phoenix providers                   Print the provider capability matrix");
    Console.WriteLine("  phoenix verify <capsule>            Verify a previously created .phoenix capsule");
    Console.WriteLine("  phoenix help                        Show this message");
    return;
}

var allowBuild = args.Contains("--build");
switch (args[0])
{
    case "resurrect":
        if (args.Length < 2) { Console.WriteLine("error: resurrect requires a path"); return; }
        Resurrect(args[1], allowBuild);
        break;
    case "insure":
        if (args.Length < 2) { Console.WriteLine("error: insure requires a path"); return; }
        Insure(args[1]);
        break;
    case "verify":
        if (args.Length < 2) { Console.WriteLine("error: verify requires a capsule path"); return; }
        Verify(args[1]);
        break;
    case "simulate":
        if (args.Length < 2) { Console.WriteLine("error: simulate requires a path"); return; }
        Simulate(args[1]);
        break;
    case "cascade":
        if (args.Length < 2) { Console.WriteLine("error: cascade requires a dependency name"); return; }
        Cascade(args[1]);
        break;
    case "report":
        Report();
        break;
    case "dashboard":
        Dashboard();
        break;
    case "providers":
        Providers();
        break;
    default:
        Console.WriteLine($"unknown command: {args[0]}");
        break;
}

void Verify(string capsulePath)
{
    var manifest = Path.Combine(capsulePath, "metadata", "phoenix.manifest.json");
    if (!File.Exists(manifest)) { Console.WriteLine("INVALID: no manifest found"); return; }
    var hash = Crypto.Sha256File(manifest);
    Console.WriteLine($"Capsule manifest SHA-256: {hash}");
    var expected = File.ReadAllLines(Path.Combine(capsulePath, "CAPSULE.sha256")).FirstOrDefault()?.Split(' ')[0];
    var ok = hash == expected;
    Console.WriteLine($"Integrity: {(ok ? "OK" : "MISMATCH")}");
    var locked = new LockVerifier().ReadLock(capsulePath);
    Console.WriteLine($"Locked dependencies: {locked.Count}");
    db.InsertRecoveryTest(null, null, "capsule-integrity", ok ? "PASS" : "FAIL");
}

void Insure(string sourcePath)
{
    if (!Directory.Exists(sourcePath)) { Console.WriteLine($"error: path not found: {sourcePath}"); return; }
    var snapshot = new ForensicSnapshot(log).Create(sourcePath, forensicRoot);
    var profile = new ArchaeologyEngine().Analyze(snapshot);
    var projectId = db.ProjectIdBySourcePath(sourcePath);
    if (projectId == 0) projectId = db.InsertProject(new DirectoryInfo(sourcePath).Name, sourcePath, Crypto.Sha256Directory(snapshot), null, null, null);
    var ctx = new ResurrectionContext { ProjectId = projectId, SnapshotPath = snapshot, SnapshotHash = Crypto.Sha256Directory(snapshot), Db = db, Log = log };
    new Coordinator().Add(new ArchaeologistAgent()).Add(new DependencyAnalystAgent()).Run(ctx);
    var status = vault.ComputeInsurance(ctx.Dependencies);
    Console.WriteLine($"\n=== INSURANCE ===\nDependencies: {ctx.Dependencies.Count}\nInsurance status: {status}");
}

void Cascade(string depName)
{
    var result = new CascadeSimulator(db).Simulate(depName);
    Console.WriteLine($"\n=== DEPENDENCY CASCADE :: {depName} ===");
    Console.WriteLine($"Projects affected if '{depName}' disappears: {result.AffectedProjects}");
    foreach (var p in result.Projects) Console.WriteLine($"  - {p}");
    if (result.AffectedProjects == 0) Console.WriteLine("  (no tracked projects depend on it)");
}

void Simulate(string sourcePath)
{
    if (!Directory.Exists(sourcePath)) { Console.WriteLine($"error: path not found: {sourcePath}"); return; }
    var snapshot = new ForensicSnapshot(log).Create(sourcePath, forensicRoot);
    var projectId = db.ProjectIdBySourcePath(sourcePath);
    if (projectId == 0) projectId = db.InsertProject(new DirectoryInfo(sourcePath).Name, sourcePath, Crypto.Sha256Directory(snapshot), null, null, null);
    var ctx = new ResurrectionContext { ProjectId = projectId, SnapshotPath = snapshot, SnapshotHash = Crypto.Sha256Directory(snapshot), Db = db, Log = log };
    new Coordinator().Add(new ArchaeologistAgent()).Add(new DependencyAnalystAgent()).Run(ctx);

    var insurance = vault.ComputeInsurance(ctx.Dependencies);
    var scenarios = new DisasterSimulator().Simulate(ctx.Dependencies, insurance);
    Console.WriteLine($"\n=== PHOENIX DOOMSDAY :: {ctx.Dependencies.Count} deps, insurance {insurance} ===");
    foreach (var s in scenarios)
        Console.WriteLine($"  {s.Scenario,-32} survival {s.SurvivalPct,6}%  ({s.Recoverable}/{s.Total} recoverable)");
}

void Report()
{
    var fully = db.CountProjectsByStatus("FULLY RESURRECTED");
    var total = db.CountProjects();
    var buildable = db.CountProjectsByStatus("BUILDS BUT DOES NOT RUN") + fully;
    var deps = db.CountDependencies();
    var recoverable = db.CountDependenciesWithVersion();
    var vuln = db.CountDependenciesByState(DependencyState.Vulnerable);
    var rp = db.CountRecoveryTestsByResult("PASS");
    var rf = db.CountRecoveryTestsByResult("FAIL");

    Console.WriteLine($"\n=== PROJECT PHOENIX :: HEALTH DASHBOARD ===");
    Console.WriteLine($"Projects:            {total}");
    Console.WriteLine($"  Fully resurrected: {fully}");
    Console.WriteLine($"  Buildable:         {buildable}");
    Console.WriteLine($"Dependencies:        {deps}");
    Console.WriteLine($"  Recoverable(pinned): {recoverable}");
    Console.WriteLine($"  Vulnerable:        {vuln}");
    Console.WriteLine($"Recovery tests:      PASS {rp} / FAIL {rf}");
}

void Dashboard()
{
    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Phoenix", "Dashboard");
    Directory.CreateDirectory(dir);
    var outPath = Path.Combine(dir, "PhoenixDashboard.html");
    Phoenix.Archive.DashboardGenerator.Generate(db, outPath);
    Console.WriteLine($"\n=== DASHBOARD ===\nWrote {outPath}");
}

void Providers()
{
    Console.WriteLine($"\n=== PROVIDER CAPABILITY MATRIX ===");
    Console.WriteLine($"{"Provider",-14}{"DL",3}{"UP",3}{"Meta",5}{"Hash",5}{"Sig",4}{"Arch",5}{"Priv",5}{"Avail"}");
    foreach (var p in new ProviderCatalog().All())
        Console.WriteLine($"{p.Name,-14}{(p.Download ? "Y" : "-"),3}{(p.Upload ? "Y" : "-"),3}" +
            $"{(p.Metadata ? "Y" : "-"),5}{(p.Checksums ? "Y" : "-"),5}{(p.Signatures ? "Y" : "-"),4}" +
            $"{(p.Archives ? "Y" : "-"),5}{(p.PrivatePackages ? "Y" : "-"),5}{p.Availability}");
}

void Resurrect(string sourcePath, bool build)
{
    if (!Directory.Exists(sourcePath)) { Console.WriteLine($"error: path not found: {sourcePath}"); return; }

    Console.WriteLine($"\n=== PROJECT PHOENIX :: RESURRECT ===\nTarget: {sourcePath}");
    db.InsertEvent(null, "discover", "Archaeologist", "Phoenix.CLI", Crypto.Sha256(sourcePath), null, "start");

    // Principle 2: Preserve before repairing — immutable forensic copy
    var snapshot = new ForensicSnapshot(log).Create(sourcePath, forensicRoot);
    var snapshotHash = Crypto.Sha256Directory(snapshot);
    Console.WriteLine($"\n[FORENSICS] Immutable snapshot created.");

    var projectId = db.InsertProject(new DirectoryInfo(sourcePath).Name, sourcePath, snapshotHash, null, null, null);
    db.InsertSnapshot(projectId, snapshot, snapshotHash, "forensic-copy");

    var fs = new FileSystemForensics();
    var scanned = fs.Scan(snapshot);
    var secretCount = 0;
    foreach (var f in scanned)
    {
        if (f.Classification == "secret") secretCount++;
        var fileHash = Crypto.Sha256File(f.AbsolutePath);
        db.InsertFile(projectId, f.RelativePath, f.AbsolutePath, fileHash, f.Size, f.Classification);
        db.UpsertChecksum(fileHash, fileHash, f.Size);
    }
    Console.WriteLine($"[FORENSICS] {scanned.Count} files inventoried & content-addressed."
        + (secretCount > 0 ? $" {secretCount} secret file(s) masked." : ""));

    // Multi-agent resurrection pipeline (spec #25/#26)
    var ctx = new ResurrectionContext
    {
        ProjectId = projectId, SourcePath = sourcePath, SnapshotPath = snapshot,
        SnapshotHash = snapshotHash, AllowBuild = build, Db = db, Log = log
    };
    new Coordinator()
        .Add(new ArchaeologistAgent())
        .Add(new DependencyAnalystAgent())
        .Add(new EnvironmentEngineerAgent())
        .Add(new SecurityEngineerAgent())
        .Add(new BuildEngineerAgent())
        .Add(new RepairEngineerAgent())
        .Add(new JudgeAgentWrapper())
        .Add(new ArchivistAgent())
        .Run(ctx);

    ctx.Insurance = vault.ComputeInsurance(ctx.Dependencies);
    Console.WriteLine($"\n[INSURANCE] Status: {ctx.Insurance}  (vault: {vaultRoot})");

    var outcome = Coordinator.Summarize(ctx);
    Console.WriteLine($"\n=== RESURRECTION COMPLETE ===");
    Console.WriteLine($"Language:    {outcome.Language ?? "unknown"}");
    Console.WriteLine($"Build:       {outcome.BuildSystem ?? "unknown"}");
    Console.WriteLine($"Deps:        {outcome.DependencyCount}");
    Console.WriteLine($"Security:    {outcome.SecurityFindingCount} finding(s)");
    Console.WriteLine($"Environment: {outcome.Environment.Count} finding(s)");
    Console.WriteLine($"Recovery:    {new JudgeAgent().Verdict(outcome.Recovery)}");
    Console.WriteLine($"Capsule:     {outcome.CapsulePath}");
    Console.WriteLine();
}
