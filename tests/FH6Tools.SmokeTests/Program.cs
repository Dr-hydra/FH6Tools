using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FH6Tools;

var repoRoot = FindRepoRoot();
var root = Path.Combine(repoRoot, "artifacts", "smoke-data", "FH6ToolsSmoke-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("FH6TOOLS_APPDATA_ROOT", root);
Environment.SetEnvironmentVariable("FH6TOOLS_SKIP_INSTALLER_LAUNCH", "1");
var reportPath = Path.Combine(repoRoot, "artifacts", "smoke-report.txt");
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, $"FH6Tools smoke test started: {DateTimeOffset.Now:O}{Environment.NewLine}");

var tests = new List<(string Name, Func<Task> Run)>
{
    ("language-default-persistence", TestLanguagePersistence),
    ("manifest-local-tools", TestManifestAndLocalTools),
    ("download-resume-sha-cancel", TestDownloadResumeShaAndCancel),
    ("zip-exe-msi-install-config-snapshot", TestZipInstallAndConfigSnapshots),
    ("runtime-single-backend-frontend-port", TestRuntimeAndPorts),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
        File.AppendAllText(reportPath, $"PASS {test.Name}{Environment.NewLine}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}: {ex}");
        File.AppendAllText(reportPath, $"FAIL {test.Name}: {ex}{Environment.NewLine}");
    }
}

try
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
catch
{
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke failures:");
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    File.AppendAllText(reportPath, $"FAILED {failures.Count} test(s){Environment.NewLine}");
    return 1;
}

File.AppendAllText(reportPath, $"PASSED {tests.Count} test(s){Environment.NewLine}");
return 0;

Task TestLanguagePersistence()
{
    FhLanguage.SetLanguage("en-US");
    Require(FhLanguage.IsEnglish, "English language selection was not applied");
    FhLanguage.Load();
    Require(FhLanguage.IsEnglish, "English language selection was not persisted");
    FhLanguage.SetLanguage("zh-CN");
    Require(!FhLanguage.IsEnglish, "Simplified Chinese language selection was not restored");
    return Task.CompletedTask;
}

async Task TestManifestAndLocalTools()
{
    FhPaths.Ensure();
    var localExe = Path.Combine(root, "local-tool.exe");
    File.WriteAllText(localExe, "placeholder");

    var manifestJson = """
    {
      "schemaVersion": 1,
      "updatedAt": "2026-06-04T00:00:00Z",
      "tools": [
        {
          "id": "curated-single",
          "name": "Curated Single",
          "version": "1.0.0",
          "installType": "local",
          "toolType": "single",
          "single": { "executable": "%SystemRoot%\\System32\\cmd.exe", "arguments": ["/c", "exit", "0"] },
          "configFiles": [{ "name": "Main", "path": "config\\main.json" }],
          "riskLevel": "normal"
        }
      ]
    }
    """;
    await File.WriteAllTextAsync(FhPaths.ManifestPath, manifestJson);

    var service = new ToolManifestService();
    await service.AddLocalToolAsync(localExe);
    var tools = await service.LoadToolsAsync();

    Require(tools.Any(t => t.Id == "curated-single"), "curated manifest tool missing");
    Require(tools.Any(t => t.Id.StartsWith("local-tool", StringComparison.OrdinalIgnoreCase) || t.Id == "local-local-tool"), "local tool missing");
}

async Task TestDownloadResumeShaAndCancel()
{
    var bytes = Encoding.UTF8.GetBytes(new string('x', 128_000));
    var port = GetFreePort();
    using var server = new RangeServer(port, bytes, slow: false);
    server.Start();

    var target = Path.Combine(FhPaths.DownloadRoot, "payload.bin");
    Directory.CreateDirectory(FhPaths.DownloadRoot);
    await File.WriteAllBytesAsync(target + ".part", bytes[..4096]);
    var expected = Sha256(bytes);

    var net = new FhNet();
    await net.DownloadFileAsync($"http://127.0.0.1:{port}/payload.bin", target, expected, new Progress<ToolDownloadProgress>(), CancellationToken.None);
    Require(File.ReadAllBytes(target).SequenceEqual(bytes), "resume download did not reconstruct payload");

    await ExpectThrows<InvalidDataException>(async () =>
        await net.DownloadFileAsync($"http://127.0.0.1:{port}/payload.bin", Path.Combine(FhPaths.DownloadRoot, "bad.bin"), "00", new Progress<ToolDownloadProgress>(), CancellationToken.None));

    var slowPort = GetFreePort();
    using var slowServer = new RangeServer(slowPort, bytes, slow: true);
    slowServer.Start();
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(100);
    await ExpectThrows<OperationCanceledException>(async () =>
        await net.DownloadFileAsync($"http://127.0.0.1:{slowPort}/payload.bin", Path.Combine(FhPaths.DownloadRoot, "cancel.bin"), "", new Progress<ToolDownloadProgress>(), cts.Token));
}

async Task TestZipInstallAndConfigSnapshots()
{
    var zipPath = Path.Combine(root, "tool.zip");
    var zipSource = Path.Combine(root, "zip-source");
    Directory.CreateDirectory(zipSource);
    await File.WriteAllTextAsync(Path.Combine(zipSource, "tool.exe"), "fake exe");
    ZipFile.CreateFromDirectory(zipSource, zipPath);
    var zipBytes = await File.ReadAllBytesAsync(zipPath);

    var port = GetFreePort();
    using var server = new RangeServer(port, zipBytes, slow: false);
    server.Start();

    var tool = new ToolManifestEntry
    {
        Id = "zip-tool",
        Name = "Zip Tool",
        Version = "1.0.0",
        InstallType = "zip",
        DownloadUrl = $"http://127.0.0.1:{port}/tool.zip",
        Sha256 = Sha256(zipBytes),
        ToolType = "single",
        Single = new ToolEndpointDefinition { Executable = "tool.exe" },
        ConfigFiles = new List<ToolConfigFileEntry> { new ToolConfigFileEntry { Name = "Main", Path = "config\\main.json" } }
    };

    var installer = new ToolInstallService();
    var installPath = await installer.DownloadAndInstallAsync(tool, new Progress<ToolDownloadProgress>(), CancellationToken.None);
    Require(File.Exists(Path.Combine(installPath, "tool.exe")), "zip install did not extract tool.exe");
    Require(installer.IsInstalled(tool), "zip tool should be installed");

    var configService = new ConfigSnapshotService();
    var configPath = configService.ResolveConfigPath(tool, tool.ConfigFiles[0]);
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    await File.WriteAllTextAsync(configPath, "{\"value\":1}");
    var snapshot = configService.Backup(tool, tool.ConfigFiles[0]);
    await File.WriteAllTextAsync(configPath, "{\"value\":2}");
    configService.Restore(snapshot, configPath);
    Require(await File.ReadAllTextAsync(configPath) == "{\"value\":1}", "config restore did not restore original content");
    Require(configService.ListSnapshots(tool).Count > 0, "snapshot list is empty");

    await TestInstallerDownloadExtension(installer, "exe");
    await TestInstallerDownloadExtension(installer, "msi");
}

async Task TestRuntimeAndPorts()
{
    var runtime = new ToolRuntimeService();

    var singleTool = new ToolManifestEntry
    {
        Id = "single-runtime",
        Name = "Single Runtime",
        InstallType = "local",
        ToolType = "single",
        Single = PowerShellEndpoint("-NoProfile", "-Command", "Start-Sleep -Seconds 3")
    };
    await runtime.StartAllAsync(singleTool);
    var singleStatus = await runtime.GetStatusAsync(singleTool);
    Require(singleStatus.FrontendProcessAlive, "single process is not alive");
    runtime.StopTool(singleTool);

    var environmentPath = Path.Combine(root, "runtime-environment.txt");
    var escapedEnvironmentPath = environmentPath.Replace("'", "''");
    var environmentTool = new ToolManifestEntry
    {
        Id = "shared-runtime-environment",
        Name = "Shared Runtime Environment",
        InstallType = "local",
        ToolType = "single",
        Single = PowerShellEndpoint(
            "-NoProfile",
            "-Command",
            $"Set-Content -LiteralPath '{escapedEnvironmentPath}' -Value ($env:DOTNET_ROOT_X64 + '|' + $env:DOTNET_MULTILEVEL_LOOKUP + '|' + $env:PATH)")
    };
    await runtime.StartAllAsync(environmentTool);
    for (var i = 0; i < 20 && !File.Exists(environmentPath); i++) await Task.Delay(100);
    Require(File.Exists(environmentPath), "shared runtime environment was not captured");
    var runtimeEnvironment = await File.ReadAllTextAsync(environmentPath);
    Require(runtimeEnvironment.StartsWith(FhPaths.SharedDotNetRoot + "|0|" + FhPaths.SharedDotNetRoot, StringComparison.OrdinalIgnoreCase),
        "tool process did not receive the shared runtime environment");
    runtime.StopTool(environmentTool);

    var backendPort = GetFreePort();
    var backendTool = new ToolManifestEntry
    {
        Id = "backend-runtime",
        Name = "Backend Runtime",
        InstallType = "local",
        ToolType = "backendOnly",
        Backend = PowerShellTcpHttpEndpoint(backendPort)
    };
    await runtime.StartBackendAsync(backendTool);
    var backendStatus = await WaitForHealthy(runtime, backendTool);
    Require(backendStatus.HealthOk && backendStatus.PortListening, $"backend health or port status failed: {Describe(backendStatus)}");
    runtime.StopTool(backendTool);

    var healthFailurePort = GetFreePort();
    var healthFailureTool = new ToolManifestEntry
    {
        Id = "health-failure-runtime",
        Name = "Health Failure Runtime",
        InstallType = "local",
        ToolType = "backendOnly",
        Backend = PowerShellEndpoint("-NoProfile", "-Command", "Start-Sleep -Seconds 3")
    };
    healthFailureTool.Backend.DefaultPort = healthFailurePort;
    healthFailureTool.Backend.HealthUrl = $"http://127.0.0.1:{healthFailurePort}/";
    await runtime.StartBackendAsync(healthFailureTool);
    var healthFailureStatus = await runtime.GetStatusAsync(healthFailureTool);
    Require(healthFailureStatus.BackendProcessAlive && !healthFailureStatus.HealthOk, $"health failure status did not surface correctly: {Describe(healthFailureStatus)}");
    runtime.StopTool(healthFailureTool);

    var frontendPort = GetFreePort();
    var frontendTool = new ToolManifestEntry
    {
        Id = "frontend-backend-runtime",
        Name = "Frontend Backend Runtime",
        InstallType = "local",
        ToolType = "frontendBackend",
        Backend = PowerShellTcpHttpEndpoint(frontendPort),
        Frontend = PowerShellEndpoint("-NoProfile", "-Command", "Start-Sleep -Seconds 2")
    };
    await runtime.StartAllAsync(frontendTool);
    var frontendStatus = await WaitForHealthy(runtime, frontendTool);
    Require(frontendStatus.HealthOk, $"frontendBackend did not report healthy backend: {Describe(frontendStatus)}");
    runtime.StopTool(frontendTool);

    var conflictPort = GetFreePort();
    using var listener = new TcpListener(IPAddress.Loopback, conflictPort);
    listener.Start();
    var conflictTool = new ToolManifestEntry
    {
        Id = "conflict-runtime",
        Name = "Conflict Runtime",
        InstallType = "local",
        ToolType = "backendOnly",
        Backend = PowerShellTcpHttpEndpoint(conflictPort)
    };
    await ExpectThrows<ToolPortConflictException>(async () => await runtime.StartBackendAsync(conflictTool));
    await runtime.StartBackendAsync(conflictTool, reuseExistingService: true);
}

static ToolEndpointDefinition PowerShellEndpoint(params string[] args)
{
    return new ToolEndpointDefinition
    {
        Executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        Arguments = args.ToList()
    };
}

static async Task TestInstallerDownloadExtension(ToolInstallService installer, string installType)
{
    var bytes = Encoding.UTF8.GetBytes($"fake {installType} installer");
    var port = GetFreePort();
    using var server = new RangeServer(port, bytes, slow: false);
    server.Start();
    var tool = new ToolManifestEntry
    {
        Id = $"{installType}-installer-tool",
        Name = $"{installType.ToUpperInvariant()} Installer Tool",
        Version = "1.0.0",
        InstallType = installType,
        DownloadUrl = $"http://127.0.0.1:{port}/installer.{installType}",
        Sha256 = Sha256(bytes),
        ToolType = "single"
    };
    await installer.DownloadAndInstallAsync(tool, new Progress<ToolDownloadProgress>(), CancellationToken.None);
    Require(File.Exists(Path.Combine(FhPaths.DownloadRoot, $"{installType}-installer-tool-1.0.0.{installType}")), $"{installType} installer was not downloaded with .{installType} extension");
}

static ToolEndpointDefinition PowerShellTcpHttpEndpoint(int port)
{
    var command = "$listener=[Net.HttpListener]::new();" +
                  $"$listener.Prefixes.Add('http://127.0.0.1:{port}/');" +
                  "$listener.Start();" +
                  "while($listener.IsListening){" +
                  "$ctx=$listener.GetContext();" +
                  "$bytes=[Text.Encoding]::UTF8.GetBytes('ok');" +
                  "$ctx.Response.ContentLength64=$bytes.Length;" +
                  "$ctx.Response.OutputStream.Write($bytes,0,$bytes.Length);" +
                  "$ctx.Response.Close()}";
    var endpoint = PowerShellEndpoint("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command);
    endpoint.DefaultPort = port;
    endpoint.HealthUrl = $"http://127.0.0.1:{port}/";
    return endpoint;
}

static string Describe(ToolRuntimeStatus status)
{
    return $"installed={status.IsInstalled}, backendProcess={status.BackendProcessAlive}, frontendProcess={status.FrontendProcessAlive}, port={status.Port}, listening={status.PortListening}, health={status.HealthOk}, message={status.Message}";
}

static async Task<ToolRuntimeStatus> WaitForHealthy(ToolRuntimeService runtime, ToolManifestEntry tool)
{
    for (var i = 0; i < 30; i++)
    {
        var status = await runtime.GetStatusAsync(tool);
        if (status.HealthOk) return status;
        await Task.Delay(200);
    }
    return await runtime.GetStatusAsync(tool);
}

static int GetFreePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static string Sha256(byte[] bytes)
{
    return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

static async Task ExpectThrows<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "agent.md"))) return current.FullName;
        current = current.Parent;
    }
    return Directory.GetCurrentDirectory();
}

sealed class RangeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly byte[] _bytes;
    private readonly bool _slow;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public RangeServer(int port, byte[] bytes, bool slow)
    {
        _bytes = bytes;
        _slow = slow;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var start = 0;
                    var range = context.Request.Headers["Range"];
                    if (!string.IsNullOrWhiteSpace(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        var first = range["bytes=".Length..].Split('-')[0];
                        int.TryParse(first, out start);
                        context.Response.StatusCode = 206;
                    }
                    var length = _bytes.Length - start;
                    context.Response.ContentLength64 = length;
                    context.Response.AddHeader("Accept-Ranges", "bytes");
                    if (start > 0) context.Response.AddHeader("Content-Range", $"bytes {start}-{_bytes.Length - 1}/{_bytes.Length}");

                    var offset = start;
                    while (offset < _bytes.Length)
                    {
                        var count = Math.Min(4096, _bytes.Length - offset);
                        await context.Response.OutputStream.WriteAsync(_bytes.AsMemory(offset, count), token);
                        offset += count;
                        if (_slow) await Task.Delay(20, token);
                    }
                }
                catch
                {
                }
                finally
                {
                    try { context.Response.Close(); } catch { }
                }
            }, token);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _loop?.Wait(500); } catch { }
        _cts?.Dispose();
    }
}
