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
                Dim releaseNotes = ""
                Dim bodyElement As JsonElement
                If root.TryGetProperty("body", bodyElement) AndAlso bodyElement.ValueKind = JsonValueKind.String Then
                    releaseNotes = bodyElement.GetString()
                End If
                Dim zipUrl = ""
                Dim assetsElement As JsonElement
                If root.TryGetProperty("assets", assetsElement) Then
                    Dim assets = assetsElement.EnumerateArray().
                        Where(Function(asset) asset.GetProperty("name").GetString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).
                        OrderByDescending(Function(asset) GetUpdateAssetScore(asset.GetProperty("name").GetString())).
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
                    .ReleaseNotes = releaseNotes,
                    .ZipDownloadUrl = zipUrl
                }
            End Using
        End Using
    End Function

    Public Async Function PrepareAndLaunchUpdateAsync(update As AppUpdateInfo, progress As IProgress(Of ToolDownloadProgress)) As Task
        If update Is Nothing OrElse String.IsNullOrWhiteSpace(update.ZipDownloadUrl) Then
            Throw New InvalidOperationException("The latest release does not contain a ZIP update package.")
        End If

        Dim updateRoot = GetUpdateRoot()
        Dim archivePath = Path.Combine(updateRoot, "FH6Tools-update.zip")
        Dim stagingPath = Path.Combine(updateRoot, "staging")
        Try
            CleanupUpdateArtifacts()
            Directory.CreateDirectory(updateRoot)
            Await Network.DownloadFileAsync(update.ZipDownloadUrl, archivePath, "", progress, Threading.CancellationToken.None)
            Directory.CreateDirectory(stagingPath)
            ZipFile.ExtractToDirectory(archivePath, stagingPath)

            Dim executable = Directory.EnumerateFiles(stagingPath, "FH6Tools.exe", SearchOption.AllDirectories).
                OrderBy(Function(path) path.Length).
                FirstOrDefault()
            If String.IsNullOrWhiteSpace(executable) Then Throw New InvalidDataException("The update package does not contain FH6Tools.exe.")

            Dim sourceRoot = Path.GetDirectoryName(executable)
            Dim targetRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            Dim scriptPath = Path.Combine(Path.GetTempPath(), $"FH6Tools-apply-update-{Guid.NewGuid():N}.ps1")
            Dim script =
                "$ErrorActionPreference = 'Stop'" & vbCrLf &
                $"$source = '{EscapePowerShell(sourceRoot)}'" & vbCrLf &
                $"$target = '{EscapePowerShell(targetRoot)}'" & vbCrLf &
                $"$updateRoot = '{EscapePowerShell(updateRoot)}'" & vbCrLf &
                $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue" & vbCrLf &
                "Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -ne 'FH6ToolsData' } | ForEach-Object {" & vbCrLf &
                "  Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force" & vbCrLf &
                "}" & vbCrLf &
                "Remove-Item -LiteralPath $updateRoot -Recurse -Force -ErrorAction SilentlyContinue" & vbCrLf &
                "Start-Process -FilePath (Join-Path $target 'FH6Tools.exe')" & vbCrLf &
                "Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue" & vbCrLf
            Await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8)

            Dim info As New ProcessStartInfo With {.FileName = "powershell.exe", .UseShellExecute = False, .CreateNoWindow = True}
            info.ArgumentList.Add("-NoProfile")
            info.ArgumentList.Add("-ExecutionPolicy")
            info.ArgumentList.Add("Bypass")
            info.ArgumentList.Add("-File")
            info.ArgumentList.Add(scriptPath)
            If Process.Start(info) Is Nothing Then Throw New InvalidOperationException("The update helper could not be started.")
        Catch
            CleanupUpdateArtifacts()
            Throw
        End Try
    End Function

    Public Shared Sub CleanupUpdateArtifacts()
        Dim updateRoot = GetUpdateRoot()
        If Not Directory.Exists(updateRoot) Then Return
        For attempt = 1 To 5
            Try
                Directory.Delete(updateRoot, True)
                Return
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                If attempt = 5 Then
                    Debug.WriteLine($"FH6Tools update cleanup failed: {ex}")
                    Return
                End If
                Threading.Thread.Sleep(100 * attempt)
            End Try
        Next
    End Sub

    Private Shared Function IsNewer(tag As String, current As Version) As Boolean
        Dim normalized = If(tag, "").Trim().TrimStart("v"c).Split("-"c)(0)
        Dim latest As Version = Nothing
        If Not Version.TryParse(normalized, latest) OrElse current Is Nothing Then
            Return Not String.Equals(normalized, current?.ToString(3), StringComparison.OrdinalIgnoreCase)
        End If
        Return latest > current
    End Function

    Private Shared Function GetUpdateAssetScore(name As String) As Integer
        Dim lower = If(name, "").ToLowerInvariant()
        Dim score = If(lower.EndsWith(".zip"), 10, 0)
        If lower.Contains("win-x64") Then score += 100
        If lower.Contains("-update.") OrElse lower.Contains("-update-") OrElse lower.Contains("update-only") Then score += 1000
        If lower.Contains("with-runtime") Then score += 10
        Return score
    End Function

    Private Shared Function EscapePowerShell(value As String) As String
        Return If(value, "").Replace("'", "''")
    End Function

    Private Shared Function GetUpdateRoot() As String
        Return Path.Combine(FhPaths.AppDataRoot, "app-update")
    End Function

    Public Shared Function FormatReleaseNotes(value As String, Optional maximumLength As Integer = 4000) As String
        Dim notes = If(value, "").ReplaceLineEndings(vbLf).Trim()
        If String.IsNullOrWhiteSpace(notes) Then Return FhLanguage.Text("该版本未提供更新说明。", "No release notes were provided for this version.")
        notes = notes.Replace(vbLf, vbCrLf)
        If notes.Length <= maximumLength Then Return notes
        Return notes.Substring(0, Math.Max(0, maximumLength)).TrimEnd() & vbCrLf & FhLanguage.Text("……内容过长，请前往发布页面查看完整说明。", "...Content truncated. Open the release page for the full notes.")
    End Function
End Class

Public Class AppUpdateInfo
    Public Property ReleaseAvailable As Boolean
    Public Property UpdateAvailable As Boolean
    Public Property CurrentVersion As String = ""
    Public Property LatestVersion As String = ""
    Public Property ReleaseUrl As String = ""
    Public Property ReleaseNotes As String = ""
    Public Property ZipDownloadUrl As String = ""
End Class
