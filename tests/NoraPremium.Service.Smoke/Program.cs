using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NoraPremium.Service;

var root = Path.Combine(Path.GetTempPath(), "nora-premium-smoke-" + Guid.NewGuid().ToString("N"));
var checks = new List<string>();

void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException("FAILED: " + name);
    checks.Add(name);
    Console.WriteLine("PASS  " + name);
}

try
{
    Directory.CreateDirectory(root);
    var pepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    using var crypto = new PremiumCrypto(root, pepper);
    var db = new PremiumDb(root, crypto);
    const string adminPassword = "smoke-admin-password-2026";
    db.Initialize(adminPassword);

    Check(db.VerifyAdmin("admin", adminPassword), "admin accepts correct password");
    Check(!db.VerifyAdmin("admin", "wrong-password"), "admin rejects wrong password");
    Check(PremiumCrypto.NormalizeCode("bad-code") == "", "invalid code format rejected");

    var code = db.CreateLicenses(1, 2, "smoke base", 0, "127.0.0.1").Single();
    var normalized = PremiumCrypto.NormalizeCode(code);
    Check(normalized.Length == 27, "generated code normalizes");
    Check(PremiumCrypto.NormalizeCode(code.ToLowerInvariant()) == normalized, "code normalization is case-insensitive");
    byte[] databaseBytes;
    using (var databaseFile = new FileStream(Path.Combine(root, "premium.db"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
        databaseBytes = new byte[databaseFile.Length];
        databaseFile.ReadExactly(databaseBytes);
    }
    var databaseText = Encoding.UTF8.GetString(databaseBytes);
    Check(!databaseText.Contains(code, StringComparison.Ordinal), "plaintext code absent from database");

    var install1 = Guid.NewGuid().ToString("D");
    var install2 = Guid.NewGuid().ToString("D");
    var install3 = Guid.NewGuid().ToString("D");
    var first = db.Activate(normalized, install1, "1.0.0", "127.0.0.1");
    Check(first is { Status: "active", Grant: not null }, "first device activates");
    var idempotent = db.Activate(normalized, install1, "1.0.1", "127.0.0.1");
    Check(idempotent.Grant?.ActivationId == first.Grant?.ActivationId, "same installation is idempotent");
    Check(db.Activate(normalized, install2, "1.0.0", "127.0.0.1").Status == "active", "second device activates");
    Check(db.Activate(normalized, install3, "1.0.0", "127.0.0.1").Status == "activation_limit", "device limit enforced");
    Check(db.RefreshAllowed(first.Grant!.LicenseId, first.Grant.ActivationId, install1, "1.0.2", "127.0.0.1"), "active device refreshes");
    Check(!db.RefreshAllowed(first.Grant.LicenseId, first.Grant.ActivationId, Guid.NewGuid().ToString("D"), "1.0.2", "127.0.0.1"), "refresh is installation-bound");

    db.SetActivationRevoked(first.Grant.ActivationId, true, "127.0.0.1");
    Check(!db.RefreshAllowed(first.Grant.LicenseId, first.Grant.ActivationId, install1, "1.0.2", "127.0.0.1"), "revoked device cannot refresh");
    Check(db.Activate(normalized, install1, "1.0.2", "127.0.0.1").Status == "device_revoked", "revoked device cannot reactivate");
    db.SetActivationRevoked(first.Grant.ActivationId, false, "127.0.0.1");
    Check(db.RefreshAllowed(first.Grant.LicenseId, first.Grant.ActivationId, install1, "1.0.2", "127.0.0.1"), "restored device refreshes");

    db.SetLicenseRevoked(first.Grant.LicenseId, true, "127.0.0.1");
    Check(db.Activate(normalized, install1, "1.0.2", "127.0.0.1").Status == "revoked", "revoked license cannot activate");
    Check(!db.RefreshAllowed(first.Grant.LicenseId, first.Grant.ActivationId, install1, "1.0.2", "127.0.0.1"), "revoked license cannot refresh");
    db.SetLicenseRevoked(first.Grant.LicenseId, false, "127.0.0.1");
    Check(db.RefreshAllowed(first.Grant.LicenseId, first.Grant.ActivationId, install1, "1.0.2", "127.0.0.1"), "restored license refreshes");

    var now = DateTimeOffset.UtcNow;
    var payload = new PremiumTokenPayload(1, first.Grant.LicenseId, first.Grant.ActivationId, install1, "visual_premium",
        ["appearance.equalizer", "appearance.server_slideshow"], now.ToUnixTimeSeconds(), now.AddDays(7).ToUnixTimeSeconds(), now.AddDays(90).ToUnixTimeSeconds(), crypto.KeyId);
    var token = crypto.Sign(payload);
    Check(crypto.TryVerify(token, out var verified) && verified.InstallationId == install1, "signed token verifies");
    var tokenParts = token.Split('.');
    tokenParts[1] = (tokenParts[1][0] == 'A' ? 'B' : 'A') + tokenParts[1][1..];
    var tampered = string.Join('.', tokenParts);
    Check(!crypto.TryVerify(tampered, out _), "tampered token rejected");

    var expiringCode = db.CreateLicenses(1, 1, "smoke expired", 1, "127.0.0.1").Single();
    var expiringNormalized = PremiumCrypto.NormalizeCode(expiringCode);
    var expiredLicense = db.Dashboard().Licenses.Single(x => x.CodeLast4 == expiringNormalized[^4..]);
    using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "premium.db")}"))
    {
        connection.Open();
        using var expire = connection.CreateCommand();
        expire.CommandText = "UPDATE licenses SET expires_at=$past WHERE id=$id";
        expire.Parameters.AddWithValue("$past", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        expire.Parameters.AddWithValue("$id", expiredLicense.Id);
        expire.ExecuteNonQuery();
    }
    Check(db.Activate(expiringNormalized, Guid.NewGuid().ToString("D"), "1.0.0", "127.0.0.1").Status == "expired", "expired license rejected");

    var raceCode = db.CreateLicenses(1, 3, "smoke race", 0, "127.0.0.1").Single();
    var raceNormalized = PremiumCrypto.NormalizeCode(raceCode);
    var raceResults = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        db.Activate(raceNormalized, Guid.NewGuid().ToString("D"), "race-" + index, "127.0.0.1"))));
    var raceActive = raceResults.Count(x => x.Status == "active");
    var raceLimited = raceResults.Count(x => x.Status == "activation_limit");
    var raceLicense = db.Dashboard().Licenses.Single(x => x.CodeLast4 == raceNormalized[^4..]);
    Check(raceActive == 3 && raceLimited == 9, "concurrent activation returns deterministic outcomes");
    Check(raceLicense.ActiveDevices == 3, "concurrent activation never exceeds limit");

    var audit = db.Dashboard().Audit;
    Check(audit.Any(x => x.Action == "activation.created"), "activation audit recorded");
    Check(audit.Any(x => x.Action == "license.revoked"), "revocation audit recorded");

    Console.WriteLine($"SMOKE COMPLETE: {checks.Count} checks passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
