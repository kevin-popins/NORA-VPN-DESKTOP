# Windows subscriptions

Keyboard input, clipboard text, files and NORA activation links use the same input boundary. External whitespace, a BOM and matched link wrappers are removed; credential bytes, URL path/query case and percent escapes are preserved. Invisible characters inside constrained link fields produce an import error. Profile activation links cannot silently become a remote subscription request. Importing or refreshing never starts a tunnel.

HTTP probes retain a usable candidate when another client identity returns an error, HTML, malformed content or a timeout. Each attempt has a 35-second headers/body budget, at most five HTTP redirects, a 64 KiB header limit and a 16 MiB body limit. Caller cancellation stops the operation. Failed refreshes preserve the last committed snapshot. The subscription manifest is the atomic generation boundary; individual server files are compatibility mirrors. Failed startup refreshes retry after backoff even when the periodic interval is zero.

## Existing technical entries

The exact supported legacy maintenance entry stays in the ordinary subscription list with the provider's original text. It is a technical display row, never a usable transport. Marker-only content is a successful subscription import. Mixed real and technical rows retain provider order; technical entries are excluded from automatic selection, latency probes, failover and core startup. Ordinary blackhole routing is not a technical server.

A technical-only refresh retains last-good real profiles internally without replacing the existing active core or selecting the technical row. Retained profiles are recovery data, separate from current provider rows. Loader/restart and direct configuration paths apply the same nonconnectable-entry guard. Unknown provider placeholders do not qualify as a valid replacement for usable transport, and usable HTTP fallback candidates remain available.

No service banner, Home notification, status widget or new announcement editor is added. Existing announcement details retain their established placement. The producer's existing flag, text and response format remain unchanged.

## Optional protected-display metadata

The existing subscription GET advertises:

```http
X-Nora-Client: nora-windows
X-Nora-Metadata-Accept: 1
X-Nora-Capabilities: display-v1
```

This is capability negotiation, not device or application authentication. Existing token/HWID checks remain unchanged.

An ordinary HTTP 200 subscription response may add one `X-Nora-Metadata` header: unpadded Base64URL encoding of strict UTF-8 JSON, at most 4096 encoded ASCII bytes and 3072 decoded bytes. The body, selected format and HTTP status remain unchanged. Application metadata is separate from executable Xray, sing-box and Clash configuration.

```json
{
  "v": 1,
  "mode": "full",
  "revision": 2,
  "issued_at": 1794916800,
  "ttl_seconds": 300,
  "display": {"protected": true, "public_label": "Example subscription"}
}
```

The timestamp is illustrative; providers send a current timestamp. Revision orders display-policy changes, and freshness is measured from issued_at. Repeats or HTTP 304 do not extend the deadline. Duplicate keys/headers, invalid types/encoding, unsupported versions, stale values and conflicting replays are rejected.

Metadata is validated before the body and adopted atomically with a valid supported body. A known exact technical display entry may qualify as valid subscription content, but never as usable transport. A malformed, unknown or empty body cannot unlock protection or advance replay watermarks. HTTP 204, 304, 4xx and 5xx do not adopt or clear policy. A successful legacy response without an envelope preserves accepted policy and label.

Authoritative metadata requires the authorized HTTPS origin throughout the redirect chain. Leaving that origin or downgrading HTTPS permanently removes metadata authority for that request, including after a redirect back.

## Disclosure policy

Protection belongs to one subscription and its children. Its public label replaces the endpoint in the existing endpoint surface; a missing label uses “Protected server”. Reveal and connection-detail copying, outbound QR/share/raw export/edit are blocked before disclosure, including the subscription URL. CLI configuration export and diagnostics enforce the same restrictions. Incoming import remains allowed and never auto-connects.

After protection is accepted, absent/invalid metadata, failed refresh, unsupported version, HTTP 304 or TTL expiry never removes it. Unlocking requires a fresh authoritative explicit protected:false update and valid content. Legacy hide-settings: 1 may tighten restrictions; absence cannot unlock them. Other subscriptions retain their own policy. These are official-client restrictions; the operating system still needs actual transport information.

## Location artwork and motion

Explicit Bermuda and Vatican names use bundled BM/VA flags and their three-photo series. Presentation correction leaves provider data and routing unchanged. Existing Antarctica handling, flags and artwork remain available. Hidden, minimized and detached visual trees suspend cosmetic rendering, slideshows and audio-visualizer leases; VPN traffic and subscription/business scheduling continue. Restoring the window resumes one resource subscription without replaying missed slideshow ticks.
