Imports System.Text.Json
Imports System.Text.RegularExpressions

Public Class ToolManifestService
    Private Shared ReadOnly ManifestClient As New HttpClient With {.Timeout = TimeSpan.FromSeconds(5)}
    Private TrustedManifestInitialized As Boolean
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
            ApplyMetadata(manifest.Tools, Await LoadMetadataAsync())
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
        Await EnsureManifestAsync()
        Dim refreshed As Boolean
        Dim source = Environment.GetEnvironmentVariable("FH6TOOLS_METADATA_URL")
        If String.IsNullOrWhiteSpace(source) AndAlso File.Exists(FhPaths.MetadataSourcePath) Then
            source = (Await File.ReadAllTextAsync(FhPaths.MetadataSourcePath)).Trim()
        End If
        If Not String.IsNullOrWhiteSpace(source) Then
            Try
                Dim json = Await ManifestClient.GetStringAsync(source)
                Dim metadata = JsonSerializer.Deserialize(Of ToolMetadataManifest)(json, JsonOptions)
                If metadata IsNot Nothing AndAlso metadata.Tools IsNot Nothing AndAlso
                   metadata.Tools.All(Function(tool) Not String.IsNullOrWhiteSpace(tool.Id)) Then
                    Await File.WriteAllTextAsync(FhPaths.MetadataPath, JsonSerializer.Serialize(metadata, JsonOptions))
                    refreshed = True
                End If
            Catch ex As Exception
                Logger.Warn(ex, "Dynamic tool metadata refresh failed; using cached metadata.")
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
                                                      If asset.ValueKind = JsonValueKind.Undefined Then
                                                          tool.OnlineStatus = "unavailable"
                                                          Return True
                                                      End If
                                                      tool.Version = root.GetProperty("tag_name").GetString()
                                                      tool.DownloadUrl = asset.GetProperty("browser_download_url").GetString()
                                                      tool.InstallType = If(asset.GetProperty("name").GetString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "zip", "portableExe")
                                                      tool.OnlineStatus = "available"
                                                      Return True
                                                  End Using
                                              Catch ex As Exception
                                                  tool.OnlineStatus = "unavailable"
                                                  Logger.Warn(ex, $"Release metadata refresh failed for {tool.Name}.")
                                                  Return True
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
            .DescriptionZh = "用户手动添加的本地工具。",
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

    Private Async Function LoadMetadataAsync() As Task(Of ToolMetadataManifest)
        If Not File.Exists(FhPaths.MetadataPath) Then
            Dim bundled = Path.Combine(AppContext.BaseDirectory, "Data", "tool-metadata.json")
            If File.Exists(bundled) Then File.Copy(bundled, FhPaths.MetadataPath, True)
        End If
        If Not File.Exists(FhPaths.MetadataPath) Then Return New ToolMetadataManifest
        Try
            Return If(JsonSerializer.Deserialize(Of ToolMetadataManifest)(Await File.ReadAllTextAsync(FhPaths.MetadataPath), JsonOptions), New ToolMetadataManifest)
        Catch ex As Exception
            Logger.Warn(ex, "Tool metadata cache could not be read.")
            Return New ToolMetadataManifest
        End Try
    End Function

    Private Shared Sub ApplyMetadata(tools As IEnumerable(Of ToolManifestEntry), metadata As ToolMetadataManifest)
        If metadata?.Tools Is Nothing Then Return
        Dim entries = metadata.Tools.
            Where(Function(item) Not String.IsNullOrWhiteSpace(item.Id)).
            GroupBy(Function(item) item.Id, StringComparer.OrdinalIgnoreCase).
            ToDictionary(Function(group) group.Key, Function(group) group.First(), StringComparer.OrdinalIgnoreCase)
        For Each tool In tools
            Dim item As ToolMetadataEntry = Nothing
            If Not entries.TryGetValue(tool.Id, item) Then Continue For
            If Not String.IsNullOrWhiteSpace(item.Name) Then tool.Name = item.Name
            If Not String.IsNullOrWhiteSpace(item.Description) Then tool.Description = item.Description
            If Not String.IsNullOrWhiteSpace(item.DescriptionZh) Then tool.DescriptionZh = item.DescriptionZh
            If Not String.IsNullOrWhiteSpace(item.Homepage) Then tool.Homepage = item.Homepage
        Next
    End Sub

    Private Async Function EnsureManifestAsync() As Task
        If TrustedManifestInitialized Then Return
        Dim bundled = Path.Combine(AppContext.BaseDirectory, "Data", "sample-manifest.json")
        If File.Exists(bundled) Then
            ' Operational and safety fields are always restored from the trusted bundled manifest.
            File.Copy(bundled, FhPaths.ManifestPath, True)
        ElseIf Not File.Exists(FhPaths.ManifestPath) Then
            Await File.WriteAllTextAsync(FhPaths.ManifestPath, JsonSerializer.Serialize(New ToolManifest, JsonOptions))
        End If
        TrustedManifestInitialized = True
    End Function
End Class
