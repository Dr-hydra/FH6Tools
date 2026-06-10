Imports System.IO.Compression
Imports System.Text.Json
Imports System.Text.RegularExpressions

Public Class ToolInstallService
    Private ReadOnly Network As New FhNet
    Private Shared ReadOnly ReleaseClient As New HttpClient

    Public Function GetInstallPath(tool As ToolManifestEntry) As String
        If tool.InstallType = "local" Then
            Dim endpoint = If(tool.[Single], tool.Backend)
            If endpoint IsNot Nothing Then Return FhPaths.ExpandPath(endpoint.Executable)
        End If
        Return Path.Combine(FhPaths.ToolsRoot, tool.Id)
    End Function

    Public Function IsInstalled(tool As ToolManifestEntry) As Boolean
        If IsBlocked(tool) Then Return False
        If tool.InstallType = "local" Then Return File.Exists(GetInstallPath(tool))
        Dim installPath = GetInstallPath(tool)
        If Not Directory.Exists(installPath) Then Return False
        If Not File.Exists(Path.Combine(installPath, ".fh6tools-version")) Then Return False
        Return Directory.EnumerateFileSystemEntries(installPath).
            Any(Function(entryPath) Not String.Equals(IO.Path.GetFileName(entryPath), ".fh6tools-version", StringComparison.OrdinalIgnoreCase))
    End Function

    Public Async Function DownloadAndInstallAsync(tool As ToolManifestEntry, progress As IProgress(Of Double), cancellationToken As Threading.CancellationToken) As Task(Of String)
        FhPaths.Ensure()
        If IsBlocked(tool) Then Throw New InvalidOperationException("This tool is blocked by the FH6Tools safety policy.")
        If String.Equals(tool.InstallType, "local", StringComparison.OrdinalIgnoreCase) Then Return GetInstallPath(tool)
        If String.IsNullOrWhiteSpace(tool.DownloadUrl) Then Throw New InvalidOperationException("Tool has no download URL.")
        Dim download = Await ResolveDownloadAsync(tool.DownloadUrl, cancellationToken)
        Dim downloadFileName = If(String.IsNullOrWhiteSpace(download.FileName),
                                  tool.Id & "-" & tool.Version & SelectDownloadExtension(tool.InstallType),
                                  tool.Id & "-" & tool.Version & "-" & download.FileName)
        Dim downloadPath = Path.Combine(FhPaths.DownloadRoot, SafeFileName(downloadFileName))
        Await Network.DownloadFileAsync(download.Url, downloadPath, tool.Sha256, progress, cancellationToken)

        Dim installPath = GetInstallPath(tool)
        Directory.CreateDirectory(installPath)
        Select Case tool.InstallType.ToLowerInvariant()
            Case "zip"
                If Directory.Exists(installPath) Then Directory.Delete(installPath, True)
                Directory.CreateDirectory(installPath)
                ZipFile.ExtractToDirectory(downloadPath, installPath)
            Case "portableexe"
                File.Copy(downloadPath, Path.Combine(installPath, Path.GetFileName(downloadPath)), True)
            Case "exe", "msi"
                If Not String.Equals(Environment.GetEnvironmentVariable("FH6TOOLS_SKIP_INSTALLER_LAUNCH"), "1", StringComparison.OrdinalIgnoreCase) Then
                    Process.Start(New ProcessStartInfo With {.FileName = downloadPath, .UseShellExecute = True})
                End If
            Case Else
                File.Copy(downloadPath, Path.Combine(installPath, Path.GetFileName(downloadPath)), True)
        End Select
        If Directory.Exists(installPath) Then
            Await File.WriteAllTextAsync(Path.Combine(installPath, ".fh6tools-version"), If(tool.Version, ""))
        End If
        Return installPath
    End Function

    Public Function IsUpdateAvailable(tool As ToolManifestEntry) As Boolean
        If Not IsInstalled(tool) OrElse String.Equals(tool.InstallType, "local", StringComparison.OrdinalIgnoreCase) Then Return False
        Dim marker = Path.Combine(GetInstallPath(tool), ".fh6tools-version")
        If Not File.Exists(marker) Then Return False
        Dim installedVersion = File.ReadAllText(marker).Trim()
        Return Not String.IsNullOrWhiteSpace(tool.Version) AndAlso
               Not String.Equals(tool.Version, "latest", StringComparison.OrdinalIgnoreCase) AndAlso
               Not String.Equals(installedVersion, tool.Version, StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Function ResolveInstalledExecutable(endpoint As ToolEndpointDefinition, installPath As String) As String
        If endpoint Is Nothing OrElse String.IsNullOrWhiteSpace(endpoint.Executable) Then Return ""
        Dim executable = FhPaths.ExpandPath(endpoint.Executable, installPath)
        If File.Exists(executable) Then Return executable
        If endpoint.Executable.Contains("*") OrElse endpoint.Executable.Contains("?") Then
            Dim pattern = endpoint.Executable.Replace("/"c, "\"c)
            Dim folderPart = Path.GetDirectoryName(pattern)
            Dim filePart = Path.GetFileName(pattern)
            Dim searchRoot = If(String.IsNullOrWhiteSpace(folderPart), installPath, FhPaths.ExpandPath(folderPart, installPath))
            If Directory.Exists(searchRoot) Then
                Dim match = Directory.GetFiles(searchRoot, filePart, SearchOption.AllDirectories).FirstOrDefault()
                If match IsNot Nothing Then Return match
            End If
        End If
        Return ""
    End Function

    Public Shared Function IsBlocked(tool As ToolManifestEntry) As Boolean
        Return String.Equals(tool.RiskLevel, "blocked", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function SelectDownloadExtension(installType As String) As String
        Select Case If(installType, "").Trim().ToLowerInvariant()
            Case "zip"
                Return ".zip"
            Case "exe", "portableexe"
                Return ".exe"
            Case "msi"
                Return ".msi"
            Case Else
                Return ".download"
        End Select
    End Function

    Private Shared Async Function ResolveDownloadAsync(downloadUrl As String, cancellationToken As Threading.CancellationToken) As Task(Of ResolvedDownload)
        If Not downloadUrl.StartsWith("github-release://", StringComparison.OrdinalIgnoreCase) Then
            Return New ResolvedDownload With {.Url = downloadUrl, .FileName = ""}
        End If

        Dim rest = downloadUrl.Substring("github-release://".Length)
        Dim parts = rest.Split({"/"c}, 3)
        If parts.Length < 3 Then Throw New InvalidOperationException("Invalid GitHub release URL. Use github-release://owner/repo/pattern.")
        Dim apiUrl = $"https://api.github.com/repos/{parts(0)}/{parts(1)}/releases/latest"
        Using request As New HttpRequestMessage(HttpMethod.Get, apiUrl)
            request.Headers.UserAgent.ParseAdd("FH6Tools")
            Using response = Await ReleaseClient.SendAsync(request, cancellationToken)
                response.EnsureSuccessStatusCode()
                Using stream = Await response.Content.ReadAsStreamAsync(cancellationToken)
                    Using document = Await JsonDocument.ParseAsync(stream, cancellationToken:=cancellationToken)
                        Dim assets = document.RootElement.GetProperty("assets")
                        Dim pattern = parts(2)
                        For Each asset In assets.EnumerateArray()
                            Dim name = asset.GetProperty("name").GetString()
                            If WildcardMatch(name, pattern) Then
                                Return New ResolvedDownload With {
                                    .Url = asset.GetProperty("browser_download_url").GetString(),
                                    .FileName = name
                                }
                            End If
                        Next
                        Throw New FileNotFoundException($"No GitHub release asset matched '{pattern}'.")
                    End Using
                End Using
            End Using
        End Using
    End Function

    Private Shared Function WildcardMatch(value As String, pattern As String) As Boolean
        Dim patternRegex = "^" & Regex.Escape(pattern).Replace("\*", ".*").Replace("\?", ".") & "$"
        Return Regex.IsMatch(If(value, ""), patternRegex, RegexOptions.IgnoreCase)
    End Function

    Private Shared Function SafeFileName(value As String) As String
        For Each c In Path.GetInvalidFileNameChars()
            value = value.Replace(c, "-"c)
        Next
        Return value
    End Function

    Private Class ResolvedDownload
        Public Property Url As String = ""
        Public Property FileName As String = "download.bin"
    End Class
End Class
