using System.Text.Json;
using ServiceLib.ViewModels;

namespace ServiceLib.Manager;

/// <summary>
/// Core process processing class
/// </summary>
public class CoreManager
{
    private static readonly Lazy<CoreManager> _instance = new(() => new());
    public static CoreManager Instance => _instance.Value;
    private Config _config;

    [SupportedOSPlatform("windows")]
    private WindowsJobService? _processJob;

    private ProcessService? _processService;
    private ProcessService? _processPreService;
    private readonly List<ProcessService> _extraProcessServices = [];
    private bool _linuxSudo = false;
    private Func<bool, string, Task>? _updateFunc;
    private const string _tag = "CoreHandler";

    public async Task Init(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        //Copy the bin folder to the storage location (for init)
        if (Environment.GetEnvironmentVariable(Global.LocalAppData) == "1")
        {
            var fromPath = Utils.GetBaseDirectory("bin");
            var toPath = Utils.GetBinPath("");
            if (fromPath != toPath)
            {
                FileUtils.CopyDirectory(fromPath, toPath, true, false);
            }
        }

        if (Utils.IsNonWindows())
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
            foreach (var it in coreInfo)
            {
                if (it.CoreType == ECoreType.v2rayN)
                {
                    if (Utils.UpgradeAppExists(out var upgradeFileName))
                    {
                        await Utils.SetLinuxChmod(upgradeFileName);
                    }
                    continue;
                }

                foreach (var name in it.CoreExes)
                {
                    var exe = Utils.GetBinPath(Utils.GetExeName(name), it.CoreType.ToString());
                    if (File.Exists(exe))
                    {
                        await Utils.SetLinuxChmod(exe);
                    }
                }
            }
        }
    }

    /// <param name="mainContext">Resolved main context (with pre-socks ports already merged if applicable).</param>
    /// <param name="preContext">Optional pre-socks context passed to <see cref="CoreStartPreService"/>.</param>
    public async Task LoadCore(CoreConfigContext? mainContext, CoreConfigContext? preContext)
    {
        if (mainContext == null)
        {
            await UpdateFunc(false, ResUI.CheckServerSettings);
            return;
        }

        var node = mainContext.Node;
        await CoreStop();
        await Task.Delay(100);
        if (!await SniSpoofingManager.Instance.StartAsync(node, _updateFunc))
        {
            return;
        }

        var fileName = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        var result = await CoreConfigHandler.GenerateClientConfig(mainContext, fileName);
        if (result.Success != true)
        {
            await SniSpoofingManager.Instance.StopAsync();
            await UpdateFunc(true, result.Msg);
            return;
        }

        await UpdateFunc(false, $"{node.GetSummary()}");
        await UpdateFunc(false, $"{Utils.GetRuntimeInfo()}");
        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));

        if (Utils.IsWindows() && (mainContext?.IsTunEnabled == true || preContext?.IsTunEnabled == true))
        {
            await Task.Delay(100);
            await WindowsUtils.RemoveTunDevice();
        }

        if (node.ConfigType == EConfigType.ProxyChain)
        {
            var childIds = Utils.String2List(node.GetProtocolExtra()?.ChildItems) ?? [];
            var children = await AppManager.Instance.GetProfileItemsByIndexIds(childIds);
            var mid = children.ElementAtOrDefault(0);
            var exit = children.ElementAtOrDefault(1);

            if (mid != null && exit != null)
            {
                if (mid.ConfigType == EConfigType.Custom && exit.ConfigType == EConfigType.Custom)
                {
                    var midPort = mid.PreSocksPort is > 0 and <= 65535 ? mid.PreSocksPort.Value : (mid.CoreType == ECoreType.aether ? 1819 : 1080);
                    var exitPort = exit.PreSocksPort is > 0 and <= 65535 ? exit.PreSocksPort.Value : (exit.CoreType == ECoreType.aether ? 1819 : 1080);
                    if (exitPort == midPort)
                    {
                        exitPort = midPort == 1080 ? 1081 : 1080;
                        exit = JsonUtils.DeepCopy(exit);
                        exit.PreSocksPort = exitPort;
                        if (mainContext.AllProxiesMap.ContainsKey(exit.IndexId))
                        {
                            mainContext.AllProxiesMap[exit.IndexId] = exit;
                        }
                    }

                    // 1. Start Middle core first
                    var midProc = await StartCustomChildCore(mid, "configChain_mid.json");
                    if (midProc != null)
                    {
                        _extraProcessServices.Add(midProc);
                    }
                    await WaitForPort(midPort);

                    // 2. Start Exit core chained through Middle core
                    var upstreamProxy = $"socks5://127.0.0.1:{midPort}";
                    var exitProc = await StartCustomChildCore(exit, "configChain_exit.json", upstreamProxy);
                    if (exitProc != null)
                    {
                        _extraProcessServices.Add(exitProc);
                    }
                    await WaitForPort(exitPort);
                    if (exit.CoreType == ECoreType.psiphon)
                    {
                        await WaitForPsiphonTunnel(25);
                    }

                    // 3. Re-generate main core config with updated AllProxiesMap and start main core (Xray / Sing-box)
                    await CoreConfigHandler.GenerateClientConfig(mainContext, fileName);
                    await CoreStart(mainContext);
                    if (preContext != null)
                    {
                        await WaitForProxyPort(preContext);
                        await CoreStartPreService(preContext);
                    }
                    AppManager.Instance.RunningCoreType = preContext?.RunCoreType ?? mainContext.RunCoreType;
                    if (_processService != null)
                    {
                        await UpdateFunc(true, $"{node.GetSummary()}");
                    }
                    return;
                }
                else if (mid.ConfigType == EConfigType.Custom)
                {
                    var midPort = mid.PreSocksPort is > 0 and <= 65535 ? mid.PreSocksPort.Value : (mid.CoreType == ECoreType.aether ? 1819 : 1080);
                    var midProc = await StartCustomChildCore(mid, "configChain_mid.json");
                    if (midProc != null)
                    {
                        _extraProcessServices.Add(midProc);
                    }
                    await WaitForPort(midPort);

                    await CoreStart(mainContext);
                    if (preContext != null)
                    {
                        await WaitForProxyPort(preContext);
                        await CoreStartPreService(preContext);
                    }
                    AppManager.Instance.RunningCoreType = preContext?.RunCoreType ?? mainContext.RunCoreType;
                    if (_processService != null)
                    {
                        await UpdateFunc(true, $"{node.GetSummary()}");
                    }
                    return;
                }
                else if (exit.ConfigType == EConfigType.Custom)
                {
                    await CoreStart(mainContext);
                    var localSocksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
                    await WaitForPort(localSocksPort);

                    var upstreamProxy = $"socks5://127.0.0.1:{localSocksPort}";
                    var exitPort = exit.PreSocksPort is > 0 and <= 65535 ? exit.PreSocksPort.Value : (exit.CoreType == ECoreType.aether ? 1819 : 1080);
                    var exitProc = await StartCustomChildCore(exit, "configChain_exit.json", upstreamProxy);
                    if (exitProc != null)
                    {
                        _extraProcessServices.Add(exitProc);
                    }
                    await WaitForPort(exitPort);
                    if (exit.CoreType == ECoreType.psiphon)
                    {
                        await WaitForPsiphonTunnel(25);
                    }

                    if (preContext != null)
                    {
                        await WaitForProxyPort(preContext);
                        await CoreStartPreService(preContext);
                    }
                    AppManager.Instance.RunningCoreType = preContext?.RunCoreType ?? mainContext.RunCoreType;
                    if (_processService != null)
                    {
                        await UpdateFunc(true, $"{node.GetSummary()}");
                    }
                    return;
                }
            }
        }

        if (preContext?.Node?.ConfigType == EConfigType.Custom)
        {
            await CoreStartPreService(preContext, isFirst: true);
            await WaitForProxyPort(preContext);
            await CoreStart(mainContext);
        }
        else
        {
            await CoreStart(mainContext);
            await WaitForProxyPort(preContext);
            await CoreStartPreService(preContext);
        }

        AppManager.Instance.RunningCoreType = preContext?.RunCoreType ?? mainContext.RunCoreType;

        if (_processService != null)
        {
            await UpdateFunc(true, $"{node.GetSummary()}");
        }
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(List<ServerTestItem> selecteds)
    {
        var coreType = selecteds.FirstOrDefault()?.CoreType == ECoreType.sing_box ? ECoreType.sing_box : ECoreType.Xray;
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, configPath, selecteds, coreType);
        await UpdateFunc(false, result.Msg);
        if (result.Success != true)
        {
            return null;
        }

        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await UpdateFunc(false, configPath);

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(ServerTestItem testItem)
    {
        var node = await AppManager.Instance.GetProfileItem(testItem.IndexId);
        if (node is null)
        {
            return null;
        }

        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName);
        var (context, _) = await CoreConfigContextBuilder.Build(_config, node);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, context, testItem, configPath);
        if (result.Success != true)
        {
            return null;
        }

        var coreType = context.RunCoreType;
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task CoreStop()
    {
        try
        {
            if (_linuxSudo)
            {
                await CoreAdminManager.Instance.KillProcessAsLinuxSudo();
                _linuxSudo = false;
            }

            if (_processService != null)
            {
                await _processService.StopAsync();
                _processService.Dispose();
                _processService = null;
            }

            if (_processPreService != null)
            {
                await _processPreService.StopAsync();
                _processPreService.Dispose();
                _processPreService = null;
            }

            foreach (var p in _extraProcessServices)
            {
                try
                {
                    await p.StopAsync();
                    p.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
            }
            _extraProcessServices.Clear();

            await SniSpoofingManager.Instance.StopAsync();
            _psiphonIsConnected = false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #region Private

    private async Task CoreStart(CoreConfigContext context)
    {
        var node = context.Node;
        var coreType = AppManager.Instance.GetCoreType(node, node.ConfigType);
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);

        var displayLog = node.ConfigType != EConfigType.Custom || node.DisplayLog || node.CoreType is ECoreType.psiphon or ECoreType.aether;
        var proc = await RunProcess(coreInfo, Global.CoreConfigFileName, displayLog, true, context.IsTunEnabled, node);
        if (proc is null)
        {
            return;
        }
        _processService = proc;
        if (node.CoreType == ECoreType.psiphon)
        {
            await WaitForPsiphonTunnel(25);
        }
    }

    private async Task CoreStartPreService(CoreConfigContext? preContext, bool isFirst = false)
    {
        if ((isFirst || _processService is { HasExited: false }) && preContext != null)
        {
            var preCoreType = preContext?.Node?.CoreType ?? ECoreType.sing_box;
            var fileName = Utils.GetBinConfigPath(Global.CorePreConfigFileName);
            var result = await CoreConfigHandler.GenerateClientConfig(preContext, fileName);
            if (result.Success)
            {
                var coreInfo = CoreInfoManager.Instance.GetCoreInfo(preCoreType);
                var displayLog = preContext.Node.ConfigType != EConfigType.Custom || preContext.Node.DisplayLog || preContext.Node.CoreType is ECoreType.psiphon or ECoreType.aether;
                var proc = await RunProcess(coreInfo, Global.CorePreConfigFileName, displayLog, true, preContext.IsTunEnabled, preContext.Node);
                if (proc is null)
                {
                    return;
                }
                _processPreService = proc;
            }
        }
    }

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }

    private async Task<ProcessService?> StartCustomChildCore(ProfileItem node, string configFileName, string? upstreamProxy = null)
    {
        var coreType = AppManager.Instance.GetCoreType(node, node.ConfigType);
        var fullConfigPath = Utils.GetBinConfigPath(configFileName);
        RetResult result;
        if (node.CoreType == ECoreType.psiphon)
        {
            result = await CoreConfigHandler.GenerateClientPsiphonConfig(node, fullConfigPath, upstreamProxy);
        }
        else if (node.CoreType == ECoreType.aether)
        {
            result = await CoreConfigHandler.GenerateClientAetherConfig(node, fullConfigPath);
        }
        else
        {
            var (ctx, _) = await CoreConfigContextBuilder.Build(_config, node);
            result = await CoreConfigHandler.GenerateClientConfig(ctx, fullConfigPath);
        }

        if (!result.Success)
        {
            await UpdateFunc(false, result.Msg);
            return null;
        }

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        var displayLog = node.DisplayLog || node.CoreType is ECoreType.psiphon or ECoreType.aether;
        return await RunProcess(coreInfo, configFileName, displayLog, false, false, node, upstreamProxy);
    }

    private static async Task WaitForProxyPort(CoreConfigContext? preContext)
    {
        if (preContext is null)
        {
            return;
        }

        var port = preContext.Node.ConfigType == EConfigType.Custom
            ? (preContext.Node.PreSocksPort is > 0 and <= 65535 ? preContext.Node.PreSocksPort.Value : (preContext.Node.CoreType == ECoreType.aether ? 1819 : 1080))
            : preContext.Node.Port;
        await WaitForPort(port);
        if (preContext.Node.CoreType == ECoreType.psiphon)
        {
            await WaitForPsiphonTunnel(25);
        }
    }

    private static async Task WaitForPort(int port)
    {
        if (port <= 0 || port > 65535)
        {
            return;
        }

        using var rootCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rootToken = rootCts.Token;

        // SOCKS5 client greeting: VER=5, NMETHODS=1, METHOD=0x00 (no auth)
        ReadOnlyMemory<byte> greeting = new byte[] { 0x05, 0x01, 0x00 };
        var buf = new byte[2];

        while (!rootToken.IsCancellationRequested)
        {
            using var tcp = new TcpClient();
            using var attemptCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rootToken, attemptCts.Token);
            var linkedToken = linkedCts.Token;
            try
            {
                await tcp.ConnectAsync(Global.Loopback, port, linkedToken);
                var stream = tcp.GetStream();

                await stream.WriteAsync(greeting, linkedToken);

                var read = await stream.ReadAsync(buf.AsMemory(0, 2), linkedToken);

                // Server selection: VER=5, METHOD=0x00 or any read response — proxy is ready
                if (read > 0)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                if (!rootToken.IsCancellationRequested)
                {
                    continue;
                }
                Logging.SaveLog($"WaitForPort Timeout waiting for port {port} to be ready.");
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                // Connection refused, proxy not ready yet, wait 50ms before retrying
                try
                {
                    await Task.Delay(50, rootToken);
                }
                catch (OperationCanceledException)
                {
                    Logging.SaveLog($"WaitForPort Timeout waiting for port {port} to be ready.");
                    return;
                }
            }
            catch
            {
                // Ignore other exceptions and continue
            }
        }
    }

    #endregion Private

    #region Process

    /// <summary>
    ///     Decides whether a core launch must be elevated on non-Windows platforms.
    ///     The TUN state comes from the immutable <see cref="CoreConfigContext" /> snapshot that
    ///     generated the config, never from the live mutable config: the generated config and the
    ///     launch mode must always agree, even if TUN is toggled while a reload is in flight.
    /// </summary>
    public static bool ShouldRunAsSudo(bool isTunLaunch, ECoreType? coreType, bool isNonWindows)
    {
        return isTunLaunch
            && coreType is ECoreType.sing_box or ECoreType.mihomo or ECoreType.Xray
            && isNonWindows;
    }

    private async Task<ProcessService?> RunProcess(CoreInfo? coreInfo, string configPath, bool displayLog, bool mayNeedSudo, bool isTunLaunch = false, ProfileItem? node = null, string? upstreamProxy = null)
    {
        var fileName = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var msg);
        if (fileName.IsNullOrEmpty())
        {
            await UpdateFunc(false, msg);
            return null;
        }

        try
        {
            if (mayNeedSudo
                && ShouldRunAsSudo(isTunLaunch, coreInfo.CoreType, Utils.IsNonWindows()))
            {
                _linuxSudo = true;
                await CoreAdminManager.Instance.Init(_config, _updateFunc);
                return await CoreAdminManager.Instance.RunProcessAsLinuxSudo(fileName, coreInfo, configPath);
            }

            return await RunProcessNormal(fileName, coreInfo, configPath, displayLog, node, upstreamProxy);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(mayNeedSudo, ex.Message);
            return null;
        }
    }

    private async Task<ProcessService?> RunProcessNormal(string fileName, CoreInfo? coreInfo, string configPath, bool displayLog, ProfileItem? node = null, string? upstreamProxy = null)
    {
        var environmentVars = new Dictionary<string, string>
        {
            ["GOMEMLIMIT"] = "64MiB",
            ["GOGC"] = "30"
        };
        foreach (var kv in coreInfo.Environment)
        {
            environmentVars[kv.Key] = string.Format(kv.Value, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath);
        }

        var arguments = string.Format(coreInfo.Arguments, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath);

        if (coreInfo.CoreType == ECoreType.aether && node != null)
        {
            var extra = node.GetProtocolExtra();
            var argsList = new List<string>();

            switch (extra?.AetherProtocol)
            {
                case "masque-h2":
                    argsList.Add("--masque --h2");
                    break;
                case "mim":
                    argsList.Add("--mim");
                    break;
                case "wg":
                    argsList.Add("--wg");
                    break;
                case "gool":
                    argsList.Add("--gool");
                    break;
                case "masque":
                default:
                    argsList.Add("--masque");
                    break;
            }

            if (extra?.AetherScan.IsNullOrEmpty() == false)
            {
                argsList.Add($"--scan {extra.AetherScan}");
            }
            if (extra?.AetherNoize.IsNullOrEmpty() == false)
            {
                argsList.Add($"--noize {extra.AetherNoize}");
            }
            if (extra?.AetherPeer.IsNullOrEmpty() == false)
            {
                argsList.Add($"--peer {extra.AetherPeer}");
            }

            var socksPort = node.PreSocksPort is > 0 and <= 65535 ? node.PreSocksPort.Value : 1819;
            argsList.Add($"--bind 127.0.0.1:{socksPort}");

            if (upstreamProxy.IsNotEmpty())
            {
                argsList.Add($"--upstream {upstreamProxy}");
                environmentVars["ALL_PROXY"] = upstreamProxy;
                environmentVars["all_proxy"] = upstreamProxy;
                environmentVars["HTTP_PROXY"] = upstreamProxy;
                environmentVars["http_proxy"] = upstreamProxy;
                environmentVars["HTTPS_PROXY"] = upstreamProxy;
                environmentVars["https_proxy"] = upstreamProxy;
            }

            arguments = $"{arguments} {string.Join(" ", argsList)}";

            if (extra?.AetherProtocol.IsNullOrEmpty() == false)
            {
                environmentVars["AETHER_PROTOCOL"] = extra.AetherProtocol;
            }
            if (extra?.AetherScan.IsNullOrEmpty() == false)
            {
                environmentVars["AETHER_SCAN"] = extra.AetherScan;
            }
            if (extra?.AetherNoize.IsNullOrEmpty() == false)
            {
                environmentVars["AETHER_NOIZE"] = extra.AetherNoize;
            }
            if (extra?.AetherPeer.IsNullOrEmpty() == false)
            {
                environmentVars["AETHER_PEER"] = extra.AetherPeer;
            }
            environmentVars["AETHER_BIND"] = $"127.0.0.1:{socksPort}";
            environmentVars["AETHER_SOCKS"] = socksPort.ToString();
        }

        var updateFuncToUse = _updateFunc;
        if (coreInfo.CoreType == ECoreType.psiphon)
        {
            var dirSuffix = configPath.IsNotEmpty() && Path.GetFileNameWithoutExtension(configPath) != "config"
                ? $"_{Path.GetFileNameWithoutExtension(configPath)}"
                : "";
            var dataDir = Path.Combine(Utils.GetBinConfigPath(), $"psiphon_data{dirSuffix}");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            var absConfig = Utils.GetBinConfigPath(configPath);
            arguments = $"-config {absConfig.AppendQuotes()} -dataRootDirectory {dataDir.AppendQuotes()} -formatNotices";
            var serverListPath = CoreConfigHandler.EnsurePsiphonServerList(dataDir);
            if (serverListPath.IsNotEmpty() && File.Exists(serverListPath))
            {
                arguments += $" -serverList {serverListPath.AppendQuotes()}";
            }

            _psiphonIsConnected = false;
            UpdatePsiphonStatus(ResUI.PsiphonConnecting, isConnected: false);
            updateFuncToUse = async (notify, rawMsg) =>
            {
                await HandlePsiphonOutput(rawMsg, _updateFunc);
            };
        }

        var procService = new ProcessService(
            fileName: fileName,
            arguments: arguments,
            workingDirectory: Utils.GetBinConfigPath(),
            displayLog: displayLog,
            redirectInput: false,
            environmentVars: environmentVars,
            updateFunc: updateFuncToUse
        );

        await procService.StartAsync();

        await Task.Delay(100);

        if (procService is null or { HasExited: true })
        {
            throw new Exception(ResUI.FailedToRunCore);
        }
        AddProcessJob(procService.Handle);

        return procService;
    }

    private void AddProcessJob(nint processHandle)
    {
        if (Utils.IsWindows())
        {
            _processJob ??= new();
            try
            {
                _processJob?.AddProcess(processHandle);
            }
            catch { }
        }
    }

    private static bool _psiphonIsConnected = false;

    public static async Task WaitForPsiphonTunnel(int timeoutSeconds = 25)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (!cts.IsCancellationRequested && !_psiphonIsConnected)
        {
            try
            {
                await Task.Delay(200, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task HandlePsiphonOutput(string rawMsg, Func<bool, string, Task>? baseUpdateFunc)
    {
        if (rawMsg.IsNullOrEmpty())
        {
            return;
        }

        var trimmed = rawMsg.Trim();
        string? noticeType = null;
        JsonElement dataElem = default;
        bool hasData = false;

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("noticeType", out var nt))
                {
                    noticeType = nt.GetString();
                    if (root.TryGetProperty("data", out var d))
                    {
                        dataElem = d.Clone();
                        hasData = true;
                    }
                }
            }
            catch { }
        }
        else
        {
            // Formatted notices: "2026-09-24T12:50:15.277Z Tunnels {"count":1}"
            var firstSpace = trimmed.IndexOf(' ');
            if (firstSpace > 0)
            {
                var secondSpace = trimmed.IndexOf(' ', firstSpace + 1);
                if (secondSpace > 0)
                {
                    noticeType = trimmed.Substring(firstSpace + 1, secondSpace - firstSpace - 1);
                    var jsonPart = trimmed.Substring(secondSpace + 1).Trim();
                    if (jsonPart.StartsWith('{') && jsonPart.EndsWith('}'))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(jsonPart);
                            dataElem = doc.RootElement.Clone();
                            hasData = true;
                        }
                        catch { }
                    }
                }
            }
        }

        if (noticeType.IsNotEmpty())
        {
            var timeStr = DateTime.Now.ToString("HH:mm:ss");
            switch (noticeType)
            {
                case "ListeningSocksProxyPort":
                    {
                        var port = hasData && dataElem.TryGetProperty("port", out var p) ? p.ToString() : "1080";
                        var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] SOCKS proxy listening on 127.0.0.1:{port}" + Environment.NewLine;
                        if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        return;
                    }

                case "CandidateServers":
                    {
                        var count = hasData && dataElem.TryGetProperty("count", out var c) ? c.ToString() : "0";
                        var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] Found {count} candidate servers. Connecting..." + Environment.NewLine;
                        UpdatePsiphonStatus(ResUI.PsiphonConnecting, isConnected: false);
                        if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        return;
                    }

                case "ConnectingServer":
                    {
                        var region = hasData && (dataElem.TryGetProperty("region", out var r) || dataElem.TryGetProperty("egressRegion", out r)) ? r.GetString() : null;
                        var proto = hasData && dataElem.TryGetProperty("protocol", out var p) ? p.GetString() : null;
                        var detail = region.IsNotEmpty() ? $" ({region} / {proto})" : "";
                        var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] Connecting to server{detail}..." + Environment.NewLine;
                        UpdatePsiphonStatus(ResUI.PsiphonConnecting, isConnected: false);
                        if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        return;
                    }

                case "ConnectedServer":
                case "ConnectedServerRegion":
                    {
                        var region = hasData && (dataElem.TryGetProperty("serverRegion", out var r) || dataElem.TryGetProperty("egressRegion", out r) || dataElem.TryGetProperty("region", out r)) ? r.GetString() : null;
                        var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] Connected to server" + (region.IsNotEmpty() ? $" in {region}" : "") + ". Establishing tunnel..." + Environment.NewLine;
                        if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        return;
                    }

                case "ActiveTunnel":
                    {
                        var proto = hasData && dataElem.TryGetProperty("protocol", out var p) ? p.GetString() : null;
                        var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] Active tunnel established ({proto})." + Environment.NewLine;
                        if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        return;
                    }

                case "Tunnels":
                    {
                        var count = hasData && dataElem.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                        if (count > 0)
                        {
                            var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] Connected! Active tunnels: {count}" + Environment.NewLine;
                            UpdatePsiphonStatus(string.Format(ResUI.PsiphonConnected, count), isConnected: true);
                            if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        }
                        else
                        {
                            var logLine = $"[{timeStr}] [Psiphon Shirokhorshid] Disconnected (0 active tunnels). Reconnecting..." + Environment.NewLine;
                            UpdatePsiphonStatus(ResUI.PsiphonDisconnected, isConnected: false);
                            if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                        }
                        return;
                    }

                case "Alert":
                    {
                        var msg = hasData && dataElem.TryGetProperty("message", out var m) ? m.GetString() : null;
                        if (msg.IsNotEmpty())
                        {
                            var logLine = $"[{timeStr}] [Psiphon Shirokhorshid Alert] {msg}" + Environment.NewLine;
                            if (baseUpdateFunc != null) await baseUpdateFunc(false, logLine);
                            return;
                        }
                        break;
                    }

                case "BytesTransferred":
                case "ServerTimestamp":
                case "ActiveAuthorizationIDs":
                case "TrafficRateLimits":
                case "ClientUpgradeAvailable":
                case "Homepage":
                case "SessionId":
                case "NetworkID":
                case "RequestingTactics":
                case "RequestedTactics":
                case "ApplicationParameters":
                    return;
            }
        }

        if (baseUpdateFunc != null)
        {
            await baseUpdateFunc(false, rawMsg);
        }
    }

    private static void UpdatePsiphonStatus(string status, bool isConnected)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            StatusBarViewModel.Instance.RunningInfoDisplay = status;
            var serverDisplay = StatusBarViewModel.Instance.RunningServerDisplay;
            if (serverDisplay.IsNotEmpty() && !Utils.IsLinux())
            {
                StatusBarViewModel.Instance.RunningServerToolTipText = $"{serverDisplay} ({status})";
            }
        });

        if (isConnected && !_psiphonIsConnected)
        {
            _psiphonIsConnected = true;
            NoticeManager.Instance.Enqueue(status);
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                await Task.Delay(1500);
                await StatusBarViewModel.Instance.TestServerAvailability();
            });
        }
        else if (!isConnected)
        {
            _psiphonIsConnected = false;
        }
    }

    public static void TrimAllProcessesMemory()
    {
        try
        {
            Utils.TrimMemory();

            if (Instance._processService is { HasExited: false } proc)
            {
                Utils.TrimProcessMemory(proc.Handle);
            }

            foreach (var extra in Instance._extraProcessServices)
            {
                if (extra is { HasExited: false })
                {
                    Utils.TrimProcessMemory(extra.Handle);
                }
            }
        }
        catch { }
    }

    #endregion Process
}
