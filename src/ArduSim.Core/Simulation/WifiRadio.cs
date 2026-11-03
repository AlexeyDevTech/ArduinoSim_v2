using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Simulation;

/// <summary>The cores' wl_status_t values.</summary>
public enum WifiStatus
{
    Idle = 0,
    NoSsidAvailable = 1,
    ScanCompleted = 2,
    Connected = 3,
    ConnectFailed = 4,
    ConnectionLost = 5,
    Disconnected = 6,
}

/// <summary>
/// The simulated Wi-Fi radio of an ESP board (ADR-010): scans list the project's networks after
/// about 2 s, a connection to one of them comes up after about 1 s when the password matches, and
/// then the board has the addresses of a home network. Data travels over the host's real network
/// (the Runner's NetworkService); this class only decides whether the board is connected.
/// </summary>
public sealed class WifiRadio
{
    public const ulong ScanTime = 2 * VirtualTime.Second;
    public const ulong ConnectTime = 1 * VirtualTime.Second;

    private readonly SimulationEngine _engine;
    private readonly IReadOnlyList<WifiNetworkSettings> _networks;
    private IReadOnlyList<WifiNetworkSettings>? _scan;
    private ScheduledEvent? _pending;
    private WifiNetworkSettings? _joined;

    public WifiRadio(SimulationEngine engine, WifiSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _networks = settings?.Networks is { Count: > 0 } networks ? networks : [new WifiNetworkSettings("ArduSim")];
    }

    /// <summary>The station or the access point changed (the board's canvas indicator follows it).</summary>
    public event Action? Changed;

    public WifiStatus Status { get; private set; } = WifiStatus.Disconnected;

    public bool Scanning { get; private set; }

    public bool AccessPoint { get; private set; }

    public bool Connecting => _pending is not null;

    /// <summary>For the board's canvas indicator: 0 off, 1 connecting or scanning, 2 connected, 3 access point.</summary>
    public int Indicator => Status == WifiStatus.Connected ? 2 : AccessPoint ? 3 : Connecting || Scanning ? 1 : 0;

    /// <summary>The radio is busy, which keeps ADC2 pins from working on an ESP32.</summary>
    public bool Active => Status == WifiStatus.Connected || Connecting || AccessPoint || Scanning;

    public string? Ssid => _joined?.Ssid;

    public int Rssi => Status == WifiStatus.Connected && _joined is { } network ? network.Rssi : 0;

    public IReadOnlyList<WifiNetworkSettings>? ScanResults => _scan;

    private SimulationLog Log => _engine.Log;

    public void StartScan()
    {
        if (Scanning)
        {
            return;
        }

        Scanning = true;
        _scan = null;
        _engine.Clock.Schedule(ScanTime, () =>
        {
            Scanning = false;
            _scan = _networks;
            Changed?.Invoke();
        });
        Changed?.Invoke();
    }

    public void Begin(string ssid, string password)
    {
        _pending?.Cancel();
        _joined = null;
        Status = WifiStatus.Disconnected;
        _pending = _engine.Clock.Schedule(ConnectTime, () => Join(ssid, password));
        Changed?.Invoke();
    }

    public void Disconnect()
    {
        _pending?.Cancel();
        _pending = null;
        _joined = null;
        Status = WifiStatus.Disconnected;
        Changed?.Invoke();
    }

    public bool StartAccessPoint(string ssid, string password)
    {
        if (password.Length is > 0 and < 8)
        {
            Log.ReportOnce("wifi-ap-password", SimSeverity.Warning, "wifi-ap-password", $"WiFi.softAP(\"{ssid}\"): the password needs at least 8 characters; the access point does not start, as on the board.");
            return false;
        }

        AccessPoint = true;
        Changed?.Invoke();
        return true;
    }

    public void StopAccessPoint()
    {
        AccessPoint = false;
        Changed?.Invoke();
    }

    /// <summary>0 local IP, 1 gateway, 2 subnet mask, 3 DNS, 4 soft-AP IP; first octet in the lowest byte.</summary>
    public uint Address(int which) => which switch
    {
        0 => Status == WifiStatus.Connected ? Ip(192, 168, 1, 100) : 0,
        1 or 3 => Status == WifiStatus.Connected ? Ip(192, 168, 1, 1) : 0,
        2 => Status == WifiStatus.Connected ? Ip(255, 255, 255, 0) : 0,
        4 => AccessPoint ? Ip(192, 168, 4, 1) : 0,
        _ => 0,
    };

    private static uint Ip(byte a, byte b, byte c, byte d) => (uint)(a | (b << 8) | (c << 16) | (d << 24));

    private void Join(string ssid, string password)
    {
        _pending = null;
        WifiNetworkSettings? network = _networks.FirstOrDefault(n => n.Ssid == ssid);
        if (network is null)
        {
            Status = WifiStatus.NoSsidAvailable;
            Log.ReportOnce($"wifi-ssid-{ssid}", SimSeverity.Warning, "wifi-no-ssid", $"WiFi.begin(\"{ssid}\"): no network with this name; the board sees {string.Join(", ", _networks.Select(n => "\"" + n.Ssid + "\""))}. Add it to settings.wifi.networks in project.simproj.json.");
        }
        else if (network.Password != password)
        {
            Status = WifiStatus.ConnectFailed;
            Log.ReportOnce($"wifi-password-{ssid}", SimSeverity.Warning, "wifi-password", $"WiFi.begin(\"{ssid}\"): wrong password; the connection fails as on the board. The password is set in settings.wifi.networks in project.simproj.json.");
        }
        else
        {
            _joined = network;
            Status = WifiStatus.Connected;
        }

        Changed?.Invoke();
    }
}
