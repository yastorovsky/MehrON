namespace ServiceLib.Aether;

/// <summary>
/// PattN: the names the Aether editor's lists give the options of a profile, each in the order of the option's All
/// (PattNG arrays.xml: aether_protocol_entries and the other *_entries). The names of protocols, transports,
/// fingerprints and IP versions are PattNG's own and not translated there either; the others are read from ResUI as the
/// lists are made, so that they follow the language of the app.
/// </summary>
public static class AetherLabels
{
    /// <summary>The name the editor and the profile list give <paramref name="protocol" /> (PattNG aether_protocol_entries).</summary>
    public static string ProtocolLabel(AetherProtocol protocol)
    {
        if (protocol == AetherProtocol.Masque)
        {
            return "MASQUE";
        }
        if (protocol == AetherProtocol.WireGuard)
        {
            return "WireGuard";
        }
        if (protocol == AetherProtocol.Gool)
        {
            return "WARP-in-WARP";
        }
        if (protocol == AetherProtocol.Mim)
        {
            return "MASQUE-in-MASQUE";
        }
        return "WireGuard over MASQUE";
    }

    public static IReadOnlyList<string> Protocols => AetherProtocol.All.Select(ProtocolLabel).ToList();

    public static IReadOnlyList<string> Transports => ["HTTP/3", "HTTP/2"];

    public static IReadOnlyList<string> Fingerprints => ["Chrome", "Chrome 120", "Firefox", "Semi-Python", "Go"];

    public static IReadOnlyList<string> IpVersions => ["IPv4", "IPv6", "IPv4 + IPv6"];

    public static IReadOnlyList<string> ScanModes =>
    [
        ResUI.TbAetherScanTurbo, ResUI.TbAetherScanBalanced, ResUI.TbAetherScanThorough, ResUI.TbAetherScanVerified,
        ResUI.TbAetherScanIronclad,
    ];

    public static IReadOnlyList<string> Obfuscations =>
    [
        ResUI.TbAetherNoizeAuto, ResUI.TbAetherNoizeOff, ResUI.TbAetherNoizeLight, ResUI.TbAetherNoizeFirewall,
        ResUI.TbAetherNoizeBalanced, ResUI.TbAetherNoizeGfw, ResUI.TbAetherNoizeAggressive,
    ];

    public static IReadOnlyList<string> Psiphons =>
        [ResUI.TbAetherPsiphonOff, ResUI.TbAetherPsiphonChain, ResUI.TbAetherPsiphonReverse, ResUI.TbAetherPsiphonOnly];

    public static IReadOnlyList<string> PsiphonModes =>
        [ResUI.TbAetherPsiphonModeAuto, ResUI.TbAetherPsiphonModeCdn, ResUI.TbAetherPsiphonModeDirect];

    public static IReadOnlyList<string> PsiphonCdnSets =>
    [
        ResUI.TbAetherCdnSetCloudflare, ResUI.TbAetherCdnSetFastly, ResUI.TbAetherCdnSetCloudfront, ResUI.TbAetherCdnSetAkamai,
        ResUI.TbAetherCdnSetBunny, ResUI.TbAetherCdnSetVercel, ResUI.TbAetherCdnSetGithub, ResUI.TbAetherCdnSetCurated,
        ResUI.TbAetherCdnSetLegacy,
    ];

    public static IReadOnlyList<string> Tors =>
        [ResUI.TbAetherTorOff, ResUI.TbAetherTorChain, ResUI.TbAetherTorReverse, ResUI.TbAetherTorOnly];

    public static IReadOnlyList<string> TorBridges =>
        [ResUI.TbAetherTorBridgesAuto, ResUI.TbAetherTorBridgesFirst, ResUI.TbAetherTorBridgesNever, ResUI.TbAetherTorBridgesOwn];

    public static IReadOnlyList<string> TorRelays =>
        [ResUI.TbAetherTorRelaysAuto, ResUI.TbAetherTorRelaysOnly, ResUI.TbAetherTorRelaysOff];
}
