namespace ServiceLib.Aether;

/// <summary>
/// Kotlin's reading of text, which PattNG trims, checks and capitalizes Aether settings by, in one place for every
/// Aether file. Kotlin's whitespace (Char.isWhitespace) is not .NET's: it takes the separators U+001C to U+001F, which
/// Java counts as whitespace, and leaves U+0085 out. Text the core reads as written, such as a command line's protocol
/// name or a hop, is trimmed as the core trims it instead, with .NET's string.Trim: Rust's whitespace is .NET's.
/// </summary>
internal static class KotlinText
{
    /// <summary>Kotlin's Char.isWhitespace(): char.IsWhiteSpace without U+0085, and with U+001C to U+001F.</summary>
    public static bool IsWhitespace(char c)
    {
        return c is >= '\u001C' and <= '\u001F' || (char.IsWhiteSpace(c) && c != '\u0085');
    }

    /// <summary>Kotlin's isNullOrBlank(): null, or <see cref="IsWhitespace" /> alone.</summary>
    public static bool IsBlank(string? text)
    {
        return text == null || text.All(IsWhitespace);
    }

    /// <summary>Kotlin's nullIfBlank: <paramref name="text" /> as it is, or null when it is blank.</summary>
    public static string? NullIfBlank(string? text)
    {
        return IsBlank(text) ? null : text;
    }

    /// <summary>Kotlin's trim() of <paramref name="text" />.</summary>
    public static string Trim(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsWhitespace(text[start]))
        {
            start++;
        }
        while (end > start && IsWhitespace(text[end - 1]))
        {
            end--;
        }
        return text[start..end];
    }

    /// <summary>Kotlin's text?.trim(): null staying null.</summary>
    public static string? TrimOrNull(string? text)
    {
        return text == null ? null : Trim(text);
    }

    /// <summary>
    /// Kotlin's uppercase(Locale.ROOT), as far as it gives ASCII letters: ToUpperInvariant, which maps one character at
    /// a time and leaves the dotless i as it is, after the mappings Java makes that it does not, the dotless i to I and
    /// the letters and ligatures that become two or three capitals (ß to SS, ﬁ to FI). Java's other full mappings give
    /// letters that are no ASCII ones either way.
    /// </summary>
    public static string Uppercase(string text)
    {
        var mapped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            mapped.Append(c switch
            {
                'ı' => "I",
                'ß' => "SS",
                'ﬀ' => "FF",
                'ﬁ' => "FI",
                'ﬂ' => "FL",
                'ﬃ' => "FFI",
                'ﬄ' => "FFL",
                'ﬅ' or 'ﬆ' => "ST",
                _ => c.ToString(),
            });
        }
        return mapped.ToString().ToUpperInvariant();
    }
}
