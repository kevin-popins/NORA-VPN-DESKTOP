#if WINDOWS
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nvp;

// Separate invocations verify the real store boundary with synthetic data only.
internal static class NoraSubscriptionRestartSelfTests
{
    private const string TechnicalText = "  Технические работы e\u0301 🌐  ";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(TextWriter output, string fixtureName, string phase)
    {
        var previousRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        try
        {
            Require(phase is "write" or "read", "phase must be write or read");
            var root = FixtureRoot(fixtureName);
            var subscriptions = Path.Combine(root, "subscriptions");
            var expectedPath = Path.Combine(root, "restart-expected.json");
            if (phase == "write")
                Require(!Directory.Exists(subscriptions) && !File.Exists(subscriptions) && !File.Exists(expectedPath) && !Directory.Exists(expectedPath), "write refuses an existing fixture");
            else
            {
                Require(Directory.Exists(subscriptions) && File.Exists(expectedPath), "read requires a completed write");
                RejectReparseTree(root);
            }
            Directory.CreateDirectory(root);
            RejectReparseAncestors(root);
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", root);
            if (phase == "write") Write(root, expectedPath, fixtureName);
            else Read(root, expectedPath);
            output.WriteLine($"SUBSCRIPTION RESTART {phase.ToUpperInvariant()} PASS: technical/current/retained rows and display policy; process={Environment.ProcessId}");
            return 0;
        }
        catch (Exception exception) { output.WriteLine("SUBSCRIPTION RESTART FAIL: " + exception.GetType().Name + ": " + exception.Message); return 1; }
        finally { Environment.SetEnvironmentVariable("NORA_DATA_ROOT", previousRoot); }
    }

    private static void Write(string root, string expectedPath, string fixtureName)
    {
        var now = DateTimeOffset.UtcNow;
        var source = "https://restart.example.test/protected/" + fixtureName;
        const string real = "vless://11111111-1111-4111-8111-111111111111@198.51.100.43:443?security=tls&type=ws&path=%2Ffixture#Protected-fixture";
        const string ordinaryPayload = "vless://22222222-2222-4222-8222-222222222222@203.0.113.47:443?security=tls&type=ws&path=%2Ffixture#Ordinary-fixture";
        var initial = NoraSubscriptionStore.ParsePayload(real, source, new Dictionary<string, string>(), _ => { }, utcNow: now);
        var envelope = NoraSubscriptionMetadata.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            v = 1, mode = "full", revision = 1, issued_at = now.ToUnixTimeSeconds(), ttl_seconds = 300,
            display = new { @protected = true, public_label = "Перезапуск 🌐 / Restart" }
        })));
        initial.AppMetadata = NoraSubscriptionMetadata.ParseHeader(envelope, NoraSubscriptionMetadata.BindSource(source), now)!;
        NoraSubscriptionStore.Store(initial, source, utcNow: now);
        var active = initial.Servers.Single();
        NoraAppState.SaveActiveProfilePath(active.LocalPath);
        var originalIdentity = NoraSubscriptionStore.RuntimeIdentity(active);
        var selectedPath = Hash(Path.GetFullPath(active.LocalPath));
        var ordinary = NoraSubscriptionStore.ParsePayload(ordinaryPayload, source + "-ordinary", new Dictionary<string, string>(), _ => { }, utcNow: now);
        NoraSubscriptionStore.Store(ordinary, source + "-ordinary", utcNow: now);
        Require(NoraSubscriptionStore.MoveSubscription(ordinary.Id, -1), "actual persisted reorder");
        var marker = "vless://" + NoraLegacySubscriptionNotice.MarkerId + "@127.0.0.1:1?security=none&encryption=none&type=tcp#" + Uri.EscapeDataString(TechnicalText);
        var updated = NoraSubscriptionStore.ParsePayload(marker, source, new Dictionary<string, string>(), _ => { }, utcNow: now);
        NoraSubscriptionStore.Store(updated, source, utcNow: now);
        Require(updated.Servers.Count == 1 && updated.Servers[0].IsTechnical && updated.Servers[0].Name == TechnicalText, "technical row is the current list");
        Require(updated.RetainedServers.Count == 1 && updated.RetainedServers[0].Id == active.Id && updated.RetainedServers[0].IsRetained, "real profile stays private and retained");
        Require(NoraSubscriptionStore.RuntimeIdentity(updated.RetainedServers[0]) == originalIdentity && Hash(Path.GetFullPath(updated.RetainedServers[0].LocalPath)) == selectedPath,
            "retained identity and selected path remain exact");
        Require(!active.IsRetained && !NoraSubscriptionStore.IsUsableServer(active), "old active object is untouched but cannot begin a new dial");
        var loaded = NoraSubscriptionStore.LoadAll();
        Require(loaded.Count == 2 && loaded[0].Id == ordinary.Id, "subscription order survives technical refresh");
        File.WriteAllText(expectedPath, JsonSerializer.Serialize(new Expected
        {
            WriterProcessId = Environment.ProcessId, ProtectedId = initial.Id, PublicLabel = updated.AppMetadata.PublicLabel,
            Revision = updated.AppMetadata.Revision, IssuedAtUnix = updated.AppMetadata.IssuedAtUnix, TtlSeconds = updated.AppMetadata.TtlSeconds,
            SelectedPathSha256 = selectedPath, Subscriptions = loaded.Select(Snapshot).ToArray()
        }, JsonOptions));
        File.WriteAllText(Path.Combine(root, "restart-write.json"), JsonSerializer.Serialize(new { passed = true, processId = Environment.ProcessId, currentRows = loaded.Sum(subscription => subscription.Servers.Count), retainedRows = loaded.Sum(subscription => subscription.RetainedServers.Count) }, JsonOptions));
    }

    private static void Read(string root, string expectedPath)
    {
        var expected = JsonSerializer.Deserialize<Expected>(File.ReadAllText(expectedPath), JsonOptions) ?? throw new InvalidOperationException("missing expectations");
        Require(expected.WriterProcessId != Environment.ProcessId, "read must use a fresh process");
        var loaded = NoraSubscriptionStore.LoadAll();
        Require(loaded.Count == expected.Subscriptions.Length, "exact subscription count");
        for (var index = 0; index < loaded.Count; index++)
            Require(JsonSerializer.Serialize(Snapshot(loaded[index])) == JsonSerializer.Serialize(expected.Subscriptions[index]), "current/retained identity, raw bytes, text and provider order survive restart");
        var protectedSubscription = loaded.Single(subscription => subscription.Id == expected.ProtectedId);
        var technical = protectedSubscription.Servers.Single();
        var retained = protectedSubscription.RetainedServers.Single();
        var policy = NoraSubscriptionMetadata.EffectiveFor(technical);
        Require(technical.IsTechnical && technical.Name == TechnicalText && !NoraSubscriptionStore.IsUsableServer(technical), "technical row cannot dial");
        Require(retained.IsRetained && !NoraSubscriptionStore.IsUsableServer(retained), "retained data is not current transport");
        Require(policy.Protected && policy.PublicLabel == expected.PublicLabel && policy.Revision == expected.Revision && policy.IssuedAtUnix == expected.IssuedAtUnix && policy.TtlSeconds == expected.TtlSeconds,
            "display policy and original replay/TTL fields survive");
        Require(Hash(Path.GetFullPath(retained.LocalPath)) == expected.SelectedPathSha256 && NoraAppState.TryLoadActiveProfilePath() == retained.LocalPath, "selected path remains exact");
        Require(!NoraSubscriptionStore.TryLoadServer(technical.LocalPath, out _) && NoraSubscriptionStore.TryLoadServer(retained.LocalPath, out var saved) && saved.IsRetained && !NoraSubscriptionStore.IsUsableServer(saved), "restart loader rejects technical dial and identifies retained selection");
        var effects = 0;
        Require(!NoraProtectedDisplay.TryDisclose(retained, () => effects++) && effects == 0, "protected retained callback blocked before side effect");
        Require(Blocked(() => NoraSubscriptionStore.BuildXrayConfig(technical)) && Blocked(() => NoraSubscriptionStore.BuildXrayConfig(retained)), "core serialization refuses display/retained rows");
        var ordinary = loaded.Single(subscription => subscription.Id != expected.ProtectedId).Servers.Single();
        Require(NoraSubscriptionStore.IsUsableServer(ordinary) && !NoraSubscriptionMetadata.EffectiveFor(ordinary).Protected, "other subscription stays usable");
        Require(NoraProtectedDisplay.TryDisclose(ordinary, () => effects++) && effects == 1, "ordinary inert callback still works");
        File.WriteAllText(Path.Combine(root, "restart-read.json"), JsonSerializer.Serialize(new { passed = true, writerProcessId = expected.WriterProcessId, readerProcessId = Environment.ProcessId, technicalRows = 1, retainedRows = 1, selectedPathSha256 = expected.SelectedPathSha256, protectedEffects = 0, ordinaryEffects = effects }, JsonOptions));
    }

    private static SubscriptionSnapshot Snapshot(NoraSubscriptionInfo subscription) => new()
    {
        Id = subscription.Id, DisplayOrder = subscription.DisplayOrder,
        Current = subscription.Servers.Select(Node).ToArray(), Retained = subscription.RetainedServers.Select(Node).ToArray()
    };
    private static NodeSnapshot Node(NoraSubscriptionServer server) => new()
    {
        Id = server.Id, Name = server.Name, DisplayOrder = server.DisplayOrder, IsTechnical = server.IsTechnical, IsRetained = server.IsRetained,
        RuntimeIdentity = server.IsTechnical ? "" : NoraSubscriptionStore.RuntimeIdentity(server), RawSha256 = Hash(server.RawConfigJson),
        LocalPathSha256 = Hash(Path.GetFullPath(server.LocalPath)), SourceLinkSha256 = Hash(server.SourceLink)
    };
    private static bool Blocked(Action action) { try { action(); return false; } catch (NoraAppException) { return true; } }
    private static string FixtureRoot(string name)
    {
        Require(Regex.IsMatch(name, "\\A[a-z0-9-]{1,64}\\z", RegexOptions.CultureInvariant), "invalid fixture name");
        var explicitRoot = Environment.GetEnvironmentVariable("NORA_SELFTEST_ROOT");
        Require(!string.IsNullOrWhiteSpace(explicitRoot) && Path.IsPathFullyQualified(explicitRoot), "NORA_SELFTEST_ROOT must be explicit absolute path");
        var parent = Path.GetFullPath(explicitRoot!);
        Require(!parent.StartsWith("\\\\", StringComparison.Ordinal), "fixture root must be local");
        var root = Path.GetFullPath(Path.Combine(parent, name));
        Require(Within(root, parent), "fixture escapes explicit root");
        var installed = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA VPN"));
        Require(!Same(root, installed) && !Within(root, installed) && !Within(installed, root), "fixture overlaps installed data");
        RejectReparseAncestors(root); return root;
    }
    private static void RejectReparseAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) || File.Exists(current)) Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "fixture traverses reparse point");
    }
    private static void RejectReparseTree(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                Require((attributes & FileAttributes.ReparsePoint) == 0, "fixture contains reparse point");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
    }
    private static bool Same(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string child, string parent) => child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Expected
    {
        public int WriterProcessId { get; set; }
        public string ProtectedId { get; set; } = "";
        public string PublicLabel { get; set; } = "";
        public long Revision { get; set; }
        public long IssuedAtUnix { get; set; }
        public int TtlSeconds { get; set; }
        public string SelectedPathSha256 { get; set; } = "";
        public SubscriptionSnapshot[] Subscriptions { get; set; } = [];
    }
    private sealed class SubscriptionSnapshot
    {
        public string Id { get; set; } = "";
        public int DisplayOrder { get; set; }
        public NodeSnapshot[] Current { get; set; } = [];
        public NodeSnapshot[] Retained { get; set; } = [];
    }
    private sealed class NodeSnapshot
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int DisplayOrder { get; set; }
        public bool IsTechnical { get; set; }
        public bool IsRetained { get; set; }
        public string RuntimeIdentity { get; set; } = "";
        public string RawSha256 { get; set; } = "";
        public string LocalPathSha256 { get; set; } = "";
        public string SourceLinkSha256 { get; set; } = "";
    }
}
#endif
