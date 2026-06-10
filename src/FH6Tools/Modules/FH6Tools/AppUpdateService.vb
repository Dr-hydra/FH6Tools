Imports System.IO.Compression
Imports System.Text.Json

Public Class AppUpdateService
    Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(15)}
    Private ReadOnly Network As New FhNet

    Shared Sub New()
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("FH6Tools")
    End Sub

    Public Async Function CheckAsync() As Task(Of AppUpdateInfo)
        Using response = Await Client.GetAsync("https://api.github.com/repos/Dr-hydra/FH6Tools/releases/latest")
            If response.StatusCode = Net.HttpStatusCode.NotFound Then Return New AppUpdateInfo With {.ReleaseAvailable = False}
            response.EnsureSuccessStatusCode()
            Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                Dim root = document.RootElement
                Dim tag = root.GetProperty("tag_name").GetString()
                Dim releaseUrl = root.GetProperty("html_url").GetString()
                Dim zipUrl = ""
                Dim assetsElement As JsonElement
                If root.TryGetProperty("assets", assetsElement) Then
                    Dim assets = assetsElement.EnumerateArray().
                        Where(Function(asset) asset.GetProperty("name").GetString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).
                        OrderByDescending(Function(asset)
                                              Dim name = asset.GetProperty("name").GetString().ToLowerInvariant()
                                              Return If(name.Contains("win-x64"), 100, 0) + If(name.Contains("runtime"), 10, 0)
                                          End Function).
                        ToList()
                    If assets.Count > 0 Then zipUrl = assets(0).GetProperty("browser_download_url").GetString()
                End If
                Dim current = Reflection.Assembly.GetExecutingAssembly().GetName().Version
                Return New AppUpdateInfo With {
                    .ReleaseAvailable = True,
                    .UpdateAvailable = IsNewer(tag, current),
                    .CurrentVersion = current?.ToString(3),
                    .LatestVersion = tag,
                    .ReleaseUrl = releaseUrl,
                    .ZipDownloadUrl = zipUrl
                }
            End Using
        End Using
    End Function

    Public Async Function PrepareAndLaunchUpdateAsync(update As AppUpdateInfo, progress As IProgress(Of ToolDownloadProgress)) As Task
        If update Is Nothing OrElse String.IsNullOrWhiteSpace(update.ZipDownloadUrl) Then
            Throw New InvalidOperationException("The latest release does not contain a ZIP update package.")
        End If

        Dim updateRoot = Path.Combine(FhPaths.AppDataRoot, "app-update")
        Dim archivePath = Path.Combine(updateRoot, "FH6Tools-update.zip")
        Dim stagingPath = Path.Combine(updateRoot, "staging")
        Directory.CreateDirectory(updateRoot)
        Await Network.DownloadFileAsync(update.ZipDownloadUrl, archivePath, "", progress, Threading.CancellationToken.None)
        If Directory.Exists(stagingPath) Then Directory.Delete(stagingPath, True)
        Directory.CreateDirectory(stagingPath)
        ZipFile.ExtractToDirectory(archivePath, stagingPath)

        Dim executable = Directory.EnumerateFiles(stagingPath, "FH6Tools.exe", SearchOption.AllDirectories).
            OrderBy(Function(path) path.Length).
            FirstOrDefault()
        If String.IsNullOrWhiteSpace(executable) Then Throw New InvalidDataException("The update package does not contain FH6Tools.exe.")

        Dim sourceRoot = Path.GetDirectoryName(executable)
        Dim targetRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
        Dim scriptPath = Path.Combine(updateRoot, "apply-update.ps1")
        Dim script =
            "$ErrorActionPreference = 'Stop'" & vbCrLf &
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue" & vbCrLf &
            $"$source = '{EscapePowerShell(sourceRoot)}'" & vbCrLf &
            $"$target = '{EscapePowerShell(targetRoot)}'" & vbCrLf &
            "Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -ne 'FH6ToolsData' } | ForEach-Object {" & vbCrLf &
            "  Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force" & vbCrLf &
            "}" & vbCrLf &
            $"Start-Process -FilePath (Join-Path $target 'FH6Tools.exe')" & vbCrLf
        Await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8)

        Dim info As New ProcessStartInfo With {.FileName = "powershell.exe", .UseShellExecute = False, .CreateNoWindow = True}
        info.ArgumentList.Add("-NoProfile")
        info.ArgumentList.Add("-ExecutionPolicy")
        info.ArgumentList.Add("Bypass")
        info.ArgumentList.Add("-File")
        info.ArgumentList.Add(scriptPath)
        Process.Start(info)
    End Function

    Private Shared Function IsNewer(tag As String, current As Version) As Boolean
        Dim normalized = If(tag, "").Trim().TrimStart("v"c).Split("-"c)(0)
        Dim latest As Version = Nothing
        If Not Version.TryParse(normalized, latest) OrElse current Is Nothing Then
            Return Not String.Equals(normalized, current?.ToString(3), StringComparison.OrdinalIgnoreCase)
        End If
        Return latest > current
    End Function

    Private Shared Function EscapePowerShell(value As String) As String
        Return If(value, "").Replace("'", "''")
    End Function
End Class

Public Class AppUpdateInfo
    Public Property ReleaseAvailable As Boolean
    Public Property UpdateAvailable As Boolean
    Public Property CurrentVersion As String = ""
    Public Property LatestVersion As String = ""
    Public Property ReleaseUrl As String = ""
    Public Property ZipDownloadUrl As String = ""
End Class
