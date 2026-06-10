Public Class GameBackupService
    Private Const MicrosoftStorePackage As String = "Microsoft.624F8B84B80_8wekyb3d8bbwe"

    Public ReadOnly Property BackupRoot As String
        Get
            Return Path.Combine(FhPaths.AppDataRoot, "game-save-backups")
        End Get
    End Property

    Public Function GetSavePath(game As GameInstallState) As String
        If String.Equals(game?.Source, "Microsoft Store", StringComparison.OrdinalIgnoreCase) Then
            Return GetMicrosoftStoreSavePath()
        End If
        If String.Equals(game?.Source, "Xbox", StringComparison.OrdinalIgnoreCase) OrElse
           String.Equals(game?.Source, "Steam", StringComparison.OrdinalIgnoreCase) Then
            Return GetXboxAndSteamSavePath()
        End If

        Dim xboxAndSteam = GetXboxAndSteamSavePath()
        If Directory.Exists(xboxAndSteam) Then Return xboxAndSteam
        Return GetMicrosoftStoreSavePath()
    End Function

    Public Async Function BackupAsync(game As GameInstallState, backupType As String) As Task(Of String)
        Dim source = GetSavePath(game)
        If Not Directory.Exists(source) Then Return ""

        Return Await Task.Run(Function()
                                  Directory.CreateDirectory(BackupRoot)
                                  Dim platform = SafeName(If(game?.Source, "Unknown"))
                                  Dim target = Path.Combine(BackupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{platform}-{SafeName(backupType)}")
                                  Try
                                      CopyDirectory(source, target)
                                  Catch
                                      Try
                                          If Directory.Exists(target) Then Directory.Delete(target, True)
                                      Catch
                                          ' Preserve the original copy exception.
                                      End Try
                                      Throw
                                  End Try
                                  PruneBackups(backupType, If(String.Equals(backupType, "before-launch", StringComparison.OrdinalIgnoreCase), 3, 10))
                                  Return target
                              End Function)
    End Function

    Public Async Function WaitForStableSaveAsync(game As GameInstallState, Optional quietPeriod As TimeSpan = Nothing, Optional maximumWait As TimeSpan = Nothing) As Task(Of Boolean)
        If quietPeriod = Nothing Then quietPeriod = TimeSpan.FromSeconds(60)
        If maximumWait = Nothing Then maximumWait = TimeSpan.FromMinutes(5)
        Dim source = GetSavePath(game)
        If Not Directory.Exists(source) Then Return False

        Dim started = DateTime.UtcNow
        Dim stableSince = DateTime.UtcNow
        Dim previous = Await Task.Run(Function() GetDirectoryFingerprint(source))
        While DateTime.UtcNow - started < maximumWait
            Await Task.Delay(TimeSpan.FromSeconds(5))
            Dim current = Await Task.Run(Function() GetDirectoryFingerprint(source))
            If Not String.Equals(previous, current, StringComparison.Ordinal) Then
                previous = current
                stableSince = DateTime.UtcNow
            ElseIf DateTime.UtcNow - stableSince >= quietPeriod Then
                Return True
            End If
        End While
        Return False
    End Function

    Private Shared Function GetMicrosoftStoreSavePath() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Packages", MicrosoftStorePackage, "SystemAppData", "wgs")
    End Function

    Private Shared Function GetXboxAndSteamSavePath() As String
        Return "C:\XboxGames\GameSave\pgs"
    End Function

    Private Shared Function GetDirectoryFingerprint(root As String) As String
        Try
            Return String.Join("|", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).
                OrderBy(Function(filePath) filePath, StringComparer.OrdinalIgnoreCase).
                Select(Function(filePath)
                           Dim info As New FileInfo(filePath)
                           Return $"{filePath}:{info.Length}:{info.LastWriteTimeUtc.Ticks}"
                       End Function))
        Catch
            Return Guid.NewGuid().ToString()
        End Try
    End Function

    Private Sub PruneBackups(backupType As String, keep As Integer)
        Dim suffix = "-" & SafeName(backupType)
        For Each backupDirectory As String In Directory.EnumerateDirectories(BackupRoot).
            Where(Function(candidatePath) IO.Path.GetFileName(candidatePath).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).
            OrderByDescending(Function(candidatePath) Directory.GetCreationTimeUtc(candidatePath)).
            Skip(keep)
            Directory.Delete(backupDirectory, True)
        Next
    End Sub

    Private Shared Sub CopyDirectory(source As String, target As String)
        Directory.CreateDirectory(target)
        For Each sourceFile In Directory.EnumerateFiles(source)
            CopyReadableFile(sourceFile, Path.Combine(target, Path.GetFileName(sourceFile)))
        Next
        For Each sourceDirectory In Directory.EnumerateDirectories(source)
            CopyDirectory(sourceDirectory, Path.Combine(target, Path.GetFileName(sourceDirectory)))
        Next
    End Sub

    Private Shared Sub CopyReadableFile(source As String, target As String)
        Using input = New FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            Using output = New FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                input.CopyTo(output)
            End Using
        End Using
    End Sub

    Private Shared Function SafeName(value As String) As String
        Dim result = If(String.IsNullOrWhiteSpace(value), "unknown", value)
        For Each invalid In Path.GetInvalidFileNameChars()
            result = result.Replace(invalid, "-"c)
        Next
        Return result
    End Function
End Class
