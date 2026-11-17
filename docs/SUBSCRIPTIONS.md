# Windows subscriptions

Keyboard input, clipboard text, files and NORA activation links use the same input boundary. External whitespace, a BOM and matched link wrappers are removed; credential bytes, URL path/query case and percent escapes are preserved. Invisible characters inside a transport link produce an import error. Profile activation links cannot silently become a remote subscription request. Importing or refreshing never starts a tunnel.

HTTP probes retain a usable candidate when another client identity returns an error, HTML, malformed content or a timeout. Each attempt has a 35-second headers/body budget, at most five HTTP redirects, a 64 KiB header limit and a 16 MiB body limit. Caller cancellation stops the operation. A failed refresh retains the last committed profile generation. The subscription manifest is the atomic generation boundary; individual server files are compatibility mirrors. Failed startup refreshes retry after backoff even when the provider's periodic update interval is zero.

## Optional application metadata

The existing subscription GET advertises these capabilities:

```http
X-Nora-Client: nora-windows
X-Nora-Metadata-Accept: 1
X-Nora-Capabilities: display-v1,status-v1
```

An authoritative response may contain one `X-Nora-Metadata` header: unpadded Base64URL encoding of strict UTF-8 JSON, at most 4096 encoded bytes and 3072 decoded bytes. Metadata is separate from executable Xray, sing-box and Clash configuration.

```json
{
  "v": 1,
  "mode": "full",
  "revision": 2,
  "issued_at": 1794916800,
  "ttl_seconds": 300,
  "display": {"protected": true, "public_label": "Example subscription"},
  "status": {"access": "active", "availability": "available", "message": null}
}
```

The timestamp above is illustrative. Providers must send a current timestamp. `revision` orders display-policy changes; `issued_at` orders status changes. TTL is measured from `issued_at`, not receipt time. Repeating a response or receiving HTTP 304 does not extend it. Duplicate keys/headers, invalid types, unsupported versions, stale or conflicting values are rejected.

| Response | Accepted result |
|---|---|
| 200, valid v1 active + available/degraded, usable supported body | Commit metadata and profiles together. |
| 200, no envelope, valid legacy body | Import legacy content without clearing accepted policy. |
| 204, valid v1 unavailable/access status, exactly empty body | Save the notice; retain old settings internally for recovery and disable new connections from them. |
| Invalid envelope/body, contradictory status, 304, 4xx or 5xx | Preserve the last committed state and replay watermarks. |

Authoritative metadata requires the authorized HTTPS origin throughout the redirect chain. Leaving that origin or downgrading HTTPS permanently removes metadata authority for that request, including after a redirect back to the original origin. A valid `protected:false` envelope with a malformed body cannot unlock a subscription.

## Protected display and service notices

Protection belongs to one subscription and its children. Its public label replaces the endpoint in the existing endpoint surface; a missing label uses “Protected server”. Reveal, connection-detail copying, outbound QR/share and raw export are blocked before disclosure. The restriction includes the subscription URL. CLI configuration export and connection diagnostics enforce the same policy. Protected values are removed from client diagnostic presentation.

After protection has been accepted, an absent/invalid header, failed refresh, unsupported version, HTTP 304 or TTL expiry never removes it. Unlocking requires an explicit fresh authoritative `protected:false` update with valid content. Legacy `hide-settings: 1` can tighten display restrictions; an absent legacy flag cannot unlock them. These are official-client restrictions; the operating system still needs the actual transport information.

Only confirmed service/access states produce a service notice. An ordinary announcement or description does not add a banner above the Home scene. Notices are scoped to the selected subscription; another subscription's status is not borrowed. The exact reserved legacy maintenance record is converted into provider text and removed from selectable servers. An arbitrary server name, loopback address or blackhole routing rule is insufficient. A notice never stops an already running tunnel.

An expired status TTL becomes unknown while display protection remains enforced. Successful available-state recovery restores the current real profile set. Raw transports, UUIDs, provider order, routing and DNS remain independent of presentation metadata.

## Location artwork and motion

Explicit Bermuda and Vatican names use the bundled BM/VA flags and their three-photo series. Display correction leaves provider data and routing unchanged. Existing Antarctica handling, flags and artwork remain available. Hidden, minimized and detached visual trees suspend cosmetic rendering, slideshows and audio-visualizer leases; VPN traffic and subscription/business scheduling continue. Restoring the window resumes motion without accumulating handlers or replaying missed slideshow ticks.
