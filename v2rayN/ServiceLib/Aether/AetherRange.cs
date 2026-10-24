namespace ServiceLib.Aether;

/// <summary>
/// A number or a range of numbers the core takes, n or a-b, such as the fragment size and delay
/// (PattNG dto/AetherRange.kt).
/// </summary>
public sealed record AetherRange(int Min, int Max)
{
    /// <summary>The fragment sizes the core takes, in bytes.</summary>
    public static readonly AetherRange FragmentSize = new(1, 4096);

    /// <summary>The fragment delays the core takes, in milliseconds.</summary>
    public static readonly AetherRange FragmentDelay = new(0, 1000);

    public override string ToString()
    {
        return Min == Max ? $"{Min}" : $"{Min}-{Max}";
    }

    public bool Contains(int value)
    {
        return value >= Min && value <= Max;
    }

    /// <summary>
    /// The number or the range <paramref name="text" /> names, each end inside <paramref name="bounds" />, the
    /// smaller end first whichever way it is written; null for anything else. Digits of any script count (see
    /// <see cref="AetherDigits" />), and the spaces around the numbers are Kotlin's (see <see cref="KotlinText" />).
    /// </summary>
    public static AetherRange? Parse(string? text, AetherRange bounds)
    {
        if (text == null)
        {
            return null;
        }
        var parts = KotlinText.Trim(text).Split('-');
        if (parts.Length is < 1 or > 2)
        {
            return null;
        }
        var values = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!AetherDigits.TryParse(KotlinText.Trim(part), out var value) || !bounds.Contains(value))
            {
                return null;
            }
            values.Add(value);
        }
        return new AetherRange(values.Min(), values.Max());
    }
}
