namespace ServiceLib.Aether;

/// <summary>
/// PattN: the names an Aether core sends to Xray before its tunnel is up (design 2.7.6 point 3, critique 11). The core
/// hands every name to its upstream unresolved (socks5h), so Xray looks it up for the freedom exit-node; in TUN mode its
/// DNS would send a name that is not protected to the remote DNS, through the proxy, which is the Aether outbound whose
/// core is not up yet. CoreConfigContextBuilder adds these names to the protected domains, which the direct DNS answers.
/// </summary>
public static class AetherBootstrapNames
{
    /// <summary>
    /// The WARP API, which the core calls for a key it does not have, by the name it goes by first, unless its command
    /// names an address, a server name or a host of its own.
    /// </summary>
    public const string WarpApi = AetherKeys.DefaultApiHost;

    /// <summary>The WARP API's other name, which the core goes by next when <see cref="WarpApi" /> gets no answer.</summary>
    public const string WarpApiFallback = AetherKeys.FallbackApiHost;

    /// <summary>Where Psiphon fetches its remote server list from before it has a tunnel.</summary>
    public const string PsiphonRemoteList = "s3.amazonaws.com";

    /// <summary>
    /// What Tor around the tunnel, or Tor alone, reaches before any tunnel (the fork's aether/src/bridges.rs): the
    /// bridge distributors and the address check, and the fronts, CDN hosts and STUN servers of the built-in snowflake
    /// and meek bridges.
    /// </summary>
    public static IReadOnlyList<string> TorBootstrap { get; } =
    [
        "bridges.torproject.org",
        "onionoo.torproject.org",
        "www.cloudflare.com",
        "1098762253.rsc.cdn77.org",
        "1603026938.rsc.cdn77.org",
        "app.datapacket.com",
        "www.datapacket.com",
        "www.phpmyadmin.net",
        "stun.epygi.com",
        "stun.uls.co.za",
        "stun.voipgate.com",
        "stun.mixvoip.com",
        "stun.telnyx.com",
        "stun.hot-chilli.net",
        "stun.fitauto.ru",
        "stun.m-online.net",
    ];

    /// <summary>
    /// The domain names a core started with <paramref name="arguments" /> asks Xray for before its tunnel is up, when it
    /// dials out by the freedom exit-node: the names of the WARP API, and the address, the server name and the host its
    /// command sends the calls to, when they are names, the address with or without a port (the calls go to the address,
    /// or else to the server name, which follows the host; a name given that the core then does not look up does no
    /// harm here); the host the DNS-over-HTTPS resolver of its ECH key lookup is reached at (the @address= name when it
    /// has one, the host of its URL otherwise), or the ECH domain itself when the key is asked of a plain DNS resolver,
    /// whose query a sing-box TUN in front of Xray answers by its DNS rules; when Tor runs around
    /// the tunnel or alone, the bootstrap names of Tor and the hosts the profile's own bridge lines dial; and Psiphon's
    /// remote server list when Psiphon runs around the tunnel or alone (inside the tunnel, Psiphon fetches it through
    /// the tunnel, as Tor inside it reaches its names). IP addresses need no lookup and are left out. Names Psiphon's
    /// fronted transports build in, and bridges Tor learns from moat or keeps from an earlier run, are not known here:
    /// they are left to the device checks.
    /// </summary>
    public static List<string> Of(IReadOnlyList<string> arguments)
    {
        var names = new List<string> { WarpApi, WarpApiFallback };
        AddDomain(names, HostOf(AetherArguments.ValueAfter(arguments, "--api-address")));
        AddApiName(names, AetherArguments.ValueAfter(arguments, "--api-sni"));
        AddApiName(names, AetherArguments.ValueAfter(arguments, "--api-host"));
        AddDomain(names, DohHostOf(AetherArguments.ValueAfter(arguments, "--ech-dns")));
        if (LooksUpEchKeyOverPlainDns(arguments))
        {
            AddDomain(names, AetherArguments.ValueAfter(arguments, "--ech-domain") ?? AetherArguments.DefaultEchDomain);
        }
        if (TorBeforeTheTunnel(arguments))
        {
            names.AddRange(TorBootstrap);
            foreach (var line in ValuesAfter(arguments, "--tor-bridge"))
            {
                foreach (var host in BridgeHostsOf(line))
                {
                    AddDomain(names, host);
                }
            }
        }
        if (PsiphonBeforeTheTunnel(arguments))
        {
            names.Add(PsiphonRemoteList);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The host of the proxy a command written by hand names with --upstream, when it is a domain name: the core looks it
    /// up itself, with the system resolver, whose query enters the TUN. Null for an IP address, or without --upstream.
    /// </summary>
    public static string? UpstreamHostOf(IReadOnlyList<string> arguments)
    {
        var upstream = AetherArguments.ValueAfter(arguments, AetherArguments.Upstream);
        if (upstream == null || !Uri.TryCreate(upstream, UriKind.Absolute, out var uri))
        {
            return null;
        }
        var host = uri.Host.Trim('[', ']');
        return Utils.IsDomain(host) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? host : null;
    }

    /// <summary>
    /// Whether the core started with <paramref name="arguments" /> runs Psiphon before any tunnel (around it, or alone):
    /// its fronted transports dial names the client builds in, which nothing here protects.
    /// </summary>
    public static bool PsiphonBeforeTheTunnel(IReadOnlyList<string> arguments)
    {
        var psiphon = AetherArguments.PsiphonModeOf(arguments);
        return psiphon == AetherPsiphon.Reverse || psiphon == AetherPsiphon.Only;
    }

    /// <summary>
    /// Whether the core started with <paramref name="arguments" /> runs Tor before any tunnel (around it, or alone): the
    /// bridges Tor learns from moat, or keeps from an earlier run, may dial names that nothing here protects.
    /// </summary>
    public static bool TorBeforeTheTunnel(IReadOnlyList<string> arguments)
    {
        var tor = AetherArguments.TorModeOf(arguments);
        return tor == AetherTor.Reverse || tor == AetherTor.Only;
    }

    /// <summary>
    /// The host names a bridge line <paramref name="line" /> dials (its url= host, its front= name, each of its fronts=
    /// names and the STUN servers of its ice=), IP addresses and names alike; the bridge's own address is an IP address.
    /// </summary>
    public static List<string> BridgeHostsOf(string line)
    {
        var hosts = new List<string>();
        foreach (var word in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = word.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }
            var value = word[(equals + 1)..];
            switch (word[..equals].ToLowerInvariant())
            {
                case "url":
                    if (Uri.TryCreate(value, UriKind.Absolute, out var url))
                    {
                        hosts.Add(url.Host.Trim('[', ']'));
                    }
                    break;

                case "front":
                case "fronts":
                    hosts.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;

                case "ice":
                    foreach (var server in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // stun:host:port, or stuns:...; an IPv6 address in brackets.
                        var address = server[(server.IndexOf(':') + 1)..];
                        if (HostOf(address) is { } host)
                        {
                            hosts.Add(host);
                        }
                    }
                    break;
            }
        }
        return hosts;
    }

    /// <summary>
    /// Whether the core started with <paramref name="arguments" /> looks its ECH key up (--ech auto) with a plain DNS
    /// query, over UDP or TCP: the resolver --ech-dns names, udp://1.1.1.1 by default, is no https:// one.
    /// </summary>
    private static bool LooksUpEchKeyOverPlainDns(IReadOnlyList<string> arguments)
    {
        if (!string.Equals(AetherArguments.ValueAfter(arguments, "--ech")?.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var resolver = AetherArguments.ValueAfter(arguments, "--ech-dns") ?? AetherArguments.DefaultEchDns;
        return resolver.StartsWith("udp://", StringComparison.OrdinalIgnoreCase)
               || resolver.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The host of <paramref name="address" />, written as a name or an address, with a :port or without, an IPv6 address
    /// in brackets then (see the core's --api-address); null when there is none.
    /// </summary>
    private static string? HostOf(string? address)
    {
        var text = address?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            return close > 1 ? text[1..close] : null;
        }
        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            return text;
        }
        // Two colons or more without brackets: an IPv6 address alone.
        return text.IndexOf(':', colon + 1) >= 0 ? text : text[..colon];
    }

    /// <summary>
    /// Adds the name <paramref name="value" />, of the core's --api-sni or --api-host, gives the calls to the WARP API, as
    /// the core reads it: trimmed and without a trailing dot, when it is a name the core takes, which it checks as it
    /// checks --masque-sni (see AetherFmt.IsMasqueSni). Such a name may be none <see cref="Utils.IsDomain" /> takes, as
    /// one with an underscore.
    /// </summary>
    private static void AddApiName(List<string> names, string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || !AetherFmt.IsMasqueSni(text))
        {
            return;
        }
        names.Add(text.EndsWith('.') ? text[..^1] : text);
    }

    /// <summary>
    /// The host the core connects to for the DNS-over-HTTPS resolver <paramref name="echDns" />
    /// (https://host[:port]/path, then @address= and @sni= in either order, see AetherFmt.IsEchDns): the @address= value
    /// when there is one, the URL's host otherwise. Null for a resolver of another scheme, which names an IP address.
    /// </summary>
    private static string? DohHostOf(string? echDns)
    {
        const string scheme = "https://";
        if (echDns == null || !echDns.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var pieces = echDns[scheme.Length..].Split('@');
        foreach (var piece in pieces.Skip(1))
        {
            if (piece.StartsWith("address=", StringComparison.Ordinal))
            {
                return piece["address=".Length..];
            }
        }
        var authority = new string(pieces[0].TakeWhile(c => c is not ('/' or '?' or '#')).ToArray());
        if (authority.StartsWith('['))
        {
            return null;
        }
        var colon = authority.LastIndexOf(':');
        return colon >= 0 ? authority[..colon] : authority;
    }

    /// <summary>The value after every <paramref name="flag" /> in <paramref name="arguments" />, in order: the core takes each one of a repeated flag.</summary>
    private static IEnumerable<string> ValuesAfter(IReadOnlyList<string> arguments, string flag)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == flag)
            {
                yield return arguments[index + 1];
            }
        }
    }

    private static void AddDomain(List<string> names, string? name)
    {
        if (name != null && Utils.IsDomain(name))
        {
            names.Add(name);
        }
    }
}
