using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace ServiceLib.Aether;

/// <summary>How an update of psiphon_servers went.</summary>
public enum PsiphonServersUpdateOutcome
{
    /// <summary>A newer list took the place of the one in use.</summary>
    Updated,

    /// <summary>The list in use is the newest one published.</summary>
    Current,

    /// <summary>What came is not Psiphon's signed list; the one in use stays.</summary>
    Invalid,

    /// <summary>The list could not be downloaded or put in place; the one in use stays.</summary>
    Failed,
}

/// <summary>
/// What an update of psiphon_servers gave: how it went, how many server entries the new list has, when the list in use
/// was published, and why one could not be had.
/// </summary>
public sealed record PsiphonServersUpdateResult(
    PsiphonServersUpdateOutcome Outcome,
    int Entries = 0,
    DateTime? PublishedAtUtc = null,
    string? Reason = null);

/// <summary>What a download of the list gave: nothing newer than the list in use, or a file and the time its server says it was published.</summary>
public sealed record PsiphonListDownload(bool NotModified, DateTime? LastModifiedUtc = null);

/// <summary>
/// PattN (design 2.11): the update of the Psiphon server list the check-update window offers. The list is downloaded into
/// a temporary file, checked (Psiphon's signature and at least <see cref="PsiphonServerList.MinimumEntries" /> entries) and
/// only then put in place of the list in use, with the time its server says it was published (the time of the download
/// without one): PattNG checks a list only as a core starts, and a broken download here would replace a good list. No core
/// needs to stop: the next core start unpacks the new list.
/// </summary>
public static class PsiphonServersUpdate
{
    /// <summary>
    /// Downloads <paramref name="url" /> into <paramref name="file" />; asked for nothing older than
    /// <paramref name="ifModifiedSinceUtc" /> when given. Throws what an HTTP request or a file write throws.
    /// </summary>
    public delegate Task<PsiphonListDownload> Downloader(string url, string file, DateTime? ifModifiedSinceUtc, CancellationToken ct);

    /// <summary>
    /// Updates the list in use at <paramref name="active" /> from <paramref name="url" /> through
    /// <paramref name="download" />, by way of <paramref name="tempFile" />, which is gone afterwards. A list published no
    /// later than the one in use leaves it as it is; one that is not Psiphon's signed list with
    /// <see cref="PsiphonServerList.MinimumEntries" /> entries, by <paramref name="signingKey" />, is refused. A list in use
    /// that is not such a list itself (replaced by hand, or damaged) counts as none, whatever its time: any signed list
    /// takes its place, as PattNG's download always replaces its copy. A cancellation of <paramref name="ct" /> throws
    /// <see cref="OperationCanceledException" />.
    /// </summary>
    public static async Task<PsiphonServersUpdateResult> RunAsync(
        Downloader download,
        string url,
        string active,
        string tempFile,
        Func<DateTime> utcNow,
        string signingKey = PsiphonServerList.SigningKey,
        CancellationToken ct = default)
    {
        DateTime? inUse = await Task.Run(() => UsableListTime(active, signingKey), ct).ConfigureAwait(false);
        try
        {
            PsiphonListDownload fetched;
            try
            {
                fetched = await download(url, tempFile, inUse, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsDownloadFailure(ex, ct))
            {
                Logging.SaveLog($"PsiphonServers: the list could not be downloaded; the one in use stays", ex);
                return new PsiphonServersUpdateResult(PsiphonServersUpdateOutcome.Failed, PublishedAtUtc: inUse, Reason: ex.Message);
            }
            if (fetched.NotModified)
            {
                return new PsiphonServersUpdateResult(PsiphonServersUpdateOutcome.Current, PublishedAtUtc: inUse);
            }
            var publishedAt = fetched.LastModifiedUtc ?? utcNow();
            if (inUse is { } held && publishedAt <= held)
            {
                return new PsiphonServersUpdateResult(PsiphonServersUpdateOutcome.Current, PublishedAtUtc: held);
            }
            int entries;
            try
            {
                entries = PsiphonServerList.Verify(await File.ReadAllBytesAsync(tempFile, ct).ConfigureAwait(false), signingKey);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logging.SaveLog($"PsiphonServers: the download is not Psiphon's signed server list ({ex.Message}); the list in use stays");
                return new PsiphonServersUpdateResult(PsiphonServersUpdateOutcome.Invalid, PublishedAtUtc: inUse, Reason: ex.Message);
            }
            try
            {
                PsiphonServerList.Install(tempFile, active, publishedAt);
            }
            catch (IOException ex)
            {
                Logging.SaveLog("PsiphonServers: the new list could not be put in place; the one in use stays", ex);
                return new PsiphonServersUpdateResult(PsiphonServersUpdateOutcome.Failed, PublishedAtUtc: inUse, Reason: ex.Message);
            }
            return new PsiphonServersUpdateResult(PsiphonServersUpdateOutcome.Updated, entries, publishedAt);
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logging.SaveLog("PsiphonServers: a downloaded list could not be removed", ex);
            }
        }
    }

    /// <summary>How long a download of the list may take, the body included (PattNG's fetch script: curl --max-time 180).</summary>
    public static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The download of PattN (<see cref="Downloader" />): a GET of <paramref name="url" /> through
    /// <paramref name="proxy" />, or directly without one, with If-Modified-Since, so that a list no newer than the one in
    /// use is not sent again; the body goes into <paramref name="file" />, and the Last-Modified header is when the list
    /// was published. TLS is 1.2 or 1.3, and the server's certificate is checked against <paramref name="chainPolicy" />
    /// when given, the root store the settings name, as for every other download (DownloaderHelper). The whole download,
    /// its body included, gets <paramref name="limit" /> (<see cref="DownloadLimit" /> by default); one that takes longer
    /// throws a <see cref="TimeoutException" />.
    /// </summary>
    public static async Task<PsiphonListDownload> DownloadAsync(
        IWebProxy? proxy,
        string url,
        string file,
        DateTime? ifModifiedSinceUtc,
        CancellationToken ct = default,
        X509ChainPolicy? chainPolicy = null,
        TimeSpan? limit = null)
    {
        var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = proxy != null,
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
        if (chainPolicy != null)
        {
            handler.SslOptions.CertificateChainPolicy = chainPolicy;
        }
        var allowed = limit ?? DownloadLimit;
        // HttpClient.Timeout ends once the headers are in (ResponseHeadersRead); the body would have no limit.
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(allowed);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (ifModifiedSinceUtc is { } since)
            {
                request.Headers.IfModifiedSince = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc));
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new PsiphonListDownload(NotModified: true);
            }
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(output, deadline.Token).ConfigureAwait(false);
            }
            return new PsiphonListDownload(NotModified: false, response.Content.Headers.LastModified?.UtcDateTime);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture,
                $"the list did not arrive within {allowed.TotalSeconds:0} seconds"), ex);
        }
    }

    /// <summary>
    /// The time of the list in use at <paramref name="active" />, when it is Psiphon's signed list by
    /// <paramref name="signingKey" /> with <see cref="PsiphonServerList.MinimumEntries" /> entries; null without such a
    /// list there.
    /// </summary>
    private static DateTime? UsableListTime(string active, string signingKey)
    {
        try
        {
            if (!File.Exists(active))
            {
                return null;
            }
            using (var stream = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                PsiphonServerList.Verify(bytes.ToArray(), signingKey);
            }
            return File.GetLastWriteTimeUtc(active);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog($"PsiphonServers: the list in use is not Psiphon's signed server list ({ex.Message}); any signed list takes its place");
            return null;
        }
    }

    private static bool IsDownloadFailure(Exception ex, CancellationToken ct)
    {
        return ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested);
    }
}
