# Changelog

## Unreleased

- Restored provider ordering and distinct named VLESS routing variants while
  keeping repeated quota and instruction rows to one occurrence each.
- Preserved double-encoded Unicode titles and server names and added a graceful
  bundled-image fallback for unsupported ZWJ emoji sequences.
- Added automatic subscription refresh at application startup and at each
  provider-supplied update interval, with coalesced writes and quiet retry after
  transient failures. A single summary notification now confirms each automatic
  refresh pass or explains that NORA will retry.
- Preserved selected nodes and custom ordering while migrating existing
  subscriptions to semantic IDs; subscription file mutations are serialized
  and written atomically.
- Discord Mode can now safely switch an active VLESS or KRot connection: NORA
  stops the current tunnel, changes routing mode, and reconnects the selected
  server. Failed transitions roll back to the previous mode and connection.

## 0.1.0-pre.4 — 2026-07-16

- Kept the interface responsive while Windows scans physical and virtual
  network adapters during latency checks, connection startup and diagnostics.
- Prevented Live Traffic from counting a long Windows pause as a sudden traffic
  spike.
- Added regression checks for the shared network-adapter cache and WPF render
  responsiveness.

## 0.1.0-pre.3 — 2026-07-16

- Added routing presets with the first beta preset, Discord Mode.
- Discord Mode routes only Discord through a selected VLESS or KRot server;
  other applications keep using the normal connection.
- Added the Routing Rules page, opened from the computer icon on the left side
  of the Home screen.

## 0.1.0-pre.2 — 2026-07-15

- Added a connection test on the Logs page with a ready-to-share diagnostic report.
- Made Live Traffic smoother and more responsive during active connections.

## 0.1.0-pre.1 — 2026-07-13

First public pre-release.

- Windows desktop client for KRot, VLESS/Reality/XHTTP and AmneziaWG 2.0.
- Subscription import, secure connection verification, direct latency probing,
  tray lifecycle, Premium visual options and server-location artwork.
- Public source tree, reproducible portable packaging, security policy and
  third-party notices.

### Pre-release note

Treat this build as an early public preview. Do not use it as the only path to
critical network access. Provider-owned HAPP crypt5 private key material is
intentionally not shipped in the public source tree or public portable build.
