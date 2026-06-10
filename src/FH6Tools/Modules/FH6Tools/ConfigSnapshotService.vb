Imports System.IO.Compression

Public Class ConfigSnapshotService
    Public Function ResolveConfigPath(tool As ToolManifestEntry, config As ToolConfigFileEntry) As String
        Dim toolRoot = (New ToolInstallService).GetInstallPath(tool)
        If File.Exists(toolRoot) Then toolRoot = Path.GetDirectoryName(toolRoot)
        Return FhPaths.ExpandPath(config.Path, toolRoot)
    End Function

    Public Function Backup(tool As ToolManifestEntry, config As ToolConfigFileEntry) As String
        Dim source = ResolveConfigPath(tool, config)
        Dim targetDir = Path.Combine(FhPaths.SnapshotRoot, tool.Id)
        Directory.CreateDirectory(targetDir)
        If IsDirectoryConfig(config, source) Then
            If Not Directory.Exists(source) Then Throw New DirectoryNotFoundException("Config directory not found: " & source)
            Dim name = If(String.IsNullOrWhiteSpace(config.Name), Path.GetFileName(source), config.Name)
            Dim target = Path.Combine(targetDir, $"{SafeName(name)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip")
            If File.Exists(target) Then File.Delete(target)
            ZipFile.CreateFromDirectory(source, target)
            Return target
        Else
            If Not File.Exists(source) Then Throw New FileNotFoundException("Config file not found.", source)
            Dim target = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(source)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(source)}")
            File.Copy(source, target, True)
            Return target
        End If
    End Function

    Public Sub Restore(snapshotPath As String, targetPath As String)
        If Not File.Exists(snapshotPath) Then Throw New FileNotFoundException("Snapshot not found.", snapshotPath)
        If String.Equals(Path.GetExtension(snapshotPath), ".zip", StringComparison.OrdinalIgnoreCase) Then
            Directory.CreateDirectory(targetPath)
            ZipFile.ExtractToDirectory(snapshotPath, targetPath, True)
        Else
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath))
            File.Copy(snapshotPath, targetPath, True)
        End If
    End Sub

    Public Function ListSnapshots(tool As ToolManifestEntry) As List(Of String)
        Dim targetDir = Path.Combine(FhPaths.SnapshotRoot, tool.Id)
        If Not Directory.Exists(targetDir) Then Return New List(Of String)
        Return Directory.GetFiles(targetDir).OrderByDescending(Function(p) File.GetLastWriteTimeUtc(p)).ToList()
    End Function

    Public Shared Function IsDirectoryConfig(config As ToolConfigFileEntry, resolvedPath As String) As Boolean
        If config IsNot Nothing AndAlso String.Equals(config.Kind, "directory", StringComparison.OrdinalIgnoreCase) Then Return True
        If Directory.Exists(resolvedPath) Then Return True
        Return String.IsNullOrWhiteSpace(Path.GetExtension(resolvedPath))
    End Function

    Private Shared Function SafeName(value As String) As String
        For Each c In Path.GetInvalidFileNameChars()
            value = value.Replace(c, "-"c)
        Next
        Return value
    End Function
End Class
