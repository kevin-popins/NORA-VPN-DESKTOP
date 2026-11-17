#if WINDOWS
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nvp;

// Two separate invocations exercise the actual Store/LoadAll persistence
// boundary. The caller must explicitly supply an isolated local fixture root.
internal static class NoraSubscriptionRestartSelfTests
{
    internal static int Run(TextWriter output, string fixtureName, string phase)
    {
        var previousRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        string? root = null;
        try
        {
            Require(phase is "write" or "read", "phase must be write or read");
            root = FixtureRoot(fixtureName);
            var subscriptions = Path.Combine(root, "subscriptions");
            var expectedPath = Path.Combine(root, "restart-expected.json");
            if (phase == "write")
                Require(!Directory.Exists(subscriptions) && !File.Exists(subscriptions) && !File.Exists(expectedPath) && !Directory.Exists(expectedPath), "write refuses an existing subscriptions store or expectation");
            else
            {
                Require(Directory.Exists(subscriptions) && File.Exists(expectedPath), "read requires a completed fixture write");
                RejectReparseTree(root);
            }
            Directory.CreateDirectory(root);
            RejectReparseAncestors(root);
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", root);
            if (phase == "write") Write(root, expectedPath, fixtureName);
            else Read(root, expectedPath);
            output.WriteLine($"SUBSCRIPTION RESTART {phase.ToUpperInvariant()} PASS: isolated actual store; process={Environment.ProcessId}; fixture={fixtureName}");
            return 0;
        }
        catch (Exception exception)
        {
            output.WriteLine("SUBSCRIPTION RESTART FAIL: " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
        finally { Environment.SetEnvironmentVariable("NORA_DATA_ROOT", previousRoot); }
    }

    private static void Write(string root, string expectedPath, string fixtureName)
    {
        var now = DateTimeOffset.UtcNow;
        var source = "https://restart.example.test/protected/" + fixtureName;
        var ordinarySource = "https://restart.example.test/ordinary/" + fixtureName;
        const string protectedPayload = "vless://11111111-1111-4111-8111-111111111111@198.51.100.43:443?security=tls&type=ws&path=%2Ffixture&sni=protected.example.test#Protected-fixture";
        const string ordinaryPayload = "vless://22222222-2222-4222-8222-222222222222@203.0.113.47:443?security=tls&type=ws&path=%2Ffixture&sni=ordinary.example.test#Ordinary-fixture";
        var headers = new Dictionary<string, string> { ["profile-title"] = "Synthetic protected restart" };
        var initial = NoraSubscriptionStore.ParsePayload(protectedPayload, source, headers, _ => { }, utcNow: now);
        initial.AppMetadata = Metadata(source, now, 1, "available");
        NoraSubscriptionStore.Store(initial, source, utcNow: now);
        var selected = initial.Servers.Single();
        var initialId = selected.Id;
        var initialIdentity = NoraSubscriptionStore.RuntimeIdentity(selected);
        var selectedPathHash = Hash(Path.GetFullPath(selected.LocalPath));
        var ordinary = NoraSubscriptionStore.ParsePayload(ordinaryPayload, ordinarySource,
            new Dictionary<string, string> { ["profile-title"] = "Synthetic ordinary restart" }, _ => { }, utcNow: now);
        NoraSubscriptionStore.Store(ordinary, ordinarySource, utcNow: now);
        var ordinaryBefore = Identity(ordinary);
        Require(NoraSubscriptionStore.MoveSubscription(ordinary.Id, -1), "actual persisted subscription reorder");

        // Same empty parsed candidate as a valid v1 HTTP204 notice, without any
        // HTTP handler or network request. The Store must retain its old node.
        var notice = NoraSubscriptionStore.ParsePayload("", source, headers, _ => { }, allowEmpty: true, utcNow: now);
        notice.AppMetadata = Metadata(source, now, 2, "node_maintenance");
        NoraSubscriptionStore.Store(notice, source, utcNow: now);
        Require(notice.Servers.Count == 1 && notice.Servers[0].Id == initialId, "notice preserves exact selected node ID");
        Require(NoraSubscriptionStore.RuntimeIdentity(notice.Servers[0]) == initialIdentity && Hash(Path.GetFullPath(notice.Servers[0].LocalPath)) == selectedPathHash,
            "notice preserves runtime identity and selected path");
        var loaded = NoraSubscriptionStore.LoadAll();
        Require(loaded.Count == 2 && loaded[0].Id == ordinary.Id && loaded[1].Id == initial.Id, "nontrivial order survives notice commit");
        Require(Identity(loaded[0]).RuntimeIdentity == ordinaryBefore.RuntimeIdentity && !NoraSubscriptionMetadata.EffectiveFor(loaded[0]).Protected,
            "ordinary subscription unaffected by protected notice");
        var expected = new Expected
        {
            WriterProcessId = Environment.ProcessId, ProtectedId = initial.Id, PublicLabel = notice.AppMetadata.PublicLabel,
            IssuedAtUnix = notice.AppMetadata.IssuedAtUnix, TtlSeconds = notice.AppMetadata.TtlSeconds,
            Access = notice.AppMetadata.Access, Availability = notice.AppMetadata.Availability, Message = notice.AppMetadata.Message,
            SelectionPathSha256 = selectedPathHash, Subscriptions = loaded.Select(Identity).ToArray()
        };
        File.WriteAllText(expectedPath, JsonSerializer.Serialize(expected, JsonOptions));
        File.WriteAllText(Path.Combine(root, "restart-write.json"), JsonSerializer.Serialize(new
        {
            passed = true, processId = Environment.ProcessId, actualStoreWriter = true,
            selectedNodeId = initialId, selectionPathSha256 = selectedPathHash,
            order = loaded.Select(subscription => subscription.Id), subscriptions = loaded.Count,
            nodeCount = loaded.Sum(subscription => subscription.Servers.Count), networkUsed = false, productStateWritten = false,
            guiCreated = false, queueUsed = false, audioUsed = false, coreCreated = false
        }, JsonOptions));
    }

    private static void Read(string root, string expectedPath)
    {
        var expected = JsonSerializer.Deserialize<Expected>(File.ReadAllText(expectedPath), JsonOptions)
            ?? throw new InvalidOperationException("missing fixture expectations");
        Require(expected.WriterProcessId != Environment.ProcessId, "read must run in a fresh process, not the writer process");
        var loaded = NoraSubscriptionStore.LoadAll();
        Require(loaded.Count == 2 && expected.Subscriptions.Length == 2, "exactly two subscriptions reload");
        for (var index = 0; index < loaded.Count; index++)
        {
            var actual = Identity(loaded[index]);
            var saved = expected.Subscriptions[index];
            Require(actual.SubscriptionId == saved.SubscriptionId && actual.DisplayOrder == saved.DisplayOrder && actual.NodeId == saved.NodeId && actual.NodeOrder == saved.NodeOrder,
                "persisted subscription/node identity and exact ordering");
            Require(actual.RuntimeIdentity == saved.RuntimeIdentity && actual.LocalPathSha256 == saved.LocalPathSha256 && actual.SourceLinkSha256 == saved.SourceLinkSha256,
                "persisted runtime identity, source link and selected path");
            Require(loaded[index].Servers.All(server => !NoraLegacySubscriptionNotice.IsNoticeMarker(server)), "no notice marker reloads as a connectable node");
        }
        var protectedSubscription = loaded.Single(subscription => subscription.Id == expected.ProtectedId);
        var protectedNode = protectedSubscription.Servers.Single();
        var policy = NoraSubscriptionMetadata.EffectiveFor(protectedNode);
        Require(policy.Protected && policy.CachedOnly && policy.PublicLabel == expected.PublicLabel && NoraProtectedDisplay.EndpointText(protectedNode) == expected.PublicLabel,
            "protected public-label policy and cached-only restriction survive restart");
        Require(Hash(Path.GetFullPath(protectedNode.LocalPath)) == expected.SelectionPathSha256, "selected-path hash survives restart");
        var now = DateTimeOffset.UtcNow;
        var deadline = DateTimeOffset.FromUnixTimeSeconds(expected.IssuedAtUnix).AddSeconds(expected.TtlSeconds);
        var fresh = deadline > now && DateTimeOffset.FromUnixTimeSeconds(expected.IssuedAtUnix) <= now.AddSeconds(300);
        Require(policy.Access == (fresh ? expected.Access : "unknown") && policy.Availability == (fresh ? expected.Availability : "unknown") && policy.Message == (fresh ? expected.Message : ""),
            "fresh status remains visible; expired status becomes unknown without losing policy");
        var protectedEffects = 0;
        Blocked(() => NoraProtectedDisplay.RequireDisclosure(protectedNode), () => protectedEffects++);
        Blocked(() => NoraProtectedDisplay.RequireConnectable(protectedNode), () => protectedEffects++);
        Require(!NoraProtectedDisplay.TryDisclose(protectedNode, () => protectedEffects++) && protectedEffects == 0, "protected cached guards block before every inert side effect");
        var ordinary = loaded.Single(subscription => subscription.Id != expected.ProtectedId).Servers.Single();
        var ordinaryPolicy = NoraSubscriptionMetadata.EffectiveFor(ordinary);
        Require(!ordinaryPolicy.Protected && !ordinaryPolicy.CachedOnly, "ordinary subscription remains unaffected");
        NoraProtectedDisplay.RequireDisclosure(ordinary);
        NoraProtectedDisplay.RequireConnectable(ordinary);
        var ordinaryEffects = 0;
        Require(NoraProtectedDisplay.TryDisclose(ordinary, () => ordinaryEffects++) && ordinaryEffects == 1, "ordinary inert callback still works");
        File.WriteAllText(Path.Combine(root, "restart-read.json"), JsonSerializer.Serialize(new
        {
            passed = true, writerProcessId = expected.WriterProcessId, readerProcessId = Environment.ProcessId,
            freshProcess = true, actualLoadAll = true, policy.Protected, policy.CachedOnly, policy.PublicLabel,
            statusFresh = fresh, statusDeadline = deadline, policy.Access, policy.Availability,
            protectedEffects, ordinaryEffects, selectionPathSha256 = expected.SelectionPathSha256,
            order = loaded.Select(subscription => subscription.Id), noNoticeNodes = true,
            networkUsed = false, productStateWritten = false, guiCreated = false, queueUsed = false, audioUsed = false, coreCreated = false
        }, JsonOptions));
    }

    private static NoraSubscriptionMetadataState Metadata(string source, DateTimeOffset now, int revision, string availability)
    {
        var json = JsonSerializer.Serialize(new
        {
            v = 1, mode = "full", revision, issued_at = now.ToUnixTimeSeconds(), ttl_seconds = 300,
            display = new { @protected = true, public_label = "Перезапуск 🌐 / Restart" },
            status = new { access = "active", availability, message = availability == "node_maintenance" ? "Synthetic maintenance notice" : "" }
        });
        var envelope = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return NoraSubscriptionMetadata.ParseHeader(envelope, NoraSubscriptionMetadata.BindSource(source), now)
            ?? throw new InvalidOperationException("valid synthetic metadata rejected");
    }

    private static IdentitySnapshot Identity(NoraSubscriptionInfo subscription)
    {
        var node = subscription.Servers.Single();
        return new IdentitySnapshot
        {
            SubscriptionId = subscription.Id, DisplayOrder = subscription.DisplayOrder, NodeId = node.Id, NodeOrder = node.DisplayOrder,
            RuntimeIdentity = NoraSubscriptionStore.RuntimeIdentity(node), LocalPathSha256 = Hash(Path.GetFullPath(node.LocalPath)), SourceLinkSha256 = Hash(node.SourceLink)
        };
    }

    private static string FixtureRoot(string name)
    {
        Require(Regex.IsMatch(name, "\\A[a-z0-9-]{1,64}\\z", RegexOptions.CultureInvariant), "invalid fixture name");
        var explicitRoot = Environment.GetEnvironmentVariable("NORA_SELFTEST_ROOT");
        Require(!string.IsNullOrWhiteSpace(explicitRoot) && Path.IsPathFullyQualified(explicitRoot), "NORA_SELFTEST_ROOT must be an explicit absolute path");
        var parent = Path.GetFullPath(explicitRoot!);
        Require(!parent.StartsWith("\\\\", StringComparison.Ordinal), "fixture root must be local, not a network share");
        var root = Path.GetFullPath(Path.Combine(parent, name));
        Require(Within(root, parent), "fixture path escapes its explicit root");
        var installed = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA VPN"));
        Require(!Same(root, installed) && !Within(root, installed) && !Within(installed, root), "fixture must be isolated from installed application data");
        RejectReparseAncestors(root);
        return root;
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) || File.Exists(current))
                Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "fixture path cannot traverse a reparse point");
    }
    private static void RejectReparseTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                Require((attributes & FileAttributes.ReparsePoint) == 0, "fixture cannot contain reparse points");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
    }
    private static bool Same(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string child, string parent) => child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Blocked(Action guard, Action effect)
    {
        try { guard(); effect(); }
        catch (NoraAppException) { return; }
        throw new InvalidOperationException("restricted action reached an inert side effect");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed class Expected
    {
        public int WriterProcessId { get; set; }
        public string ProtectedId { get; set; } = "";
        public string PublicLabel { get; set; } = "";
        public long IssuedAtUnix { get; set; }
        public int TtlSeconds { get; set; }
        public string Access { get; set; } = "";
        public string Availability { get; set; } = "";
        public string Message { get; set; } = "";
        public string SelectionPathSha256 { get; set; } = "";
        public IdentitySnapshot[] Subscriptions { get; set; } = [];
    }
    private sealed class IdentitySnapshot
    {
        public string SubscriptionId { get; set; } = "";
        public int DisplayOrder { get; set; }
        public string NodeId { get; set; } = "";
        public int NodeOrder { get; set; }
        public string RuntimeIdentity { get; set; } = "";
        public string LocalPathSha256 { get; set; } = "";
        public string SourceLinkSha256 { get; set; } = "";
    }
}
#endif
