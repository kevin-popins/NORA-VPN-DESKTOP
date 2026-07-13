using Microsoft.Data.Sqlite;

namespace NoraPremium.Service;

internal sealed class PremiumDb
{
    private readonly string _connectionString;
    private readonly PremiumCrypto _crypto;

    public PremiumDb(string dataRoot, PremiumCrypto crypto)
    {
        Directory.CreateDirectory(dataRoot);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataRoot, "premium.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        _crypto = crypto;
    }

    public void Initialize(string bootstrapPassword)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS admins (
                username TEXT PRIMARY KEY,
                password_hash TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS licenses (
                id TEXT PRIMARY KEY,
                code_hash TEXT NOT NULL UNIQUE,
                code_last4 TEXT NOT NULL,
                note TEXT NOT NULL DEFAULT '',
                max_devices INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NULL,
                revoked_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS activations (
                id TEXT PRIMARY KEY,
                license_id TEXT NOT NULL REFERENCES licenses(id) ON DELETE CASCADE,
                installation_hash TEXT NOT NULL,
                installation_label TEXT NOT NULL,
                app_version TEXT NOT NULL,
                first_seen_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL,
                revoked_at TEXT NULL,
                UNIQUE(license_id, installation_hash)
            );
            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                action TEXT NOT NULL,
                detail TEXT NOT NULL,
                remote_ip TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_activations_license ON activations(license_id);
            CREATE INDEX IF NOT EXISTS ix_audit_created ON audit_log(created_at DESC);
            """;
        command.ExecuteNonQuery();

        using var count = db.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM admins";
        if (Convert.ToInt32(count.ExecuteScalar()) == 0)
        {
            if (bootstrapPassword.Length < 14)
                throw new InvalidOperationException("Bootstrap admin password must be at least 14 characters.");
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO admins(username,password_hash,updated_at) VALUES('admin',$hash,$now)";
            insert.Parameters.AddWithValue("$hash", PremiumCrypto.HashPassword(bootstrapPassword));
            insert.Parameters.AddWithValue("$now", Now());
            insert.ExecuteNonQuery();
        }
    }

    public bool VerifyAdmin(string username, string password)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT password_hash FROM admins WHERE username=$username";
        command.Parameters.AddWithValue("$username", username.Trim().ToLowerInvariant());
        return command.ExecuteScalar() is string hash && PremiumCrypto.VerifyPassword(password, hash);
    }

    public bool ChangePassword(string username, string currentPassword, string newPassword)
    {
        if (newPassword.Length < 14 || !VerifyAdmin(username, currentPassword))
            return false;
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE admins SET password_hash=$hash,updated_at=$now WHERE username=$username";
        command.Parameters.AddWithValue("$hash", PremiumCrypto.HashPassword(newPassword));
        command.Parameters.AddWithValue("$now", Now());
        command.Parameters.AddWithValue("$username", username);
        return command.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<string> CreateLicenses(int count, int maxDevices, string note, int validDays, string remoteIp)
    {
        count = Math.Clamp(count, 1, 100);
        maxDevices = Math.Clamp(maxDevices, 1, 20);
        note = (note ?? "").Trim();
        if (note.Length > 160)
            note = note[..160];
        var codes = new List<string>(count);
        using var db = Open();
        using var tx = db.BeginTransaction();
        for (var i = 0; i < count; i++)
        {
            var code = _crypto.GenerateCode();
            var normalized = PremiumCrypto.NormalizeCode(code);
            using var command = db.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO licenses(id,code_hash,code_last4,note,max_devices,created_at,expires_at)
                VALUES($id,$hash,$last4,$note,$max,$created,$expires)
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$hash", _crypto.DigestCode(normalized));
            command.Parameters.AddWithValue("$last4", normalized[^4..]);
            command.Parameters.AddWithValue("$note", note);
            command.Parameters.AddWithValue("$max", maxDevices);
            command.Parameters.AddWithValue("$created", Now());
            command.Parameters.AddWithValue("$expires", validDays > 0 ? DateTimeOffset.UtcNow.AddDays(Math.Clamp(validDays, 1, 3650)).ToString("O") : DBNull.Value);
            command.ExecuteNonQuery();
            codes.Add(code);
        }
        Audit(db, tx, "codes.generated", $"count={count}; max_devices={maxDevices}; note={note}", remoteIp);
        tx.Commit();
        return codes;
    }

    public (string Status, ActivationGrant? Grant) Activate(string normalizedCode, string installationId, string appVersion, string remoteIp)
    {
        var codeHash = _crypto.DigestCode(normalizedCode);
        var installHash = _crypto.DigestInstallation(installationId);
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var license = db.CreateCommand();
        license.Transaction = tx;
        license.CommandText = "SELECT id,max_devices,expires_at,revoked_at FROM licenses WHERE code_hash=$hash";
        license.Parameters.AddWithValue("$hash", codeHash);
        using var reader = license.ExecuteReader();
        if (!reader.Read())
            return ("invalid_code", null);
        var licenseId = reader.GetString(0);
        var maxDevices = reader.GetInt32(1);
        var expiresAt = reader.IsDBNull(2) ? null : reader.GetString(2);
        var revokedAt = reader.IsDBNull(3) ? null : reader.GetString(3);
        reader.Close();
        if (revokedAt is not null)
            return ("revoked", null);
        if (expiresAt is not null && DateTimeOffset.Parse(expiresAt) <= DateTimeOffset.UtcNow)
            return ("expired", null);

        using var existing = db.CreateCommand();
        existing.Transaction = tx;
        existing.CommandText = "SELECT id,revoked_at FROM activations WHERE license_id=$license AND installation_hash=$install";
        existing.Parameters.AddWithValue("$license", licenseId);
        existing.Parameters.AddWithValue("$install", installHash);
        using var existingReader = existing.ExecuteReader();
        if (existingReader.Read())
        {
            var activationId = existingReader.GetString(0);
            var activationRevoked = existingReader.IsDBNull(1) ? null : existingReader.GetString(1);
            existingReader.Close();
            if (activationRevoked is not null)
                return ("device_revoked", null);
            TouchActivation(db, tx, activationId, appVersion);
            Audit(db, tx, "activation.refreshed", $"license={licenseId}; activation={activationId}", remoteIp);
            tx.Commit();
            return ("active", new ActivationGrant(licenseId, activationId));
        }
        existingReader.Close();

        using var count = db.CreateCommand();
        count.Transaction = tx;
        count.CommandText = "SELECT COUNT(*) FROM activations WHERE license_id=$license AND revoked_at IS NULL";
        count.Parameters.AddWithValue("$license", licenseId);
        if (Convert.ToInt32(count.ExecuteScalar()) >= maxDevices)
            return ("activation_limit", null);

        var id = Guid.NewGuid().ToString("N");
        var now = Now();
        using var insert = db.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO activations(id,license_id,installation_hash,installation_label,app_version,first_seen_at,last_seen_at)
            VALUES($id,$license,$hash,$label,$version,$now,$now)
            """;
        insert.Parameters.AddWithValue("$id", id);
        insert.Parameters.AddWithValue("$license", licenseId);
        insert.Parameters.AddWithValue("$hash", installHash);
        insert.Parameters.AddWithValue("$label", installHash[..12]);
        insert.Parameters.AddWithValue("$version", CleanVersion(appVersion));
        insert.Parameters.AddWithValue("$now", now);
        insert.ExecuteNonQuery();
        Audit(db, tx, "activation.created", $"license={licenseId}; activation={id}", remoteIp);
        tx.Commit();
        return ("active", new ActivationGrant(licenseId, id));
    }

    public bool RefreshAllowed(string licenseId, string activationId, string installationId, string appVersion, string remoteIp)
    {
        var installHash = _crypto.DigestInstallation(installationId);
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT l.expires_at,l.revoked_at,a.revoked_at
            FROM licenses l JOIN activations a ON a.license_id=l.id
            WHERE l.id=$license AND a.id=$activation AND a.installation_hash=$install
            """;
        command.Parameters.AddWithValue("$license", licenseId);
        command.Parameters.AddWithValue("$activation", activationId);
        command.Parameters.AddWithValue("$install", installHash);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;
        var expiresAt = reader.IsDBNull(0) ? null : reader.GetString(0);
        var licenseRevoked = reader.IsDBNull(1) ? null : reader.GetString(1);
        var activationRevoked = reader.IsDBNull(2) ? null : reader.GetString(2);
        reader.Close();
        if (licenseRevoked is not null || activationRevoked is not null)
            return false;
        if (expiresAt is not null && DateTimeOffset.Parse(expiresAt) <= DateTimeOffset.UtcNow)
            return false;
        TouchActivation(db, tx, activationId, appVersion);
        Audit(db, tx, "token.refreshed", $"license={licenseId}; activation={activationId}", remoteIp);
        tx.Commit();
        return true;
    }

    public DashboardData Dashboard()
    {
        using var db = Open();
        int Scalar(string sql)
        {
            using var c = db.CreateCommand();
            c.CommandText = sql;
            return Convert.ToInt32(c.ExecuteScalar());
        }
        var licenses = new List<LicenseRecord>();
        using (var command = db.CreateCommand())
        {
            command.CommandText = """
                SELECT l.id,l.code_last4,l.note,l.max_devices,l.created_at,l.expires_at,l.revoked_at,
                    SUM(CASE WHEN a.id IS NOT NULL AND a.revoked_at IS NULL THEN 1 ELSE 0 END),COUNT(a.id)
                FROM licenses l LEFT JOIN activations a ON a.license_id=l.id
                GROUP BY l.id ORDER BY l.created_at DESC LIMIT 300
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                licenses.Add(new LicenseRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), NullableString(reader, 5), NullableString(reader, 6), reader.GetInt32(7), reader.GetInt32(8)));
        }
        var activations = new List<ActivationRecord>();
        using (var command = db.CreateCommand())
        {
            command.CommandText = """
                SELECT a.id,a.license_id,l.code_last4,a.installation_label,a.app_version,a.first_seen_at,a.last_seen_at,a.revoked_at
                FROM activations a JOIN licenses l ON l.id=a.license_id
                ORDER BY a.last_seen_at DESC LIMIT 300
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                activations.Add(new ActivationRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), NullableString(reader, 7)));
        }
        var audit = new List<AuditRecord>();
        using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT created_at,action,detail,remote_ip FROM audit_log ORDER BY id DESC LIMIT 120";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                audit.Add(new AuditRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        return new DashboardData(
            Scalar("SELECT COUNT(*) FROM licenses"),
            Scalar("SELECT COUNT(*) FROM licenses WHERE revoked_at IS NULL AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)"),
            Scalar("SELECT COUNT(*) FROM licenses WHERE revoked_at IS NOT NULL"),
            Scalar("SELECT COUNT(*) FROM activations WHERE revoked_at IS NULL"),
            licenses, activations, audit);
    }

    public void SetLicenseRevoked(string id, bool revoked, string remoteIp)
        => SetRevoked("licenses", id, revoked, remoteIp, "license");

    public void SetActivationRevoked(string id, bool revoked, string remoteIp)
        => SetRevoked("activations", id, revoked, remoteIp, "activation");

    public void WriteAudit(string action, string detail, string remoteIp)
    {
        using var db = Open();
        Audit(db, null, action, detail, remoteIp);
    }

    private void SetRevoked(string table, string id, bool revoked, string remoteIp, string kind)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"UPDATE {table} SET revoked_at=$value WHERE id=$id";
        command.Parameters.AddWithValue("$value", revoked ? Now() : DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        Audit(db, tx, $"{kind}.{(revoked ? "revoked" : "restored")}", $"{kind}={id}", remoteIp);
        tx.Commit();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        using var pragma = db.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return db;
    }

    private static void TouchActivation(SqliteConnection db, SqliteTransaction tx, string id, string appVersion)
    {
        using var touch = db.CreateCommand();
        touch.Transaction = tx;
        touch.CommandText = "UPDATE activations SET last_seen_at=$now,app_version=$version WHERE id=$id";
        touch.Parameters.AddWithValue("$now", Now());
        touch.Parameters.AddWithValue("$version", CleanVersion(appVersion));
        touch.Parameters.AddWithValue("$id", id);
        touch.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection db, SqliteTransaction? tx, string action, string detail, string remoteIp)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "INSERT INTO audit_log(created_at,action,detail,remote_ip) VALUES($now,$action,$detail,$ip)";
        command.Parameters.AddWithValue("$now", Now());
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$detail", detail.Length > 400 ? detail[..400] : detail);
        command.Parameters.AddWithValue("$ip", remoteIp.Length > 64 ? remoteIp[..64] : remoteIp);
        command.ExecuteNonQuery();
    }

    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private static string CleanVersion(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim()[..Math.Min(40, value.Trim().Length)];
}
