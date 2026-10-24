namespace ServiceLib.Aether;

/// <summary>
/// PattN: the Xray outbound of an Aether profile (PattNG core/CoreOutboundBuilder.kt): a SOCKS one to its core on the
/// loopback address, whose finalMask and dialMode belong to the exit-node the core's own traffic leaves Xray by.
/// </summary>
public static class AetherOutbound
{
    /// <summary>The targetStrategy that has Xray's DNS look names up first, IPv4 before IPv6.</summary>
    public const string ForceIPv4v6 = "ForceIPv4v6";

    /// <summary>
    /// The targetStrategy of <paramref name="profile" /> when it stores none (PattNG defaultTargetStrategy). A profile
    /// whose traffic leaves its core through WARP gets <see cref="ForceIPv4v6" />: the core looks names up inside the
    /// tunnel with no cache, once for every UDP datagram, so Xray's DNS, with its cache, looks them up first, IPv4 before
    /// IPv6, and a name it cannot look up is not sent at all. One whose traffic leaves through Tor or Psiphon passes names
    /// on as they are (AsIs, Xray's own): they look names up at their exit, and a forced lookup would drop .onion names
    /// and every name only the exit resolves.
    /// </summary>
    public static string DefaultTargetStrategy(ProfileItem profile)
    {
        return AetherCore.LeavesThroughWarp(profile) ? ForceIPv4v6 : Global.AsIs;
    }

    /// <summary>
    /// The targetStrategy the outbound of <paramref name="profile" /> carries (PattNG applyTargetStrategy): the stored
    /// one, or the default when it stores none (critique 5: ProfileItem.GetTargetStrategy reads a stored AsIs as none,
    /// which would bring the default back), and none for AsIs, Xray's default.
    /// </summary>
    public static string? TargetStrategyOf(ProfileItem profile)
    {
        var stored = profile.TargetStrategy?.Trim();
        var strategy = string.IsNullOrEmpty(stored) ? DefaultTargetStrategy(profile) : stored;
        return strategy.Equals(Global.AsIs, StringComparison.OrdinalIgnoreCase) ? null : strategy;
    }

    /// <summary>
    /// Whether the outbound of <paramref name="profile" />, a hop after the first of a proxy chain, keeps a
    /// targetStrategy (PattNG applyChainTargetStrategies): only one its profile stores. Such a hop carries the
    /// connection of the hop before it to that hop's server, whose name a lookup by Xray would ask of the DNS that
    /// reaches out through this same chain.
    /// </summary>
    public static bool KeepsTargetStrategyBehindAHop(ProfileItem profile)
    {
        return !string.IsNullOrWhiteSpace(profile.TargetStrategy);
    }

    /// <summary>
    /// The domain strategy that looks names up with Xray's DNS alone by the family <paramref name="strategy" />, a domain
    /// strategy of the direct outbound, asks for: its Force form (UseIPv4 gives ForceIPv4), a Force one as it is, and
    /// ForceIP for none, AsIs or one it does not know. Xray falls back to the OS resolver only for a Use strategy whose
    /// lookup fails.
    /// </summary>
    public static string ForcedStrategyOf(string? strategy)
    {
        const string use = "Use";
        const string force = "Force";
        var trimmed = strategy?.Trim() ?? string.Empty;
        var forced = trimmed.StartsWith(use, StringComparison.OrdinalIgnoreCase) ? force + trimmed[use.Length..]
            : trimmed.StartsWith(force, StringComparison.OrdinalIgnoreCase) ? force + trimmed[force.Length..]
            : string.Empty;
        return Global.TargetStrategies.FirstOrDefault(known => known.StartsWith(force, StringComparison.Ordinal)
                   && known.Equals(forced, StringComparison.OrdinalIgnoreCase))
               ?? "ForceIP";
    }
}
