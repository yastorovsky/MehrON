namespace ServiceLib.Aether;

/// <summary>What a scan found: the endpoint, and the inner hop's of a two-hop tunnel that names one.</summary>
public sealed record AetherScanResult(AetherEndpoint Endpoint, AetherEndpoint? InnerHop = null);

/// <summary>
/// Reading the endpoint a scan of the core found out of its output (PattNG core/AetherScanner.kt).
/// </summary>
public static partial class AetherScanner
{
    /// <summary>How long a scan may take.</summary>
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(8);

    /// <summary>The source label of a scan's output.</summary>
    public const string Source = "aether-scan";

    // PattNG's \S is Java's: anything but ASCII white space, where .NET's also leaves out the other spaces of Unicode.
    private const string NonSpace = @"[^ \t\n\x0B\f\r]";

    /// <summary>
    /// Scans for an endpoint of <paramref name="profile" /> (PattNG scan): a core of its own on the scan arguments of the
    /// profile's settings (<see cref="AetherArguments.Build" /> with scan), dialling out through the profile's exit-node,
    /// until its output names what <see cref="Matcher" /> looks for, for at most <see cref="ScanTimeout" />. Its lines go
    /// to <paramref name="onOutput" /> as they come, for the log panel of a window. The value of a run that ended without
    /// an endpoint is null; <see cref="AetherRunResult{T}.Outcome" /> tells a run whose exit did not open
    /// (<see cref="AetherRunOutcome.NoExit" />), whose core could not start (<see cref="AetherRunOutcome.NotStarted" />),
    /// and one that a stop of every core ended or kept from starting (<see cref="AetherRunOutcome.Stopped" />: an update
    /// of aether-core, or PattN exits). Only a cancellation of <paramref name="ct" /> throws
    /// <see cref="OperationCanceledException" />.
    /// </summary>
    public static Task<AetherRunResult<AetherScanResult>> ScanAsync(
        ProfileItem profile,
        Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        return ScanAsync(AetherCoreManager.Instance, profile, onOutput, ScanTimeout, ct);
    }

    /// <summary>
    /// <see cref="ScanAsync(ProfileItem, Action{string}?, CancellationToken)" /> on the cores of <paramref name="cores" />,
    /// for at most <paramref name="timeout" />.
    /// </summary>
    public static async Task<AetherRunResult<AetherScanResult>> ScanAsync(
        AetherCoreManager cores,
        ProfileItem profile,
        Action<string>? onOutput,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var extra = profile.GetProtocolExtra();
        var protocol = AetherProtocol.FromString(extra.AetherProtocol);
        // A real port only for Tor around the tunnel, which opens a socket to find one, off the ports of a session's core.
        var listenPort = cores.ListenPort;
        var port = await Task.Run(() => AetherPorts.ScanPort(profile, listenPort), ct).ConfigureAwait(false);
        var run = new AetherStandaloneRun(AetherArguments.Build(profile, port, scan: true), AetherRole.Scan)
        {
            Exit = AetherExit.Of(profile),
            OnOutput = onOutput,
        };
        var exitRuled = !KotlinText.IsBlank(extra.AetherExitLoc);
        return await cores.RunUntilAsync(run, timeout, Matcher(protocol, exitRuled), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// What ends a scan, line by line. Without an exit rule, the line that names the endpoint. With one, the core names
    /// its endpoint before it checks the exit behind it and looks again when the rule refuses it, so the endpoint named
    /// last counts only once the core has accepted its exit. Masque-in-masque and WireGuard over MASQUE say they are ready
    /// after that check, so their line stands on its own either way.
    /// </summary>
    public static Func<string, AetherScanResult?> Matcher(AetherProtocol protocol, bool exitRuled)
    {
        if (!exitRuled || protocol == AetherProtocol.Mim || protocol == AetherProtocol.WgOverMasque)
        {
            return line => Parse(protocol, line);
        }
        AetherScanResult? named = null;
        return line =>
        {
            var found = Parse(protocol, line);
            if (found != null)
            {
                named = found;
                return null;
            }
            if (ExitAcceptedRegex().IsMatch(line))
            {
                return named;
            }
            if (ExitRejectedRegex().IsMatch(line))
            {
                named = null;
            }
            return null;
        };
    }

    /// <summary>The endpoint <paramref name="line" /> names for a tunnel of <paramref name="protocol" />; null when it names none.</summary>
    public static AetherScanResult? Parse(AetherProtocol protocol, string line)
    {
        if (protocol == AetherProtocol.Gool)
        {
            return HopsOf(GoolHopsRegex(), line);
        }
        if (protocol == AetherProtocol.Mim)
        {
            return HopsOf(MimHopsRegex(), line);
        }
        if (protocol == AetherProtocol.Masque)
        {
            return OneHopOf(MasqueGatewayRegex(), line);
        }
        if (protocol == AetherProtocol.WireGuard)
        {
            return OneHopOf(WireGuardEndpointRegex(), line);
        }
        // Once the whole tunnel carries, the gateway alone: the WireGuard endpoint inside it is the profile's, which the scan kept.
        return OneHopOf(GoolOverMasqueReadyRegex(), line);
    }

    private static AetherScanResult? OneHopOf(Regex regex, string line)
    {
        var match = regex.Match(line);
        if (!match.Success)
        {
            return null;
        }
        var endpoint = EndpointOf(match.Groups[1].Value);
        return endpoint == null ? null : new AetherScanResult(endpoint);
    }

    private static AetherScanResult? HopsOf(Regex hops, string line)
    {
        var match = hops.Match(line);
        if (!match.Success)
        {
            return null;
        }
        var outer = EndpointOf(match.Groups[1].Value);
        var inner = EndpointOf(match.Groups[2].Value);
        return outer != null && inner != null ? new AetherScanResult(outer, inner) : null;
    }

    /// <summary>The endpoint the core wrote, which its log may write as an IPv6 address and a port without brackets.</summary>
    private static AetherEndpoint? EndpointOf(string text)
    {
        var endpoint = AetherEndpoint.Parse(text);
        if (endpoint != null)
        {
            return endpoint;
        }
        var separator = text.LastIndexOf(':');
        if (separator <= 0)
        {
            return null;
        }
        return AetherEndpoint.Of(text.Substring(0, separator), text.Substring(separator + 1));
    }

    [GeneratedRegex("selected MASQUE gateway (" + NonSpace + "+)", RegexOptions.CultureInvariant)]
    private static partial Regex MasqueGatewayRegex();

    [GeneratedRegex("selected WireGuard endpoint (" + NonSpace + "+)", RegexOptions.CultureInvariant)]
    private static partial Regex WireGuardEndpointRegex();

    [GeneratedRegex("using cloudflare edge (" + NonSpace + @"+) \(outer\) and (" + NonSpace + @"+) \(inner\)", RegexOptions.CultureInvariant)]
    private static partial Regex GoolHopsRegex();

    [GeneratedRegex("masque-in-masque ready: (" + NonSpace + @"+) \(outer\) and (" + NonSpace + @"+) \(inner\)", RegexOptions.CultureInvariant)]
    private static partial Regex MimHopsRegex();

    [GeneratedRegex("gool ready: masque (" + NonSpace + "+) carries wireguard (" + NonSpace + "+)", RegexOptions.CultureInvariant)]
    private static partial Regex GoolOverMasqueReadyRegex();

    [GeneratedRegex("exit location " + NonSpace + "+ accepted", RegexOptions.CultureInvariant)]
    private static partial Regex ExitAcceptedRegex();

    [GeneratedRegex("exit location " + NonSpace + "+ rejected", RegexOptions.CultureInvariant)]
    private static partial Regex ExitRejectedRegex();
}
