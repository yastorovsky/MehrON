using System.Formats.Tar;
using System.IO.Compression;

namespace ServiceLib.Aether;

/// <summary>
/// The programs of the Aether core in bin/aether (PattNG AetherCoreManager binary, psiphonBinary and transportBinary,
/// and isSupported, isPsiphonSupported and isTorTransportsSupported), and the unpacking of a release archive of the core
/// into that folder. PattNG ships the three programs in its APK; PattN downloads them from patterniha/Aether's releases
/// on the check-update page, and its release archives bundle them.
/// </summary>
public static class AetherBinaries
{
    /// <summary>The core, which every Aether profile runs on.</summary>
    public const string CoreName = "aether";

    /// <summary>The Psiphon client the core runs for a profile with Psiphon.</summary>
    public const string PsiphonName = "psiphon-tunnel-core";

    /// <summary>The pluggable transport Tor's bridges run through.</summary>
    public const string LyrebirdName = "lyrebird";

    /// <summary>The three programs, the core first.</summary>
    public static IReadOnlyList<string> Names { get; } = [CoreName, PsiphonName, LyrebirdName];

    /// <summary>bin/aether, the folder of <see cref="ECoreType.aether" />.</summary>
    public static string Folder => Utils.GetBinPath("", nameof(ECoreType.aether));

    /// <summary>bin/aether/aether[.exe].</summary>
    public static string CoreFile => FileIn(Folder, CoreName);

    /// <summary>bin/aether/psiphon-tunnel-core[.exe].</summary>
    public static string PsiphonFile => FileIn(Folder, PsiphonName);

    /// <summary>bin/aether/lyrebird[.exe].</summary>
    public static string LyrebirdFile => FileIn(Folder, LyrebirdName);

    /// <summary>
    /// The notice PattN's release archives and packages put beside the three programs (package-aether.sh, critique 28):
    /// their names, their licenses (AGPL-3.0 for aether, GPL-3.0 for psiphon-tunnel-core, a BSD license for lyrebird),
    /// the files beside them that hold the license texts, and where their source code is. The Help menu opens it.
    /// </summary>
    public const string NoticeName = "NOTICE.txt";

    /// <summary>
    /// The folder of license files a release archive of the core can carry beside aether (patterniha/Aether's LICENSES:
    /// the licenses of the three programs, and the notices of the libraries built into aether). <see cref="Extract" />
    /// keeps it as a folder in bin/aether, so that an update from the check-update page brings the licenses of the build
    /// it installs.
    /// </summary>
    public const string LicensesFolderName = "LICENSES";

    /// <summary>
    /// bin/aether/NOTICE.txt in the program's own folder, where a release put it. It is read there rather than in
    /// <see cref="Folder" />: where PattN keeps its data in the local application data folder, the copy of bin made at
    /// the first start is never refreshed by an upgrade (as AetherPaths.BundledPsiphonServersFile).
    /// </summary>
    public static string BundledNoticeFile => Path.Combine(Utils.GetBaseDirectory("bin"), nameof(ECoreType.aether), NoticeName);

    /// <summary>Opens <see cref="BundledNoticeFile" /> with the system's program for text files.</summary>
    public static void OpenNotice()
    {
        var os = Utils.IsWindows() ? OSPlatform.Windows : Utils.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        var (fileName, arguments) = OpenCommand(BundledNoticeFile, os);
        ProcUtils.ProcessStart(fileName, arguments);
    }

    /// <summary>
    /// How <paramref name="file" /> is handed to the system's program for it, as MainWindowViewModel.OpenTheFileLocation
    /// hands over a folder: Windows opens the file itself through the shell, Linux through xdg-open and macOS through
    /// open, with the path as an argument, so that a path with a space in it still opens.
    /// </summary>
    public static (string FileName, string Arguments) OpenCommand(string file, OSPlatform os)
    {
        if (os == OSPlatform.Windows)
        {
            return (file, "");
        }
        return (os == OSPlatform.OSX ? "open" : "xdg-open", file);
    }

    /// <summary>Whether the core is there to run (PattNG isSupported).</summary>
    public static bool IsCoreAvailable() => IsRunnable(CoreFile);

    /// <summary>Whether the Psiphon client is there to run; without it a profile with Psiphon cannot connect (PattNG isPsiphonSupported).</summary>
    public static bool IsPsiphonAvailable() => IsRunnable(PsiphonFile);

    /// <summary>Whether lyrebird is there to run; without it Tor has no bridges where it is blocked (PattNG isTorTransportsSupported).</summary>
    public static bool IsLyrebirdAvailable() => IsRunnable(LyrebirdFile);

    /// <summary>The file of the program <paramref name="name" /> in <paramref name="folder" />: name.exe on Windows.</summary>
    public static string FileIn(string folder, string name) => Path.Combine(folder, Utils.GetExeName(name));

    /// <summary>
    /// The full paths of those of the three programs that are in <paramref name="folder" />, the core first. A routing
    /// rule of the TUN sends what they dial out directly: a core whose command names an upstream of its own dials out
    /// itself, and so do the helpers a command leaves without one, which must not loop back into the TUN.
    /// </summary>
    public static List<string> ExistingFiles(string folder)
    {
        return Names.Select(name => FileIn(folder, name)).Where(File.Exists).Select(Path.GetFullPath).ToList();
    }

    /// <summary>
    /// Whether <paramref name="file" /> can be run (PattNG File.canExecute): it exists and, outside Windows, has an
    /// execute bit, without which neither PattN nor the core can start it.
    /// </summary>
    public static bool IsRunnable(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }
            if (OperatingSystem.IsWindows())
            {
                return true;
            }
            const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(file) & execute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Outside Windows, gives each of the three programs in <paramref name="folder" /> that is there its execute bits,
    /// at start and after an update: without them the core cannot start a helper, nor PattN the core.
    /// </summary>
    public static async Task SetExecutableAsync(string folder)
    {
        if (!Utils.IsNonWindows())
        {
            return;
        }
        foreach (var name in Names)
        {
            var file = Path.Combine(folder, name);
            if (File.Exists(file))
            {
                await Utils.SetLinuxChmod(file);
            }
        }
    }

    /// <summary>
    /// Unpacks a release archive of the core into <paramref name="folder" />, flat: aether[.exe], and lyrebird and
    /// psiphon-tunnel-core out of the archive's pt/ folder, all three side by side; the core looks for its helpers
    /// beside itself as well as in pt/. An archive whose name holds .tar.gz is read as a gzip-compressed tar, any other
    /// as a zip, as the check-update page reads the archives of every core. Folders, links and macOS's AppleDouble files
    /// ("._name", which an archive made on a Mac can carry beside each file) are left out. The archive's LICENSES folder
    /// is the one folder kept (<see cref="LicensesFolderName" />): its files go into folder/LICENSES with the folders
    /// they are in there, and replace the LICENSES folder of an earlier build. A file already in the folder is replaced,
    /// and no other file of the folder is touched, so that an archive without LICENSES leaves PattN's notice, the
    /// license texts beside it and an earlier LICENSES folder as they are.
    /// </summary>
    /// <returns>The files written, relative to the folder with '/', in the order of the archive.</returns>
    /// <exception cref="InvalidDataException">The archive is not a zip, or not a gzip-compressed tar.</exception>
    /// <exception cref="IOException">A file could not be written, as when a running core holds it on Windows.</exception>
    public static List<string> Extract(string archive, string folder)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        var licensesReplaced = false;
        // The file an entry is written to, with the folders it needs in LICENSES. The LICENSES folder of an earlier build
        // goes before the first file of this archive's is written, so that it holds this build's licenses alone.
        string Prepare(string name)
        {
            var file = Path.Combine(folder, name);
            if (name.StartsWith(LicensesFolderName + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                var licenses = Path.Combine(folder, LicensesFolderName);
                if (!licensesReplaced && Directory.Exists(licenses))
                {
                    Directory.Delete(licenses, recursive: true);
                }
                licensesReplaced = true;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            }
            return file;
        }

        if (archive.Contains(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    || TargetName(entry.Name) is not { } name)
                {
                    continue;
                }
                entry.ExtractToFile(Prepare(name), overwrite: true);
                written.Add(name.Replace(Path.DirectorySeparatorChar, '/'));
            }
        }
        else
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                if (TargetName(entry.FullName) is not { } name)
                {
                    continue;
                }
                entry.ExtractToFile(Prepare(name), overwrite: true);
                written.Add(name.Replace(Path.DirectorySeparatorChar, '/'));
            }
        }
        return written;
    }

    /// <summary>
    /// Where an entry of an archive is unpacked to, relative to the folder: a file in the archive's LICENSES folder, at
    /// whatever depth of the archive that folder is, keeps the folders it is in below LICENSES
    /// (LICENSES/crates/ring/LICENSE); any other file is unpacked flat (<see cref="FlatName" />), so that a license file
    /// is never taken for a program of the same name. Null for an entry that is left out: a folder, an AppleDouble file
    /// (in __MACOSX or as "._name"), a name no file can have, or a name in LICENSES that would climb out of it.
    /// </summary>
    private static string? TargetName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var names = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(name => name != ".").ToList();
        var at = names.IndexOf(LicensesFolderName);
        if (at < 0)
        {
            return FlatName(path);
        }
        var inside = names.Skip(at + 1).ToList();
        if (normalized.EndsWith('/')
            || inside.Count == 0
            || names.Take(at).Contains("__MACOSX")
            || inside.Any(name => FileName(name) is null))
        {
            return null;
        }
        return Path.Combine([LicensesFolderName, .. inside]);
    }

    /// <summary>
    /// The name an entry of an archive is unpacked to: the last part of its path, whether the archive separates the
    /// parts with '/' or, as a zip made by Windows PowerShell does, with '\'; null for a folder, an AppleDouble file or a
    /// name no file can have, so that no entry is ever written outside the folder.
    /// </summary>
    private static string? FlatName(string path)
    {
        var name = path.Replace('\\', '/');
        return FileName(name[(name.LastIndexOf('/') + 1)..]);
    }

    /// <summary>
    /// <paramref name="name" /> when a file can have it; null for "", "." and "..", an AppleDouble name ("._name") and a
    /// name with a character no file name can hold.
    /// </summary>
    private static string? FileName(string name)
    {
        if (name.Length == 0
            || name is "." or ".."
            || name.StartsWith("._", StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }
        return name;
    }
}
