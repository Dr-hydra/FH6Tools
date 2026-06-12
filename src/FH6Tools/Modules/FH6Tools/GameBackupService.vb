Public Class GameBackupService
    Private Const LegacyGameSavePath As String = "C:\XboxGames\GameSave\pgs"

    Public ReadOnly Property BackupRoot As String
        Get
            Return Path.Combine(FhPaths.AppDataRoot, "game-save-backups")
        End Get
    End Property

    Public Function GetSavePath(game As GameInstallState) As String
        If Not String.IsNullOrWhiteSpace(game?.SavePath) Then Return Path.GetFullPath(game.SavePath)
        If String.Equals(game?.Source, GameLaunchService.GameSourceXbox, StringComparison.OrdinalIgnoreCase) Then
            Dim installRoot = GetXboxInstallRoot(game.InstallPath)
            If Not String.IsNullOrWhiteSpace(installRoot) Then Return Path.Combine(installRoot, "GameSave", "pgs")
        End If
        Return LegacyGameSavePath
    End Function

    Private Shared Function GetXboxInstallRoot(installPath As String) As String
        If String.IsNullOrWhiteSpace(installPath) Then Return ""
        Dim normalized = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        If File.Exists(normalized) OrElse Path.HasExtension(normalized) Then normalized = Path.GetDirectoryName(normalized)
        If String.Equals(Path.GetFileName(normalized), "Forza Horizon 6", StringComparison.OrdinalIgnoreCase) Then
            Return Path.GetDirectoryName(normalized)
        End If
        Return normalized
    End Function

    Public Function GetBackups() As List(Of GameSaveBackupInfo)
        If Not Directory.Exists(BackupRoot) Then Return New List(Of GameSaveBackupInfo)
        Return Directory.EnumerateDirectories(BackupRoot).
            Select(Function(backupPath) GameSaveBackupInfo.FromDirectory(backupPath)).
            Where(Function(info) info IsNot Nothing).
            OrderByDescending(Function(info) info.CreatedAt).
            ToList()
    End Function

    Public Async Function BackupAsync(game As GameInstallState, backupType As String, Optional protectedBackupPath As String = "") As Task(Of String)
        Dim source = GetSavePath(game)
        If Not Directory.Exists(source) Then Return ""

        Return Await Task.Run(Function()
                                  Directory.CreateDirectory(BackupRoot)
                                  Dim platform = SafeName(If(game?.Source, "Unknown"))
                                  Dim target = Path.Combine(BackupRoot, $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{platform}-{SafeName(backupType)}")
                                  Dim skippedFiles As New List(Of String)
                                  Try
                                      Dim copiedFiles = CopyDirectory(source, target, skippedFiles)
                                      If copiedFiles = 0 Then Throw New IOException("No game save files could be copied.")
                                  Catch
                                      Try
                                          If Directory.Exists(target) Then Directory.Delete(target, True)
                                      Catch
                                          ' Preserve the original copy exception.
                                      End Try
                                      Throw
                                  End Try
                                  If skippedFiles.Count > 0 Then
                                      Logger.Warn($"Game save backup skipped {skippedFiles.Count} changing or locked file(s): {String.Join(", ", skippedFiles.Take(5))}")
                                  End If
                                  PruneBackups(backupType, If(String.Equals(backupType, "before-launch", StringComparison.OrdinalIgnoreCase), 3, 10), protectedBackupPath)
                                  Logger.Info($"Game save backup created: {target}")
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

    Public Async Function RestoreAsync(game As GameInstallState, backupPath As String) As Task(Of String)
        Dim source = ValidateBackupPath(backupPath)
        Dim target = GetSavePath(game)
        If Not Directory.Exists(source) Then Throw New DirectoryNotFoundException("The selected save backup no longer exists.")
        If String.IsNullOrWhiteSpace(target) Then Throw New DirectoryNotFoundException("The game save path could not be determined.")

        Dim safetyBackup = Await BackupAsync(game, "before-restore", source)
        Await Task.Run(Sub()
                           Directory.CreateDirectory(target)
                           ClearDirectory(target)
                           Dim skippedFiles As New List(Of String)
                           If CopyDirectory(source, target, skippedFiles) = 0 Then
                               Throw New IOException("No game save files could be restored.")
                           End If
                           If skippedFiles.Count > 0 Then
                               Logger.Warn($"Game save restore skipped {skippedFiles.Count} changing or locked file(s): {String.Join(", ", skippedFiles.Take(5))}")
                           End If
                       End Sub)
        Return safetyBackup
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

    Private Sub PruneBackups(backupType As String, keep As Integer, protectedBackupPath As String)
        Dim suffix = "-" & SafeName(backupType)
        Dim protectedFullPath = If(String.IsNullOrWhiteSpace(protectedBackupPath), "", Path.GetFullPath(protectedBackupPath))
        For Each backupDirectory As String In Directory.EnumerateDirectories(BackupRoot).
            Where(Function(candidatePath) IO.Path.GetFileName(candidatePath).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).
            Where(Function(candidatePath) String.IsNullOrWhiteSpace(protectedFullPath) OrElse
                                          Not String.Equals(Path.GetFullPath(candidatePath), protectedFullPath, StringComparison.OrdinalIgnoreCase)).
            OrderByDescending(Function(candidatePath) Directory.GetCreationTimeUtc(candidatePath)).
            Skip(keep)
            Directory.Delete(backupDirectory, True)
        Next
    End Sub

    Private Shared Function CopyDirectory(source As String, target As String, skippedFiles As List(Of String)) As Integer
        Directory.CreateDirectory(target)
        Dim copiedFiles As Integer
        Try
            For Each sourceFile In Directory.EnumerateFiles(source)
                If CopyReadableFileWithRetry(sourceFile, Path.Combine(target, Path.GetFileName(sourceFile))) Then
                    copiedFiles += 1
                Else
                    skippedFiles.Add(sourceFile)
                End If
            Next
            For Each sourceDirectory In Directory.EnumerateDirectories(source)
                copiedFiles += CopyDirectory(sourceDirectory, Path.Combine(target, Path.GetFileName(sourceDirectory)), skippedFiles)
            Next
        Catch ex As DirectoryNotFoundException
            Logger.Warn(ex, $"Game save directory changed during backup: {source}")
        End Try
        Return copiedFiles
    End Function

    Private Shared Sub ClearDirectory(target As String)
        For Each filePath In Directory.EnumerateFiles(target)
            File.SetAttributes(filePath, FileAttributes.Normal)
            File.Delete(filePath)
        Next
        For Each directoryPath In Directory.EnumerateDirectories(target)
            Directory.Delete(directoryPath, True)
        Next
    End Sub

    Private Function ValidateBackupPath(backupPath As String) As String
        If String.IsNullOrWhiteSpace(backupPath) Then Throw New ArgumentException("No save backup was selected.")
        Dim root = Path.GetFullPath(BackupRoot).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        Dim candidate = Path.GetFullPath(backupPath).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        If Not candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then
            Throw New UnauthorizedAccessException("The selected folder is outside the FH6Tools save backup directory.")
        End If
        Return candidate.TrimEnd(Path.DirectorySeparatorChar)
    End Function

    Private Shared Function CopyReadableFileWithRetry(source As String, target As String) As Boolean
        For attempt = 1 To 3
            Try
                Using input = New FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                    Using output = New FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)
                        input.CopyTo(output)
                    End Using
                End Using
                Return True
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                If attempt = 3 Then Return False
                Threading.Thread.Sleep(100 * attempt)
            End Try
        Next
        Return False
    End Function

    Private Shared Function SafeName(value As String) As String
        Dim result = If(String.IsNullOrWhiteSpace(value), "unknown", value)
        For Each invalid In Path.GetInvalidFileNameChars()
            result = result.Replace(invalid, "-"c)
        Next
        Return result
    End Function
End Class

Public Class GameSaveBackupInfo
    Public Property Path As String = ""
    Public Property Name As String = ""
    Public Property CreatedAt As DateTime
    Public Property Platform As String = ""
    Public Property BackupType As String = ""
    Public Property SizeBytes As Long

    Public ReadOnly Property CreatedAtText As String
        Get
            Return CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        End Get
    End Property

    Public ReadOnly Property TypeText As String
        Get
            Select Case BackupType
                Case "before-launch"
                    Return FhLanguage.Text("启动前", "Before launch")
                Case "after-exit"
                    Return FhLanguage.Text("退出后", "After exit")
                Case "before-restore"
                    Return FhLanguage.Text("恢复前保护", "Before restore")
                Case Else
                    Return BackupType
            End Select
        End Get
    End Property

    Public ReadOnly Property SizeText As String
        Get
            If SizeBytes >= 1024L * 1024 * 1024 Then Return $"{SizeBytes / 1024.0 / 1024 / 1024:0.00} GB"
            If SizeBytes >= 1024L * 1024 Then Return $"{SizeBytes / 1024.0 / 1024:0.0} MB"
            If SizeBytes >= 1024 Then Return $"{SizeBytes / 1024.0:0.0} KB"
            Return $"{SizeBytes} B"
        End Get
    End Property

    Public Shared Function FromDirectory(directoryPath As String) As GameSaveBackupInfo
        Try
            Dim name = IO.Path.GetFileName(directoryPath)
            Dim parts = name.Split("-"c)
            If parts.Length < 5 Then Return Nothing
            Dim createdAt As DateTime
            If Not DateTime.TryParseExact(parts(0) & "-" & parts(1), {"yyyyMMdd-HHmmssfff", "yyyyMMdd-HHmmss"},
                                          Globalization.CultureInfo.InvariantCulture,
                                          Globalization.DateTimeStyles.None, createdAt) Then
                createdAt = Directory.GetCreationTime(directoryPath)
            End If
            Dim backupType = String.Join("-", parts.Skip(parts.Length - 2))
            Dim platform = String.Join("-", parts.Skip(2).Take(parts.Length - 4))
            Return New GameSaveBackupInfo With {
                .Path = directoryPath,
                .Name = name,
                .CreatedAt = createdAt,
                .Platform = platform,
                .BackupType = backupType,
                .SizeBytes = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).
                    Sum(Function(filePath) New FileInfo(filePath).Length)
            }
        Catch
            Return Nothing
        End Try
    End Function
End Class
