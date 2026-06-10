Imports Microsoft.Win32

Public Class GameBackupService
    Public ReadOnly Property BackupRoot As String
        Get
            Return Path.Combine(FhPaths.AppDataRoot, "game-backups")
        End Get
    End Property

    Public Function GetCandidateDataRoots() As List(Of String)
        Dim localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Dim documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        Return New List(Of String) From {
            Path.Combine(documents, "My Games", "Forza Horizon 6"),
            Path.Combine(localAppData, "ForzaHorizon6"),
            Path.Combine(localAppData, "FH6"),
            Path.Combine(localAppData, "Packages", "Microsoft.FH6_8wekyb3d8bbwe", "SystemAppData"),
            Path.Combine(localAppData, "Packages", "Microsoft.SunriseBaseGame_8wekyb3d8bbwe", "SystemAppData")
        }.Where(Function(path) Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Public Function ChooseDataRoot(owner As Window) As String
        Dim candidates = GetCandidateDataRoots()
        If candidates.Count = 1 Then Return candidates(0)
        If candidates.Count > 1 Then
            Dim selected = MyMsgBoxSelect(candidates, "Choose FH6 data folder")
            If selected >= 0 AndAlso selected < candidates.Count Then Return candidates(selected)
        End If

        Dim dialog As New OpenFolderDialog With {
            .Title = "Choose FH6 config or save folder to backup"
        }
        If dialog.ShowDialog(owner) Then Return dialog.FolderName
        Return ""
    End Function

    Public Function BackupFolder(sourceFolder As String) As String
        If String.IsNullOrWhiteSpace(sourceFolder) OrElse Not Directory.Exists(sourceFolder) Then
            Throw New DirectoryNotFoundException("Game data folder was not found.")
        End If

        Directory.CreateDirectory(BackupRoot)
        Dim target = Path.Combine(BackupRoot, $"{Path.GetFileName(sourceFolder)}-{DateTime.Now:yyyyMMdd-HHmmss}")
        CopyDirectory(sourceFolder, target)
        Return target
    End Function

    Private Shared Sub CopyDirectory(source As String, target As String)
        Directory.CreateDirectory(target)
        For Each sourceFile In Directory.GetFiles(source)
            IO.File.Copy(sourceFile, Path.Combine(target, Path.GetFileName(sourceFile)), True)
        Next
        For Each sourceDirectory In Directory.GetDirectories(source)
            CopyDirectory(sourceDirectory, Path.Combine(target, Path.GetFileName(sourceDirectory)))
        Next
    End Sub
End Class
