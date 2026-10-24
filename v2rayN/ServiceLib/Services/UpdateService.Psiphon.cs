using System.Globalization;

namespace ServiceLib.Services;

// PattN: the Psiphon server list of the Aether core (design 2.11), beside the core and geo-file updates of UpdateService.cs
public partial class UpdateService
{
    /// <summary>PattN: the name the check-update window gives the Psiphon server list.</summary>
    public const string PsiphonServersName = "psiphon_servers";

    /// <summary>
    /// PattN (design 2.11): downloads Psiphon's server list into a temporary file, through the local proxy when
    /// <paramref name="blProxy" /> is set and a core serves it, directly otherwise, as the geo files are, with the root
    /// store the settings name; takes its Last-Modified time as when it was published; and puts it in place of the list
    /// in use only once it is Psiphon's signed list with at least 50 entries. A broken download keeps the list in use, and
    /// says so; one that takes longer than 3 minutes is a broken one. No core needs to stop: the next core start unpacks
    /// the new list (critique 16).
    /// </summary>
    public async Task<PsiphonServersUpdateResult> UpdatePsiphonServersAsync(bool blProxy = true, CancellationToken cancellationToken = default)
    {
        await UpdateFunc(false, string.Format(ResUI.MsgStartUpdating, PsiphonServersName));
        var proxy = blProxy ? await LocalSocksProxyAsync(cancellationToken) : null;
        var chainPolicy = CertPemManager.Instance.BuildCertificateChainPolicy();
        var result = await PsiphonServersUpdate.RunAsync(
            (url, file, since, ct) => PsiphonServersUpdate.DownloadAsync(proxy, url, file, since, ct, chainPolicy),
            Global.PsiphonServersUrl,
            AetherPaths.ActivePsiphonServersFile,
            Utils.GetTempPath(Utils.GetGuid()),
            () => DateTime.UtcNow,
            ct: cancellationToken);
        await UpdateFunc(result.Outcome == PsiphonServersUpdateOutcome.Updated, PsiphonServersMessage(result));
        return result;
    }

    /// <summary>PattN: what the check-update window says of <paramref name="result" />.</summary>
    public static string PsiphonServersMessage(PsiphonServersUpdateResult result)
    {
        var published = result.PublishedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
        return result.Outcome switch
        {
            PsiphonServersUpdateOutcome.Updated => string.Format(ResUI.MsgPsiphonServersUpdated, result.Entries, published),
            PsiphonServersUpdateOutcome.Current => string.Format(ResUI.MsgPsiphonServersCurrent, published),
            PsiphonServersUpdateOutcome.Invalid => ResUI.MsgPsiphonServersInvalid,
            _ => result.Reason.IsNullOrEmpty() ? ResUI.OperationFailed : $"{ResUI.OperationFailed}: {result.Reason}",
        };
    }

    /// <summary>
    /// PattN: the local SOCKS proxy, when a core serves it, as DownloadService picks it for the geo files; null otherwise,
    /// and the download goes directly.
    /// </summary>
    private static async Task<IWebProxy?> LocalSocksProxyAsync(CancellationToken cancellationToken)
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        return await AetherCoreManager.AnswersSocksAsync(port, cancellationToken)
            ? new WebProxy($"socks5://{Global.Loopback}:{port}")
            : null;
    }
}
