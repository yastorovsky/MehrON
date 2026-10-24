namespace ServiceLib.Aether;

/// <summary>The WARP identity a key file holds: its device and the addresses WARP gave it.</summary>
public sealed record AetherIdentity(string DeviceId, string Ipv4, string Ipv6);

/// <summary>The keys a tunnel of <see cref="Protocol" /> uses: its own, and the inner hop's for a two-hop one.</summary>
public sealed record AetherIdentityStatus(AetherProtocol Protocol, AetherIdentity? Primary, AetherIdentity? Secondary = null);

/// <summary>A key file of the identity folder, one of <see cref="AetherPaths.KeyFiles" />, with the identity it holds; null without one.</summary>
public sealed record AetherKey(string File, AetherIdentity? Identity);

/// <summary>
/// The WARP key files and their renewal (PattNG core/AetherIdentityManager.kt). The keys in use live in
/// <see cref="AetherPaths.WorkDir" />; a renewal gathers new keys in <see cref="AetherPaths.RenewalDir" /> and puts them in
/// place only once every one of them is there.
/// </summary>
public static partial class AetherIdentityManager
{
    /// <summary>
    /// Five registrations, two MASQUE key enrollments and five WARP enablings at most, each of which the core may retry:
    /// a key is saved only once WARP is enabled on it.
    /// </summary>
    public static readonly TimeSpan RenewTimeout = TimeSpan.FromMinutes(4);

    /// <summary>
    /// A renewal through Tor or Psiphon, which a command written by hand may ask for, waits for the carrier to come up
    /// first, which takes what a scan may take.
    /// </summary>
    public static readonly TimeSpan RenewThroughCarrierTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The source label of a key run's output.</summary>
    public const string Source = "aether-key";

    // PattNG's \s and \S are Java's: ASCII white space alone, which .NET's are not.
    private const string Space = @"[ \t\n\x0B\f\r]";
    private const string NonSpace = @"[^ \t\n\x0B\f\r]";

    /// <summary>One renewal at a time: they share the renewal folder.</summary>
    private static readonly SemaphoreSlim RenewalLock = new(1, 1);

    /// <summary>The keys a tunnel of <paramref name="protocol" /> uses, after <see cref="Settle()" />.</summary>
    public static Task<AetherIdentityStatus> StatusAsync(AetherProtocol protocol)
    {
        return Task.Run(() =>
        {
            Settle();
            return Status(AetherPaths.WorkDir, protocol);
        });
    }

    /// <summary>Every key of the identity folder, each of <see cref="AetherPaths.KeyFiles" /> with the identity it holds.</summary>
    public static Task<List<AetherKey>> KeysAsync()
    {
        return Task.Run(() =>
        {
            Settle();
            return Keys(AetherPaths.WorkDir, AetherPaths.KeyFiles);
        });
    }

    /// <summary>
    /// Registers new keys of <paramref name="kind" /> through <paramref name="register" />, which runs the core to write
    /// them into the folder it is given, and puts them in place of the keys in use once all of them are there. Until
    /// then the keys in use stay where they are, untouched, so a failure, a cancellation or PattN being killed leaves
    /// them in use; the keys of another kind are never touched. Returns the new keys, or null when the keys in use stay.
    /// <paramref name="register" /> is PattNG's register: one core run on the arguments with their info lines
    /// (<see cref="AetherArguments.WithInfoLines" />), under <see cref="Source" />, the keys written into the folder it is
    /// given, true once a line <see cref="IsRegistered" /> within <see cref="RegisterTimeout" />.
    /// </summary>
    public static async Task<List<AetherKey>?> RenewAsync(
        AetherKeyKind kind,
        Func<string, CancellationToken, Task<bool>> register,
        CancellationToken ct = default)
    {
        await RenewalLock.WaitAsync(ct);
        try
        {
            var workDir = AetherPaths.WorkDir;
            var renewalDir = AetherPaths.RenewalDir;
            var files = FilesOf(kind);
            if (!await RenewAsync(workDir, renewalDir, files, token => register(renewalDir, token), ct: ct))
            {
                return null;
            }
            return await StepAsync<List<AetherKey>>(OnThreadPool, () =>
            {
                // The renewal settled already unless a move failed there; this tries once more.
                if (!Settle(workDir, renewalDir))
                {
                    Logging.SaveLog("AetherIdentity: the new keys are ready but not all in place yet; the next core start moves the rest");
                }
                return Keys(workDir, files);
            }, ct);
        }
        finally
        {
            RenewalLock.Release();
        }
    }

    /// <summary>
    /// Registers new keys of <paramref name="kind" /> by running the core on <paramref name="arguments" />, which register
    /// them (see <see cref="AetherKeys" />), and puts them in place of the keys in use once all of them are there (PattNG
    /// renew(context, kind, arguments, exit, onOutput)). Until then the keys in use stay where they are, untouched; the
    /// keys of another kind are never touched. The core dials out through the exit-node <paramref name="exit" />, and its
    /// lines go to <paramref name="onOutput" /> as they come. Returns the new keys, or null when the keys in use stay, as
    /// they do when a stop of every core ends the run first (an update of aether-core, or PattN exits); only a
    /// cancellation of <paramref name="ct" /> throws <see cref="OperationCanceledException" />.
    /// </summary>
    public static Task<List<AetherKey>?> RenewAsync(
        AetherKeyKind kind,
        IReadOnlyList<string> arguments,
        AetherExit exit,
        Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        return RenewAsync(kind, Register(AetherCoreManager.Instance, arguments, exit, onOutput), ct);
    }

    /// <summary>
    /// PattNG register: the run of the core on <paramref name="arguments" />, among the cores of <paramref name="cores" />,
    /// that registers the keys they ask for into the folder it is given, through a carrier if they name one, and ends
    /// without scanning or opening a tunnel. It takes its turn at the exit with scans and other key runs, at a level that
    /// writes the core's info lines (<see cref="AetherArguments.WithInfoLines" />), and is over at the core's word that the
    /// keys are saved (<see cref="IsRegistered" />), within <see cref="RegisterTimeout" />. True once that word came; false
    /// without it, as when the exit did not open, the core could not start, or a stop of every core ended the run first
    /// (<see cref="AetherRunOutcome" />, which the log tells). Only a cancellation of its token throws.
    /// </summary>
    public static Func<string, CancellationToken, Task<bool>> Register(
        AetherCoreManager cores,
        IReadOnlyList<string> arguments,
        AetherExit exit,
        Action<string>? onOutput = null)
    {
        return async (keysDir, ct) =>
        {
            var run = new AetherStandaloneRun(AetherArguments.WithInfoLines(arguments), AetherRole.Key)
            {
                Exit = exit,
                KeysDir = keysDir,
                OnOutput = onOutput,
            };
            var result = await cores.RunUntilAsync(run, RegisterTimeout(arguments), line => IsRegistered(line) ? line : null, ct)
                .ConfigureAwait(false);
            if (result.Outcome != AetherRunOutcome.Done)
            {
                Logging.SaveLog(result.Value == null
                    ? $"AetherIdentity: the key run did not run to its end ({result.Outcome}); the keys in use stay"
                    : $"AetherIdentity: the key run was stopped ({result.Outcome}) once its keys were saved");
            }
            return result.Value != null;
        };
    }

    /// <summary>
    /// How long a key run on <paramref name="arguments" /> may take to say its keys are saved, a word it writes at the
    /// info level: longer when it reaches WARP through Tor or Psiphon.
    /// </summary>
    public static TimeSpan RegisterTimeout(IReadOnlyList<string> arguments)
    {
        return AetherArguments.ReachesWarpThroughCarrier(arguments) ? RenewThroughCarrierTimeout : RenewTimeout;
    }

    /// <summary><see cref="SettleIn" /> guiConfigs; whatever reads the keys, and every core start, calls it first.</summary>
    public static bool Settle()
    {
        return SettleIn(Utils.GetConfigPath());
    }

    /// <summary>
    /// Does what <see cref="Settle(string, string)" /> does on the identity folder and the renewal folder in
    /// <paramref name="configDir" />; the keys in use stay as they are.
    /// </summary>
    public static bool SettleIn(string configDir)
    {
        return Settle(Path.Combine(configDir, AetherPaths.WorkDirName), Path.Combine(configDir, AetherPaths.RenewalDirName));
    }

    public static AetherIdentityStatus Status(string workDir, AetherProtocol protocol)
    {
        if (protocol == AetherProtocol.Masque)
        {
            return new AetherIdentityStatus(protocol, Read(Path.Combine(workDir, AetherPaths.MasqueFile)));
        }
        if (protocol == AetherProtocol.WireGuard)
        {
            return new AetherIdentityStatus(protocol, Read(Path.Combine(workDir, AetherPaths.WireGuardFile)));
        }
        if (protocol == AetherProtocol.Gool)
        {
            return new AetherIdentityStatus(
                protocol,
                Read(Path.Combine(workDir, AetherPaths.WireGuardFile)),
                Read(Path.Combine(workDir, AetherPaths.WireGuardInnerFile)));
        }
        if (protocol == AetherProtocol.Mim)
        {
            return new AetherIdentityStatus(
                protocol,
                Read(Path.Combine(workDir, AetherPaths.MasqueFile)),
                Read(Path.Combine(workDir, AetherPaths.MasqueInnerFile)));
        }
        return new AetherIdentityStatus(
            protocol,
            Read(Path.Combine(workDir, AetherPaths.MasqueFile)),
            Read(Path.Combine(workDir, AetherPaths.MasqueGoolFile)));
    }

    /// <summary>The key files a registration of <paramref name="kind" /> writes, in the order of <see cref="AetherPaths.KeyFiles" />.</summary>
    public static IReadOnlyList<string> FilesOf(AetherKeyKind kind)
    {
        if (kind == AetherKeyKind.AllKeys)
        {
            return AetherPaths.KeyFiles;
        }
        if (kind == AetherKeyKind.WireGuard)
        {
            return [AetherPaths.WireGuardFile];
        }
        if (kind == AetherKeyKind.Masque)
        {
            return [AetherPaths.MasqueFile];
        }
        if (kind == AetherKeyKind.Gool)
        {
            return [AetherPaths.WireGuardFile, AetherPaths.WireGuardInnerFile];
        }
        if (kind == AetherKeyKind.Mim)
        {
            return [AetherPaths.MasqueFile, AetherPaths.MasqueInnerFile];
        }
        return [AetherPaths.MasqueFile, AetherPaths.MasqueGoolFile];
    }

    /// <summary>The key files a tunnel of <paramref name="protocol" /> uses: its own, and the inner hop's for a two-hop one.</summary>
    public static IReadOnlyList<string> FilesOf(AetherProtocol protocol)
    {
        if (protocol == AetherProtocol.Masque)
        {
            return [AetherPaths.MasqueFile];
        }
        if (protocol == AetherProtocol.WireGuard)
        {
            return [AetherPaths.WireGuardFile];
        }
        if (protocol == AetherProtocol.Gool)
        {
            return [AetherPaths.WireGuardFile, AetherPaths.WireGuardInnerFile];
        }
        if (protocol == AetherProtocol.Mim)
        {
            return [AetherPaths.MasqueFile, AetherPaths.MasqueInnerFile];
        }
        return [AetherPaths.MasqueFile, AetherPaths.MasqueGoolFile];
    }

    /// <summary>
    /// The key files a core on <paramref name="arguments" /> needs, which the WARP keys window gets: those of the protocol
    /// they run, read as the core reads it, and none when they run Psiphon or Tor alone, with no WARP tunnel.
    /// </summary>
    public static IReadOnlyList<string> FilesNeededBy(IReadOnlyList<string> arguments)
    {
        if (AetherArguments.PsiphonModeOf(arguments) == AetherPsiphon.Only || AetherArguments.TorModeOf(arguments) == AetherTor.Only)
        {
            return [];
        }
        return FilesOf(AetherArguments.ProtocolOf(arguments));
    }

    /// <summary>Which of <paramref name="files" /> the identity folder lacks: not there, or not readable as a key.</summary>
    public static Task<List<string>> MissingAsync(IReadOnlyList<string> files)
    {
        return Task.Run(() =>
        {
            Settle();
            return Missing(AetherPaths.WorkDir, files);
        });
    }

    public static List<string> Missing(string dir, IReadOnlyList<string> files)
    {
        return files.Where(it => Read(Path.Combine(dir, it)) == null).ToList();
    }

    /// <summary>Each of <paramref name="files" /> in <paramref name="dir" /> with the identity it holds.</summary>
    public static List<AetherKey> Keys(string dir, IReadOnlyList<string> files)
    {
        return files.Select(it => new AetherKey(it, Read(Path.Combine(dir, it)))).ToList();
    }

    /// <summary>
    /// The identity a key file's <paramref name="text" /> holds: the first device_id, ipv4 and ipv6 of its lines, none
    /// inside a multi-line string such as a certificate; null without a device.
    /// </summary>
    public static AetherIdentity? Parse(string text)
    {
        var fields = new Dictionary<string, string>();
        var insideMultiline = false;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (!insideMultiline)
            {
                var match = IdentityFieldRegex().Match(line);
                if (match.Success)
                {
                    fields.TryAdd(match.Groups[1].Value, match.Groups[2].Value);
                }
            }
            if (MultilineDelimiterRegex().Count(line) % 2 == 1)
            {
                insideMultiline = !insideMultiline;
            }
        }
        if (!fields.TryGetValue("device_id", out var deviceId) || string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }
        return new AetherIdentity(deviceId, fields.GetValueOrDefault("ipv4", string.Empty), fields.GetValueOrDefault("ipv6", string.Empty));
    }

    /// <summary>
    /// The tunnels over MASQUE share the MASQUE key and the others the WireGuard key; a two-hop tunnel adds a second key,
    /// of its kind or, for WireGuard over MASQUE, a WireGuard key of its own, which no other tunnel uses.
    /// </summary>
    public static bool SharesIdentity(AetherProtocol first, AetherProtocol second)
    {
        return first.OverMasque == second.OverMasque;
    }

    /// <summary>
    /// Whether a tunnel of <paramref name="protocol" /> uses a key that new keys of <paramref name="kind" /> would replace
    /// (PattNG AetherSession.usesKeysOf); the keys of the other protocols can change under it.
    /// </summary>
    public static bool UsesKeysOf(AetherProtocol protocol, AetherKeyKind kind)
    {
        var replaced = FilesOf(kind);
        return FilesOf(protocol).Any(replaced.Contains);
    }

    /// <summary>True for the core's last word in a registration, once every key it was asked for is saved.</summary>
    public static bool IsRegistered(string line)
    {
        return KeysRegisteredRegex().IsMatch(line);
    }

    /// <summary>
    /// Gathers new keys in <paramref name="renewalDir" /> through <paramref name="register" /> and moves them over the keys
    /// in <paramref name="workDir" /> once every one of <paramref name="files" /> is there and reads as a key. Nothing in
    /// <paramref name="workDir" /> is touched before that moment, so whatever stops a renewal earlier leaves the keys in
    /// use as they were; a stop after it is finished by <see cref="Settle(string, string)" />. A key not among
    /// <paramref name="files" /> stays as it is. True when the new keys took the place of the old ones. The steps on the
    /// files run through <paramref name="io" />, on the thread pool unless given; a cancellation throws
    /// <see cref="OperationCanceledException" />.
    /// </summary>
    public static async Task<bool> RenewAsync(
        string workDir,
        string renewalDir,
        IReadOnlyList<string> files,
        Func<CancellationToken, Task<bool>> register,
        Func<Func<bool>, Task<bool>>? io = null,
        CancellationToken ct = default)
    {
        io ??= OnThreadPool<bool>;
        var cleared = await StepAsync(io, () =>
        {
            // A renewal whose keys were all ready is finished first; what one stopped earlier left is no key anyone uses.
            return Settle(workDir, renewalDir) && DeleteRecursively(renewalDir) && MakeDirectory(renewalDir);
        }, ct);
        if (!cleared)
        {
            return false;
        }
        // A cancellation can be delivered as the result of a step comes back, after the step itself has run; whether the
        // new keys were marked ready is therefore recorded inside that step, and the finally block goes by the record
        // rather than by a value the step returned.
        var ready = false;
        try
        {
            if (await register(ct))
            {
                await StepAsync(io, () =>
                {
                    // A key the run left beside the ones asked for goes with the folder rather than into place.
                    foreach (var name in AetherPaths.KeyFiles.Except(files))
                    {
                        DeleteFile(Path.Combine(renewalDir, name));
                    }
                    ready = IsComplete(renewalDir, files) && MarkReady(renewalDir);
                    return ready;
                }, ct);
            }
        }
        finally
        {
            await StepAsync(io, () => ready ? Settle(workDir, renewalDir) : DeleteRecursively(renewalDir), CancellationToken.None);
        }
        return ready;
    }

    /// <summary>
    /// Moves the new keys of a renewal whose keys were all ready over the keys in <paramref name="workDir" />, one after
    /// another, and then removes <paramref name="renewalDir" />. Without the ready mark there is nothing to move: the new
    /// keys are incomplete, or a renewal is still gathering them, and the keys in use stay. Another process may be
    /// finishing the same renewal at the same time; a key it moved first is simply gone here. True when no renewal with
    /// ready keys waits any longer.
    /// </summary>
    public static bool Settle(string workDir, string renewalDir)
    {
        if (!File.Exists(Path.Combine(renewalDir, AetherPaths.ReadyMark)))
        {
            return true;
        }
        try
        {
            Directory.CreateDirectory(workDir);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // The move below fails then, and says so.
        }
        foreach (var name in AetherPaths.KeyFiles)
        {
            var renewed = Path.Combine(renewalDir, name);
            if (File.Exists(renewed) && !MoveOver(renewed, Path.Combine(workDir, name)))
            {
                return false;
            }
        }
        // The mark goes with the folder, after every key: until then it tells that keys are left to move.
        return DeleteRecursively(renewalDir);
    }

    /// <summary>Whether every one of <paramref name="files" /> is in <paramref name="dir" /> and reads as a key.</summary>
    public static bool IsComplete(string dir, IReadOnlyList<string> files)
    {
        return files.All(it => Read(Path.Combine(dir, it)) != null);
    }

    private static Task<T> OnThreadPool<T>(Func<T> step)
    {
        return Task.Run(step);
    }

    /// <summary>
    /// Runs <paramref name="step" /> through <paramref name="io" /> the way Kotlin's withContext runs a block: not at all
    /// once <paramref name="ct" /> is cancelled, to its end once started, and with a cancellation that came meanwhile
    /// delivered as its result comes back.
    /// </summary>
    private static async Task<T> StepAsync<T>(Func<Func<T>, Task<T>> io, Func<T> step, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await io(step);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>
    /// Moves <paramref name="source" /> over <paramref name="target" /> in one step that replaces the key in use, or leaves
    /// it as it was (MoveFileEx with MOVEFILE_REPLACE_EXISTING on Windows, rename(2) elsewhere; both folders are in
    /// guiConfigs, on one volume); true once <paramref name="source" /> is gone, moved by this call or by another process
    /// finishing the same renewal.
    /// </summary>
    private static bool MoveOver(string source, string target)
    {
        try
        {
            File.Move(source, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return !File.Exists(source);
        }
    }

    /// <summary>Creates the ready mark; false when it is there already or cannot be written, as Java's createNewFile.</summary>
    private static bool MarkReady(string dir)
    {
        try
        {
            new FileStream(Path.Combine(dir, AetherPaths.ReadyMark), FileMode.CreateNew, FileAccess.Write).Dispose();
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    /// <summary>Creates <paramref name="path" />; false when something is there already or it cannot be made, as Java's mkdirs.</summary>
    private static bool MakeDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
        {
            return false;
        }
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    /// <summary>Removes <paramref name="path" /> with everything in it; true once nothing is left there, or nothing was.</summary>
    private static bool DeleteRecursively(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Told by what is left.
        }
        return !Directory.Exists(path) && !File.Exists(path);
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // It goes with the folder, or the key is not complete.
        }
    }

    /// <summary>
    /// The identity <paramref name="file" /> holds; null when it is not a file, cannot be read or holds none. The file is
    /// opened with FileShare.Delete and closed at once: the core re-saves each key at every start through a temporary
    /// file and a rename, which Windows refuses over a file held open without it, and the core stops on that.
    /// </summary>
    private static AetherIdentity? Read(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // UTF-8 as written, a byte order mark kept as text, as Kotlin's readText reads it.
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return null;
        }
    }

    /// <summary>The failures Java reports as an IOException: .NET reports a refused access apart.</summary>
    private static bool IsFileError(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException;
    }

    [GeneratedRegex("^(device_id|ipv4|ipv6)" + Space + "*=" + Space + "*\"([^\"]*)\"$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityFieldRegex();

    [GeneratedRegex("\"\"\"|'''", RegexOptions.CultureInvariant)]
    private static partial Regex MultilineDelimiterRegex();

    [GeneratedRegex("identities ready: " + NonSpace, RegexOptions.CultureInvariant)]
    private static partial Regex KeysRegisteredRegex();
}
