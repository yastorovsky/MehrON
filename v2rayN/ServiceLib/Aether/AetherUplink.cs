using System.Globalization;

namespace ServiceLib.Aether;

/// <summary>
/// A default route of the system as <see cref="AetherUplink" /> weighs it: the interface it leaves by, its metric (the
/// lower, the more the system prefers it), and whether it can carry anything now.
/// </summary>
public sealed record AetherUplinkRoute(string Interface, long Metric, bool Up = true);

/// <summary>
/// The default routes of one system (<see cref="AetherWindowsUplinkReader" />, <see cref="AetherLinuxUplinkReader" />,
/// <see cref="AetherMacUplinkReader" />), or fakes in tests.
/// </summary>
public interface IAetherUplinkReader
{
    /// <summary>
    /// The default routes, tier by tier in the order they are tried: a tier is read only when no route of the tiers
    /// before it is usable. Windows and Linux: the IPv4 default routes, then the IPv6 ones; macOS: the primary IPv4
    /// interface, then the primary IPv6 one, then the interface of the default route.
    /// </summary>
    IEnumerable<IReadOnlyList<AetherUplinkRoute>> DefaultRoutes();
}

/// <summary>
/// The uplink: the interface the system sends to the internet by when no TUN of PattN were there (D2). Under a TUN, the
/// short-lived Xray processes of Aether (the exit of a scan or a key run, the Xray of a latency test) bind their outbounds
/// to it, as the session's Xray binds its own (its TUN's autoOutboundsInterface): an unbound socket of theirs enters the
/// session's TUN, where a query of port 53 from Xray's program meets the TUN's DNS rule, which answers no HTTPS record,
/// so the core's ECH lookups through its upstream fail, and every other socket of theirs makes a detour through the TUN.
/// It is the interface of the default route with the lowest metric that is up and no TUN, IPv4 first, then IPv6; found
/// anew for every run, since the network may have changed meanwhile.
/// </summary>
public static class AetherUplink
{
    /// <summary>
    /// The names PattN gives its TUN: Xray's (V2rayInboundService) and sing-box's (SingboxInboundService), and the one
    /// WindowsUtils.RemoveTunDevice derives sing-box's Wintun adapter from. On macOS either TUN is a utun that the system
    /// or Xray numbers (<see cref="IsTun" />).
    /// </summary>
    public static readonly IReadOnlyList<string> TunNames = ["xray_tun", "singbox_tun", "wintunsingbox_tun"];

    /// <summary>
    /// Whether <paramref name="name" /> is the interface of a TUN of PattN, on macOS when <paramref name="isMacOS" />, where
    /// the TUN is a utun: <paramref name="tunInterface" />, the one the configuration of the running TUN names (Xray's own,
    /// which numbers its utun at random); every utun while that name is not known (sing-box's TUN takes the next free one),
    /// since none can be told from the TUN then. Another utun, as another VPN's, is no TUN of PattN (D2-5).
    /// </summary>
    public static bool IsTun(string name, bool isMacOS, string? tunInterface = null)
    {
        return TunNames.Contains(name, StringComparer.OrdinalIgnoreCase)
               || (isMacOS && name.StartsWith("utun", StringComparison.Ordinal)
                           && (tunInterface.IsNullOrEmpty() || name.Equals(tunInterface, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The name of the TUN interface the configuration <paramref name="content" /> opens: the name of Xray's tun inbound,
    /// or the interface_name of sing-box's; null when it opens none, names none, or cannot be read.
    /// </summary>
    public static string? TunInterfaceOf(string? content)
    {
        if (content.IsNullOrEmpty() || JsonUtils.ParseJson(content) is not JsonObject config || config["inbounds"] is not JsonArray inbounds)
        {
            return null;
        }
        foreach (var inbound in inbounds.OfType<JsonObject>())
        {
            var name = TextOf(inbound["protocol"]) == "tun" ? TextOf((inbound["settings"] as JsonObject)?["name"])
                : TextOf(inbound["type"]) == "tun" ? TextOf(inbound["interface_name"])
                : null;
            if (!name.IsNullOrEmpty())
            {
                return name;
            }
        }
        return null;

        static string? TextOf(JsonNode? node)
        {
            return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
    }

    /// <summary>
    /// The interface of the first tier of <paramref name="tiers" /> that has a usable route: up, named, and no TUN as
    /// <paramref name="isTun" /> tells; within a tier the lowest metric, the first one listed on a tie. Null when no tier
    /// has one. The tiers after it are not read.
    /// </summary>
    public static string? Select(IEnumerable<IReadOnlyList<AetherUplinkRoute>> tiers, Func<string, bool> isTun)
    {
        foreach (var tier in tiers)
        {
            AetherUplinkRoute? best = null;
            foreach (var route in tier)
            {
                if (!route.Up || route.Interface.IsNullOrEmpty() || isTun(route.Interface))
                {
                    continue;
                }
                if (best == null || route.Metric < best.Metric)
                {
                    best = route;
                }
            }
            if (best != null)
            {
                return best.Interface;
            }
        }
        return null;
    }

    /// <summary>
    /// The uplink of this system, by the reader of its kind, beside the running TUN, whose interface is
    /// <paramref name="tunInterface" /> when the configuration names it (<see cref="IsTun" />); null when it has none, or on
    /// a system of no such kind, or when the routes cannot be read, which is logged.
    /// </summary>
    public static string? Find(string? tunInterface = null)
    {
        IAetherUplinkReader? reader = Utils.IsWindows() ? new AetherWindowsUplinkReader()
            : Utils.IsLinux() ? new AetherLinuxUplinkReader()
            : Utils.IsMacOS() ? new AetherMacUplinkReader()
            : null;
        return reader == null ? null : Find(reader, Utils.IsMacOS(), tunInterface);
    }

    /// <summary>The uplink <paramref name="reader" /> finds; null when it finds none, or fails, which is logged.</summary>
    public static string? Find(IAetherUplinkReader reader, bool isMacOS, string? tunInterface = null)
    {
        try
        {
            return Select(reader.DefaultRoutes(), name => IsTun(name, isMacOS, tunInterface));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("AetherCore: the default routes of the system could not be read", ex);
            return null;
        }
    }

    #region Windows

    /// <summary>
    /// The tiers of Windows default routes (<see cref="AetherWindowsRouteRow" />): the IPv4 ones, then the IPv6 ones, each
    /// weighed by its effective metric, the metric of the route plus the metric of its interface, as Windows itself
    /// weighs them.
    /// </summary>
    public static IEnumerable<IReadOnlyList<AetherUplinkRoute>> WindowsTiers(IEnumerable<AetherWindowsRouteRow> rows)
    {
        var all = rows.ToList();
        foreach (var ipv6 in new[] { false, true })
        {
            yield return all
                .Where(row => row.IPv6 == ipv6)
                .Select(row => new AetherUplinkRoute(row.Interface, (long)row.RouteMetric + row.InterfaceMetric, row.Up))
                .ToList();
        }
    }

    #endregion Windows

    #region Linux

    // linux/route.h and linux/ipv6_route.h: the route is up; the route rejects what it matches (an unreachable route).
    private const long RtfUp = 0x0001;
    private const long RtfReject = 0x0200;

    /// <summary>
    /// The IPv4 default routes of <paramref name="procNetRoute" />, the text of /proc/net/route (the main routing table):
    /// destination and mask 0, under the device's name, with their decimal metric; usable when RTF_UP is set and
    /// RTF_REJECT is not. The loopback device and the "*" of a route without one are no uplink.
    /// </summary>
    public static List<AetherUplinkRoute> ParseLinuxIPv4Routes(string? procNetRoute)
    {
        var routes = new List<AetherUplinkRoute>();
        foreach (var fields in LinesOf(procNetRoute))
        {
            // Iface Destination Gateway Flags RefCnt Use Metric Mask MTU Window IRTT; the header line parses as no route.
            if (fields.Length < 8
                || !TryHex(fields[1], out var destination)
                || !TryHex(fields[3], out var flags)
                || !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var metric)
                || !TryHex(fields[7], out var mask)
                || destination != 0
                || mask != 0
                || IsNoDevice(fields[0]))
            {
                continue;
            }
            routes.Add(new AetherUplinkRoute(fields[0], metric, IsUsable(flags)));
        }
        return routes;
    }

    /// <summary>
    /// The IPv6 default routes of <paramref name="procNetIpv6Route" />, the text of /proc/net/ipv6_route: destination ::
    /// with prefix length 0, under the device's name, with their metric (hexadecimal there); usable as for IPv4. The
    /// kernel lists an unreachable default route on the loopback device, which is never usable.
    /// </summary>
    public static List<AetherUplinkRoute> ParseLinuxIPv6Routes(string? procNetIpv6Route)
    {
        var routes = new List<AetherUplinkRoute>();
        foreach (var fields in LinesOf(procNetIpv6Route))
        {
            // dest dest_prefix_len src src_prefix_len next_hop metric refcnt use flags device
            if (fields.Length < 10
                || fields[0].Length != 32
                || fields[0].Any(digit => digit != '0')
                || !TryHex(fields[1], out var prefixLength)
                || prefixLength != 0
                || !TryHex(fields[5], out var metric)
                || !TryHex(fields[8], out var flags)
                || IsNoDevice(fields[9]))
            {
                continue;
            }
            routes.Add(new AetherUplinkRoute(fields[9], metric, IsUsable(flags)));
        }
        return routes;
    }

    private static IEnumerable<string[]> LinesOf(string? text)
    {
        return (text ?? string.Empty)
            .Split('\n')
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool TryHex(string text, out long value)
    {
        return long.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsUsable(long flags)
    {
        return (flags & RtfUp) != 0 && (flags & RtfReject) == 0;
    }

    private static bool IsNoDevice(string device)
    {
        return device is "*" or "lo";
    }

    /// <summary>
    /// The first Linux release whose kernel lets a process without CAP_NET_RAW bind a socket to an interface
    /// (SO_BINDTODEVICE), which Xray's sockopt.interface does. Before it the bind fails with EPERM, and so every dial of an
    /// Xray bound to the uplink.
    /// </summary>
    public static readonly Version UnprivilegedBindKernel = new(5, 7);

    private static readonly Regex KernelReleaseVersion = new(@"^\s*(\d+)\.(\d+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// The major and minor version of the Linux release <paramref name="release" />, as /proc/sys/kernel/osrelease (uname
    /// -r) writes it: "5.4.0-150-generic", "6.8.0", "4.19.113+", "5.10.0-rc1"; whatever follows the minor version is left
    /// out. Null for a text that starts with no such version.
    /// </summary>
    public static Version? ParseKernelRelease(string? release)
    {
        if (release == null || KernelReleaseVersion.Match(release) is not { Success: true } match
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return null;
        }
        return new Version(major, minor);
    }

    /// <summary>
    /// Whether the Linux kernel of release <paramref name="release" /> lets an unprivileged process bind to an interface:
    /// from <see cref="UnprivilegedBindKernel" /> on. A release that cannot be read counts as one that does, as every
    /// kernel since 2020 does.
    /// </summary>
    public static bool KernelBindsToInterface(string? release)
    {
        return ParseKernelRelease(release) is not { } version || version >= UnprivilegedBindKernel;
    }

    private static readonly Lazy<bool> _systemBindsToInterface = new(() =>
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }
        try
        {
            return KernelBindsToInterface(File.ReadAllText("/proc/sys/kernel/osrelease"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog("AetherCore: the release of the kernel could not be read", ex);
            return true;
        }
    });

    /// <summary>
    /// Whether this system lets the short-lived Xray of Aether, which runs unprivileged, bind to an interface: Windows and
    /// macOS do; Linux from 5.7 on (<see cref="KernelBindsToInterface" />), read once, since the kernel does not change
    /// while the app runs.
    /// </summary>
    public static bool SystemBindsToInterface()
    {
        return _systemBindsToInterface.Value;
    }

    #endregion Linux

    #region macOS

    private static readonly Regex ScutilPrimaryInterface = new(@"^\s*PrimaryInterface\s*:\s*(\S+)\s*$", RegexOptions.Multiline);
    private static readonly Regex RouteInterface = new(@"^\s*interface\s*:\s*(\S+)\s*$", RegexOptions.Multiline);

    /// <summary>
    /// The PrimaryInterface of what scutil wrote for "show State:/Network/Global/IPv4" (or IPv6): the interface the
    /// system sends that family by; null when it wrote none ("No such key" without a network).
    /// </summary>
    public static string? ParseScutilPrimaryInterface(string? output)
    {
        return output != null && ScutilPrimaryInterface.Match(output) is { Success: true } match ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// The interface of what "route -n get default" wrote; null when it wrote none ("not in table" without a default
    /// route).
    /// </summary>
    public static string? ParseRouteGetInterface(string? output)
    {
        return output != null && RouteInterface.Match(output) is { Success: true } match ? match.Groups[1].Value : null;
    }

    #endregion macOS
}

/// <summary>A default route of Windows, its interface found, as <see cref="AetherUplink.WindowsTiers" /> weighs it.</summary>
/// <param name="Interface">The friendly name of the interface, which Go's net.InterfaceByName on Windows looks up.</param>
/// <param name="Up">Whether the interface is up.</param>
/// <param name="IPv6">Whether it is the IPv6 default route (::/0) rather than the IPv4 one (0.0.0.0/0).</param>
/// <param name="RouteMetric">The metric of the route.</param>
/// <param name="InterfaceMetric">The metric of the interface for the family of the route.</param>
public sealed record AetherWindowsRouteRow(string Interface, bool Up, bool IPv6, uint RouteMetric, uint InterfaceMetric);

/// <summary>
/// The default routes of Windows: iphlpapi's forwarding table of both families (GetIpForwardTable2), the metric of each
/// route's interface for its family (GetIpInterfaceEntry), and the interface's friendly name and state as
/// <see cref="NetworkInterface" /> reads them from GetAdaptersAddresses, as Go's net.InterfaceByName on Windows does:
/// Xray's sockopt.interface looks the name up that way and binds by the index it finds (IP_UNICAST_IF).
/// </summary>
public sealed class AetherWindowsUplinkReader : IAetherUplinkReader
{
    // ws2def.h ADDRESS_FAMILY
    private const ushort AfUnspec = 0;
    private const ushort AfInet = 2;
    private const ushort AfInet6 = 23;

    public IEnumerable<IReadOnlyList<AetherUplinkRoute>> DefaultRoutes()
    {
        return OperatingSystem.IsWindows() ? AetherUplink.WindowsTiers(ReadDefaultRows()) : [];
    }

    [SupportedOSPlatform("windows")]
    private static List<AetherWindowsRouteRow> ReadDefaultRows()
    {
        var interfaces = InterfacesByIndex();
        var rows = new List<AetherWindowsRouteRow>();
        var status = GetIpForwardTable2(AfUnspec, out var table);
        if (status != 0)
        {
            Logging.SaveLog($"AetherCore: the routing table of the system could not be read (GetIpForwardTable2 {status})");
            return rows;
        }
        try
        {
            var count = Marshal.ReadInt32(table);
            var first = Marshal.OffsetOf<MibIpForwardTable2>(nameof(MibIpForwardTable2.Table)).ToInt64();
            var size = Marshal.SizeOf<MibIpForwardRow2>();
            var interfaceMetrics = new Dictionary<(ulong Luid, ushort Family), uint?>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibIpForwardRow2>(table + (nint)(first + (long)i * size));
                var family = row.DestinationPrefix.Prefix.Family;
                if (row.DestinationPrefix.PrefixLength != 0 || family is not (AfInet or AfInet6))
                {
                    continue;
                }
                var ipv6 = family == AfInet6;
                if (!interfaces.TryGetValue((ipv6, row.InterfaceIndex), out var networkInterface))
                {
                    continue;
                }
                if (!interfaceMetrics.TryGetValue((row.InterfaceLuid, family), out var interfaceMetric))
                {
                    interfaceMetric = InterfaceMetric(row.InterfaceLuid, family);
                    interfaceMetrics[(row.InterfaceLuid, family)] = interfaceMetric;
                }
                if (interfaceMetric is not { } metric)
                {
                    continue;
                }
                rows.Add(new AetherWindowsRouteRow(
                    networkInterface.Name, networkInterface.OperationalStatus == OperationalStatus.Up, ipv6, row.Metric, metric));
            }
        }
        finally
        {
            FreeMibTable(table);
        }
        return rows;
    }

    /// <summary>The interfaces by the index of each family they have, IPv4's and IPv6's (the same on Windows, as a rule).</summary>
    private static Dictionary<(bool IPv6, uint Index), NetworkInterface> InterfacesByIndex()
    {
        var interfaces = new Dictionary<(bool IPv6, uint Index), NetworkInterface>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            if (IndexOf(() => properties.GetIPv4Properties()?.Index) is { } ipv4)
            {
                interfaces.TryAdd((false, ipv4), networkInterface);
            }
            if (IndexOf(() => properties.GetIPv6Properties()?.Index) is { } ipv6)
            {
                interfaces.TryAdd((true, ipv6), networkInterface);
            }
        }
        return interfaces;
    }

    /// <summary>An interface index, null for an interface without the family, for which the properties throw.</summary>
    private static uint? IndexOf(Func<int?> index)
    {
        try
        {
            var value = index();
            return value is >= 0 ? (uint)value.Value : null;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    /// <summary>The metric of an interface for a family, null when it has none (no such family on it).</summary>
    [SupportedOSPlatform("windows")]
    private static uint? InterfaceMetric(ulong luid, ushort family)
    {
        var row = new MibIpInterfaceRow();
        InitializeIpInterfaceEntry(ref row);
        row.Family = family;
        row.InterfaceLuid = luid;
        return GetIpInterfaceEntry(ref row) == 0 ? row.Metric : null;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIpForwardTable2(ushort family, out nint table);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeMibTable(nint memory);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void InitializeIpInterfaceEntry(ref MibIpInterfaceRow row);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIpInterfaceEntry(ref MibIpInterfaceRow row);

    // The structures of netioapi.h and ws2ipdef.h (Windows SDK 10.0.26100), field by field in their order: the same layout
    // on x64 and arm64 (and x86), where ULONG64 aligns to 8, ULONG and enums to 4, USHORT to 2 and BOOLEAN/UCHAR to 1.
    // AetherUplinkTests pins their sizes and offsets.

    /// <summary>SOCKADDR_INET: the union of SOCKADDR_IN (16 bytes) and SOCKADDR_IN6 (28), laid out as the latter; 4-aligned.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SockaddrInet
    {
        /// <summary>si_family, the family of either member.</summary>
        public ushort Family;

        public ushort Port;
        public uint FlowInfo;
        public uint Address0;
        public uint Address1;
        public uint Address2;
        public uint Address3;
        public uint ScopeId;
    }

    /// <summary>IP_ADDRESS_PREFIX: 32 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IpAddressPrefix
    {
        public SockaddrInet Prefix;
        public byte PrefixLength;
    }

    /// <summary>MIB_IPFORWARD_ROW2: 104 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MibIpForwardRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public IpAddressPrefix DestinationPrefix;
        public SockaddrInet NextHop;
        public byte SitePrefixLength;
        public uint ValidLifetime;
        public uint PreferredLifetime;
        public uint Metric;
        public int Protocol;
        public byte Loopback;
        public byte AutoconfigureAddress;
        public byte Publish;
        public byte Immortal;
        public uint Age;
        public int Origin;
    }

    /// <summary>MIB_IPFORWARD_TABLE2: NumEntries, then the rows from offset 8.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MibIpForwardTable2
    {
        public uint NumEntries;
        public MibIpForwardRow2 Table;
    }

    /// <summary>ULONG ZoneIndices[ScopeLevelCount], ScopeLevelCount being 16: 64 bytes.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct ZoneIndices
    {
        public uint First;
    }

    /// <summary>MIB_IPINTERFACE_ROW: 168 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MibIpInterfaceRow
    {
        public ushort Family;
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public uint MaxReassemblySize;
        public ulong InterfaceIdentifier;
        public uint MinRouterAdvertisementInterval;
        public uint MaxRouterAdvertisementInterval;
        public byte AdvertisingEnabled;
        public byte ForwardingEnabled;
        public byte WeakHostSend;
        public byte WeakHostReceive;
        public byte UseAutomaticMetric;
        public byte UseNeighborUnreachabilityDetection;
        public byte ManagedAddressConfigurationSupported;
        public byte OtherStatefulConfigurationSupported;
        public byte AdvertiseDefaultRoute;
        public int RouterDiscoveryBehavior;
        public uint DadTransmits;
        public uint BaseReachableTime;
        public uint RetransmitTime;
        public uint PathMtuDiscoveryTimeout;
        public int LinkLocalAddressBehavior;
        public uint LinkLocalAddressTimeout;
        public ZoneIndices ZoneIndices;
        public uint SitePrefixLength;
        public uint Metric;
        public uint NlMtu;
        public byte Connected;
        public byte SupportsWakeUpPatterns;
        public byte SupportsNeighborDiscovery;
        public byte SupportsRouterDiscovery;
        public uint ReachableTime;
        public byte TransmitOffload;
        public byte ReceiveOffload;
        public byte DisableDefaultRoutes;
    }
}

/// <summary>
/// The default routes of Linux: the main routing table as /proc/net/route lists it, then /proc/net/ipv6_route, read
/// only when no IPv4 default route is usable. A TUN that routes with a table of its own (sing-box) is not there; one
/// that adds its default route to the main table (Xray's xray_tun, metric 1) is, and is left out by its name. Xray's
/// sockopt.interface binds a socket with SO_BINDTODEVICE, which an unprivileged process may do since Linux 5.7; on an older
/// kernel nothing is bound (<see cref="AetherUplinkBinding.CanBind" />).
/// </summary>
public sealed class AetherLinuxUplinkReader : IAetherUplinkReader
{
    public IEnumerable<IReadOnlyList<AetherUplinkRoute>> DefaultRoutes()
    {
        yield return AetherUplink.ParseLinuxIPv4Routes(File.ReadAllText("/proc/net/route"));
        yield return File.Exists("/proc/net/ipv6_route")
            ? AetherUplink.ParseLinuxIPv6Routes(File.ReadAllText("/proc/net/ipv6_route"))
            : [];
    }
}

/// <summary>
/// The default routes of macOS: the primary interface of IPv4, then of IPv6, as scutil reads them from the dynamic store
/// (State:/Network/Global/IPv4, IPv6), then the interface "route -n get default" names, each run only when the ones
/// before gave none that is usable. Xray's sockopt.interface binds a socket with IP_BOUND_IF (IPV6_BOUND_IF).
/// </summary>
public sealed class AetherMacUplinkReader : IAetherUplinkReader
{
    private const string Scutil = "/usr/sbin/scutil";
    private const string Route = "/sbin/route";

    /// <summary>How long scutil or route may take; neither waits for anything.</summary>
    private static readonly TimeSpan ToolWait = TimeSpan.FromSeconds(3);

    public IEnumerable<IReadOnlyList<AetherUplinkRoute>> DefaultRoutes()
    {
        yield return Routes(AetherUplink.ParseScutilPrimaryInterface(Run(Scutil, string.Empty, "show State:/Network/Global/IPv4\n")));
        yield return Routes(AetherUplink.ParseScutilPrimaryInterface(Run(Scutil, string.Empty, "show State:/Network/Global/IPv6\n")));
        yield return Routes(AetherUplink.ParseRouteGetInterface(Run(Route, "-n get default", null)));
    }

    private static List<AetherUplinkRoute> Routes(string? name)
    {
        return name == null ? [] : [new AetherUplinkRoute(name, 0)];
    }

    /// <summary>What a tool wrote, given <paramref name="input" /> on its stdin; null when it did not run or end in time, which is logged.</summary>
    private static string? Run(string fileName, string arguments, string? input)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(input ?? string.Empty);
            process.StandardInput.Close();
            if (!process.WaitForExit(ToolWait))
            {
                Logging.SaveLog($"AetherCore: {fileName} did not end in time");
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It ended meanwhile.
                }
                return null;
            }
            _ = error.GetAwaiter().GetResult();
            return output.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Logging.SaveLog($"AetherCore: {fileName} could not be run", ex);
            return null;
        }
    }
}

/// <summary>
/// When the short-lived Xray processes of Aether bind their outbounds to the uplink (D2): while a TUN of PattN runs (Xray's
/// own, or sing-box's in front of Xray) and the settings name no bind interface, which wins as it does for every
/// configuration; PattN's own in <see cref="Default" />, fakes in tests.
/// </summary>
public sealed class AetherUplinkBinding
{
    /// <summary>The exit Xray a scan or a key run dials out through (AetherExitService).</summary>
    public const string ExitRole = "exit";

    /// <summary>The Xray of a latency test (AetherDelayTester).</summary>
    public const string TestRole = "test";

    /// <summary>Whether a TUN of PattN runs now.</summary>
    public required Func<bool> TunRunning { get; init; }

    /// <summary>The uplink as the system lists it now (<see cref="AetherUplink.Find()" />); null when it has none.</summary>
    public required Func<string?> FindUplink { get; init; }

    /// <summary>Where the line goes that tells an Xray runs unbound for want of an uplink.</summary>
    public Action<string> Log { get; init; } = Logging.SaveLog;

    /// <summary>
    /// Whether the system lets the Xray, which runs unprivileged, bind to an interface at all: not on a Linux kernel before
    /// 5.7 (E5). PattN's own asks the system (<see cref="AetherUplink.SystemBindsToInterface" />); fakes in tests.
    /// </summary>
    public Func<bool> CanBind { get; init; } = () => true;

    // The roles the line about the kernel was written for: once each, since the kernel does not change while the app runs.
    private readonly HashSet<string> _toldKernel = [];

    /// <summary>What PattN's own exits and tests run with.</summary>
    public static AetherUplinkBinding Default { get; } = new()
    {
        TunRunning = () => CoreManager.Instance.IsTunRunning,
        FindUplink = () => AetherUplink.Find(CoreManager.Instance.TunInterface),
        CanBind = AetherUplink.SystemBindsToInterface,
    };

    /// <summary>
    /// Has the outbounds of the configuration <paramref name="context" /> is generated for bound to the uplink, found now
    /// (<see cref="AetherBuild.Uplink" />), when a TUN runs and the settings name no bind interface; the uplink, or null
    /// when they are left as they are. With no uplink to be found, the Xray of <paramref name="role" /> runs unbound, as
    /// it did before, and a line says so. On a Linux kernel before 5.7 (<see cref="CanBind" />) nothing is bound, since
    /// every dial of the Xray would fail there, and a line says so once for each role (E5).
    /// </summary>
    public string? Apply(CoreConfigContext context, string role)
    {
        if (!context.AppConfig.CoreBasicItem.BindInterface.IsNullOrEmpty() || !TunRunning())
        {
            return null;
        }
        if (!CanBind())
        {
            bool first;
            lock (_toldKernel)
            {
                first = _toldKernel.Add(role);
            }
            if (first)
            {
                Log($"AetherCore: this kernel needs CAP_NET_RAW to bind to an interface; the {role} Xray runs unbound");
            }
            return null;
        }
        var uplink = FindUplink();
        if (uplink.IsNullOrEmpty())
        {
            Log($"AetherCore: no network interface outside the TUN was found; the {role} Xray runs unbound");
            return null;
        }
        context.Aether.Uplink = uplink;
        return uplink;
    }
}
