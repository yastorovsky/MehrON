namespace ServiceLib.Aether;

/// <summary>
/// What CoreManager.LoadCore does for the Aether core, through <see cref="AetherCoreLoad.BeforeStopAsync" />,
/// <see cref="AetherCoreLoad.BeforeXrayAsync" /> and <see cref="AetherCoreLoad.AfterXrayAsync" />; fakes in tests.
/// </summary>
public sealed class AetherCoreLoadSteps
{
    /// <summary>The Aether core the configuration needs (CoreConfigContext.Aether.Core), read once it was generated.</summary>
    public required Func<AetherCore?> Core { get; init; }

    /// <summary>Why the Aether core cannot run, as when its program is missing; null when it can.</summary>
    public required Func<string?> MissingCore { get; init; }

    /// <summary>Starts a new generation of the cores (<see cref="AetherCoreTurns.NewGeneration" />) and returns it.</summary>
    public required Func<long> NewGeneration { get; init; }

    /// <summary>Cancels a latency test's core, which may hold the Aether listen port (AetherCoreManager.CancelProbesAsync).</summary>
    public required Func<Task> CancelProbes { get; init; }

    /// <summary>
    /// Kills the cores an earlier PattN left behind and waits for them (AetherCoreManager.ReapLeftovers): one may hold a
    /// port the check before Xray starts looks at, which nothing else would free (PattNG reapStale before its check).
    /// </summary>
    public required Action ReapLeftovers { get; init; }

    /// <summary>
    /// The port of the secondary-socks inbound Xray opens for the core to dial out through
    /// (<see cref="AetherBuild.SecondaryInboundPort" />), read once the configuration was generated; null when it has none.
    /// </summary>
    public required Func<int?> SecondaryPort { get; init; }

    /// <summary>
    /// Whether a loopback port can be listened on now, by TCP, and by UDP as well when asked: PattN's own probe,
    /// <see cref="AetherCoreLoad.CanListen" />, which tests use with listeners of their own on the ports.
    /// </summary>
    public Func<int, bool, bool> CanListen { get; init; } = AetherCoreLoad.CanListen;

    /// <summary>How long a port the old cores held is given to be released (<see cref="AetherCoreLoad.PortReleaseWait" />).</summary>
    public TimeSpan PortRelease { get; init; } = AetherCoreLoad.PortReleaseWait;

    /// <summary>Whether Xray runs.</summary>
    public required Func<bool> XrayUp { get; init; }

    /// <summary>Waits, for a short while, until a loopback port accepts connections.</summary>
    public required Func<int, Task<bool>> WaitForPort { get; init; }

    /// <summary>Starts the Aether session of a generation; the localized reason when it does not start.</summary>
    public required Func<AetherCore, long, Task<string?>> StartSession { get; init; }

    /// <summary>Stops the cores that run, the Aether session first.</summary>
    public required Func<Task> StopCores { get; init; }

    /// <summary>Tells the user (true: also as a notification) and the message view.</summary>
    public required Func<bool, string, Task> Report { get; init; }
}

/// <summary>How the Aether part of a load went, see <see cref="AetherCoreLoad.AfterXrayAsync" />.</summary>
public enum AetherCoreLoadOutcome
{
    /// <summary>The configuration needs no Aether core.</summary>
    NoCore,

    /// <summary>Xray is not up, so no Aether core was started either; the rest of the load goes on as before.</summary>
    XrayDown,

    /// <summary>The Aether core did not start, and Xray was stopped with it; the load ends there.</summary>
    AetherFailed,

    Started,
}

/// <summary>
/// PattN: the Aether part of CoreManager.LoadCore (design 2.6.4, PattNG CoreServiceManager.launchCore), in steps that
/// LoadCore takes among its own lines, so that the order is tested without processes. Once the configuration is
/// generated, <see cref="BeforeStopAsync" />: a configuration whose Aether core cannot run leaves the cores that run alone,
/// and a latency test's core, which may hold the Aether listen port, is cancelled before the old cores stop. Once they
/// stopped, <see cref="BeforeXrayAsync" />: nothing starts unless the ports the core and Xray's inbound for it listen on
/// are free (D3). Once Xray started, <see cref="AfterXrayAsync" />: the Aether core dials out through Xray's
/// secondary-socks inbound, so it starts once Xray runs and that inbound listens, and never when Xray did not start or is
/// gone by then. Xray and the Aether core run together or not at all: a core that does not start stops Xray as well.
/// </summary>
public static class AetherCoreLoad
{
    /// <summary>How long the start of an Aether core waits for Xray's inbound it dials out through to listen.</summary>
    public static readonly TimeSpan UpstreamWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a port the cores of the old session held is given to be released before Xray starts (D3): the old Xray was
    /// killed a moment ago, without a wait for its end (ProcessService.StopAsync), and may hold its listeners for a moment.
    /// </summary>
    public static readonly TimeSpan PortReleaseWait = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan PortPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// What follows the generation of the configuration, before the old cores stop: null, after telling why, when the
    /// configuration needs an Aether core that cannot run (the cores that run keep running, and the generation stays, so
    /// that the end of the old session still stops the old Xray); otherwise the generation of the cores the load starts,
    /// with a latency test's core cancelled when the configuration needs an Aether core.
    /// </summary>
    public static async Task<long?> BeforeStopAsync(AetherCoreLoadSteps steps)
    {
        var core = steps.Core();
        if (core != null && steps.MissingCore() is { } missing)
        {
            await steps.Report(true, missing);
            return null;
        }
        var generation = steps.NewGeneration();
        if (core != null)
        {
            await steps.CancelProbes();
        }
        return generation;
    }

    /// <summary>
    /// What follows the stop of the old cores, before Xray starts (D3): once the cores an earlier PattN left behind are
    /// killed, every loopback port the Aether core the configuration needs listens on, and the port of the secondary-socks
    /// inbound Xray opens for it, by UDP as well, which that inbound serves, can be listened on now. A port another
    /// program holds would have the core or Xray fail to listen, or an Aether outbound dial that program; one the system
    /// keeps from programs (a Windows excluded port range) would have them fail. A port that is not free is looked at
    /// again for <see cref="AetherCoreLoadSteps.PortRelease" /> at most, while the old cores' hold of it ends. False, after
    /// telling which port, when one cannot: the load ends there, and the cores it stopped stay stopped. True otherwise,
    /// and for a configuration that needs no core.
    /// </summary>
    public static async Task<bool> BeforeXrayAsync(AetherCoreLoadSteps steps)
    {
        if (steps.Core() is not { } core)
        {
            return true;
        }
        steps.ReapLeftovers();
        var secondaryPort = steps.SecondaryPort();
        var problem = PortsProblem(core, secondaryPort, steps.CanListen);
        var waited = Stopwatch.StartNew();
        // A port no listener can have, an address the core does not take, or a port two listeners are told to share, is
        // not released by waiting (D3-1).
        while (problem != null && InvalidPort(core) == null && core.InvalidListener == null && core.UnreachableListener == null
               && core.SharedPort == null && waited.Elapsed + PortPoll <= steps.PortRelease)
        {
            await Task.Delay(PortPoll);
            problem = PortsProblem(core, secondaryPort, steps.CanListen);
        }
        if (problem == null)
        {
            return true;
        }
        Logging.SaveLog($"CoreManager: no core is started: {problem}");
        await steps.Report(true, problem);
        return false;
    }

    /// <summary>
    /// Why the ports of <paramref name="core" />, and <paramref name="secondaryPort" /> (by TCP and UDP), cannot be listened
    /// on, as <paramref name="canListen" /> finds them (<see cref="CanListen" />), naming the first port that cannot; null
    /// when every one can. A port of the core outside 1..65535, which a command written by hand may name (D3-1), comes
    /// first, with what it is; then the option of a listener whose address the core does not take, where it would quietly
    /// listen on its default instead (PattNG launchCore), and that of the listener the app dials where it does not reach
    /// it; then a port two listeners of such a command share, which the core would bind once, named.
    /// </summary>
    public static string? PortsProblem(AetherCore core, int? secondaryPort, Func<int, bool, bool> canListen)
    {
        if (InvalidPort(core) is { } invalid)
        {
            return string.Format(ResUI.MsgAetherListenPortInvalid, invalid);
        }
        if (core.InvalidListener is { } option)
        {
            return string.Format(ResUI.MsgAetherListenAddressInvalid, option);
        }
        // Nor a listener the app dials where it does not reach it: the session would stay connecting.
        if (core.UnreachableListener is { } unreachable)
        {
            return string.Format(ResUI.MsgAetherListenAddressUnreachable, unreachable);
        }
        if (core.SharedPort is { } shared)
        {
            return string.Format(ResUI.MsgAetherListenPortShared, shared);
        }
        foreach (var port in core.Ports)
        {
            if (!canListen(port, false))
            {
                return string.Format(ResUI.MsgAetherListenPortInUse, port);
            }
        }
        if (secondaryPort is { } secondary && !canListen(secondary, true))
        {
            return string.Format(ResUI.MsgAetherSecondaryPortInUse, secondary);
        }
        return null;
    }

    /// <summary>
    /// The first port <paramref name="core" /> is told to listen on that is no port, outside 1..65535; null when there is
    /// none. The listener the app dials cannot take 0 either, which has the core listen on a port the app does not know.
    /// </summary>
    public static int? InvalidPort(AetherCore core)
    {
        if (core.Port is < 1 or > IPEndPoint.MaxPort)
        {
            return core.Port;
        }
        return core.Ports.Where(port => port is < 1 or > IPEndPoint.MaxPort).Select(port => (int?)port).FirstOrDefault();
    }

    /// <summary>
    /// Whether a program can listen on the loopback <paramref name="port" /> now, by TCP, and with <paramref name="udp" />
    /// by UDP as well (D3): the port is taken and given back at once, as the core and Xray take it, with nothing more. False
    /// when another program holds it, or the system keeps it from programs (a Windows excluded port range answers "access
    /// denied"), and for a number that is no port, outside 1..65535 (D3-1).
    /// </summary>
    public static bool CanListen(int port, bool udp)
    {
        if (port is < 1 or > IPEndPoint.MaxPort)
        {
            return false;
        }
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        try
        {
            using (var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                if (!OperatingSystem.IsWindows())
                {
                    // As Xray and the core bind there (Go and Rust listeners): connections of the old cores that wait out
                    // their close (TIME_WAIT) leave the port free. On Windows they do anyway, and the option would let a
                    // listener share the port of another.
                    tcp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                }
                tcp.Bind(endpoint);
                tcp.Listen(1);
            }
            if (udp)
            {
                using var datagrams = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                datagrams.Bind(endpoint);
            }
            return true;
        }
        catch (SocketException)
        {
            // In use, or access denied.
            return false;
        }
    }

    /// <summary>
    /// What follows the start of Xray: the session of <paramref name="generation" /> on the Aether core the configuration
    /// needs, once Xray's inbound it dials out through listens (for <see cref="UpstreamWait" /> at most). No core starts
    /// when Xray is not up, before the wait or after it (critique 31): Xray reported why, and the core would only dial a
    /// port nobody listens on. A core that does not start is reported, and stops Xray with it.
    /// </summary>
    public static async Task<AetherCoreLoadOutcome> AfterXrayAsync(AetherCoreLoadSteps steps, long generation)
    {
        if (steps.Core() is not { } core)
        {
            return AetherCoreLoadOutcome.NoCore;
        }
        if (!steps.XrayUp())
        {
            Logging.SaveLog("CoreManager: Xray did not start, so the Aether core is not started");
            return AetherCoreLoadOutcome.XrayDown;
        }
        if (UpstreamPort(core) is { } upstream)
        {
            var listens = await steps.WaitForPort(upstream);
            // Xray may end a moment after it was seen up (a port it cannot bind, a wrong password for sudo), and a
            // listener of another program on that port answers the wait all the same.
            if (!steps.XrayUp())
            {
                Logging.SaveLog("CoreManager: Xray ended while the Aether core waited for it, so the Aether core is not started");
                return AetherCoreLoadOutcome.XrayDown;
            }
            if (!listens)
            {
                Logging.SaveLog($"CoreManager: nothing listens on {upstream}, which the Aether core dials out through; it starts all the same");
            }
        }
        var failure = await steps.StartSession(core, generation);
        if (failure != null)
        {
            await steps.Report(true, failure);
            await steps.StopCores();
            return AetherCoreLoadOutcome.AetherFailed;
        }
        return AetherCoreLoadOutcome.Started;
    }

    /// <summary>
    /// The loopback port the core dials out through, which its --upstream names (socks5://127.0.0.1:port, as
    /// <see cref="AetherCore.Through" /> writes it); null when it names none, or a proxy elsewhere.
    /// </summary>
    public static int? UpstreamPort(AetherCore core)
    {
        var upstream = AetherArguments.ValueAfter(core.Arguments, AetherArguments.Upstream);
        if (upstream == null || !Uri.TryCreate(upstream, UriKind.Absolute, out var uri) || uri.Port <= 0)
        {
            return null;
        }
        var host = uri.Host.Trim('[', ']');
        var loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                       || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
        return loopback ? uri.Port : null;
    }

    /// <summary>
    /// Why the Aether core cannot run here: its program is not in bin/aether (the message every missing core gets), or
    /// cannot be run; null when it can.
    /// </summary>
    public static string? MissingCoreMessage()
    {
        if (AetherBinaries.IsCoreAvailable())
        {
            return null;
        }
        var info = CoreInfoManager.Instance.GetCoreInfo(ECoreType.aether);
        return CoreInfoManager.Instance.GetCoreExecFile(info, out var message).IsNullOrEmpty() && message.IsNotEmpty()
            ? message
            : ResUI.MsgAetherCoreCannotStart;
    }

    /// <summary>
    /// Waits until the loopback <paramref name="port" /> accepts a connection, for at most <paramref name="timeout" />:
    /// a bounded wait while a process comes up. A connection to itself is no listener (<see cref="IsSelfConnect" />): it
    /// is closed, which gives the port back, and the wait goes on. False when the time is up.
    /// </summary>
    public static Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct = default)
    {
        return WaitForPortAsync(port, timeout, () => new TcpClient(), ct);
    }

    /// <summary>
    /// <see cref="WaitForPortAsync(int, TimeSpan, CancellationToken)" /> with <paramref name="newClient" /> making the client
    /// of each attempt; tests make one that dials from the port it waits for, which connects it to itself (D6).
    /// </summary>
    public static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, Func<TcpClient> newClient, CancellationToken ct = default)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var client = newClient();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(AetherCoreManager.ProbeTimeout);
                await client.ConnectAsync(Global.Loopback, port, attempt.Token).ConfigureAwait(false);
                if (!IsSelfConnect(client.Client.LocalEndPoint, client.Client.RemoteEndPoint))
                {
                    return true;
                }
                Logging.SaveLog($"AetherCore: a connection to {port} was to itself; nothing listens there yet");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // That attempt took too long.
            }
            catch (SocketException)
            {
                // Nothing listens yet.
            }
            if (deadline.Elapsed + PortPoll >= timeout)
            {
                return false;
            }
            await Task.Delay(PortPoll, ct).ConfigureAwait(false);
        }
    }

    /// <summary><see cref="WaitForPortAsync(int, TimeSpan, CancellationToken)" /> for <see cref="UpstreamWait" />.</summary>
    public static Task<bool> WaitForPortAsync(int port)
    {
        return WaitForPortAsync(port, UpstreamWait);
    }

    /// <summary>
    /// Whether a TCP connection from <paramref name="local" /> to <paramref name="remote" /> is one to itself (D6): on Linux
    /// a connection to a closed loopback port in the ephemeral range may be given that same port to dial from, and the
    /// simultaneous open then connects the socket to itself, with nothing listening. No wait counts such a connection as
    /// a listener.
    /// </summary>
    public static bool IsSelfConnect(EndPoint? local, EndPoint? remote)
    {
        return local is IPEndPoint mine && remote is IPEndPoint theirs
               && mine.Port == theirs.Port && Unmapped(mine.Address).Equals(Unmapped(theirs.Address));
    }

    // An IPv4 address as a dual-mode socket gives it, ::ffff:127.0.0.1, is that IPv4 address.
    private static IPAddress Unmapped(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}

/// <summary>
/// PattN (critique 15): the turns CoreManager's loads and stops take, and the stop that follows an Aether core that exits on
/// its own, which takes its turn with them. Each load that replaces the cores, and each stop, starts a new generation; the
/// session started by a load carries its generation, and its end stops the cores only while that generation is still the
/// one that runs: a core that dies while a reload starts the next cores never stops them. A load that fails before it
/// replaces anything keeps the generation, so that the end of the old session still stops the old Xray.
/// </summary>
public sealed class AetherCoreTurns
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private long _generation;

    /// <summary>The generation of the cores that run now.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Runs <paramref name="action" />, a load or a stop, in its turn.</summary>
    public async Task RunAsync(Func<Task> action)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>Starts a new generation, from inside a turn, as the cores are replaced or stopped; returns it.</summary>
    public long NewGeneration()
    {
        return Interlocked.Increment(ref _generation);
    }

    /// <summary>
    /// Runs <paramref name="stop" /> in its turn, as a new generation, when <paramref name="generation" /> is still the
    /// generation of the cores; true when it ran, false when a load or a stop began since.
    /// </summary>
    public async Task<bool> EndIfCurrentAsync(long generation, Func<Task> stop)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Generation)
            {
                return false;
            }
            NewGeneration();
            await stop().ConfigureAwait(false);
            return true;
        }
        finally
        {
            _turn.Release();
        }
    }
}
