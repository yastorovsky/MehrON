namespace ServiceLib.Aether;

/// <summary>
/// PattN: what the Aether outbounds of one Xray configuration need (design 2.7.2), shared by every copy of its
/// <see cref="CoreConfigContext" />, as <see cref="CoreConfigContext.EchOutbounds" /> is. The outbounds record
/// themselves here while they are built; CoreConfigV2rayService.GenAether then settles on the one core they need, which
/// it leaves in <see cref="Core" /> for the session or the test that starts it. The exit-node lookups are the builder's
/// (they need the database, the generator does not wait for it) and outlive a generation; everything else is the
/// generation's own, see <see cref="Reset" />.
/// </summary>
public sealed class AetherBuild
{
    // The outbound objects themselves, as CoreConfigContext.CustomOutboundMap knows its outbounds: their tags change
    // after they are built (group members, chain hops and routing targets are tagged by their callers).
    private readonly Dictionary<Outbounds4Ray, AetherUse> _outbounds = new(ReferenceEqualityComparer.Instance);

    private int _chains;

    /// <summary>Every Aether outbound built, in the order it was built; AetherDependency.Of reads them.</summary>
    public List<AetherUse> Uses { get; } = [];

    /// <summary>
    /// What each exit-node name an Aether profile of the configuration names finds, by the name as
    /// <see cref="AetherExit.Node" /> holds it (trimmed). Filled by CoreConfigContextBuilder, which registers the one
    /// profile found as it registers any profile, so that its custom outbound is read and its server names protected.
    /// </summary>
    public Dictionary<string, ExitNodeLookup> ExitNodes { get; } = new(StringComparer.Ordinal);

    /// <summary>The first reason the configuration cannot run, already localized; the builders refuse through <see cref="Refuse" />.</summary>
    public string? Failure { get; private set; }

    /// <summary>
    /// Whether a hop of a proxy chain is tagged exit-node already: the first chain whose Aether hop dials out through a
    /// hop names it so, and every later chain keeps its own copy of its hop under the chain's tag.
    /// </summary>
    public bool ExitNodeTagTaken { get; set; }

    /// <summary>
    /// The core to start, dialling out through Xray (<see cref="AetherCore.Through" />) unless its command names an
    /// upstream of its own, its exit-node keyed with the content of its node; null when no outbound needs a core. Read
    /// it once the generation succeeded: a refused configuration starts no core.
    /// </summary>
    public AetherCore? Core { get; set; }

    /// <summary>
    /// The port of the secondary-socks inbound the generation gave the configuration for <see cref="Core" /> to dial out
    /// through (<see cref="AetherCore.Through" />); null when it gave none: no core, or one whose command names an upstream
    /// of its own. A session's load checks that it is free before Xray starts (AetherCoreLoad.BeforeXrayAsync, D3).
    /// </summary>
    public int? SecondaryInboundPort { get; set; }

    /// <summary>
    /// The port of the secondary-socks inbound for a latency test's own core, which the test picks (never one of the
    /// Aether listen port and the three after it); null otherwise. With it, the test's configuration routes its core
    /// through its own Xray as a session's does; without it, its Aether outbounds dial whatever core listens.
    /// </summary>
    public int? SecondaryPort { get; set; }

    /// <summary>
    /// The interface the outbounds of the short-lived Xray of a scan, a key run or a latency test are bound to while a TUN
    /// runs and the settings name no bind interface (AetherUplinkBinding, D2): the uplink, outside the TUN. It binds them
    /// as a bind interface of the settings would (ApplyOutboundBindInterface), and binds besides the ECH outbounds of the
    /// configuration and the ECH config queries of its outbounds, and has every name the Xray dials looked up by its own
    /// DNS, which leaves by the uplink as well (V2rayAetherService.LookUpNamesByUplink); null for every other configuration.
    /// </summary>
    public string? Uplink { get; set; }

    /// <summary>The Aether outbounds of the configuration, the copies a chain makes of them included.</summary>
    public IEnumerable<Outbounds4Ray> Outbounds => _outbounds.Keys;

    /// <summary>Records <paramref name="outbound" />, built for <paramref name="use" />.</summary>
    public void Add(Outbounds4Ray outbound, AetherUse use)
    {
        Uses.Add(use);
        _outbounds[outbound] = use;
    }

    /// <summary>Records <paramref name="copy" />, a copy of the Aether outbound <paramref name="original" />, as an Aether outbound too.</summary>
    public void AddCopy(Outbounds4Ray original, Outbounds4Ray copy)
    {
        if (_outbounds.TryGetValue(original, out var use))
        {
            _outbounds[copy] = use;
        }
    }

    /// <summary>Whether <paramref name="outbound" /> is an Aether outbound, a SOCKS one to the core on the loopback address.</summary>
    public bool IsAetherOutbound(Outbounds4Ray? outbound)
    {
        return outbound != null && _outbounds.ContainsKey(outbound);
    }

    /// <summary>The use <paramref name="outbound" /> was built for, null when it is no Aether outbound.</summary>
    public AetherUse? UseOf(Outbounds4Ray outbound)
    {
        return _outbounds.GetValueOrDefault(outbound);
    }

    /// <summary>A tag that tells a proxy chain built now from every other chain of the configuration (<see cref="AetherUse.ChainTag" />).</summary>
    public string NewChainTag(string baseTagName)
    {
        return $"{baseTagName}#{++_chains}";
    }

    /// <summary>Records why the configuration cannot run; the first reason counts.</summary>
    public void Refuse(string message)
    {
        Failure ??= message;
    }

    /// <summary>
    /// Forgets what a previous generation built (critique 22): a context generated twice would otherwise find the
    /// exit-node tag taken by its own earlier outbounds. The exit-node lookups, the test's secondary port and the uplink,
    /// which come from outside the generation, stay.
    /// </summary>
    public void Reset()
    {
        Uses.Clear();
        _outbounds.Clear();
        _chains = 0;
        Failure = null;
        ExitNodeTagTaken = false;
        Core = null;
        SecondaryInboundPort = null;
    }
}
