# ADR-010: Wi-Fi and the network

- Status: Accepted (the owner's answer of 2026-11-02: a Wi-Fi mock plus a real network).
- Date: 2026-11-02

## Decision

The radio is simulated; the transport is real.

### Wi-Fi (simulated)

- The networks a board can see are a project setting, edited in the board's properties: `settings.wifi.networks: [{ "ssid": "Home", "password": "12345678", "rssi": -55, "channel": 6 }]` (an empty password is an open network). A project without the setting sees one open network, `ArduSim`.
- `WiFi.scanNetworks()` returns them after a scan time of about 2 s of virtual time (synchronous or async as asked); `WiFi.begin(ssid, password)` connects after about 1 s when the SSID exists and the password matches, otherwise the status becomes `WL_NO_SSID_AVAIL` or `WL_CONNECT_FAILED` with one warning that names the SSID and says to add it in the board's properties.
- Connected: `localIP()` 192.168.1.100 + board index, gateway 192.168.1.1, subnet 255.255.255.0, DNS 192.168.1.1, `RSSI()` from the setting, a MAC from the board profile; events (`WiFi.onEvent`) fire as on the core. `WiFi.softAP()` gives 192.168.4.1.
- The board part shows the Wi-Fi state on the canvas (off, connecting, connected).

### Transport (real, through the host)

- Once Wi-Fi is connected, `WiFiClient` TCP connections, DNS lookups (`WiFi.hostByName`) and `WiFiUDP` use real host sockets; `HTTPClient` works on top of `WiFiClient`. `WiFiClientSecure` (HTTPS) is done by the host's TLS (`SslStream`): `setInsecure()` skips validation, `setCACert()` validates against the given PEM, otherwise the Windows certificate store is used.
- `WiFiServer` listens on 127.0.0.1 only, never on the LAN; the port is the one asked for, or the next free one, and the log says the URL to open in a browser (`http://127.0.0.1:8080/`).
- The ABI calls are non-blocking (`net_connect` returns a handle at once, `net_status`, `net_available`, `net_read`, `net_write`, `net_close`, `net_listen`, `net_accept`, `udp_*`, `dns_resolve`); the shim waits by polling with `delay`, so waiting costs virtual time like any other call.
- While a real socket is open or a lookup or connect is pending, the run is held at no more than real time (1×), so the sketch's timeouts mean wall-clock seconds; the status bar says why the speed is limited.
- Each new remote host and port is logged once (info). The project setting `settings.network.real` (default true) turns the real transport off: connections then fail with a warning, the Wi-Fi mock still works. Headless runs take `--no-network` for the same.

## Consequences

- Runs that use the real network are not reproducible; the integration tests use loopback servers they start themselves.
- A sketch can reach whatever the user's computer can reach; the log makes every destination visible, and listening is limited to localhost.
- `HTTPClient` and `WiFiClientSecure` follow the Arduino-ESP32 API; whether they are vendored from arduino-esp32 (LGPL-2.1, allowed for sources compiled by the user) with compat shims or written in the shim is decided in M6 and recorded in DECISIONS.
