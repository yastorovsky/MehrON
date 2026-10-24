namespace ServiceLib.Aether;

/// <summary>
/// An endpoint the core dials: an IP literal in its canonical form and a port (PattNG dto/AetherEndpoint.kt). The
/// canonical IPv6 form is written here rather than by IPAddress, which keeps a dotted IPv4 tail and takes zone ids,
/// both of which PattNG rewrites or refuses: lowercase hex, the longest run of two or more zero groups compressed
/// (the first one on a tie), a single zero group left as it is, and an embedded IPv4 address written as hex.
/// </summary>
public sealed record AetherEndpoint(string Host, int Port)
{
    public override string ToString()
    {
        return Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
    }

    /// <summary>
    /// The endpoint of an IP address, brackets around it or not, and a port from 1 to 65535; null when either is
    /// something else, a host name among it. Digits of any script count (see <see cref="AetherDigits" />), and the
    /// spaces around each are Kotlin's (see <see cref="KotlinText" />).
    /// </summary>
    public static AetherEndpoint? Of(string? host, string? port)
    {
        var address = host == null ? null : CanonicalAddress(RemoveSurrounding(KotlinText.Trim(host), "[", "]"));
        if (address == null)
        {
            return null;
        }
        var number = port == null ? null : Decimal(KotlinText.Trim(port));
        if (number is not (>= 1 and <= 65535))
        {
            return null;
        }
        return new AetherEndpoint(address, number.Value);
    }

    /// <summary>
    /// The endpoint written the way the core writes it, ip:port or [ipv6]:port; null for anything else.
    /// </summary>
    public static AetherEndpoint? Parse(string? value)
    {
        var text = KotlinText.TrimOrNull(value) ?? string.Empty;
        var separator = text.LastIndexOf(':');
        if (separator <= 0)
        {
            return null;
        }
        var host = text.Substring(0, separator);
        if (host.Contains(':') && !(host.StartsWith('[') && host.EndsWith(']')))
        {
            return null;
        }
        return Of(host, text.Substring(separator + 1));
    }

    /// <summary>
    /// Kotlin's removeSurrounding: <paramref name="text" /> without the prefix and the suffix when it has both.
    /// </summary>
    private static string RemoveSurrounding(string text, string prefix, string suffix)
    {
        if (text.Length >= prefix.Length + suffix.Length
            && text.StartsWith(prefix, StringComparison.Ordinal)
            && text.EndsWith(suffix, StringComparison.Ordinal))
        {
            return text.Substring(prefix.Length, text.Length - prefix.Length - suffix.Length);
        }
        return text;
    }

    private static string? CanonicalAddress(string address)
    {
        if (address.Contains(':'))
        {
            var groups = Ipv6Groups(address);
            return groups == null ? null : FormatIpv6(groups);
        }
        var octets = Ipv4Octets(address);
        return octets == null ? null : string.Join(".", octets);
    }

    /// <summary>
    /// A decimal number of one to five digits, which a port is; no sign.
    /// </summary>
    private static int? Decimal(string text)
    {
        if (text.Length == 0 || text.Length > 5)
        {
            return null;
        }
        var value = 0;
        foreach (var ch in text)
        {
            var digit = AetherDigits.DigitOf(ch);
            if (digit < 0)
            {
                return null;
            }
            value = (value * 10) + digit;
        }
        return value;
    }

    private static List<int>? Ipv4Octets(string address)
    {
        var parts = address.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }
        var octets = new List<int>(4);
        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 3 || (part.Length > 1 && AetherDigits.DigitOf(part[0]) == 0))
            {
                return null;
            }
            var octet = Decimal(part);
            if (octet is not <= 255)
            {
                return null;
            }
            octets.Add(octet.Value);
        }
        return octets;
    }

    private static List<int>? Ipv6Groups(string address)
    {
        var halves = address.Split("::");
        if (halves.Length > 2)
        {
            return null;
        }
        var compressed = halves.Length == 2;
        var head = HexGroups(halves[0], allowIpv4: !compressed);
        if (head == null)
        {
            return null;
        }
        var tail = compressed ? HexGroups(halves[1], allowIpv4: true) : [];
        if (tail == null)
        {
            return null;
        }
        var missing = 8 - head.Count - tail.Count;
        if (compressed ? missing < 1 : missing != 0)
        {
            return null;
        }
        var groups = new List<int>(8);
        groups.AddRange(head);
        groups.AddRange(Enumerable.Repeat(0, missing));
        groups.AddRange(tail);
        return groups;
    }

    private static List<int>? HexGroups(string section, bool allowIpv4)
    {
        var groups = new List<int>();
        if (section.Length == 0)
        {
            return groups;
        }
        var pieces = section.Split(':');
        for (var index = 0; index < pieces.Length; index++)
        {
            var piece = pieces[index];
            if (allowIpv4 && index == pieces.Length - 1 && piece.Contains('.'))
            {
                var octets = Ipv4Octets(piece);
                if (octets == null)
                {
                    return null;
                }
                groups.Add((octets[0] << 8) | octets[1]);
                groups.Add((octets[2] << 8) | octets[3]);
            }
            else
            {
                if (piece.Length is < 1 or > 4)
                {
                    return null;
                }
                var value = 0;
                foreach (var ch in piece)
                {
                    var digit = AetherDigits.HexDigitOf(ch);
                    if (digit < 0)
                    {
                        return null;
                    }
                    value = (value * 16) + digit;
                }
                groups.Add(value);
            }
        }
        return groups;
    }

    private static string FormatIpv6(List<int> groups)
    {
        // The longest run of two or more zero groups, the first one on a tie.
        var runStart = -1;
        var runLength = 1;
        var index = 0;
        while (index < groups.Count)
        {
            var start = index;
            while (index < groups.Count && groups[index] == 0)
            {
                index++;
            }
            if (index - start > runLength)
            {
                runStart = start;
                runLength = index - start;
            }
            index++;
        }
        var text = groups.Select(group => group.ToString("x")).ToList();
        if (runStart < 0)
        {
            return string.Join(":", text);
        }
        var head = string.Join(":", text.Take(runStart));
        var tail = string.Join(":", text.Skip(runStart + runLength));
        return $"{head}::{tail}";
    }
}
