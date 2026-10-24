namespace ServiceLib.Aether;

/// <summary>
/// The loopback ports of the Aether core (PattNG AetherCoreManager.kt): the listen port every core listens on, whatever
/// its profile, and every Aether outbound dials, unless a command written by hand names another; the two ports after it,
/// which Tor and Psiphon may take; and the third, Xray's secondary-socks inbound, which the core of a session dials out
/// through.
/// </summary>
public static class AetherPorts
{
    /// <summary>The Aether listen port unless the setting names another.</summary>
    public const int DefaultListenPort = 10819;

    /// <summary>
    /// How far above the listen port the secondary-socks inbound is: past the two after it that Psiphon and Tor may take.
    /// </summary>
    public const int SecondaryOffset = 3;

    /// <summary>Ports below this one are the system's; a program cannot listen there.</summary>
    public const int FirstListenPort = 1024;

    /// <summary>The highest Aether listen port: the three ports after it are the core's as well.</summary>
    public const int LastListenPort = 65535 - SecondaryOffset;

    /// <summary>
    /// The Aether listen port the setting <paramref name="text" /> names: a port a program can listen on, with the three
    /// ports after it ports as well; the default port for anything else.
    /// </summary>
    public static int ListenPortOf(string? text)
    {
        return ListenPortOf(AetherDigits.ToIntOrNull(text?.Trim()));
    }

    /// <summary>
    /// <see cref="ListenPortOf(string?)" /> for a setting stored as a number: a value that is no such port gives way to
    /// the default.
    /// </summary>
    public static int ListenPortOf(int? port)
    {
        return port is >= FirstListenPort and <= LastListenPort ? port.Value : DefaultListenPort;
    }

    /// <summary>The port of the secondary-socks inbound for the Aether listen port <paramref name="listenPort" />.</summary>
    public static int SecondarySocksPortOf(int listenPort)
    {
        return listenPort + SecondaryOffset;
    }

    /// <summary>
    /// PattN: how many ports from the mixed port on are the app's own: the mixed port and the six after it
    /// (AppManager.GetLocalPort: the second and the LAN SOCKS port, the PAC port, Xray's API, and the API of sing-box or
    /// mihomo, which is one higher under the TUN).
    /// </summary>
    public const int LocalPortCount = 7;

    /// <summary>
    /// PattN: the local ports of the app for the mixed port <paramref name="localPort" />, L..L+6 (critique 10), taken
    /// whatever is switched on; the Aether listen port and the three ports after it stay clear of them.
    /// </summary>
    public static IEnumerable<int> LocalPortsOf(int localPort)
    {
        return Enumerable.Range(localPort, LocalPortCount);
    }

    /// <summary>
    /// Why <paramref name="text" /> cannot be the Aether listen port beside local ports on
    /// <paramref name="localPorts" />; null when it can. The caller decides which of its ports count.
    /// </summary>
    public static AetherListenPortProblem? ListenPortProblem(string text, IEnumerable<int> localPorts)
    {
        var port = AetherDigits.ToIntOrNull(text.Trim());
        if (port is not (>= FirstListenPort and <= LastListenPort))
        {
            return AetherListenPortProblem.NotAPort;
        }
        var ports = localPorts.ToHashSet();
        for (var offset = 0; offset <= SecondaryOffset; offset++)
        {
            if (ports.Contains(port.Value + offset))
            {
                return AetherListenPortProblem.LocalProxy;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="port" /> is one the core of the Aether listen port <paramref name="listenPort" /> may
    /// take: the listen port or one of the three after it.
    /// </summary>
    public static bool IsReserved(int port, int listenPort)
    {
        var offset = (long)port - listenPort;
        return offset is >= 0 and <= SecondaryOffset;
    }

    /// <summary>
    /// <paramref name="port" />, which a speed test picked for an inbound of its own, unless the core of the Aether listen
    /// port <paramref name="listenPort" /> may take it (<see cref="IsReserved" />): then a loopback port the system gives,
    /// one that is not such a port (design 2.12). A latency test's core, or the session's, may need the port meanwhile.
    /// </summary>
    public static int Unreserved(int port, int listenPort)
    {
        if (!IsReserved(port, listenPort))
        {
            return port;
        }
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var picked = PickLoopbackPort();
            if (picked > 0 && !IsReserved(picked, listenPort))
            {
                return picked;
            }
        }
        return port;
    }

    /// <summary>A loopback port free now, which the system gives. Opens a socket to find one.</summary>
    public static int PickLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The port a scan of <paramref name="profile" /> binds: none, since nothing dials it, unless Tor around the
    /// tunnel comes along, whose own listener follows the tunnel's and so takes a real port as well, see
    /// <see cref="AetherArguments.Build" />. That port, X, comes from <paramref name="pick" /> (the system, by default:
    /// <see cref="PickLoopbackPort" />), and neither X nor Tor's X+1 is the Aether listen port
    /// <paramref name="listenPort" /> or one of the three after it, which a session started beside the scan needs (PattN:
    /// as for every other port it picks, design 2.6.5); the last port picked when none other comes.
    /// </summary>
    public static int ScanPort(ProfileItem profile, int listenPort, Func<int>? pick = null)
    {
        if (AetherTor.FromString(profile.GetProtocolExtra().AetherTor) != AetherTor.Reverse)
        {
            return 0;
        }
        pick ??= PickLoopbackPort;
        var port = 0;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            port = pick();
            if (port > 0 && port < 65535 && !IsReserved(port, listenPort) && !IsReserved(port + 1, listenPort))
            {
                return port;
            }
        }
        return port;
    }
}

/// <summary>Why a value cannot be the Aether listen port.</summary>
public enum AetherListenPortProblem
{
    /// <summary>No port a program can listen on, with the three ports after it ports as well.</summary>
    NotAPort,

    /// <summary>The port, or one of the three after it, which the core takes as well, is a local port of the app.</summary>
    LocalProxy,
}
