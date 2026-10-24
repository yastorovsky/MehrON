namespace ServiceLib.Aether;

/// <summary>
/// An Aether outbound of a configuration (PattN: what PattNG reads from its resolved outbounds), recorded while the
/// outbounds are built: the Aether <paramref name="Profile" />, the <paramref name="Core" /> its outbound dials, and
/// where it stands. Every Aether outbound is a SOCKS connection to the one core process the session starts.
/// </summary>
/// <param name="Core">The core of the profile, <see cref="AetherCore.Of" /> on the Aether listen port.</param>
/// <param name="Exit">
/// The exit-node the core dials out through where the outbound stands, when it is not the core's own: in a proxy chain
/// where the Aether profile is not the entry hop, the hops on its entry side, see <see cref="AetherExit.Through" />.
/// </param>
/// <param name="Tag">The tag of the outbound.</param>
/// <param name="ChainTag">The tag of the proxy chain the outbound is a hop of, one tag for each chain built; null outside a chain.</param>
public sealed record AetherUse(
    ProfileItem Profile,
    AetherCore Core,
    AetherExit? Exit = null,
    string? Tag = null,
    string? ChainTag = null)
{
    public AetherExit? Exit { get; set; } = Exit;

    public string? Tag { get; set; } = Tag;

    public string? ChainTag { get; set; } = ChainTag;

    /// <summary>The core this outbound needs: <see cref="Core" />, dialling out through <see cref="Exit" /> when it is set.</summary>
    public AetherCore NeededCore => Exit == null ? Core : Core with { Exit = Exit };
}

/// <summary>
/// The Aether core a configuration runs on (PattNG core/AetherDependency.kt). Every Aether outbound is a SOCKS
/// connection to the one core process the session starts, so a configuration can use one core, whether it is the
/// selected profile itself, a hop of a chain, a routing target or a policy-group member. Two profiles count as the same
/// core when it would be started with the same arguments, the port it listens on included, and would dial out through
/// the same exit-node.
/// <para>
/// In a chain the Aether hop can stand anywhere. The hops on its exit side dial through its outbound, as they dial
/// through any hop. Its outbound only reaches the core on the loopback address, so it dials through nothing itself; the
/// core dials out through the hop on its entry side, if there is one, see <see cref="AetherExit.Through" />. A chain of
/// two Aether hops would need two cores, and one runs.
/// </para>
/// </summary>
public abstract record AetherDependency
{
    private AetherDependency()
    {
    }

    /// <summary>No Aether outbound anywhere in the configuration.</summary>
    public sealed record None : AetherDependency
    {
        public static readonly None Instance = new();
    }

    /// <summary>Exactly one Aether core; the session starts it, and the Aether outbounds dial its <see cref="AetherCore.Port" />.</summary>
    public sealed record Single(AetherCore Core) : AetherDependency;

    /// <summary>Aether profiles with different settings, which one core cannot serve.</summary>
    public sealed record Conflicting : AetherDependency
    {
        public static readonly Conflicting Instance = new();
    }

    /// <summary>
    /// One tunnel that would dial out two ways: on its own and through a hop of a proxy chain, through two different
    /// hops, or by two different exit-node settings. One core dials out one way.
    /// </summary>
    public sealed record TwoExits : AetherDependency
    {
        public static readonly TwoExits Instance = new();
    }

    /// <summary>A proxy chain with more than one Aether hop.</summary>
    public sealed record TwoAetherHops(string ChainTag) : AetherDependency;

    /// <summary>
    /// The core the Aether outbounds <paramref name="uses" /> need, in the order they were built, or why one core cannot
    /// serve them all. A chain is told by its <see cref="AetherUse.ChainTag" />; a chain with two Aether hops is found
    /// where its first one stands.
    /// </summary>
    public static AetherDependency Of(IEnumerable<AetherUse> uses)
    {
        var list = uses.ToList();
        var hopsOfChain = list
            .Where(use => use.ChainTag != null)
            .GroupBy(use => use.ChainTag!)
            .ToDictionary(chain => chain.Key, chain => chain.Count());
        AetherCore? found = null;
        foreach (var use in list)
        {
            if (use.ChainTag != null && hopsOfChain[use.ChainTag] > 1)
            {
                return new TwoAetherHops(use.ChainTag);
            }
            var core = use.NeededCore;
            if (found == null)
            {
                found = core;
            }
            else if (core != found)
            {
                return core.Arguments.SequenceEqual(found.Arguments) ? TwoExits.Instance : Conflicting.Instance;
            }
        }
        return found == null ? None.Instance : new Single(found);
    }

    /// <summary>
    /// True when an inbound of the configuration <paramref name="content" />, as it is handed to Xray, listens on
    /// <paramref name="port" />. The Aether core has to listen there on the loopback address, and Xray comes first: an
    /// aether-core since 2.3.4 finds the port taken as it starts and stops, and an older one binds only once its tunnel
    /// is up, while the Aether outbound dials an inbound of its own configuration until the core gives up. PattN: this
    /// reads what the generated inbounds cannot show, the inbounds a full-configuration template brings, written by hand
    /// in any form Xray reads a port in.
    /// </summary>
    public static bool InboundListensOn(string content, int port)
    {
        if (JsonUtils.ParseJson(content) is not JsonObject config || config["inbounds"] is not JsonArray inbounds)
        {
            return false;
        }
        return inbounds.Any(inbound => inbound is JsonObject listener
                                       && InboundPorts(listener["port"]).Any(range => port >= range.Min && port <= range.Max));
    }

    /// <summary>
    /// The ports of an inbound: a number, or the text forms Xray reads, such as "1080", "1000-2000" and
    /// "53,443,1000-2000". A part that does not read, a port taken from the environment (env:) among them, is no port
    /// known here.
    /// </summary>
    private static List<(int Min, int Max)> InboundPorts(JsonNode? port)
    {
        if (port is not JsonValue value)
        {
            return [];
        }
        var text = value.GetValueKind() switch
        {
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.String => value.GetValue<string>(),
            _ => null,
        };
        var ranges = new List<(int Min, int Max)>();
        foreach (var part in text?.Split(',') ?? [])
        {
            var bounds = part.Split('-').Select(bound => AetherDigits.ToIntOrNull(KotlinText.Trim(bound))).ToList();
            if (bounds.Count is 1 or 2 && bounds.All(bound => bound != null))
            {
                ranges.Add((bounds.Min()!.Value, bounds.Max()!.Value));
            }
        }
        return ranges;
    }
}
