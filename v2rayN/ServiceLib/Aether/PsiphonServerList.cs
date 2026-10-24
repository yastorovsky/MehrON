using System.Globalization;
using System.IO.Compression;

namespace ServiceLib.Aether;

/// <summary>
/// The server list Psiphon's client starts with (PattNG core/PsiphonServerList.kt). It is the signed, compressed list
/// the client downloads for itself, kept as psiphon_servers.dat: every release ships the newest one, and the
/// check-update page replaces it. The client takes it only as a plain file of server entries, so the list is checked
/// and unpacked beside the identity files once per version of the file. A list without Psiphon's signature is not
/// handed on: the client trusts what it is given, and the file can be replaced by hand.
/// </summary>
public static partial class PsiphonServerList
{
    /// <summary>Psiphon's public key for its server list, the one its client checks downloads with; the core carries the same key.</summary>
    public const string SigningKey =
        "MIICIDANBgkqhkiG9w0BAQEFAAOCAg0AMIICCAKCAgEAt7Ls+/39r+T6zNW7GiVpJfzq/xvL9SBH" +
        "5rIFnk0RXYEYavax3WS6HOD35eTAqn8AniOwiH+DOkvgSKF2caqk/y1dfq47Pdymtwzp9ikpB1C5" +
        "OfAysXzBiwVJlCdajBKvBZDerV1cMvRzCKvKwRmvDmHgphQQ7WfXIGbRbmmk6opMBh3roE42Kcot" +
        "LFtqp0RRwLtcBRNtCdsrVsjiI1Lqz/lH+T61sGjSjQ3CHMuZYSQJZo/KrvzgQXpkaCTdbObxHqb6" +
        "/+i1qaVOfEsvjoiyzTxJADvSytVtcTjijhPEV6XskJVHE1Zgl+7rATr/pDQkw6DPCNBS1+Y6fy7G" +
        "stZALQXwEDN/qhQI9kWkHijT8ns+i1vGg00Mk/6J75arLhqcodWsdeG/M/moWgqQAnlZAGVtJI1O" +
        "geF5fsPpXu4kctOfuZlGjVZXQNW34aOzm8r8S0eVZitPlbhcPiR4gT/aSMz/wd8lZlzZYsje/Jr8" +
        "u/YtlwjjreZrGRmG8KMOzukV3lLmMppXFMvl4bxv6YFEmIuTsOhbLTwFgh7KYNjodLj/LsqRVfwz" +
        "31PgWQFTEPICV7GCvgVlPRxnofqKSjgTWI4mxDhBpVcATvaoBl1L/6WLbFvBsoAUBItWwctO2xal" +
        "KxF5szhGm8lccoc5MZr8kfE0uxMgsxz4er68iCID+rsCAQM=";

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    /// <summary>
    /// The entries file for the list in <paramref name="listDir" />, unpacked into <paramref name="workDir" /> when the
    /// list is new or has changed since; the entries kept from before when the list there cannot be used, with the
    /// reason given to <paramref name="onProblem" />, and null when there is no list at all.
    /// </summary>
    public static string? EntriesFile(string listDir, string workDir, string signingKey = SigningKey, Action<IOException>? onProblem = null)
    {
        var source = Path.Combine(listDir, AetherPaths.PsiphonServersFileName);
        if (!File.Exists(source))
        {
            return null;
        }
        var entries = Path.Combine(workDir, AetherPaths.PsiphonEntriesFileName);
        var mark = Path.Combine(workDir, AetherPaths.PsiphonEntriesSourceFileName);
        try
        {
            var info = new FileInfo(source);
            var stamp = string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
            if (File.Exists(entries) && File.Exists(mark) && ReadTextOrNull(mark) == stamp)
            {
                return entries;
            }
            var text = Unpack(ReadShared(source), signingKey);
            // The session and the editor may both unpack a new list; each writes under a name of its own.
            var fresh = Path.Combine(workDir, $"{AetherPaths.PsiphonEntriesFileName}.{Stopwatch.GetTimestamp()}.new");
            try
            {
                File.WriteAllText(fresh, text);
                if (!TryMove(fresh, entries))
                {
                    TryDelete(entries);
                    if (!TryMove(fresh, entries))
                    {
                        throw new IOException($"could not put {AetherPaths.PsiphonEntriesFileName} in place");
                    }
                }
            }
            catch
            {
                // On Windows a rename over a file another process holds fails, so a fresh file left behind each time
                // would pile up beside the keys.
                TryDelete(fresh);
                throw;
            }
            File.WriteAllText(mark, stamp);
            return entries;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            onProblem?.Invoke(e as IOException ?? new IOException(e.Message, e));
            return File.Exists(entries) ? entries : null;
        }
    }

    /// <summary>
    /// Puts <paramref name="replacement" />, a list written beside it, in the place of the list at
    /// <paramref name="active" />, as a rename over it does on Android: a download of the check-update window, or the
    /// bundled list copied out. On Windows File.Move cannot replace a file another handle holds open, even one that shares
    /// deleting as <see cref="EntriesFile" /> reads it; File.Replace can, and what that handle reads stays the old list.
    /// Throws an <see cref="IOException" /> when the list cannot be put in place, whatever the system calls the failure.
    /// </summary>
    public static void Replace(string replacement, string active)
    {
        try
        {
            if (File.Exists(active))
            {
                File.Replace(replacement, active, destinationBackupFileName: null);
            }
            else
            {
                File.Move(replacement, active);
            }
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException(e.Message, e);
        }
    }

    /// <summary>
    /// The fewest server entries a list needs to take the place of the one in use, as the release build checks the list
    /// it ships (PattNG fetch-psiphon-servers.sh): fewer is not the list.
    /// </summary>
    public const int MinimumEntries = 50;

    /// <summary>The server entries of <paramref name="entries" />: its lines that are not blank, as the build counts them.</summary>
    public static int EntryCount(string entries)
    {
        return entries.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
    }

    /// <summary>
    /// Checks a downloaded list before it takes the place of the one in use (design 2.11): Psiphon's signature
    /// (<see cref="Unpack" />), and at least <see cref="MinimumEntries" /> entries. Returns how many entries it has; throws
    /// an <see cref="IOException" /> with the reason when it is not Psiphon's list.
    /// </summary>
    public static int Verify(byte[] packed, string signingKey = SigningKey)
    {
        var count = EntryCount(Unpack(packed, signingKey));
        if (count < MinimumEntries)
        {
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"only {count} server entries in the list; that is not the list"));
        }
        return count;
    }

    /// <summary>
    /// Puts the list at <paramref name="list" /> in place of the one at <paramref name="active" />, with
    /// <paramref name="publishedAtUtc" /> as its time: a copy is written beside the active list first and then
    /// <see cref="Replace" />s it, so that whatever reads the active list meanwhile reads one list whole. The time of the
    /// list in use is when it was published, which the bundled list of a later release is compared with
    /// (<see cref="BundledListGoesOver" />). Throws an <see cref="IOException" /> when it cannot be put in place; the list
    /// in use stays then.
    /// </summary>
    public static void Install(string list, string active, DateTime publishedAtUtc)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(active)) ?? ".";
        var staged = Path.Combine(folder, $"{Path.GetFileName(active)}.{Stopwatch.GetTimestamp()}.new");
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(list, staged, overwrite: true);
            Replace(staged, active);
            File.SetLastWriteTimeUtc(active, publishedAtUtc);
        }
        catch (IOException)
        {
            TryDelete(staged);
            throw;
        }
        catch (UnauthorizedAccessException e)
        {
            TryDelete(staged);
            throw new IOException(e.Message, e);
        }
    }

    /// <summary>
    /// The copy at start (design 2.11, PattNG SettingsManager.initAssets): the list shipped with this release at
    /// <paramref name="bundled" /> goes over the list in use at <paramref name="active" /> when there is none, or when it
    /// was published, as <paramref name="stamp" /> says, after the list in use was (<see cref="BundledListGoesOver" />).
    /// The copy takes the publication time, so that the next release is compared with the list, not with the copy; without
    /// a usable stamp it takes the time of the copy, and only a missing list is filled. True when the list was put in
    /// place; a failure keeps the list in use, with the reason in the log, and never ends the start it runs in (PattNG
    /// initAssets catches every exception of the copy).
    /// </summary>
    public static bool InstallBundled(string bundled, string stamp, string active)
    {
        try
        {
            if (!File.Exists(bundled))
            {
                return false;
            }
            var publishedAt = PublishedAt(ReadTextOrNull(stamp));
            if (!BundledListGoesOver(active, publishedAt))
            {
                return false;
            }
            Install(bundled, active, publishedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(publishedAt).UtcDateTime : DateTime.UtcNow);
            Logging.SaveLog($"AetherCore: the {AetherPaths.PsiphonServersFileName} shipped with this release is the list in use now");
            return true;
        }
        catch (Exception e)
        {
            Logging.SaveLog($"AetherCore: the {AetherPaths.PsiphonServersFileName} shipped with this release could not be put in place; the list in use stays", e);
            return false;
        }
    }

    /// <summary>
    /// When the bundled list was published, from the stamp the build writes beside it: the seconds since the epoch of
    /// the download's Last-Modified header, as milliseconds; 0 without a usable stamp.
    /// </summary>
    public static long PublishedAt(string? stamp)
    {
        var seconds = AetherDigits.ToLongOrNull(stamp?.Trim());
        // A number of seconds past the last time a file can be given, the end of year 9999, is no publication time either:
        // a stamp written in milliseconds by mistake, say. PattNG's bound is what a long holds in milliseconds.
        if (seconds is not > 0 || seconds.Value > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return 0L;
        }
        return seconds.Value * 1000;
    }

    /// <summary>
    /// Whether the bundled list, published at <paramref name="publishedAt" /> (milliseconds since the epoch), goes over
    /// <paramref name="copy" />: when there is no copy, or when the list was published after the copy was made and the
    /// copy is not a file the user picked. A copy's time is when it was downloaded or, for one PattN copied out, when its
    /// list was published; a download holds the list as published up to that moment, so a later publication is a newer
    /// list and an earlier one is not. PattN has no list the user picks by hand, so its callers leave
    /// <paramref name="keptByUser" /> false.
    /// </summary>
    public static bool BundledListGoesOver(string copy, long publishedAt, bool keptByUser = false)
    {
        return !Path.Exists(copy) || (!keptByUser && publishedAt > LastModifiedOf(copy));
    }

    /// <summary>
    /// The exit countries the servers in <paramref name="entries" /> offer, as ISO 3166-1 alpha-2 codes, sorted. An
    /// entry is one hex-encoded line, "address port secret certificate {json}", with the region in the JSON; a line that
    /// does not read is skipped. Psiphon reports the same set, from the servers it knows, as AvailableEgressRegions, and
    /// takes one of them as EgressRegion.
    /// </summary>
    public static SortedSet<string> Regions(string entries)
    {
        var regions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in entries.Split(LineBreaks, StringSplitOptions.None))
        {
            var region = RegionOf(line.Trim());
            if (region != null)
            {
                regions.Add(region);
            }
        }
        return regions;
    }

    /// <summary>
    /// The countries in <paramref name="line" /> when it is the core's report of what Psiphon can leave from, else null.
    /// Psiphon reports them at every start, from every server it knows, discovered ones included, so they can name
    /// countries the list shipped with PattN does not.
    /// </summary>
    public static List<string>? RegionsOf(string line)
    {
        var match = LeavesFromRegex().Match(line);
        return match.Success ? match.Groups[1].Value.Split(' ').ToList() : null;
    }

    /// <summary>
    /// Keeps <paramref name="regions" /> in <paramref name="settings" /> as Psiphon's last report, in place of the one before:
    /// the report is the whole set, so the last one is the truth. True when that changed what was kept, so that the
    /// settings are worth saving.
    /// </summary>
    public static bool Remember(AetherItem settings, IEnumerable<string> regions)
    {
        var kept = string.Join(',', new SortedSet<string>(regions, StringComparer.Ordinal));
        if ((settings.PsiphonRegions ?? string.Empty) == kept)
        {
            return false;
        }
        settings.PsiphonRegions = kept;
        return true;
    }

    /// <summary>Psiphon's last report of the countries it can leave from, kept in <paramref name="settings" />; empty before the first.</summary>
    public static SortedSet<string> Remembered(AetherItem? settings)
    {
        return new SortedSet<string>(
            (settings?.PsiphonRegions ?? string.Empty).Split(',').Where(region => region.Length == 2),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The server entries inside <paramref name="packed" />, a signed, compressed list as Psiphon writes it; throws an
    /// <see cref="IOException" /> when it is not one, or is not signed with <paramref name="signingKey" />. Psiphon names
    /// the key by the digest of its text as written, not of the key's bytes, and signs the entry text with it.
    /// </summary>
    public static string Unpack(byte[] packed, string signingKey)
    {
        string text;
        try
        {
            using var input = new MemoryStream(packed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            text = Encoding.UTF8.GetString(output.ToArray());
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            throw new IOException("the list is not compressed the way Psiphon writes it", e);
        }
        string? data;
        string? keyDigest;
        string? signature;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new IOException("the list is not the package Psiphon writes");
            }
            data = PrimitiveOf(document.RootElement, "data");
            keyDigest = PrimitiveOf(document.RootElement, "signingPublicKeyDigest");
            signature = PrimitiveOf(document.RootElement, "signature");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new IOException("the list is not the package Psiphon writes", e);
        }
        if (data == null)
        {
            throw new IOException("the list carries no entries");
        }
        if (keyDigest == null)
        {
            throw new IOException("the list names no key");
        }
        if (signature == null)
        {
            throw new IOException("the list is not signed");
        }
        try
        {
            var expected = SHA256.HashData(Encoding.ASCII.GetBytes(signingKey));
            if (!Convert.FromBase64String(keyDigest).AsSpan().SequenceEqual(expected))
            {
                throw new IOException("the list is signed with another key");
            }
            using var key = RSA.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signingKey), out _);
            if (!key.VerifyData(Encoding.UTF8.GetBytes(data), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw new IOException("the signature of the list does not check out");
            }
        }
        catch (CryptographicException e)
        {
            throw new IOException("the signature of the list cannot be checked", e);
        }
        catch (FormatException e)
        {
            throw new IOException("the list is not encoded the way Psiphon writes it", e);
        }
        // No line that is not blank: the line breaks are white space themselves.
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new IOException("the list is empty");
        }
        return data;
    }

    private static string? RegionOf(string line)
    {
        if (line.Length == 0 || line.Length % 2 != 0)
        {
            return null;
        }
        var bytes = new byte[line.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            var high = AetherDigits.HexDigitOf(line[2 * index]);
            var low = AetherDigits.HexDigitOf(line[(2 * index) + 1]);
            if (high < 0 || low < 0)
            {
                return null;
            }
            bytes[index] = (byte)((high << 4) | low);
        }
        var parts = Encoding.UTF8.GetString(bytes).Split(' ', 5);
        if (parts.Length < 5)
        {
            return null;
        }
        string? region;
        try
        {
            using var document = JsonDocument.Parse(parts[4]);
            region = document.RootElement.ValueKind == JsonValueKind.Object ? PrimitiveOf(document.RootElement, "region") : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            region = null;
        }
        region = region?.Trim().ToUpperInvariant();
        return region is { Length: 2 } && region.All(char.IsLetter) ? region : null;
    }

    /// <summary>
    /// The text of the member <paramref name="name" /> of <paramref name="json" /> when it is a string, a number or a
    /// boolean, as PattNG's Gson reads it; null for anything else or no such member. A member written twice counts with
    /// its last value, as Gson keeps it.
    /// </summary>
    private static string? PrimitiveOf(JsonElement json, string name)
    {
        string? value = null;
        foreach (var property in json.EnumerateObject())
        {
            if (property.Name != name)
            {
                continue;
            }
            value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }
        return value;
    }

    private static long LastModifiedOf(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }

    /// <summary>
    /// The bytes of the list at <paramref name="path" />, read without keeping it from being replaced meanwhile, see
    /// <see cref="Replace" />: shared for writing and deleting, and closed at once, as the key files are read.
    /// File.ReadAllBytes shares reading alone, which on Windows makes a replacement fail while it reads.
    /// </summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static string? ReadTextOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Moves <paramref name="from" /> over <paramref name="to" />; false when it cannot, as Java's File.renameTo.</summary>
    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Deletes <paramref name="path" /> if it can, as Java's File.delete.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left where it is.
        }
    }

    /// <summary>The core's line with what Psiphon reported as AvailableEgressRegions: the countries of every server it knows.</summary>
    [GeneratedRegex(@"psiphon can leave from: ([A-Z]{2}(?: [A-Z]{2})*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LeavesFromRegex();
}
