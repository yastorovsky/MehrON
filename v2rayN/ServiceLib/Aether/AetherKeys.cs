using System.Globalization;

namespace ServiceLib.Aether;

/// <summary>
/// The settings of the window that gets new WARP keys (PattNG core/AetherKeys.kt AetherKeysSettings). They are that
/// window's alone: no profile's settings reach them, and they reach no profile. Text is kept as written and read when a
/// run is built. <see cref="Command" /> is blank unless it was edited by hand, and then it runs as written, see
/// <see cref="AetherKeys.IsCustom" />.
/// </summary>
public sealed record AetherKeysSettings
{
    public AetherKeyKind Kind { get; init; } = AetherKeyKind.AllKeys;

    /// <summary>
    /// The API address, --api-address (PattNG's enrollAddress, the name its stored setting keeps); blank, the core's own:
    /// the address of the server name.
    /// </summary>
    public string EnrollAddress { get; init; } = string.Empty;

    /// <summary>The server name of the calls to the WARP API, --api-sni; blank, the core's own: the host.</summary>
    public string ApiSni { get; init; } = string.Empty;

    /// <summary>
    /// The HTTP host of the calls to the WARP API, --api-host; blank, the core's own: <see cref="AetherKeys.DefaultApiHost" />,
    /// then api.cloudflareclient.com.
    /// </summary>
    public string ApiHost { get; init; } = string.Empty;

    public bool Fragment { get; init; }
    public string FragmentSize { get; init; } = string.Empty;
    public string FragmentDelay { get; init; } = string.Empty;
    public bool Ech { get; init; }
    public string EchDns { get; init; } = AetherArguments.DefaultEchDns;
    public string EchDomain { get; init; } = AetherArguments.DefaultEchDomain;
    public AetherFingerprint Fingerprint { get; init; } = AetherFingerprint.Chrome;
    public string ExitNode { get; init; } = string.Empty;
    public string FinalMask { get; init; } = string.Empty;
    public string DialMode { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// The exit-node the run dials out through: the profile <see cref="ExitNode" /> names by its name, see
    /// <see cref="AetherExit.Node" />, or else a plain one, with the finalMask and the dialMode set here.
    /// </summary>
    public AetherExit Exit
    {
        get
        {
            if (!AetherKeys.IsBlank(ExitNode))
            {
                return new AetherExit(Node: AetherKeys.Trim(ExitNode));
            }
            return new AetherExit(AetherKeys.IsBlank(FinalMask) ? null : FinalMask, AetherKeys.IsBlank(DialMode) ? null : DialMode);
        }
    }
}

/// <summary>Why the settings of the WARP keys window cannot run (PattNG AetherKeys.Problem).</summary>
public enum AetherKeysProblem
{
    InvalidEnrollAddress,
    InvalidApiSni,
    InvalidApiHost,
    InvalidFragment,
    InvalidEchDns,
    InvalidEchDomain,
    InvalidFinalMask,
    InvalidCommand,
}

/// <summary>
/// The run of the core that gets new WARP keys (PattNG core/AetherKeys.kt): its --register, which registers the keys
/// asked for and ends without scanning or opening a tunnel, with where the calls to the WARP API go, whether they hide
/// their server name with Encrypted Client Hello, and the cipher suites their TLS handshakes offer.
/// </summary>
public static class AetherKeys
{
    /// <summary>
    /// The HTTP host of the core's calls to the WARP API, which register and enroll the keys, unless told otherwise: the
    /// name the Cloudflare One client calls since its 2026.6.0. The server name follows the host and the address the
    /// server name, unless told otherwise; with neither the server name nor the host set, the core goes by
    /// <see cref="FallbackApiHost" /> next when this name does not serve a call.
    /// </summary>
    public const string DefaultApiHost = "api.devices.cloudflare.com";

    /// <summary>The WARP API's older name, which the core goes by when <see cref="DefaultApiHost" /> does not serve a call.</summary>
    public const string FallbackApiHost = "api.cloudflareclient.com";

    /// <summary>
    /// What the API address, server name and host of the WARP keys window offer to pick, each of them blank, the core's
    /// own, until one is picked or written (PattNG arrays.xml aether_api_name_options): the two names of the API, the one
    /// the core goes by first, then the other.
    /// </summary>
    public static IReadOnlyList<string> ApiNameOptions { get; } = [DefaultApiHost, FallbackApiHost];

    /// <summary>The option that has the core register keys and end, with the word naming which.</summary>
    internal const string RegisterFlag = "--register";

    /// <summary>
    /// The arguments <paramref name="settings" /> give: the keys to register first, the address the calls to the WARP
    /// API go to, the server name of their ClientHello and their HTTP host, each unless it is left blank, the
    /// ClientHello of those calls in pieces when fragmenting is on (--fragment, which a run that only registers keys
    /// applies to those calls alone), sized and spaced as set or as the core has them by default, and whole without the
    /// flag, Encrypted Client Hello on those calls with the key of the ECH domain, asked of the ECH DNS, and the TLS 1.2
    /// cipher suites of the fingerprint and its groups, with GREASE left out where the fingerprint has none.
    /// </summary>
    public static List<string> Arguments(AetherKeysSettings settings)
    {
        var arguments = new List<string> { RegisterFlag, settings.Kind.Register };
        var address = Trim(settings.EnrollAddress);
        if (address.Length > 0)
        {
            arguments.AddRange(["--api-address", address]);
        }
        var sni = Trim(settings.ApiSni);
        if (sni.Length > 0)
        {
            arguments.AddRange(["--api-sni", sni]);
        }
        var host = Trim(settings.ApiHost);
        if (host.Length > 0)
        {
            arguments.AddRange(["--api-host", host]);
        }
        if (settings.Fragment)
        {
            arguments.Add("--fragment");
            var size = AetherRange.Parse(settings.FragmentSize, AetherRange.FragmentSize);
            if (size != null)
            {
                arguments.AddRange(["--fragment-size", size.ToString()]);
            }
            var delay = AetherRange.Parse(settings.FragmentDelay, AetherRange.FragmentDelay);
            if (delay != null)
            {
                arguments.AddRange(["--fragment-delay", delay.ToString()]);
            }
        }
        if (settings.Ech)
        {
            arguments.AddRange(["--ech", "auto"]);
            arguments.AddRange(["--ech-dns", IfEmpty(Trim(settings.EchDns), AetherArguments.DefaultEchDns)]);
            arguments.AddRange(["--ech-domain", IfEmpty(Trim(settings.EchDomain), AetherArguments.DefaultEchDomain)]);
        }
        arguments.AddRange(settings.Fingerprint.Arguments);
        return arguments;
    }

    /// <summary>The command line <see cref="Arguments" /> makes of <paramref name="settings" />, as the window shows it.</summary>
    public static string BuiltCommand(AetherKeysSettings settings)
    {
        return new AetherCore(Arguments(settings)).Command;
    }

    /// <summary>Whether <paramref name="settings" /> carry a command written by hand: one that is not blank and not the one the settings give.</summary>
    public static bool IsCustom(AetherKeysSettings settings)
    {
        return !IsBlank(settings.Command) && Trim(settings.Command) != BuiltCommand(settings);
    }

    /// <summary>The arguments a run of <paramref name="settings" /> starts the core with: those of the command written by hand, or the settings'.</summary>
    public static List<string> RunArguments(AetherKeysSettings settings)
    {
        return IsCustom(settings) ? AetherCore.ArgumentsOf(settings.Command) : Arguments(settings);
    }

    /// <summary>
    /// The keys <paramref name="arguments" /> register, read as the core reads them: the word after the last
    /// --register; null without one it takes.
    /// </summary>
    public static AetherKeyKind? KindOf(IReadOnlyList<string> arguments)
    {
        for (var at = arguments.Count - 1; at >= 0; at--)
        {
            if (arguments[at] == RegisterFlag)
            {
                return AetherKeyKind.OfRegister(at + 1 < arguments.Count ? arguments[at + 1] : null);
            }
        }
        return null;
    }

    /// <summary>
    /// Why <paramref name="settings" /> cannot run, or null when they can. A command written by hand is only checked for
    /// what it registers: the settings it replaces do not count, and the core names what else it does not take. The
    /// finalMask counts either way, since the run dials out through it, and is checked as well while a profile is the
    /// exit-node, which leaves it out of use, as the Aether editor checks it: no broken one is kept for freedom. Whether
    /// one profile, and one only, has that name still is for the run to look up.
    /// </summary>
    public static AetherKeysProblem? Problem(AetherKeysSettings settings)
    {
        if (!AetherExit.TakesFinalMask(settings.FinalMask))
        {
            return AetherKeysProblem.InvalidFinalMask;
        }
        if (IsCustom(settings))
        {
            return KindOf(RunArguments(settings)) == null ? AetherKeysProblem.InvalidCommand : null;
        }
        var address = Trim(settings.EnrollAddress);
        if (address.Length > 0 && !IsEnrollAddress(address))
        {
            return AetherKeysProblem.InvalidEnrollAddress;
        }
        // The core checks --api-sni and --api-host as it checks --masque-sni.
        var sni = Trim(settings.ApiSni);
        if (sni.Length > 0 && !AetherFmt.IsMasqueSni(sni))
        {
            return AetherKeysProblem.InvalidApiSni;
        }
        var host = Trim(settings.ApiHost);
        if (host.Length > 0 && !AetherFmt.IsMasqueSni(host))
        {
            return AetherKeysProblem.InvalidApiHost;
        }
        if (settings.Fragment
            && !(TakesRange(settings.FragmentSize, AetherRange.FragmentSize) && TakesRange(settings.FragmentDelay, AetherRange.FragmentDelay)))
        {
            return AetherKeysProblem.InvalidFragment;
        }
        if (settings.Ech)
        {
            var dns = Trim(settings.EchDns);
            if (dns.Length > 0 && !AetherFmt.IsEchDns(dns))
            {
                return AetherKeysProblem.InvalidEchDns;
            }
            var domain = Trim(settings.EchDomain);
            if (domain.Length > 0 && !AetherFmt.IsEchDomain(domain))
            {
                return AetherKeysProblem.InvalidEchDomain;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="value" /> is an address the core sends the calls to the WARP API to, as its --api-address
    /// takes it: an IP address, an IPv6 one with or without brackets, or a domain name, alone or followed by :port, an
    /// IPv6 address then in brackets. A label of the name cannot start or end with '-', so that no value reads as an
    /// option.
    /// </summary>
    public static bool IsEnrollAddress(string value)
    {
        if (IsEnrollHost(value))
        {
            return true;
        }
        var separator = value.LastIndexOf(':');
        if (separator <= 0)
        {
            return false;
        }
        var host = value[..separator];
        if (host.Contains(':') && !(host.StartsWith('[') && host.EndsWith(']')))
        {
            return false;
        }
        var port = value[(separator + 1)..];
        return port.Length is >= 1 and <= 5
               && port.All(it => it is >= '0' and <= '9')
               && int.Parse(port, CultureInfo.InvariantCulture) is >= 1 and <= 65535
               && IsEnrollHost(host);
    }

    /// <summary>Whether <paramref name="text" /> is blank, the core's own default, or a number or range within <paramref name="bounds" />.</summary>
    private static bool TakesRange(string text, AetherRange bounds)
    {
        return IsBlank(text) || AetherRange.Parse(text, bounds) != null;
    }

    /// <summary>An IP address, an IPv6 one with or without brackets, or a domain name.</summary>
    private static bool IsEnrollHost(string host)
    {
        return AetherEndpoint.Of(host, "443") != null || AetherFmt.IsEchDomain(host);
    }

    private static string IfEmpty(string text, string fallback)
    {
        return text.Length == 0 ? fallback : text;
    }

    /// <summary>
    /// Kotlin's isBlank(), which PattNG checks the settings by, see <see cref="KotlinText" />. A null reads as empty
    /// text.
    /// </summary>
    internal static bool IsBlank(string? text)
    {
        return KotlinText.IsBlank(text);
    }

    /// <summary>Kotlin's trim(), which PattNG reads the settings by; see <see cref="IsBlank" />. A null reads as empty text.</summary>
    internal static string Trim(string? text)
    {
        return KotlinText.TrimOrNull(text) ?? string.Empty;
    }
}
