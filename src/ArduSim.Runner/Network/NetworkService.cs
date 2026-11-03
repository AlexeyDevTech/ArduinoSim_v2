using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using ArduSim.Core.Diagnostics;

namespace ArduSim.Runner.Network;

/// <summary>
/// The real network behind an ESP board's simulated Wi-Fi (ADR-010): TCP connections (with TLS
/// done by the host), servers on 127.0.0.1 only, DNS lookups and UDP. Every call returns at once;
/// sockets work on thread-pool tasks and the firmware polls. Thread-safe, since firmware calls
/// arrive on the task threads of the ESP32 core as well as on the simulation thread.
/// </summary>
internal sealed class NetworkService : IDisposable
{
    private const int FirstHandle = 1;
    private const int PortSearch = 20;

    private readonly SimulationLog _log;
    private readonly ConcurrentDictionary<int, Connection> _connections = new();
    private readonly ConcurrentDictionary<int, Listener> _listeners = new();
    private readonly ConcurrentDictionary<int, Task<IPAddress?>> _lookups = new();
    private readonly ConcurrentDictionary<int, UdpClient> _udp = new();
    private readonly ConcurrentDictionary<string, bool> _announced = new(StringComparer.Ordinal);
    private int _nextHandle = FirstHandle;

    public NetworkService(SimulationLog log, bool enabled)
    {
        _log = log;
        Enabled = enabled;
    }

    /// <summary>False when the project or the command line turned the real network off.</summary>
    public bool Enabled { get; }

    /// <summary>Something is open or pending; the run is then held at no more than real time.</summary>
    public bool Active => _connections.Values.Any(c => c.IsOpen) || !_listeners.IsEmpty || _lookups.Values.Any(t => !t.IsCompleted) || !_udp.IsEmpty;

    public void Dispose()
    {
        foreach (Connection connection in _connections.Values)
        {
            connection.Dispose();
        }

        foreach (Listener listener in _listeners.Values)
        {
            listener.Dispose();
        }

        foreach (UdpClient client in _udp.Values)
        {
            client.Dispose();
        }
    }

    /// <param name="tls">0 none, 1 validate (against <paramref name="caPem"/> or the Windows store), 2 accept any certificate.</param>
    public int Connect(string host, int port, int tls, string? caPem)
    {
        var connection = new Connection();
        int handle = Add(_connections, connection);
        if (!Refused($"{host}:{port}"))
        {
            Announce($"{host}:{port.ToString(CultureInfo.InvariantCulture)}", "net-connect", $"Network: the sketch connects to {host}:{port}{(tls != 0 ? " (TLS)" : "")} over the real network.");
            connection.Start(host, port, tls, caPem);
        }
        else
        {
            connection.Fail();
        }

        return handle;
    }

    public int State(int handle)
    {
        if (!_connections.TryGetValue(handle, out Connection? connection))
        {
            return -1;
        }

        // Socket tasks never touch the log; a failure is reported when the firmware sees it.
        if (connection.TakeFailure() is { } failure)
        {
            _log.ReportOnce("net-fail-" + failure.Target, SimSeverity.Warning, "net-connect-failed", $"Network: the connection to {failure.Target} failed ({failure.Reason}).");
        }

        return connection.State;
    }

    public int Available(int handle) => _connections.TryGetValue(handle, out Connection? c) ? c.Available : 0;

    public int Read(int handle, Span<byte> buffer) => _connections.TryGetValue(handle, out Connection? c) ? c.Read(buffer) : 0;

    public int Write(int handle, ReadOnlySpan<byte> data) => _connections.TryGetValue(handle, out Connection? c) ? c.Write(data) : 0;

    public void Close(int handle)
    {
        if (_connections.TryRemove(handle, out Connection? connection))
        {
            connection.Dispose();
        }
        else if (_listeners.TryRemove(handle, out Listener? listener))
        {
            listener.Dispose();
        }
        else if (_udp.TryRemove(handle, out UdpClient? client))
        {
            client.Dispose();
        }
    }

    /// <summary>A server on 127.0.0.1: the asked port, or the next free one; the log gives the address.</summary>
    public int Listen(int port)
    {
        if (Refused($"port {port}"))
        {
            return -1;
        }

        for (int candidate = port; candidate < port + PortSearch && candidate <= ushort.MaxValue; candidate++)
        {
            var listener = new TcpListener(IPAddress.Loopback, candidate);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                continue;
            }

            int handle = Add(_listeners, new Listener(listener));
            if (candidate == port)
            {
                _log.Report(SimSeverity.Info, "net-listen", $"Network: the sketch's server listens on http://127.0.0.1:{candidate}/; open it in a browser.");
            }
            else
            {
                _log.Report(SimSeverity.Info, "net-listen", $"Network: port {port} is in use on this computer, so the sketch's server listens on http://127.0.0.1:{candidate}/; open it in a browser.", variant: "moved");
            }

            return handle;
        }

        _log.Report(SimSeverity.Warning, "net-listen-failed", $"Network: no free port from {port} to {port + PortSearch - 1} on 127.0.0.1; the server does not start.");
        return -1;
    }

    public int Accept(int listener)
    {
        if (!_listeners.TryGetValue(listener, out Listener? server) || server.Take() is not { } socket)
        {
            return 0;
        }

        return Add(_connections, Connection.Accepted(socket));
    }

    public bool Remote(int handle, out uint ip, out ushort port)
    {
        (ip, port) = (0, 0);
        if (_connections.TryGetValue(handle, out Connection? connection) && connection.RemoteEndPoint is { } endPoint)
        {
            ip = ToIpv4(endPoint.Address);
            port = (ushort)endPoint.Port;
            return true;
        }

        return false;
    }

    public int Resolve(string host)
    {
        Task<IPAddress?> lookup = Refused(host)
            ? Task.FromResult<IPAddress?>(null)
            : LookupAsync(host);
        return Add(_lookups, lookup);
    }

    /// <returns>0 pending, 1 done, -1 failed.</returns>
    public int ResolveResult(int handle, out uint ip)
    {
        ip = 0;
        if (!_lookups.TryGetValue(handle, out Task<IPAddress?>? lookup))
        {
            return -1;
        }

        if (!lookup.IsCompleted)
        {
            return 0;
        }

        _lookups.TryRemove(handle, out _);
        if (lookup.Result is { } address)
        {
            ip = ToIpv4(address);
            return 1;
        }

        return -1;
    }

    public int UdpOpen(int port)
    {
        if (Refused($"UDP {port}"))
        {
            return -1;
        }

        try
        {
            var client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            return Add(_udp, client);
        }
        catch (SocketException e)
        {
            _log.ReportOnce($"udp-{port}", SimSeverity.Warning, "net-udp", $"Network: UDP port {port} cannot be opened on this computer ({e.SocketErrorCode}).");
            return -1;
        }
    }

    public bool UdpSend(int handle, string host, int port, ReadOnlySpan<byte> data)
    {
        if (!_udp.TryGetValue(handle, out UdpClient? client))
        {
            return false;
        }

        try
        {
            IPAddress? address = IPAddress.TryParse(host, out IPAddress? literal) ? literal : Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (address is null)
            {
                return false;
            }

            Announce($"udp {host}:{port.ToString(CultureInfo.InvariantCulture)}", "net-udp-send", $"Network: the sketch sends UDP to {host}:{port} over the real network.");
            client.Send(data, new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <returns>The datagram's length, or -1 when none is waiting.</returns>
    public int UdpReceive(int handle, Span<byte> buffer, out uint ip, out ushort port)
    {
        (ip, port) = (0, 0);
        if (!_udp.TryGetValue(handle, out UdpClient? client) || client.Available == 0)
        {
            return -1;
        }

        IPEndPoint? from = null;
        byte[] datagram = client.Receive(ref from);
        datagram.AsSpan(0, Math.Min(datagram.Length, buffer.Length)).CopyTo(buffer);
        ip = ToIpv4(from.Address);
        port = (ushort)from.Port;
        return datagram.Length;
    }

    private static async Task<IPAddress?> LookupAsync(string host)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>IPv4 with the first octet in the lowest byte, as the cores' IPAddress keeps it.</summary>
    private static uint ToIpv4(IPAddress address)
    {
        byte[] bytes = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).GetAddressBytes();
        return bytes.Length == 4 ? (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24)) : 0;
    }

    private int Add<T>(ConcurrentDictionary<int, T> table, T value)
    {
        int handle = Interlocked.Increment(ref _nextHandle);
        table[handle] = value;
        return handle;
    }

    private bool Refused(string what)
    {
        if (!Enabled)
        {
            _log.ReportOnce("net-off", SimSeverity.Warning, "net-off", $"Network: the real network is off for this project, so {what} is not reached (settings.network.real in project.simproj.json, or --no-network).");
        }

        return !Enabled;
    }

    private void Announce(string key, string code, FormattableString message)
    {
        if (_announced.TryAdd(key, true))
        {
            _log.Report(SimSeverity.Info, code, message);
        }
    }

    private sealed class Listener(TcpListener listener) : IDisposable
    {
        public Socket? Take() => listener.Pending() ? listener.AcceptSocket() : null;

        public void Dispose() => listener.Stop();
    }

    /// <summary>One TCP connection: a receive loop fills <see cref="_received"/>, writes go out in order.</summary>
    private sealed class Connection : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<byte> _received = new();
        private readonly SemaphoreSlim _sending = new(1, 1);
        private readonly CancellationTokenSource _cancel = new();
        private TcpClient? _client;
        private Stream? _stream;
        private volatile int _state;
        private Task _writes = Task.CompletedTask;

        public IPEndPoint? RemoteEndPoint { get; private set; }

        /// <summary>0 connecting, 1 connected, 2 closed by the peer, -1 failed.</summary>
        public int State => _state;

        public bool IsOpen => _state is 0 or 1;

        public int Available
        {
            get
            {
                lock (_gate)
                {
                    return _received.Count;
                }
            }
        }

        private (string Target, string Reason)? _failure;

        public static Connection Accepted(Socket socket)
        {
            var connection = new Connection
            {
                _client = new TcpClient { Client = socket },
                RemoteEndPoint = socket.RemoteEndPoint as IPEndPoint,
                _state = 1,
            };
            connection._stream = connection._client.GetStream();
            _ = connection.ReceiveAsync();
            return connection;
        }

        public void Start(string host, int port, int tls, string? caPem) => _ = ConnectAsync(host, port, tls, caPem);

        public (string Target, string Reason)? TakeFailure()
        {
            lock (_gate)
            {
                (string, string)? failure = _failure;
                _failure = null;
                return failure;
            }
        }

        public void Fail() => _state = -1;

        public int Read(Span<byte> buffer)
        {
            lock (_gate)
            {
                int count = Math.Min(buffer.Length, _received.Count);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = _received.Dequeue();
                }

                return count;
            }
        }

        public int Write(ReadOnlySpan<byte> data)
        {
            if (_state != 1 || _stream is not { } stream)
            {
                return 0;
            }

            byte[] copy = data.ToArray();
            lock (_gate)
            {
                _writes = _writes.ContinueWith(_ => SendAsync(stream, copy), TaskScheduler.Default).Unwrap();
            }

            return copy.Length;
        }

        public void Dispose()
        {
            _state = _state == 1 ? 2 : _state;
            _cancel.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _cancel.Dispose();
            _sending.Dispose();
        }

        private static X509Certificate2? LoadCa(string? caPem)
        {
            if (string.IsNullOrWhiteSpace(caPem))
            {
                return null;
            }

            try
            {
                return X509Certificate2.CreateFromPem(caPem);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        private static bool ValidateAgainst(X509Certificate2 ca, X509Certificate? certificate)
        {
            if (certificate is null)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            using var leaf = new X509Certificate2(certificate);
            return chain.Build(leaf);
        }

        private async Task SendAsync(Stream stream, byte[] data)
        {
            try
            {
                await stream.WriteAsync(data, _cancel.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
                _state = _state == 1 ? 2 : _state;
            }
        }

        private async Task ConnectAsync(string host, int port, int tls, string? caPem)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port, _cancel.Token).ConfigureAwait(false);
                RemoteEndPoint = _client.Client.RemoteEndPoint as IPEndPoint;
                Stream stream = _client.GetStream();
                if (tls != 0)
                {
                    X509Certificate2? ca = LoadCa(caPem);
                    var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, certificate, _, errors) =>
                        tls == 2 || (ca is not null ? ValidateAgainst(ca, certificate) : errors == SslPolicyErrors.None));
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, _cancel.Token).ConfigureAwait(false);
                    stream = ssl;
                }

                _stream = stream;
                _state = 1;
                await ReceiveAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException)
            {
                if (!_cancel.IsCancellationRequested)
                {
                    lock (_gate)
                    {
                        _failure = (string.Create(CultureInfo.InvariantCulture, $"{host}:{port}"), Reason(e));
                    }
                }

                _state = -1;
            }
        }

        private async Task ReceiveAsync()
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (_stream is { } stream)
                {
                    int read = await stream.ReadAsync(buffer, _cancel.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    lock (_gate)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            _received.Enqueue(buffer[i]);
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The peer or Stop closed the connection; the state below says so.
            }

            if (_state == 1)
            {
                _state = 2;
            }
        }

        private static string Reason(Exception e) => e switch
        {
            SocketException socket => socket.SocketErrorCode.ToString(),
            System.Security.Authentication.AuthenticationException => "the server's TLS certificate is not trusted",
            _ => e.Message,
        };
    }
}
