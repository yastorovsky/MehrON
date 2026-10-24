namespace ServiceLib.Aether;

// The options of an Aether profile (PattNG enums/AetherOption.kt). Each type is a closed set of static instances in
// PattNG's order (All); FromString reads the value a profile or a link holds, exactly and case-sensitively, and gives
// the default for null or anything else, so that a value a later version adds reads as the default here.

/// <summary>
/// A WARP protocol: <see cref="Type" /> is what a profile and a link hold, <see cref="Core" /> the word the core's
/// --protocol takes for it.
/// </summary>
public sealed record AetherProtocol
{
    public static readonly AetherProtocol Masque = new("masque");
    public static readonly AetherProtocol WireGuard = new("wg");

    /// <summary>WARP-in-WARP: WireGuard carried in WireGuard, the gool the core has called classic since aether 2.3.0.</summary>
    public static readonly AetherProtocol Gool = new("gool");

    public static readonly AetherProtocol Mim = new("mim");

    /// <summary>
    /// WireGuard carried inside a MASQUE tunnel, the gool of aether 2.3.0: its WireGuard key is registered through the
    /// MASQUE tunnel, from inside WARP, so the traffic leaves from an address abroad.
    /// </summary>
    public static readonly AetherProtocol WgOverMasque = new("wg-over-masque", "gool");

    public static IReadOnlyList<AetherProtocol> All { get; } = [Masque, WireGuard, Gool, Mim, WgOverMasque];

    private AetherProtocol(string type, string? core = null)
    {
        Type = type;
        Core = core ?? type;
    }

    public string Type { get; }
    public string Core { get; }

    /// <summary>
    /// Whether MASQUE carries the tunnel, which then uses the MASQUE transport, fragmentation and key, and goes through
    /// Tor or Psiphon around it, which carry TCP alone, over HTTP/2.
    /// </summary>
    public bool OverMasque => this == Masque || this == Mim || this == WgOverMasque;

    /// <summary>Whether the tunnel is two hops, an outer and an inner one, in place of one endpoint.</summary>
    public bool TwoHops => this == Gool || this == Mim || this == WgOverMasque;

    /// <summary>Whether the two hops must be different addresses, as the core requires of WARP-in-WARP and MASQUE-in-MASQUE.</summary>
    public bool DistinctHops => this == Gool || this == Mim;

    public static AetherProtocol FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? WireGuard;
    }

    public override string ToString()
    {
        return Type;
    }
}

public sealed record AetherTransport
{
    public static readonly AetherTransport Http3 = new("h3");
    public static readonly AetherTransport Http2 = new("h2");

    public static IReadOnlyList<AetherTransport> All { get; } = [Http3, Http2];

    private AetherTransport(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherTransport FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Http3;
    }

    public override string ToString()
    {
        return Type;
    }
}

public sealed record AetherScanMode
{
    public static readonly AetherScanMode Turbo = new("turbo");
    public static readonly AetherScanMode Balanced = new("balanced");
    public static readonly AetherScanMode Thorough = new("thorough");
    public static readonly AetherScanMode Verified = new("verified");
    public static readonly AetherScanMode Ironclad = new("ironclad");

    /// <summary>The name the verified mode had before aether 2.1; profiles, links and configurations from then still carry it.</summary>
    public const string Stealth = "stealth";

    public static IReadOnlyList<AetherScanMode> All { get; } = [Turbo, Balanced, Thorough, Verified, Ironclad];

    private AetherScanMode(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherScanMode FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? (type == Stealth ? Verified : Balanced);
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// The obfuscation profile, named the way the core names it. <see cref="Auto" /> leaves the choice to the core, which
/// takes firewall for the tunnels over MASQUE and balanced for WireGuard and WARP-in-WARP.
/// </summary>
public sealed record AetherObfuscation
{
    public static readonly AetherObfuscation Auto = new("auto");
    public static readonly AetherObfuscation Off = new("off");
    public static readonly AetherObfuscation Light = new("light");
    public static readonly AetherObfuscation Firewall = new("firewall");
    public static readonly AetherObfuscation Balanced = new("balanced");
    public static readonly AetherObfuscation Gfw = new("gfw");
    public static readonly AetherObfuscation Aggressive = new("aggressive");

    public static IReadOnlyList<AetherObfuscation> All { get; } = [Auto, Off, Light, Firewall, Balanced, Gfw, Aggressive];

    private AetherObfuscation(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherObfuscation FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Auto;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// The IP versions the core scans and connects over; IPv4 unless a profile says otherwise, so that a profile connects
/// on an IPv4-only network and on a dual-stack one alike.
/// </summary>
public sealed record AetherIpVersion
{
    public static readonly AetherIpVersion V4 = new("v4");
    public static readonly AetherIpVersion V6 = new("v6");
    public static readonly AetherIpVersion Dual = new("both");

    public static IReadOnlyList<AetherIpVersion> All { get; } = [V4, V6, Dual];

    private AetherIpVersion(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherIpVersion FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? V4;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>Where Psiphon stands in the tunnel of a profile, named the way the core names it.</summary>
public sealed record AetherPsiphon
{
    public static readonly AetherPsiphon Off = new("off");

    /// <summary>The tunnel carries Psiphon: the app dials Psiphon, which leaves through WARP.</summary>
    public static readonly AetherPsiphon Chain = new("chain");

    /// <summary>Psiphon carries the tunnel: WARP is reached through Psiphon, and the app dials WARP.</summary>
    public static readonly AetherPsiphon Reverse = new("reverse");

    /// <summary>No WARP at all: the app dials Psiphon itself.</summary>
    public static readonly AetherPsiphon Only = new("only");

    public static IReadOnlyList<AetherPsiphon> All { get; } = [Off, Chain, Reverse, Only];

    private AetherPsiphon(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherPsiphon FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Off;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// The lists of CDN edges built into Psiphon's client, named the way its config names them and in the order the app
/// hands them over, which is the order the scan tries them.
/// </summary>
public sealed partial record AetherPsiphonCdnSet
{
    public static readonly AetherPsiphonCdnSet Cloudflare = new("cloudflare");
    public static readonly AetherPsiphonCdnSet Fastly = new("fastly");
    public static readonly AetherPsiphonCdnSet Cloudfront = new("cloudfront");
    public static readonly AetherPsiphonCdnSet Akamai = new("psiphon-akamai");
    public static readonly AetherPsiphonCdnSet Bunny = new("psiphon-bunny");
    public static readonly AetherPsiphonCdnSet Vercel = new("vercel");
    public static readonly AetherPsiphonCdnSet Github = new("github");
    public static readonly AetherPsiphonCdnSet Curated = new("curated-fronting");
    public static readonly AetherPsiphonCdnSet Legacy = new("legacy-android-overrides");

    public static IReadOnlyList<AetherPsiphonCdnSet> All { get; } =
        [Cloudflare, Fastly, Cloudfront, Akamai, Bunny, Vercel, Github, Curated, Legacy];

    private AetherPsiphonCdnSet(string type)
    {
        Type = type;
    }

    public string Type { get; }

    /// <summary>
    /// The sets named in <paramref name="text" />, a comma or space separated list: in the order of <see cref="All" />,
    /// once each, strangers left out.
    /// </summary>
    public static List<AetherPsiphonCdnSet> Parse(string? text)
    {
        var named = SeparatorRegex().Split(text ?? string.Empty).Where(it => it.Length > 0).ToHashSet();
        return All.Where(it => named.Contains(it.Type)).ToList();
    }

    /// <summary>
    /// <paramref name="sets" /> as the core and the profile take them, comma separated in the order of
    /// <see cref="All" />; null for none.
    /// </summary>
    public static string? Join(IEnumerable<AetherPsiphonCdnSet> sets)
    {
        var chosen = sets.ToHashSet();
        var text = string.Join(",", All.Where(chosen.Contains).Select(it => it.Type));
        return text.Length == 0 ? null : text;
    }

    public override string ToString()
    {
        return Type;
    }

    // PattNG splits on [,\s]+, where \s is Java's: ASCII white space alone, which .NET's \s is not.
    [GeneratedRegex("[,\\t\\n\\x0B\\f\\r ]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();
}

/// <summary>How Psiphon reaches its servers.</summary>
public sealed record AetherPsiphonMode
{
    public static readonly AetherPsiphonMode Auto = new("auto");
    public static readonly AetherPsiphonMode Cdn = new("cdn");
    public static readonly AetherPsiphonMode Direct = new("direct");

    public static IReadOnlyList<AetherPsiphonMode> All { get; } = [Auto, Cdn, Direct];

    private AetherPsiphonMode(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherPsiphonMode FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Auto;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// Where Tor stands in the tunnel of a profile, named the way the core names it: the same three places as
/// <see cref="AetherPsiphon" />.
/// </summary>
public sealed record AetherTor
{
    public static readonly AetherTor Off = new("off");

    /// <summary>The tunnel carries Tor: the app dials Tor, which leaves through WARP, so the network never sees Tor.</summary>
    public static readonly AetherTor Chain = new("chain");

    /// <summary>Tor carries the tunnel: WARP is reached from a Tor exit, and the app dials WARP.</summary>
    public static readonly AetherTor Reverse = new("reverse");

    /// <summary>No WARP at all: the app dials Tor itself.</summary>
    public static readonly AetherTor Only = new("only");

    public static IReadOnlyList<AetherTor> All { get; } = [Off, Chain, Reverse, Only];

    private AetherTor(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherTor FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Off;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>When Tor turns to bridges.</summary>
public sealed record AetherTorBridges
{
    /// <summary>Tor is tried plainly first, and bridges are fetched when that gets nowhere.</summary>
    public static readonly AetherTorBridges Auto = new("auto");

    /// <summary>Bridges from the start, without trying Tor plainly.</summary>
    public static readonly AetherTorBridges First = new("first");

    /// <summary>Never, however blocked the network looks.</summary>
    public static readonly AetherTorBridges Never = new("never");

    /// <summary>The bridge lines of the profile, and no other.</summary>
    public static readonly AetherTorBridges Own = new("own");

    public static IReadOnlyList<AetherTorBridges> All { get; } = [Auto, First, Never, Own];

    private AetherTorBridges(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherTorBridges FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Auto;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// Where Tor's fetched bridges come from: bridgedb, the public relays onionoo lists used as plain bridges, or both.
/// </summary>
public sealed record AetherTorRelays
{
    /// <summary>bridgedb and the relays together, the core's own choice.</summary>
    public static readonly AetherTorRelays Auto = new("auto");

    /// <summary>The relays alone; bridgedb hands out few bridges, and they are blocked early.</summary>
    public static readonly AetherTorRelays Only = new("only");

    /// <summary>bridgedb alone.</summary>
    public static readonly AetherTorRelays Off = new("off");

    public static IReadOnlyList<AetherTorRelays> All { get; } = [Auto, Only, Off];

    private AetherTorRelays(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public static AetherTorRelays FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Auto;
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// Which WARP keys a registration gets: every key, or the keys of one protocol, both hops' keys for a two-hop one.
/// <see cref="Type" /> is the protocol's own value, which the WARP keys window stores; <see cref="Register" /> is the
/// word the core's --register takes for those keys.
/// </summary>
public sealed record AetherKeyKind
{
    /// <summary>Every key (PattNG's ALL; the name All is the list of kinds here).</summary>
    public static readonly AetherKeyKind AllKeys = new("all", "all");

    public static readonly AetherKeyKind WireGuard = new("wg", "wg");
    public static readonly AetherKeyKind Masque = new("masque", "masque");

    /// <summary>WARP-in-WARP, the older gool: both WireGuard hops' keys, which the core registers as gool-classic.</summary>
    public static readonly AetherKeyKind Gool = new("gool", "gool-classic");

    public static readonly AetherKeyKind Mim = new("mim", "mim");

    /// <summary>WireGuard over MASQUE, the gool of the core's --gool: the MASQUE key and the WireGuard key it carries inside.</summary>
    public static readonly AetherKeyKind WgOverMasque = new("wg-over-masque", "gool");

    public static IReadOnlyList<AetherKeyKind> All { get; } = [AllKeys, WireGuard, Masque, Gool, Mim, WgOverMasque];

    private AetherKeyKind(string type, string register)
    {
        Type = type;
        Register = register;
    }

    public string Type { get; }
    public string Register { get; }

    public static AetherKeyKind FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? AllKeys;
    }

    /// <summary>The kind the core reads <paramref name="word" /> after --register as, under any of the names it accepts; null for none.</summary>
    public static AetherKeyKind? OfRegister(string? word)
    {
        switch (word?.Trim().ToLowerInvariant())
        {
            case "all":
                return AllKeys;

            case "wg":
            case "wireguard":
            case "warp":
                return WireGuard;

            case "masque":
                return Masque;

            case "gool-classic":
            case "wiw":
            case "warp-in-warp":
                return Gool;

            case "mim":
            case "masque-in-masque":
                return Mim;

            case "gool":
            case "wg-over-masque":
                return WgOverMasque;

            default:
                return null;
        }
    }

    public override string ToString()
    {
        return Type;
    }
}

/// <summary>
/// <para>
/// The ClientHello of the TLS handshakes of the core, after a client whose ClientHello was captured: its TLS 1.2 suites as
/// BoringSSL names them, in its order, or Chrome's own rule; its TLS 1.3 suites in its order, where that order is not
/// BoringSSL's; its groups in its order, those BoringSSL has, and its key shares, where they are not BoringSSL's; its
/// signature algorithms, those BoringSSL has; the extensions it sends that BoringSSL can send, and only those: GREASE
/// values, GREASE among the signature algorithms, certificate compression, ALPS, GREASE ECH, trust anchors, server
/// padding, OCSP, signed certificate timestamps and a session ticket; and whether its extensions go in a new order on
/// each handshake. BoringSSL writes the TLS 1.3 suites before the TLS 1.2 ones, whatever the client does. Unless they
/// are named, it orders the TLS 1.3 suites by the machine's AES instructions, as Chrome does, AES-GCM first with them and
/// ChaCha20 first without, and it sends key shares for the first group and for the first one of the other kind,
/// post-quantum or not. It cannot send delegated_credentials, record_size_limit, signature_algorithms_cert,
/// encrypt_then_mac or more than one point format, always sends psk_key_exchange_modes, and keeps an order of the
/// extensions of its own.
/// </para>
/// <para>
/// Each names its suites, its groups and its signature algorithms, so that it stays the client's whatever the core's
/// defaults become; what the core sends unless told otherwise (GREASE, OCSP, signed certificate timestamps, the session
/// ticket, the new order on each handshake) a fingerprint without it turns off, and what the core leaves out unless told
/// otherwise a fingerprint with it turns on. The core takes these options from Aether 2.3.10 on, --tls13-ciphers,
/// --tls-key-shares and --enable-grease-sigalgs from 2.3.8 on (PattNG enums/AetherOption.kt).
/// </para>
/// </summary>
public sealed record AetherFingerprint
{
    /// <summary>Chrome's eight signature algorithms, which Chrome 120 sends alone and Chrome of today after ML-DSA.</summary>
    private const string ChromeEightSigalgs = "ecdsa_secp256r1_sha256:rsa_pss_rsae_sha256:rsa_pkcs1_sha256:"
        + "ecdsa_secp384r1_sha384:rsa_pss_rsae_sha384:rsa_pkcs1_sha384:rsa_pss_rsae_sha512:rsa_pkcs1_sha512";

    /// <summary>
    /// The trust anchors Chrome's ClientHello of October 2026 asks for, the 28 IDs of its trust_anchors (0xca34) in dotted
    /// decimal, in its order.
    /// </summary>
    private const string ChromeTrustAnchors = "44947.2.1:44947.2.6:44947.2.13:44947.2.14:44947.2.15:44947.2.18:44947.2.19:"
        + "44947.2.20:52580.200109.1.7:52580.200109.1.8:52580.200109.1.9:52580.200109.1.10:52580.200109.1.11:"
        + "52580.200109.1.12:52580.200109.1.13:52580.200109.1.18:52580.200109.1.19:11129.9.1:11129.9.4:11129.9.5:"
        + "11129.9.6:11129.9.7:11129.9.8:11129.9.10:11129.9.11:11129.9.12:11129.9.13:11129.9.15";

    /// <summary>
    /// <para>
    /// Chrome's own rule rather than a list, which BoringSSL orders as it does for Chrome, by the machine's AES
    /// instructions as the TLS 1.3 suites: c02b c02f c02c c030 cca9 cca8 c013 c014 009c 009d 002f 0035 with them,
    /// ChaCha20 first without. A fixed list would keep AES-GCM first there while the TLS 1.3 suites go ChaCha20 first, a
    /// ClientHello no Chrome sends. Chrome's BoringSSL of October 2026 still orders both lists so. After a GREASE value.
    /// </para>
    /// <para>
    /// Chrome's groups, as its ClientHello of October 2026 lists them after a GREASE value: X25519MLKEM768, X25519,
    /// P-256, P-384. BoringSSL gives the first a key share, and X25519, the first that is not post-quantum, another, as
    /// Chrome does.
    /// </para>
    /// <para>
    /// Its signature algorithms after a GREASE value, which BoringSSL sends only when told to: ML-DSA-44, -65 and -87,
    /// then its eight. Its extensions, all of which BoringSSL sends as Chrome does, Chrome's BoringSSL being the same:
    /// brotli certificate compression; ALPS for HTTP/2 under the new codepoint; a GREASE ECH, where its capture went with
    /// Cloudflare's ECH key; its 28 trust anchors; 6000 bytes of padding asked of the server; OCSP, signed certificate
    /// timestamps and a session ticket; and a new order on each handshake, as Chrome's since its version 110.
    /// </para>
    /// </summary>
    public static readonly AetherFingerprint Chrome = new(
        "chrome",
        "ALL:!aPSK:!ECDSA+SHA1:!3DES",
        true,
        "X25519MLKEM768:X25519:P-256:P-384",
        sigalgs: "mldsa44:mldsa65:mldsa87:" + ChromeEightSigalgs,
        greaseSigalgs: true,
        certCompression: "brotli",
        alps: "new",
        trustAnchors: ChromeTrustAnchors,
        serverPadding: 6000,
        echGrease: true);

    /// <summary>
    /// Chrome 120's, as its ClientHello was captured: Chrome's own rule, which gives the TLS 1.2 suites of Chrome's of
    /// today in the same order, after a GREASE value and BoringSSL's TLS 1.3 order. Its groups, after a GREASE value,
    /// are X25519, P-256 and P-384, none of them post-quantum, with a key share for X25519, which BoringSSL sends of
    /// itself. GREASE as Chrome's, but none among its signature algorithms, which are Chrome's eight without ML-DSA.
    /// Brotli certificate compression, ALPS for HTTP/2 under the old codepoint, a GREASE ECH, OCSP, signed certificate
    /// timestamps, a session ticket and a new order on each handshake; neither trust anchors nor server padding.
    /// </summary>
    public static readonly AetherFingerprint Chrome120 = new(
        "chrome-120",
        "ALL:!aPSK:!ECDSA+SHA1:!3DES",
        true,
        "X25519:P-256:P-384",
        sigalgs: ChromeEightSigalgs,
        certCompression: "brotli",
        alps: "old",
        echGrease: true);

    /// <summary>
    /// <para>
    /// c02b c02f cca9 cca8 c02c c030 c013 c014 009c 009d 002f 0035, without GREASE, after Firefox's TLS 1.3 suites in its
    /// own order: 1301 1303 1302.
    /// </para>
    /// <para>
    /// Firefox's groups, as its ClientHello of October 2026 lists them: X25519MLKEM768, X25519, P-256, P-384, P-521, with
    /// key shares for the first three, where BoringSSL would send two.
    /// </para>
    /// <para>
    /// Its signature algorithms; zlib, brotli and zstd certificate compression; a GREASE ECH, where its capture went with
    /// Cloudflare's ECH key, as Firefox sends one otherwise; OCSP and signed certificate timestamps; no session ticket;
    /// and one order of the extensions, BoringSSL's, as Firefox keeps one of its own. Its delegated_credentials and
    /// record_size_limit BoringSSL cannot send.
    /// </para>
    /// </summary>
    public static readonly AetherFingerprint Firefox = new(
        "firefox",
        "ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:"
        + "ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES128-SHA:ECDHE-RSA-AES256-SHA:"
        + "AES128-GCM-SHA256:AES256-GCM-SHA384:AES128-SHA:AES256-SHA",
        false,
        "X25519MLKEM768:X25519:P-256:P-384:P-521",
        sigalgs: "ecdsa_secp256r1_sha256:ecdsa_secp384r1_sha384:ecdsa_secp521r1_sha512:rsa_pss_rsae_sha256:"
        + "rsa_pss_rsae_sha384:rsa_pss_rsae_sha512:rsa_pkcs1_sha256:rsa_pkcs1_sha384:rsa_pkcs1_sha512:ecdsa_sha1:"
        + "rsa_pkcs1_sha1",
        tls13Ciphers: "TLS_AES_128_GCM_SHA256:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_256_GCM_SHA384",
        keyShares: "X25519MLKEM768:X25519:P-256",
        certCompression: "zlib:brotli:zstd",
        echGrease: true,
        sessionTicket: false,
        permuteExtensions: false);

    /// <summary>
    /// <para>
    /// c02c c030 c02b c02f cca9 cca8 c00a c014 c023 c027: Python's first ten TLS 1.2 suites, with c00a and c014
    /// (AES-256-CBC with SHA-1) in place of c024 and c028 (with SHA-384), the nearest ones BoringSSL has; Python's four
    /// DHE suites after them are left out, as BoringSSL has none. Hence semi-python: not Python's list as it is, but
    /// every suite of it a core on boring 5.2 or newer takes; an older one refuses c023. Without GREASE, after Python's TLS
    /// 1.3 suites in its own order: 1302 1303 1301.
    /// </para>
    /// <para>
    /// Python's groups, as its ClientHello of October 2026 lists them, without X448, ffdhe2048 and ffdhe3072, which
    /// BoringSSL does not have: X25519MLKEM768, X25519, P-256, P-384, P-521. Key shares for the first two, as Python
    /// sends them.
    /// </para>
    /// <para>
    /// Its signature algorithms that BoringSSL has, in its order: Ed448, the brainpool curves, RSA-PSS with PSS keys,
    /// SHA-224 and DSA left out. Neither OCSP nor signed certificate timestamps; a session ticket; one order of the
    /// extensions. Its encrypt_then_mac and its three point formats BoringSSL cannot send.
    /// </para>
    /// </summary>
    public static readonly AetherFingerprint SemiPython = new(
        "semi-python",
        "ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:"
        + "ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:ECDHE-ECDSA-AES256-SHA:ECDHE-RSA-AES256-SHA:"
        + "ECDHE-ECDSA-AES128-SHA256:ECDHE-RSA-AES128-SHA256",
        false,
        "X25519MLKEM768:X25519:P-256:P-384:P-521",
        sigalgs: "mldsa65:mldsa87:mldsa44:ecdsa_secp256r1_sha256:ecdsa_secp384r1_sha384:ecdsa_secp521r1_sha512:"
        + "ed25519:rsa_pss_rsae_sha256:rsa_pss_rsae_sha384:rsa_pss_rsae_sha512:rsa_pkcs1_sha256:rsa_pkcs1_sha384:"
        + "rsa_pkcs1_sha512",
        tls13Ciphers: "TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_128_GCM_SHA256",
        ocspStapling: false,
        sct: false,
        permuteExtensions: false);

    /// <summary>
    /// <para>
    /// c02b c02f c02c c030 cca9 cca8 c009 c013 c00a c014; Go lists its TLS 1.3 suites after them, which BoringSSL cannot
    /// do, in BoringSSL's order among themselves. Without GREASE.
    /// </para>
    /// <para>
    /// Go's groups, as its ClientHello of October 2026 lists them, without SecP256r1MLKEM768 and SecP384r1MLKEM1024,
    /// which BoringSSL does not have: X25519MLKEM768, X25519, P-256, P-384, P-521. Key shares for the first two, as Go
    /// sends them.
    /// </para>
    /// <para>
    /// Its signature algorithms; OCSP and signed certificate timestamps; no session ticket; one order of the extensions.
    /// Its signature_algorithms_cert BoringSSL cannot send.
    /// </para>
    /// </summary>
    public static readonly AetherFingerprint Go = new(
        "go",
        "ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:"
        + "ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:ECDHE-ECDSA-AES128-SHA:ECDHE-RSA-AES128-SHA:"
        + "ECDHE-ECDSA-AES256-SHA:ECDHE-RSA-AES256-SHA",
        false,
        "X25519MLKEM768:X25519:P-256:P-384:P-521",
        sigalgs: "mldsa44:mldsa65:mldsa87:rsa_pss_rsae_sha256:ecdsa_secp256r1_sha256:ed25519:rsa_pss_rsae_sha384:"
        + "rsa_pss_rsae_sha512:rsa_pkcs1_sha256:rsa_pkcs1_sha384:rsa_pkcs1_sha512:ecdsa_secp384r1_sha384:"
        + "ecdsa_secp521r1_sha512",
        sessionTicket: false,
        permuteExtensions: false);

    public static IReadOnlyList<AetherFingerprint> All { get; } = [Chrome, Chrome120, Firefox, SemiPython, Go];

    private AetherFingerprint(
        string type,
        string ciphers,
        bool grease,
        string groups,
        string sigalgs,
        string? tls13Ciphers = null,
        string? keyShares = null,
        bool greaseSigalgs = false,
        string? certCompression = null,
        string? alps = null,
        string? trustAnchors = null,
        int? serverPadding = null,
        bool echGrease = false,
        bool ocspStapling = true,
        bool sct = true,
        bool sessionTicket = true,
        bool permuteExtensions = true)
    {
        Type = type;
        Ciphers = ciphers;
        Grease = grease;
        Groups = groups;
        Sigalgs = sigalgs;
        Tls13Ciphers = tls13Ciphers;
        KeyShares = keyShares;
        GreaseSigalgs = greaseSigalgs;
        CertCompression = certCompression;
        Alps = alps;
        TrustAnchors = trustAnchors;
        ServerPadding = serverPadding;
        EchGrease = echGrease;
        OcspStapling = ocspStapling;
        Sct = sct;
        SessionTicket = sessionTicket;
        PermuteExtensions = permuteExtensions;
    }

    public string Type { get; }
    public string Ciphers { get; }
    public bool Grease { get; }

    /// <summary>The groups, in order, for --tls-groups.</summary>
    public string Groups { get; }

    /// <summary>The signature algorithms, in order, by the names BoringSSL gives them, for --tls-sigalgs.</summary>
    public string Sigalgs { get; }

    /// <summary>The TLS 1.3 suites, in order, for --tls13-ciphers; null leaves BoringSSL's order.</summary>
    public string? Tls13Ciphers { get; }

    /// <summary>The groups that get a key share, in order, for --tls-key-shares; null leaves BoringSSL's choice.</summary>
    public string? KeyShares { get; }

    /// <summary>Whether a GREASE value leads the signature algorithms, for --enable-grease-sigalgs.</summary>
    public bool GreaseSigalgs { get; }

    /// <summary>The certificate compression offered, of zlib, brotli and zstd, in order, for --tls-cert-compression.</summary>
    public string? CertCompression { get; }

    /// <summary>The ALPS codepoint, new (0x44cd) or old (0x4469), for --tls-alps; ALPS goes for HTTP/2 and HTTP/3.</summary>
    public string? Alps { get; }

    /// <summary>The trust anchor IDs asked for, in dotted decimal, for --tls-trust-anchors.</summary>
    public string? TrustAnchors { get; }

    /// <summary>The bytes of padding asked of the server, for --tls-server-padding.</summary>
    public int? ServerPadding { get; }

    /// <summary>A GREASE ECH when no ECH key is offered, --enable-ech-grease.</summary>
    public bool EchGrease { get; }

    /// <summary>OCSP asked for; without it, --disable-ocsp-stapling.</summary>
    public bool OcspStapling { get; }

    /// <summary>Signed certificate timestamps asked for; without them, --disable-sct.</summary>
    public bool Sct { get; }

    /// <summary>A session_ticket extension; without it, --disable-session-ticket.</summary>
    public bool SessionTicket { get; }

    /// <summary>The extensions in a new order on each handshake; without it, --disable-extension-permutation.</summary>
    public bool PermuteExtensions { get; }

    /// <summary>
    /// The core's options for this fingerprint: its TLS 1.2 suites, its TLS 1.3 order where it has one, its groups, its
    /// key shares where it names them, its signature algorithms, the certificate compression, ALPS, trust anchors and
    /// server padding it has, and a flag for each of the rest that is not the core's own: GREASE left out, GREASE among
    /// the signature algorithms, GREASE ECH, and OCSP, signed certificate timestamps, the session ticket and the new
    /// order on each handshake left out.
    /// </summary>
    public IReadOnlyList<string> Arguments
    {
        get
        {
            List<string> arguments = ["--tls-ciphers", Ciphers];
            if (Tls13Ciphers is not null)
            {
                arguments.AddRange(["--tls13-ciphers", Tls13Ciphers]);
            }
            arguments.AddRange(["--tls-groups", Groups]);
            if (KeyShares is not null)
            {
                arguments.AddRange(["--tls-key-shares", KeyShares]);
            }
            arguments.AddRange(["--tls-sigalgs", Sigalgs]);
            if (CertCompression is not null)
            {
                arguments.AddRange(["--tls-cert-compression", CertCompression]);
            }
            if (Alps is not null)
            {
                arguments.AddRange(["--tls-alps", Alps]);
            }
            if (TrustAnchors is not null)
            {
                arguments.AddRange(["--tls-trust-anchors", TrustAnchors]);
            }
            if (ServerPadding is int padding)
            {
                arguments.AddRange(["--tls-server-padding", padding.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            }
            if (!Grease)
            {
                arguments.Add("--disable-grease");
            }
            if (GreaseSigalgs)
            {
                arguments.Add("--enable-grease-sigalgs");
            }
            if (EchGrease)
            {
                arguments.Add("--enable-ech-grease");
            }
            if (!OcspStapling)
            {
                arguments.Add("--disable-ocsp-stapling");
            }
            if (!Sct)
            {
                arguments.Add("--disable-sct");
            }
            if (!SessionTicket)
            {
                arguments.Add("--disable-session-ticket");
            }
            if (!PermuteExtensions)
            {
                arguments.Add("--disable-extension-permutation");
            }
            return arguments;
        }
    }

    public static AetherFingerprint FromString(string? type)
    {
        return All.FirstOrDefault(it => it.Type == type) ?? Chrome;
    }

    public override string ToString()
    {
        return Type;
    }
}
