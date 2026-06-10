Public Module FhPaths
    Public ReadOnly Property SharedDotNetRoot As String
        Get
            Dim overrideRoot = Environment.GetEnvironmentVariable("FH6TOOLS_DOTNET_ROOT")
            If Not String.IsNullOrWhiteSpace(overrideRoot) Then Return Path.GetFullPath(overrideRoot)
            Return Path.Combine(AppContext.BaseDirectory, "dotnet")
        End Get
    End Property

    Public ReadOnly Property AppDataRoot As String
        Get
            Dim overrideRoot = Environment.GetEnvironmentVariable("FH6TOOLS_APPDATA_ROOT")
            If Not String.IsNullOrWhiteSpace(overrideRoot) Then Return overrideRoot
            Return Path.Combine(AppContext.BaseDirectory, "FH6ToolsData")
        End Get
    End Property

    Public ReadOnly Property ToolsRoot As String
        Get
            Dim configPath = Path.Combine(AppDataRoot, "tools-root.txt")
            If File.Exists(configPath) Then
                Dim configured = File.ReadAllText(configPath).Trim()
                If Not String.IsNullOrWhiteSpace(configured) Then Return Path.GetFullPath(configured)
            End If
            Return Path.Combine(AppDataRoot, "tools")
        End Get
    End Property

    Public Sub SetToolsRoot(path As String)
        If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException("Tool install path cannot be empty.", NameOf(path))
        Directory.CreateDirectory(path)
        Directory.CreateDirectory(AppDataRoot)
        File.WriteAllText(IO.Path.Combine(AppDataRoot, "tools-root.txt"), IO.Path.GetFullPath(path))
    End Sub

    Public ReadOnly Property ConfigRoot As String
        Get
            Return Path.Combine(AppDataRoot, "configs")
        End Get
    End Property

    Public ReadOnly Property SnapshotRoot As String
        Get
            Return Path.Combine(AppDataRoot, "snapshots")
        End Get
    End Property

    Public ReadOnly Property DownloadRoot As String
        Get
            Return Path.Combine(AppDataRoot, "downloads")
        End Get
    End Property

    Public ReadOnly Property ManifestPath As String
        Get
            Return Path.Combine(AppDataRoot, "tool-manifest.json")
        End Get
    End Property

    Public ReadOnly Property ManifestSourcePath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "Data", "manifest-url.txt")
        End Get
    End Property

    Public ReadOnly Property LocalToolsPath As String
        Get
            Return Path.Combine(AppDataRoot, "localTools.json")
        End Get
    End Property

    Public ReadOnly Property StatePath As String
        Get
            Return Path.Combine(AppDataRoot, "state.json")
        End Get
    End Property

    Public Sub Ensure()
        Directory.CreateDirectory(AppDataRoot)
        Directory.CreateDirectory(ToolsRoot)
        Directory.CreateDirectory(ConfigRoot)
        Directory.CreateDirectory(SnapshotRoot)
        Directory.CreateDirectory(DownloadRoot)
    End Sub

    Public Function ExpandPath(value As String, Optional basePath As String = "") As String
        If String.IsNullOrWhiteSpace(value) Then Return ""
        Dim expanded = ApplyTokens(value.Trim(), basePath)
        expanded = Environment.ExpandEnvironmentVariables(expanded)
        If Path.IsPathRooted(expanded) Then Return expanded
        If Not String.IsNullOrWhiteSpace(basePath) Then Return Path.GetFullPath(Path.Combine(basePath, expanded))
        Return Path.GetFullPath(expanded)
    End Function

    Public Function ApplyTokens(value As String, Optional toolRoot As String = "") As String
        If value Is Nothing Then Return ""
        Return value.
            Replace("{toolRoot}", toolRoot).
            Replace("{appDataRoot}", AppDataRoot).
            Replace("{configRoot}", ConfigRoot).
            Replace("{toolsRoot}", ToolsRoot).
            Replace("{downloadRoot}", DownloadRoot).
            Replace("{snapshotRoot}", SnapshotRoot).
            Replace("{dotnetRoot}", SharedDotNetRoot)
    End Function
End Module
