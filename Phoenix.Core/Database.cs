using System.Data;
using Microsoft.Data.Sqlite;

namespace Phoenix.Core;

public sealed class PhoenixDb : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly string _dbPath;

    public PhoenixDb(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Migrate();
    }

    private void Migrate()
    {
        EnsureVersionTable();
        var version = GetVersion();
        foreach (var (v, sql) in Migrations)
        {
            if (v <= version) continue;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            SetVersion(v);
        }
    }

    private void EnsureVersionTable()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_utc INTEGER NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    private int GetVersion()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void SetVersion(int v)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO schema_version (version, applied_utc) VALUES (@v, @t);";
        cmd.Parameters.AddWithValue("@v", v);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.Ticks);
        cmd.ExecuteNonQuery();
    }

    private static readonly List<(int, string)> Migrations = new()
    {
        (1, @"
CREATE TABLE IF NOT EXISTS projects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    source_path TEXT,
    fingerprint TEXT,
    ecosystem TEXT,
    build_system TEXT,
    detected_language TEXT,
    created_utc INTEGER,
    status TEXT NOT NULL DEFAULT 'new'
);
CREATE TABLE IF NOT EXISTS files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    absolute_path TEXT NOT NULL,
    sha256 TEXT NOT NULL,
    size INTEGER NOT NULL,
    classification TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(id)
);
CREATE TABLE IF NOT EXISTS snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    snapshot_path TEXT NOT NULL,
    sha256 TEXT NOT NULL,
    created_utc INTEGER NOT NULL,
    kind TEXT NOT NULL,
    FOREIGN KEY (project_id) REFERENCES projects(id)
);
CREATE TABLE IF NOT EXISTS dependencies (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    version TEXT,
    ecosystem TEXT NOT NULL,
    scope TEXT,
    state TEXT NOT NULL,
    registry TEXT,
    source_url TEXT,
    license TEXT,
    risk_score INTEGER,
    local_cache_status TEXT,
    archive_status TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(id)
);
CREATE TABLE IF NOT EXISTS environments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    os TEXT,
    architecture TEXT,
    runtime TEXT,
    compiler TEXT,
    sdk TEXT,
    package_manager TEXT,
    runtime_version TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(id)
);
CREATE TABLE IF NOT EXISTS builds (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    attempt INTEGER,
    started_utc INTEGER,
    finished_utc INTEGER,
    exit_code INTEGER,
    logs TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(id)
);
CREATE TABLE IF NOT EXISTS capsules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    capsule_path TEXT NOT NULL,
    manifest_sha256 TEXT NOT NULL,
    recovery_status TEXT NOT NULL,
    created_utc INTEGER NOT NULL,
    FOREIGN KEY (project_id) REFERENCES projects(id)
);
CREATE TABLE IF NOT EXISTS checksums (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    object_key TEXT NOT NULL UNIQUE,
    sha256 TEXT NOT NULL,
    blake3 TEXT,
    size INTEGER,
    stored_utc INTEGER
);
CREATE TABLE IF NOT EXISTS risk_scores (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER,
    dependency_id INTEGER,
    score INTEGER,
    reasons TEXT,
    calculated_utc INTEGER
);
CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER,
    timestamp_utc INTEGER NOT NULL,
    operation TEXT NOT NULL,
    agent TEXT,
    tool TEXT,
    input_hash TEXT,
    output_hash TEXT,
    result TEXT
);
CREATE INDEX IF NOT EXISTS idx_files_project ON files(project_id);
CREATE INDEX IF NOT EXISTS idx_deps_project ON dependencies(project_id);
CREATE INDEX IF NOT EXISTS idx_events_project ON events(project_id);
"),
        (2, @"
CREATE TABLE IF NOT EXISTS agents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    role TEXT,
    last_active_utc INTEGER
);
CREATE TABLE IF NOT EXISTS recovery_tests (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER,
    capsule_id INTEGER,
    kind TEXT,
    result TEXT,
    ran_utc INTEGER
);
CREATE TABLE IF NOT EXISTS provider_sources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    kind TEXT,
    capability_download INTEGER,
    capability_upload INTEGER,
    capability_metadata INTEGER,
    capability_checksums INTEGER,
    availability TEXT
);
")
    };

    // ---- Write helpers ----

    public long InsertProject(string name, string? sourcePath, string? fingerprint, string? ecosystem,
        string? buildSystem, string? language)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO projects (name, source_path, fingerprint, ecosystem, build_system, detected_language, created_utc, status)
                            VALUES (@name,@sp,@fp,@eco,@bs,@lang,@c,@st);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@sp", (object?)sourcePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fp", (object?)fingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eco", (object?)ecosystem ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bs", (object?)buildSystem ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lang", (object?)language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.Ticks);
        cmd.Parameters.AddWithValue("@st", "discovered");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void InsertFile(long projectId, string rel, string abs, string sha, long size, string? classification)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO files (project_id, relative_path, absolute_path, sha256, size, classification)
                            VALUES (@p,@rel,@abs,@sha,@sz,@cl);";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@rel", rel);
        cmd.Parameters.AddWithValue("@abs", abs);
        cmd.Parameters.AddWithValue("@sha", sha);
        cmd.Parameters.AddWithValue("@sz", size);
        cmd.Parameters.AddWithValue("@cl", (object?)classification ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDependencies(long projectId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dependencies WHERE project_id=@p;";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.ExecuteNonQuery();
    }

    public long InsertDependency(long projectId, string name, string? version, string ecosystem, string scope,
        DependencyState state, string? registry, string? sourceUrl, string? license, int risk, string? cache, string? archive)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO dependencies (project_id, name, version, ecosystem, scope, state, registry, source_url, license, risk_score, local_cache_status, archive_status)
                            VALUES (@p,@n,@v,@e,@s,@st,@r,@u,@l,@rk,@c,@a);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@v", (object?)version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", ecosystem);
        cmd.Parameters.AddWithValue("@s", scope);
        cmd.Parameters.AddWithValue("@st", state.ToString());
        cmd.Parameters.AddWithValue("@r", (object?)registry ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", (object?)sourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@l", (object?)license ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rk", risk);
        cmd.Parameters.AddWithValue("@c", (object?)cache ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", (object?)archive ?? DBNull.Value);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long InsertSnapshot(long projectId, string path, string sha, string kind)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO snapshots (project_id, snapshot_path, sha256, created_utc, kind)
                            VALUES (@p,@path,@sha,@c,@k);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@sha", sha);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.Ticks);
        cmd.Parameters.AddWithValue("@k", kind);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long InsertCapsule(long projectId, string path, string manifestSha, string recoveryStatus)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO capsules (project_id, capsule_path, manifest_sha256, recovery_status, created_utc)
                            VALUES (@p,@path,@sha,@st,@c);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@sha", manifestSha);
        cmd.Parameters.AddWithValue("@st", recoveryStatus);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.Ticks);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long InsertBuild(long projectId, int attempt, long startedUtc, long finishedUtc, int exitCode, string logs)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO builds (project_id, attempt, started_utc, finished_utc, exit_code, logs)
                            VALUES (@p,@a,@s,@f,@e,@l);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@a", attempt);
        cmd.Parameters.AddWithValue("@s", startedUtc);
        cmd.Parameters.AddWithValue("@f", finishedUtc);
        cmd.Parameters.AddWithValue("@e", exitCode);
        cmd.Parameters.AddWithValue("@l", logs);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void UpdateProjectStatus(long projectId, string status)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE projects SET status=@s WHERE id=@p;";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.ExecuteNonQuery();
    }

    public void UpdateCapsuleStatus(long capsuleId, string recoveryStatus)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE capsules SET recovery_status=@s WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", capsuleId);
        cmd.Parameters.AddWithValue("@s", recoveryStatus);
        cmd.ExecuteNonQuery();
    }

    public long InsertEnvironment(long projectId, string? os, string? arch, string? runtime, string? compiler,
        string? sdk, string? pkgMgr, string? runtimeVer)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO environments (project_id, os, architecture, runtime, compiler, sdk, package_manager, runtime_version)
                            VALUES (@p,@o,@a,@r,@c,@s,@m,@rv);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@o", (object?)os ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", (object?)arch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", (object?)runtime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", (object?)compiler ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", (object?)sdk ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", (object?)pkgMgr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rv", (object?)runtimeVer ?? DBNull.Value);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void UpdateDependencyState(long dependencyId, DependencyState state, int? riskScore = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = riskScore.HasValue
            ? "UPDATE dependencies SET state=@s, risk_score=@r WHERE id=@id;"
            : "UPDATE dependencies SET state=@s WHERE id=@id;";
        cmd.Parameters.AddWithValue("@s", state.ToString());
        cmd.Parameters.AddWithValue("@id", dependencyId);
        if (riskScore.HasValue) cmd.Parameters.AddWithValue("@r", riskScore.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<(string Project, string Dependency, string? Version, string Ecosystem)> GetAllDependenciesWithProject()
    {
        var list = new List<(string, string, string?, string)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT p.name, d.name, d.version, d.ecosystem FROM dependencies d JOIN projects p ON p.id=d.project_id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3)));
        return list;
    }

    public int CountProjects() => ScalarInt("SELECT COUNT(*) FROM projects;");
    public int CountProjectsByStatus(string status) => ScalarInt("SELECT COUNT(*) FROM projects WHERE status=@s;", ("@s", status));
    public int CountDependencies() => ScalarInt("SELECT COUNT(*) FROM dependencies;");
    public int CountDependenciesByState(DependencyState state) => ScalarInt("SELECT COUNT(*) FROM dependencies WHERE state=@s;", ("@s", state.ToString()));
    public int CountDependenciesWithVersion() => ScalarInt("SELECT COUNT(*) FROM dependencies WHERE version IS NOT NULL AND version <> '';");
    public int CountRecoveryTestsByResult(string result) => ScalarInt("SELECT COUNT(*) FROM recovery_tests WHERE result=@r;", ("@r", result));

    public IReadOnlyList<ProjectRow> GetProjects()
    {
        var list = new List<ProjectRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, status, fingerprint, created_utc FROM projects ORDER BY id DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProjectRow(r.GetInt64(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4)));
        return list;
    }

    public IReadOnlyList<DependencyRecord> GetDependenciesByState(DependencyState state)
    {
        var list = new List<DependencyRecord>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, project_id, name, version, ecosystem, scope, state, registry, source_url, license, risk_score, local_cache_status, archive_status FROM dependencies WHERE state=@s;";
        cmd.Parameters.AddWithValue("@s", state.ToString());
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DependencyRecord(r.GetInt64(0), r.GetInt64(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5),
                Enum.Parse<DependencyState>(r.GetString(6)), r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
                r.GetInt32(10), r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12)));
        return list;
    }

    private int ScalarInt(string sql, params (string, string)[] param)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in param) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void InsertRecoveryTest(long? projectId, long? capsuleId, string kind, string result)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO recovery_tests (project_id, capsule_id, kind, result, ran_utc)
                            VALUES (@p,@c,@k,@r,@t);";
        cmd.Parameters.AddWithValue("@p", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", (object?)capsuleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@k", kind);
        cmd.Parameters.AddWithValue("@r", result);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.Ticks);
        cmd.ExecuteNonQuery();
    }

    public void InsertEvent(long? projectId, string operation, string? agent, string? tool, string? inputHash,
        string? outputHash, string result)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO events (project_id, timestamp_utc, operation, agent, tool, input_hash, output_hash, result)
                            VALUES (@p,@t,@op,@ag,@tl,@ih,@oh,@r);";
        cmd.Parameters.AddWithValue("@p", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.Ticks);
        cmd.Parameters.AddWithValue("@op", operation);
        cmd.Parameters.AddWithValue("@ag", (object?)agent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tl", (object?)tool ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ih", (object?)inputHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@oh", (object?)outputHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", result);
        cmd.ExecuteNonQuery();
    }

    public void UpsertChecksum(string key, string sha, long size, string? blake3 = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO checksums (object_key, sha256, blake3, size, stored_utc)
                            VALUES (@k,@s,@b,@sz,@c)
                            ON CONFLICT(object_key) DO UPDATE SET sha256=excluded.sha256, blake3=excluded.blake3, size=excluded.size, stored_utc=excluded.stored_utc;";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@s", sha);
        cmd.Parameters.AddWithValue("@b", (object?)blake3 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sz", size);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.Ticks);
        cmd.ExecuteNonQuery();
    }

    public ProjectRecord? GetProject(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,source_path,fingerprint,ecosystem,build_system,detected_language,created_utc,status FROM projects WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ProjectRecord(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetInt64(7), r.GetString(8));
    }

    public long ProjectIdBySourcePath(string sourcePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM projects WHERE source_path=@p LIMIT 1;";
        cmd.Parameters.AddWithValue("@p", sourcePath);
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? 0 : Convert.ToInt64(v);
    }

    public IReadOnlyList<DependencyRecord> GetDependencies(long projectId)
    {
        var list = new List<DependencyRecord>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id,project_id,name,version,ecosystem,scope,state,registry,source_url,license,risk_score,local_cache_status,archive_status FROM dependencies WHERE project_id=@p;";
        cmd.Parameters.AddWithValue("@p", projectId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DependencyRecord(r.GetInt64(0), r.GetInt64(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5),
                Enum.TryParse<DependencyState>(r.GetString(6), out var s) ? s : DependencyState.Unknown,
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? 0 : r.GetInt32(10),
                r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return list;
    }

    public void Dispose()
    {
        _conn.Dispose();
        GC.SuppressFinalize(this);
    }
}
