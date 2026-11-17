#if WINDOWS
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Nvp;

internal static class NoraTechnicalRowSelfTests
{
    internal static int RunSelfTest(TextWriter output)
    {
        var originalRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        var root = Path.Combine(NoraSelfTestPaths.BaseDirectory, "nora-technical-rows-" + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        const string source = "https://subscription.example.test/technical-fixture";
        const string realA = "vless://11111111-1111-4111-8111-111111111111@198.51.100.10:443?security=tls&type=ws&path=%2Fexact#Real%20A";
        const string realB = "vless://22222222-2222-4222-8222-222222222222@198.51.100.11:443?security=tls&type=ws&path=%2Fsecond#Real%20B";
        const string label = "  Технические работы e\u0301 🌐 %D0%AF 🇷🇺  ";
        var marker = "vless://" + NoraLegacySubscriptionNotice.MarkerId + "@127.0.0.1:1?security=none&encryption=none&type=tcp#" + Uri.EscapeDataString(label);
        var markerJson = JsonSerializer.Serialize(new
        {
            remarks = label, outbounds = new[] { new { protocol = "vless", settings = new { vnext = new[] { new
            { address = "127.0.0.1", port = 1, users = new[] { new { id = NoraLegacySubscriptionNotice.MarkerId, encryption = "none" } } } } },
                streamSettings = new { network = "tcp", security = "none" } } }
        });
        var headers = new Dictionary<string, string>();
        NoraSubscriptionInfo Parse(string text) => NoraSubscriptionStore.ParsePayload(text, source, headers, _ => { }, utcNow: now);
        string Display(bool protect, int revision) => NoraSubscriptionMetadata.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            v = 1, mode = "full", revision, issued_at = now.ToUnixTimeSeconds(), ttl_seconds = 300,
            display = new { @protected = protect, public_label = protect ? "Protected fixture" : null }
        })));
        NoraSubscriptionMetadataState Policy(bool protect, int revision) => NoraSubscriptionMetadata.ParseHeader(Display(protect, revision), NoraSubscriptionMetadata.BindSource(source), now)!;
        async Task<NoraSubscriptionInfo> Fetch(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            var calls = 0;
            using var handler = new FixtureHandler(request => respond(request, ++calls));
            using var http = new HttpClient(handler);
            return await NoraSubscriptionStore.FetchUrlCandidateAsync(http, new Uri(source), _ => { }, "fixture-hwid", "fixture-device", utcNow: () => now);
        }
        static HttpResponseMessage Response(HttpRequestMessage request, string body, string? metadata = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent(body, Encoding.UTF8, "text/plain") };
            if (metadata is not null) response.Headers.TryAddWithoutValidation("X-Nora-Metadata", metadata);
            return response;
        }
        static bool Blocked(Action action)
        {
            try { action(); return false; }
            catch (NoraAppException) { return true; }
        }
        void ReportName(string stage, string actual, string expected)
        {
            var difference = 0;
            while (difference < actual.Length && difference < expected.Length && actual[difference] == expected[difference]) difference++;
            var equal = actual == expected;
            var actualUnit = difference < actual.Length ? ((int)actual[difference]).ToString("X4") : "end";
            var expectedUnit = difference < expected.Length ? ((int)expected[difference]).ToString("X4") : "end";
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(actual)))[..16];
            output.WriteLine($"TECHNICAL NAME {stage}: exact={equal}; utf16={actual.Length}; expected_utf16={expected.Length}; trailing_spaces={actual.Length - actual.TrimEnd(' ').Length}; first_difference={(equal ? -1 : difference)}; actual_unit={actualUnit}; expected_unit={expectedUnit}; sha256_prefix={digest}");
        }
        try
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", root);
            var exact = true;
            foreach (var format in new (string Name, string Payload)[]
            {
                ("raw", marker), ("base64", Convert.ToBase64String(Encoding.UTF8.GetBytes(marker))), ("json", markerJson)
            })
            {
                var parsedFormat = Parse(format.Payload);
                var technical = parsedFormat.Servers.FirstOrDefault();
                ReportName(format.Name, technical?.Name ?? "", label);
                output.WriteLine($"TECHNICAL FORMAT {format.Name}: rows={parsedFormat.Servers.Count}; technical={technical?.IsTechnical == true}; usable={technical is not null && NoraSubscriptionStore.IsUsableServer(technical)}");
                exact &= parsedFormat.Servers.Count == 1 && technical is not null && technical.Name == label && technical.IsTechnical && !NoraSubscriptionStore.IsUsableServer(technical);
            }
            var hashPrefix = marker[..marker.IndexOf('#')] + "##Provider text";
            ReportName("double-delimiter", Parse(hashPrefix).Servers.Single().Name, "#Provider text");
            ReportName("encoded-hash-unicode", Parse(marker[..marker.IndexOf('#')] + "#%23" + Uri.EscapeDataString(label)).Servers.Single().Name, "#" + label);
            exact &= Parse(hashPrefix).Servers.Single().Name == "#Provider text" &&
                     Parse(marker[..marker.IndexOf('#')] + "#%23" + Uri.EscapeDataString(label)).Servers.Single().Name == "#" + label;
            var mixed = Parse(realA + "\n" + marker + "\n" + realB + "\n" + marker);
            if (mixed.Servers.Count > 1) ReportName("mixed-provider-position", mixed.Servers[1].Name, label);
            var order = mixed.Servers.Select(server => server.Name).SequenceEqual(["Real A", label, "Real B"], StringComparer.Ordinal) &&
                        mixed.Servers.Count(NoraSubscriptionStore.IsUsableServer) == 2;
            var initial = Parse(realA + "\n" + realB);
            initial.AppMetadata = Policy(true, 1);
            NoraSubscriptionStore.Store(initial, source, utcNow: now);
            var active = initial.Servers[0];
            var selectedPath = active.LocalPath;
            var selectedId = active.Id;
            var selectedIdentity = NoraSubscriptionStore.RuntimeIdentity(active);
            NoraAppState.SaveActiveProfilePath(selectedPath);
            var current = Parse(marker);
            NoraSubscriptionStore.Store(current, source, utcNow: now);
            ReportName("stored-current", current.Servers[0].Name, label);
            var retained = current.RetainedServers.Single(server => server.Id == selectedId);
            var cache = current.Servers.Count == 1 && current.Servers[0].IsTechnical && current.Servers[0].Name == label &&
                current.RetainedServers.Count == 2 && retained.IsRetained && retained.LocalPath == selectedPath &&
                NoraSubscriptionStore.RuntimeIdentity(retained) == selectedIdentity && !active.IsRetained &&
                !NoraSubscriptionStore.IsUsableServer(active) && NoraAppState.TryLoadActiveProfilePath() == selectedPath &&
                NoraSubscriptionMetadata.EffectiveFor(active).Protected;
            var noDial = !NoraSubscriptionStore.TryLoadServer(current.Servers[0].LocalPath, out _) &&
                NoraSubscriptionStore.TryLoadServer(selectedPath, out var saved) && saved.IsRetained && !NoraSubscriptionStore.IsUsableServer(saved) &&
                Blocked(() => NoraSubscriptionStore.BuildXrayConfig(current.Servers[0])) &&
                Blocked(() => NoraSubscriptionStore.BuildSingBoxConfig(current.Servers[0])) &&
                Blocked(() => NoraSubscriptionStore.BuildXrayTunFrontendConfig(20808, current.Servers[0])) &&
                Blocked(() => NoraSubscriptionStore.BuildXrayConfig(retained)) &&
                !NoraSubscriptionStore.LoadAll().SelectMany(subscription => subscription.Servers).Any(NoraSubscriptionStore.IsUsableServer) &&
                NoraSubscriptionStore.GetAutomaticFailoverCandidates(active).Count == 0;
            var reloaded = NoraSubscriptionStore.LoadAll().Single();
            ReportName("reloaded-current", reloaded.Servers[0].Name, label);
            var restart = reloaded.Servers.Count == 1 && reloaded.Servers[0].IsTechnical && reloaded.Servers[0].Name == label &&
                          reloaded.RetainedServers.Count == 2 && reloaded.RetainedServers.All(server => server.IsRetained && !NoraSubscriptionStore.IsUsableServer(server));
            var manifest = Path.Combine(NoraSubscriptionStore.Root, current.Id, "subscription.json");
            var beforeFailure = File.ReadAllText(manifest);
            const string invalid = """{"outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"198.51.100.20","port":0,"users":[{"id":"official-app-placeholder"}]}]}}]}""";
            const string validJson = """{"outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"198.51.100.20","port":443,"users":[{"id":"valid-custom"}]}]}}]}""";
            const string invalidCredential = """{"outbounds":[{"protocol":"trojan","settings":{"servers":[{"address":"198.51.100.20","port":443}]}}]}""";
            var partialFailure = true;
            foreach (var partialBody in new[]
            {
                "[" + validJson + "," + invalid + "]", "[" + markerJson + "," + invalid + "]",
                "[" + validJson + "," + invalidCredential + "]", "[" + markerJson + "," + invalidCredential + "]",
                realA + "\nvless://invalid@198.51.100.20:65536#invalid", marker + "\nvless://invalid@198.51.100.20:65536#invalid",
                realA + "\nnot-a-share-record", marker + "\nnot-a-share-record"
            })
            {
                partialFailure &= Blocked(() =>
                {
                    var partial = Fetch((request, _) => Response(request, partialBody, Display(false, 2))).GetAwaiter().GetResult();
                    NoraSubscriptionStore.Store(partial, source, utcNow: now);
                });
                var directPartial = Parse(partialBody);
                partialFailure &= directPartial.BodyHasRejectedEntries;
                directPartial.AppMetadata = Policy(false, 2);
                partialFailure &= Blocked(() => NoraSubscriptionStore.Store(directPartial, source, utcNow: now)) &&
                    File.ReadAllText(manifest) == beforeFailure && NoraSubscriptionMetadata.EffectiveFor(active).Protected &&
                    NoraAppState.TryLoadActiveProfilePath() == selectedPath && NoraSubscriptionStore.RuntimeIdentity(active) == selectedIdentity;
            }
            var commentedBody = realA + "\n#profile-title: Fixture\n#profile-update-interval: 24\n#hide-settings: 0\n// provider comment\n; provider comment\n \n";
            var commented = Fetch((request, _) => Response(request, commentedBody, Display(false, 2))).GetAwaiter().GetResult();
            partialFailure &= !commented.BodyHasRejectedEntries && commented.Servers.Count == 1 && commented.AppMetadata.HasV1Policy &&
                !commented.AppMetadata.Protected && File.ReadAllText(manifest) == beforeFailure;
            var failure = Blocked(() => Fetch((request, _) => Response(request, invalid, Display(false, 2))).GetAwaiter().GetResult()) &&
                          File.ReadAllText(manifest) == beforeFailure && NoraSubscriptionMetadata.EffectiveFor(active).Protected;
            failure &= Blocked(() => Fetch((request, _) => Response(request, "", Display(false, 2), HttpStatusCode.NoContent)).GetAwaiter().GetResult()) &&
                       Blocked(() => Fetch((request, _) => Response(request, "{malformed", Display(false, 2))).GetAwaiter().GetResult());
            var displayOnly = Fetch((request, _) => Response(request, marker, Display(false, 2))).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(displayOnly, source, utcNow: now);
            var technicalDisplaySuccess = displayOnly.Servers.Count == 1 && displayOnly.Servers[0].Name == label &&
                !NoraSubscriptionMetadata.EffectiveFor(active).Protected && !NoraSubscriptionStore.IsUsableServer(active) && displayOnly.RetainedServers.Count == 2;

            // A known technical list remains a fallback, not an early winner
            // over a real multi-node response from the next supported identity.
            var fortyFive = JsonSerializer.Serialize(Enumerable.Range(0, 47).Select(index => new
            {
                remarks = "VZG fixture " + index,
                outbounds = new[] { new { protocol = "vless", settings = new { vnext = new[] { new
                { address = "198.51.100.20", port = index < 45 ? 443 : index == 45 ? 0 : 65536, users = new[] { new { id = "real-custom-" + index } } } } } } }
            }));
            var fallback = Fetch((request, call) => Response(request, call == 1 ? markerJson : fortyFive)).GetAwaiter().GetResult();
            var usableFallback = fallback.Servers.Count == 45 && fallback.BodyHasRejectedEntries && fallback.Servers.All(NoraSubscriptionStore.IsUsableServer) && fallback.UserAgent.StartsWith("Happ/", StringComparison.Ordinal);
            var legacyPartialSource = source + "-legacy-partial";
            var legacyPartial = NoraSubscriptionStore.ParsePayload(fortyFive, legacyPartialSource, headers, _ => { }, utcNow: now);
            NoraSubscriptionStore.Store(legacyPartial, legacyPartialSource, utcNow: now);
            usableFallback &= legacyPartial.Servers.Count == 45 && !legacyPartial.AppMetadata.HasV1Policy &&
                !File.ReadAllText(Path.Combine(NoraSubscriptionStore.Root, legacyPartial.Id, "subscription.json")).Contains("body_has_rejected_entries", StringComparison.Ordinal);
            var recovered = Parse(realA + "\n" + marker + "\n" + realB);
            NoraSubscriptionStore.Store(recovered, source, utcNow: now);
            var recovery = recovered.Servers.Select(server => server.Name).SequenceEqual(["Real A", label, "Real B"], StringComparer.Ordinal) &&
                recovered.RetainedServers.Count == 0 && recovered.Servers[0].Id == selectedId && recovered.Servers[0].LocalPath == selectedPath &&
                NoraSubscriptionStore.RuntimeIdentity(recovered.Servers[0]) == selectedIdentity && NoraSubscriptionStore.IsUsableServer(active);
            var ordinary = Parse(marker.Replace("127.0.0.1:1", "198.51.100.21:1", StringComparison.Ordinal));
            var narrow = ordinary.Servers.Count == 1 && !ordinary.Servers[0].IsTechnical && NoraSubscriptionStore.IsUsableServer(ordinary.Servers[0]);
            var blackhole = """{"outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"198.51.100.22","port":443,"users":[{"id":"ordinary-custom"}]}]}},{"protocol":"blackhole","tag":"maintenance"}]}""";
            narrow &= Parse(blackhole).Servers.All(server => !server.IsTechnical) && !NoraSubscriptionStore.TryParseExternalProfile(marker, "synthetic.uri", "", out _);
            var old = Parse(markerJson);
            old.Id = "legacy-upgrade";
            old.SourceUrl = source + "-upgrade";
            old.AppMetadata = new NoraSubscriptionMetadataState { SourceBinding = NoraSubscriptionMetadata.BindSource(old.SourceUrl) };
            var oldRow = old.Servers[0];
            oldRow.IsTechnical = false;
            oldRow.SubscriptionId = old.Id;
            var oldDirectory = Path.Combine(NoraSubscriptionStore.Root, old.Id);
            Directory.CreateDirectory(oldDirectory);
            oldRow.LocalPath = Path.Combine(oldDirectory, "server-" + oldRow.Id + ".json");
            var oldManifest = Path.Combine(oldDirectory, "subscription.json");
            var oldOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            File.WriteAllText(oldManifest, JsonSerializer.Serialize(old, oldOptions));
            File.WriteAllText(oldRow.LocalPath, JsonSerializer.Serialize(oldRow, oldOptions));
            var beforeUpgrade = File.ReadAllText(oldManifest);
            var loadedOld = NoraSubscriptionStore.LoadAll().Single(subscription => subscription.Id == old.Id);
            var upgrade = loadedOld.Servers.Count == 1 && loadedOld.Servers[0].IsTechnical && loadedOld.Servers[0].Name == label &&
                !NoraSubscriptionStore.IsUsableServer(loadedOld.Servers[0]) && !NoraSubscriptionStore.TryLoadServer(oldRow.LocalPath, out _) &&
                File.ReadAllText(oldManifest) == beforeUpgrade;
            var shortHost = new NoraSubscriptionServer { Host = "vpn", Port = 443, AppMetadata = new NoraSubscriptionMetadataState { Protected = true } };
            var shortIpv6 = new NoraSubscriptionServer { Host = "::1", Port = 443, AppMetadata = new NoraSubscriptionMetadataState { Protected = true } };
            var shortPrivacy = !NoraProtectedDisplay.SanitizeTargetLog(shortHost, "dial vpn failed").Contains("vpn", StringComparison.Ordinal) &&
                !NoraProtectedDisplay.SanitizeTargetLog(shortIpv6, "dial ::1 failed").Contains("::1", StringComparison.Ordinal) &&
                NoraProtectedDisplay.SanitizeTargetLog(new NoraSubscriptionServer { Host = "vpn", Port = 443 }, "dial vpn failed").Contains("vpn", StringComparison.Ordinal);
            var passed = exact && order && cache && noDial && restart && failure && partialFailure && technicalDisplaySuccess && usableFallback && recovery && narrow && upgrade && shortPrivacy;
            output.WriteLine($"TECHNICAL ROW SELF-TEST {(passed ? "PASS" : "FAIL")}: exact_text={exact}; provider_order={order}; retained_cache={cache}; no_dial={noDial}; restart={restart}; failed_body={failure}; rejected_entries_atomic={partialFailure}; display_only={technicalDisplaySuccess}; usable_fallback_45={usableFallback}; recovery_identity={recovery}; narrow_shape={narrow}; upgrade_row={upgrade}; short_host_privacy={shortPrivacy}");
            return passed ? 0 : 1;
        }
        catch (Exception exception) { output.WriteLine("TECHNICAL ROW SELF-TEST FAIL: " + exception.GetType().Name); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalRoot);
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
#endif
