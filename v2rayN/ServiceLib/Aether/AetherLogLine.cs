namespace ServiceLib.Aether;

/// <summary>The level a line of the core's output is shown at (PattNG's Android log priorities).</summary>
public enum AetherLogPriority
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Reading the lines the core writes (PattNG AetherCoreManager.kt): its log lines start with a header,
/// [2026-09-11T10:00:00.000Z INFO  aether::prober], and an error it stops on is written as Error: Variant("...").
/// </summary>
public static partial class AetherLogLine
{
    private static readonly HashSet<string> LogLevels = ["ERROR", "WARN", "INFO", "DEBUG", "TRACE"];

    /// <summary>The level <paramref name="line" /> is shown at: the level of its header, or an error for the error the core stops on.</summary>
    public static AetherLogPriority OutputPriority(string line)
    {
        if (IsFatalError(line))
        {
            return AetherLogPriority.Error;
        }
        switch (LogHeader(line)?[1])
        {
            case "ERROR":
                return AetherLogPriority.Error;

            case "WARN":
                return AetherLogPriority.Warn;

            case "DEBUG":
            case "TRACE":
                return AetherLogPriority.Debug;

            default:
                return AetherLogPriority.Info;
        }
    }

    /// <summary><paramref name="line" /> trimmed and without its log header; empty for a blank line.</summary>
    public static string OutputMessage(string line)
    {
        var text = line.Trim();
        if (LogHeader(text) == null)
        {
            return text;
        }
        return text.Substring(text.IndexOf(']') + 1).Trim();
    }

    /// <summary>True when <paramref name="line" /> is the error the core stopped on, which its main writes last.</summary>
    public static bool IsFatalError(string line)
    {
        return line.StartsWith("Error:", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="line" /> is the core's word that the listener the app dials carries traffic now:
    /// "psiphon is ready". Psiphon's own listener answers the SOCKS greeting as soon as it is bound and closes every
    /// connection until then, so the greeting alone says nothing where the app dials Psiphon. An aether-core since 2.3.4
    /// holds that listener itself and passes clients on to Psiphon only once it has said this word.
    /// </summary>
    public static bool IsReadyWord(string line)
    {
        return PsiphonReadyRegex().IsMatch(line);
    }

    /// <summary>
    /// True when <paramref name="line" /> is the core's word that it did not start the session for want of an ECH key:
    /// ECH is on, and it had no key it could offer, so it stopped rather than send the server name in the clear.
    /// </summary>
    public static bool IsNoEchKeyWord(string line)
    {
        return NoEchKeyRegex().IsMatch(line);
    }

    /// <summary>
    /// The core's words, as "tor: ..." or "psiphon: ...", when <paramref name="line" /> says that Tor or Psiphon inside
    /// the tunnel, whose listener a session started with <paramref name="arguments" /> is dialled on, has ended or could
    /// not start; null for any other line, and for a session dialled on the core's own listener (PattNG chainEndWords).
    /// The core logs it as "[-] tor: {e}" or "[-] psiphon: {e}" at the error level under the target "aether" (aether
    /// lib.rs). An aether-core before 2.3.4 goes on without that listener then, where another program may listen and get
    /// what Xray sends there; a later one ends as well. The notices of the Psiphon client that the core passes on, under
    /// the same "[-] psiphon: ", come from its psiphon module, "aether::psiphon". The label of the error's kind, "other: "
    /// or "io: ", is left out of the words.
    /// </summary>
    public static string? ChainEndWords(string line, IReadOnlyList<string> arguments)
    {
        var header = LogHeader(line.Trim());
        if (header == null || header[1] != "ERROR" || header[2] != "aether")
        {
            return null;
        }
        var listener = AetherArguments.ListenerFlagOf(arguments);
        var carrier = listener == AetherArguments.TorBind ? "tor"
            : listener == AetherArguments.PsiphonBind ? "psiphon"
            : null;
        if (carrier == null)
        {
            return null;
        }
        var message = OutputMessage(line);
        var prefix = $"[-] {carrier}: ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }
        return $"{carrier}: " + ErrorLabelRegex().Replace(message.Substring(prefix.Length), string.Empty, 1);
    }

    /// <summary>
    /// <paramref name="line" /> without ANSI colour codes, which the Tor logs of the core can carry even when its output
    /// is no terminal.
    /// </summary>
    public static string StripAnsi(string line)
    {
        return AnsiColorRegex().Replace(line, string.Empty);
    }

    /// <summary>The words of the header <paramref name="line" /> starts with: time, level, target; null without one.</summary>
    private static List<string>? LogHeader(string line)
    {
        if (!line.StartsWith('['))
        {
            return null;
        }
        var headerEnd = line.IndexOf(']');
        if (headerEnd <= 0)
        {
            return null;
        }
        var words = line.Substring(1, headerEnd - 1)
            .Split(' ')
            .Where(word => word.Length > 0)
            .ToList();
        return words.Count >= 3 && LogLevels.Contains(words[1]) ? words : null;
    }

    [GeneratedRegex("psiphon is ready", RegexOptions.CultureInvariant)]
    private static partial Regex PsiphonReadyRegex();

    [GeneratedRegex("ECH is on but there is no ECH key to offer", RegexOptions.CultureInvariant)]
    private static partial Regex NoEchKeyRegex();

    [GeneratedRegex("\\x1b\\[[0-9;]*m", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiColorRegex();

    /// <summary>The label an error of the core starts its words with, the kind it is of (aether error.rs, AetherError).</summary>
    [GeneratedRegex("^(?:io|quic|h3|tls|ech|masque|capsule|api|other|identity refused): ", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorLabelRegex();
}
