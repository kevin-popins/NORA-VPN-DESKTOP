# Changelog

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
