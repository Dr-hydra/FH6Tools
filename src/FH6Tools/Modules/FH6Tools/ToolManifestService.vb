Imports System.Text.Json
Imports System.Text.RegularExpressions

Public Class ToolManifestService
    Private Shared ReadOnly ManifestClient As New HttpClient With {.Timeout = TimeSpan.FromSeconds(5)}
    Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .WriteIndented = True
    }

    Shared Sub New()
        ManifestClient.DefaultRequestHeaders.UserAgent.ParseAdd("FH6Tools")
    End Sub

    Public Async Function LoadToolsAsync() As Task(Of List(Of ToolManifestEntry))
        FhPaths.Ensure()
        Await EnsureManifestAsync()

        Dim result As New List(Of ToolManifestEntry)
        Dim manifest = JsonSerializer.Deserialize(Of ToolManifest)(Await File.ReadAllTextAsync(FhPaths.ManifestPath), JsonOptions)
        If manifest IsNot Nothing AndAlso manifest.Tools IsNot Nothing Then
            For Each tool In manifest.Tools
                tool.Source = "curated"
                result.Add(tool)
            Next
        End If

        Dim localStore = Await LoadLocalToolsAsync()
        For Each tool In localStore.Tools
            tool.Source = "local"
            result.Add(tool)
        Next

        Return result.
            Where(Function(t) Not String.IsNullOrWhiteSpace(t.Id)).
            GroupBy(Function(t) t.Id, StringComparer.OrdinalIgnoreCase).
            Select(Function(g) g.First()).
            ToList()
    End Function

    Public Async Function RefreshDynamicManifestAsync() As Task(Of Boolean)
        Dim refreshed As Boolean
        Dim source = Environment.GetEnvironmentVariable("FH6TOOLS_MANIFEST_URL")
        If String.IsNullOrWhiteSpace(source) AndAlso File.Exists(FhPaths.ManifestSourcePath) Then
            source = (Await File.ReadAllTextAsync(FhPaths.ManifestSourcePath)).Trim()
        End If
        If Not String.IsNullOrWhiteSpace(source) Then
            Try
                Dim json = Await ManifestClient.GetStringAsync(source)
                Dim manifest = JsonSerializer.Deserialize(Of ToolManifest)(json, JsonOptions)
                If manifest IsNot Nothing AndAlso manifest.Tools IsNot Nothing Then
                    Await File.WriteAllTextAsync(FhPaths.ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions))
                    refreshed = True
                End If
            Catch ex As Exception
                Logger.Warn(ex, "Dynamic tool manifest refresh failed; using local manifest.")
            End Try
        End If
        Return Await RefreshReleaseMetadataAsync() OrElse refreshed
    End Function

    Private Async Function RefreshReleaseMetadataAsync() As Task(Of Boolean)
        If Not File.Exists(FhPaths.ManifestPath) Then Return False
        Dim manifest = JsonSerializer.Deserialize(Of ToolManifest)(Await File.ReadAllTextAsync(FhPaths.ManifestPath), JsonOptions)
        If manifest Is Nothing OrElse manifest.Tools Is Nothing Then Return False

        Dim changed As Boolean
        Dim tasks = manifest.Tools.Select(Async Function(tool)
                                              Dim repo = ParseGitHubRepository(tool.Homepage)
                                              If repo Is Nothing Then Return False
                                              Try
                                                  Dim json = Await ManifestClient.GetStringAsync($"https://api.github.com/repos/{repo.Value.Owner}/{repo.Value.Repository}/releases/latest")
                                                  Using document = JsonDocument.Parse(json)
                                                      Dim root = document.RootElement
                                                      Dim asset = SelectPreferredAsset(root.GetProperty("assets"), repo.Value.Owner)
                                                      If asset.ValueKind = JsonValueKind.Undefined Then Return False
                                                      tool.Version = root.GetProperty("tag_name").GetString()
                                                      tool.DownloadUrl = asset.GetProperty("browser_download_url").GetString()
                                                      tool.InstallType = If(asset.GetProperty("name").GetString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "zip", "portableExe")
                                                      Return True
                                                  End Using
                                              Catch ex As Exception
                                                  Logger.Warn(ex, $"Release metadata refresh failed for {tool.Name}.")
                                                  Return False
                                              End Try
                                          End Function).ToArray()
        For Each result In Await Task.WhenAll(tasks)
            changed = changed OrElse result
        Next
        If changed Then
            manifest.UpdatedAt = DateTimeOffset.Now.ToString("O")
            Await File.WriteAllTextAsync(FhPaths.ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions))
        End If
        Return changed
    End Function

    Private Shared Function ParseGitHubRepository(homepage As String) As (Owner As String, Repository As String)?
        Dim match = Regex.Match(If(homepage, ""), "^https://github\.com/([^/]+)/([^/#?]+)", RegexOptions.IgnoreCase)
        If Not match.Success Then Return Nothing
        Return (match.Groups(1).Value, match.Groups(2).Value)
    End Function

    Private Shared Function SelectPreferredAsset(assets As JsonElement, owner As String) As JsonElement
        Dim candidates = assets.EnumerateArray().
            Where(Function(asset)
                      Dim name = asset.GetProperty("name").GetString()
                      Return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) OrElse
                             name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                  End Function).
            OrderByDescending(Function(asset) GetAssetScore(asset.GetProperty("name").GetString(), owner)).
            ToList()
        Return If(candidates.Count = 0, Nothing, candidates(0))
    End Function

    Private Shared Function GetAssetScore(name As String, owner As String) As Integer
        Dim lower = name.ToLowerInvariant()
        Dim score = If(lower.EndsWith(".zip"), 100, 10)
        If owner.Equals("Dr-hydra", StringComparison.OrdinalIgnoreCase) Then
            If lower.Contains("without-runtime") Then score += 1000
            If lower.Contains("full-framework-dependent") Then score += 950
            If lower.Contains("framework-dependent") AndAlso Not lower.Contains("self-contained") Then score += 900
            If lower.Contains("with-runtime") OrElse lower.Contains("self-contained") Then score -= 1000
        End If
        If lower.Contains("backend") Then score -= 100
        If lower.Contains("installer") Then score -= 200
        Return score
    End Function

    Public Async Function LoadStateAsync() As Task(Of ToolStateStore)
        FhPaths.Ensure()
        If Not File.Exists(FhPaths.StatePath) Then Return New ToolStateStore
        Dim text = Await File.ReadAllTextAsync(FhPaths.StatePath)
        Return If(JsonSerializer.Deserialize(Of ToolStateStore)(text, JsonOptions), New ToolStateStore)
    End Function

    Public Async Function SaveStateAsync(state As ToolStateStore) As Task
        FhPaths.Ensure()
        Await File.WriteAllTextAsync(FhPaths.StatePath, JsonSerializer.Serialize(state, JsonOptions))
    End Function

    Public Async Function AddLocalToolAsync(filePath As String) As Task(Of ToolManifestEntry)
        Dim store = Await LoadLocalToolsAsync()
        Dim id = "local-" & Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant().Replace(" ", "-")
        Dim entry As New ToolManifestEntry With {
            .Id = id,
            .Name = Path.GetFileNameWithoutExtension(filePath),
            .Version = "local",
            .Category = "local",
            .Description = "User-added local tool.",
            .InstallType = "local",
            .ToolType = "single",
            .[Single] = New ToolEndpointDefinition With {.Executable = filePath},
            .Source = "local"
        }
        store.Tools.RemoveAll(Function(t) String.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        store.Tools.Add(entry)
        Await File.WriteAllTextAsync(FhPaths.LocalToolsPath, JsonSerializer.Serialize(store, JsonOptions))
        Return entry
    End Function

    Private Async Function LoadLocalToolsAsync() As Task(Of LocalToolStore)
        FhPaths.Ensure()
        If Not File.Exists(FhPaths.LocalToolsPath) Then Return New LocalToolStore
        Dim text = Await File.ReadAllTextAsync(FhPaths.LocalToolsPath)
        Return If(JsonSerializer.Deserialize(Of LocalToolStore)(text, JsonOptions), New LocalToolStore)
    End Function

    Private Async Function EnsureManifestAsync() As Task
        Dim bundled = Path.Combine(AppContext.BaseDirectory, "Data", "sample-manifest.json")
        If Not File.Exists(FhPaths.ManifestPath) Then
            If File.Exists(bundled) Then
                File.Copy(bundled, FhPaths.ManifestPath, True)
            Else
                Await File.WriteAllTextAsync(FhPaths.ManifestPath, JsonSerializer.Serialize(New ToolManifest, JsonOptions))
            End If
            Return
        End If

        If File.Exists(bundled) AndAlso Await BundledManifestIsNewerAsync(bundled, FhPaths.ManifestPath) Then
            File.Copy(bundled, FhPaths.ManifestPath, True)
        End If
    End Function

    Private Shared Async Function BundledManifestIsNewerAsync(bundledPath As String, currentPath As String) As Task(Of Boolean)
        Try
            Dim bundled = JsonSerializer.Deserialize(Of ToolManifest)(Await File.ReadAllTextAsync(bundledPath), JsonOptions)
            Dim current = JsonSerializer.Deserialize(Of ToolManifest)(Await File.ReadAllTextAsync(currentPath), JsonOptions)
            If Not CurrentManifestLooksManaged(current, bundled) Then Return False
            Dim bundledDate As DateTimeOffset
            Dim currentDate As DateTimeOffset
            If DateTimeOffset.TryParse(bundled?.UpdatedAt, bundledDate) AndAlso DateTimeOffset.TryParse(current?.UpdatedAt, currentDate) Then
                Return bundledDate > currentDate
            End If
        Catch
        End Try
        Return False
    End Function

    Private Shared Function CurrentManifestLooksManaged(current As ToolManifest, bundled As ToolManifest) As Boolean
        If current Is Nothing OrElse current.Tools Is Nothing OrElse current.Tools.Count = 0 Then Return True
        If current.Tools.Any(Function(tool) tool.Id = "fh6-notepad-tool" OrElse tool.Id.StartsWith("fh6-sample-", StringComparison.OrdinalIgnoreCase)) Then Return True
        If bundled Is Nothing OrElse bundled.Tools Is Nothing OrElse bundled.Tools.Count = 0 Then Return False
        Dim bundledIds = New HashSet(Of String)(bundled.Tools.Select(Function(tool) tool.Id), StringComparer.OrdinalIgnoreCase)
        Return current.Tools.All(Function(tool) bundledIds.Contains(tool.Id))
    End Function
End Class
