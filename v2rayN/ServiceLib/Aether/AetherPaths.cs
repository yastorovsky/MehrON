namespace ServiceLib.Aether;

/// <summary>
/// Where the Aether core and PattN keep the WARP keys and the other state of Aether: folders of guiConfigs, never of
/// bin/aether, which the core updater empties of its sub-folders and whose files it replaces. The names are PattNG's
/// (AetherIdentityManager.kt, AetherCoreManager.kt, PsiphonServerList.kt).
/// </summary>
public static class AetherPaths
{
    /// <summary>The working folder of every core, with the keys in use.</summary>
    public const string WorkDirName = "aether";

    /// <summary>
    /// Where a renewal gathers its new keys. The keys in use stay in the working folder, untouched, until every new key
    /// it registers is here; only then does each new key take the place of the old one.
    /// </summary>
    public const string RenewalDirName = "aether-renewal";

    /// <summary>
    /// Written into the renewal folder once every new key of a renewal is there. From then on the new keys are the
    /// keys: whatever stops the moving of them, the next settling finishes it.
    /// </summary>
    public const string ReadyMark = "ready";

    /// <summary>
    /// The Psiphon datastore of the session: the servers its client was given, fetched and discovered. That state is no
    /// part of the WARP identity, so it lives beside the working folder rather than inside it.
    /// </summary>
    public const string PsiphonStateDirName = "psiphon";

    /// <summary>
    /// The Psiphon datastore of a test, a scan or a renewal: apart from the session's, since the client holds its
    /// datastore under a lock and a second client on the same one gives up after a second.
    /// </summary>
    public const string PsiphonProbeDirName = "psiphon-probe";

    /// <summary>The base identity path the core is given (AETHER_CONFIG); never a key file of the keys window.</summary>
    public const string BaseFile = "aether.toml";

    public const string MasqueFile = "aether-masque.toml";
    public const string MasqueInnerFile = "aether-masque-secondary.toml";
    public const string WireGuardFile = "aether-wg.toml";
    public const string WireGuardInnerFile = "aether-wg-secondary.toml";

    /// <summary>
    /// The WireGuard key of WireGuard over MASQUE, beside <see cref="MasqueFile" /> as the core names it. --register gool
    /// writes it as it writes the other keys; a scan or a connection without it has the core register one through the
    /// MASQUE tunnel, from inside WARP.
    /// </summary>
    public const string MasqueGoolFile = "aether-masque-gool.toml";

    /// <summary>The key files of every kind, in the order the core registers them.</summary>
    public static IReadOnlyList<string> KeyFiles { get; } =
        [WireGuardFile, WireGuardInnerFile, MasqueFile, MasqueInnerFile, MasqueGoolFile];

    /// <summary>Core state beside the keys that a renewal leaves where it is; only the key files are ever moved.</summary>
    public const string WireGuardLastConnectionFile = "aether-wg-lastconn.toml";

    public const string MasqueLastConnectionFile = "aether-masque-lastconn.toml";
    public const string TorStateDirName = "aether-tor";

    /// <summary>
    /// The Psiphon datastore the core derives from the base file when it is told no other place, inside the working
    /// folder; clearing the Psiphon data removes it as well.
    /// </summary>
    public const string PsiphonInsideDirName = BaseFile + "-psiphon";

    /// <summary>Psiphon's signed server list, as it is shipped and downloaded.</summary>
    public const string PsiphonServersFileName = Global.PsiphonServersFileName;

    /// <summary>The publication time of the shipped list, in seconds since the epoch.</summary>
    public const string PsiphonServersStampFileName = Global.PsiphonServersStampFileName;

    /// <summary>The server entries unpacked from the active list, which the core hands to the Psiphon client.</summary>
    public const string PsiphonEntriesFileName = "psiphon-servers.txt";

    /// <summary>Which version of the active list the unpacked entries come from.</summary>
    public const string PsiphonEntriesSourceFileName = "psiphon-servers.source";

    /// <summary>The process ids of the session core and of a standalone core, for leftover cores on Linux and macOS.</summary>
    public const string SessionPidFileName = "aether.pid";

    public const string RunPidFileName = "aether-run.pid";

    /// <summary>guiConfigs/aether: the working folder of every core and the keys in use.</summary>
    public static string WorkDir => Utils.GetConfigPath(WorkDirName);

    /// <summary>guiConfigs/aether-renewal: the renewal staging folder.</summary>
    public static string RenewalDir => Utils.GetConfigPath(RenewalDirName);

    /// <summary>guiConfigs/psiphon: the session's Psiphon datastore.</summary>
    public static string PsiphonStateDir => Utils.GetConfigPath(PsiphonStateDirName);

    /// <summary>guiConfigs/psiphon-probe: the Psiphon datastore of tests, scans and renewals.</summary>
    public static string PsiphonProbeDir => Utils.GetConfigPath(PsiphonProbeDirName);

    /// <summary>guiConfigs/aether/aether.toml-psiphon: a datastore the core kept inside the working folder.</summary>
    public static string PsiphonInsideDir => Path.Combine(WorkDir, PsiphonInsideDirName);

    /// <summary>guiConfigs/aether/aether.toml (AETHER_CONFIG).</summary>
    public static string BaseConfigFile => Path.Combine(WorkDir, BaseFile);

    /// <summary>guiConfigs/aether/aether-masque.toml (AETHER_MASQUE_CONFIG of every core but a key run's).</summary>
    public static string MasqueConfigFile => Path.Combine(WorkDir, MasqueFile);

    /// <summary>guiConfigs/aether/psiphon_servers.dat: the active list, which neither a core nor an app update touches.</summary>
    public static string ActivePsiphonServersFile => Path.Combine(WorkDir, PsiphonServersFileName);

    /// <summary>guiConfigs/aether/aether.pid.</summary>
    public static string SessionPidFile => Path.Combine(WorkDir, SessionPidFileName);

    /// <summary>guiConfigs/aether/aether-run.pid.</summary>
    public static string RunPidFile => Path.Combine(WorkDir, RunPidFileName);

    /// <summary>
    /// The list shipped in the release archive, beside the geo files. Read from the program's own bin folder: where
    /// PattN keeps its data in the local application data folder, the bin copied there at the first start is never
    /// refreshed by an upgrade.
    /// </summary>
    public static string BundledPsiphonServersFile => Path.Combine(Utils.GetBaseDirectory("bin"), PsiphonServersFileName);

    /// <summary>The publication time of <see cref="BundledPsiphonServersFile" />.</summary>
    public static string BundledPsiphonServersStampFile =>
        Path.Combine(Utils.GetBaseDirectory("bin"), PsiphonServersStampFileName);
}
