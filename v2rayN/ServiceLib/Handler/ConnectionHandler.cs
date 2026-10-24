namespace ServiceLib.Handler;

public static class ConnectionHandler
{
    private static readonly string _tag = "ConnectionHandler";

    /// <summary>
    /// Runs ping and IP checks.
    /// </summary>
    public static async Task<AvailabilityCheckResult> RunAvailabilityCheck()
    {
        var time = await GetRealPingTimeInfo();
        var ip = time > 0 ? await GetIPInfo() : Global.None;

        return new AvailabilityCheckResult(time, ip);
    }

    /// <summary>
    /// Gets IP information using the default local proxy.
    /// </summary>
    private static async Task<string?> GetIPInfo()
    {
        var webProxy = await GetWebProxy();

        var ipInfo = await GetIPInfo(webProxy);
        return ipInfo?.ToString() ?? Global.None;
    }

    /// <summary>
    /// Measures real ping time using configured test URL.
    /// </summary>
    private static async Task<int> GetRealPingTimeInfo()
    {
        var responseTime = -1;
        try
        {
            var webProxy = await GetWebProxy();

            for (var i = 0; i < 2; i++)
            {
                responseTime = await GetRealPingTime(webProxy);
                if (responseTime > 0)
                {
                    break;
                }
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return -1;
        }
        return responseTime;
    }

    /// <summary>
    /// Creates local SOCKS proxy instance.
    /// </summary>
    private static async Task<WebProxy?> GetWebProxy()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        return new WebProxy($"socks5://{Global.Loopback}:{port}");
    }

    /// <summary>
    /// Measures response time by sending HTTP requests through proxy.
    /// </summary>
    public static async Task<int> GetRealPingTime(IWebProxy? webProxy, CancellationToken cancellationToken = default)
    {
        var url = AppManager.Instance.Config.SpeedTestItem.SpeedPingTestUrl;
        var responseTime = -1;
        try
        {
            using var timeoutCts = new CancellationTokenSource();
            timeoutCts.CancelAfter(Global.LocalFetch);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var linkedToken = linkedCts.Token;
            using var client = new HttpClient(new SocketsHttpHandler()
            {
                Proxy = webProxy,
                UseProxy = webProxy != null,
                ConnectTimeout = Global.LocalFetch,
            });

            List<int> oneTime = [];
            for (var i = 0; i < 2; i++)
            {
                var timer = Stopwatch.StartNew();
                await client.GetAsync(url, linkedToken).ConfigureAwait(false);
                timer.Stop();
                oneTime.Add((int)timer.Elapsed.TotalMilliseconds);
                await Task.Delay(100, linkedToken);
            }
            responseTime = oneTime.Where(x => x > 0).OrderBy(x => x).FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Ignore
        }
        return responseTime;
    }

    /// <summary>
    /// Gets IP and country information through specified proxy.
    /// </summary>
    public static async Task<IpInfoResult?> GetIPInfo(IWebProxy? webProxy, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = AppManager.Instance.Config.SpeedTestItem.IPAPIUrl;
            if (url.IsNullOrEmpty())
            {
                return null;
            }

            var downloadHandle = new DownloadService();
            var result = await downloadHandle.TryDownloadString(url, webProxy, "", cancellationToken);
            if (result == null)
            {
                return null;
            }

            var ipInfo = JsonUtils.Deserialize<IPAPIInfo>(result);
            if (ipInfo == null)
            {
                return null;
            }

            var ip = ipInfo.ip ?? ipInfo.clientIp ?? ipInfo.ip_addr ?? ipInfo.query;
            var countryCode = ipInfo.country_code ?? ipInfo.countryCode ?? ipInfo.location?.country_code ?? string.Empty;
            var countryName = ipInfo.country ?? ipInfo.country_name ?? string.Empty;

            if (countryCode.IsNullOrEmpty() && countryName.Length == 2)
            {
                countryCode = countryName;
                countryName = countryCode.CountryToName() ?? countryName;
            }
            else if (countryName.IsNullOrEmpty() && countryCode.IsNotEmpty())
            {
                countryName = countryCode.CountryToName() ?? countryCode;
            }

            return new IpInfoResult(countryCode.IsNotEmpty() ? countryCode : countryName, ip, countryName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
    public static async Task<int> GetRealPingTime(IWebProxy? webProxy, string? url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return -1;
        }
        var budget = System.Diagnostics.Stopwatch.StartNew();
        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = webProxy,
            UseProxy = webProxy != null,
            ConnectTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMilliseconds(1),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return await AetherDelayTester.BestOfAttemptsAsync(
            () => timeout - budget.Elapsed,
            async (left, ct) =>
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(left);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var response = await client.GetAsync(uri, attempt.Token).ConfigureAwait(false);
                    return response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.NoContent
                        ? Math.Max(1, (int)timer.ElapsedMilliseconds)
                        : -1;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return -1;
                }
                catch
                {
                    return -1;
                }
            },
            cancellationToken);
    }

}
