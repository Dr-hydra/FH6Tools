Imports Microsoft.Win32

Public Class GameLaunchService
    Private ReadOnly ManifestService As New ToolManifestService
    Private ReadOnly ProcessBindingLock As New Object
    Private LaunchBaselineProcessIds As New HashSet(Of Integer)
    Private BoundGameProcessIds As New HashSet(Of Integer)
    Private BoundGameInstallPath As String = ""
    Public Const GameSourceAuto As String = "Auto"
    Public Const GameSourceSteam As String = "Steam"
    Public Const GameSourceXbox As String = "Xbox"
    Public Const GameSourceManual As String = "Manual"

    Public Async Function DetectAsync() As Task(Of GameInstallState)
        Dim state = Await ManifestService.LoadStateAsync()
        Dim overrideSource = NormalizeGameSource(state.GameSourceOverride)
        If Not String.Equals(overrideSource, GameSourceAuto, StringComparison.OrdinalIgnoreCase) Then
            Dim manual = DetectManualOverride(state)
            If manual.IsInstalled Then Return CompleteGameState(manual, state)
        ElseIf Not String.IsNullOrWhiteSpace(state.GamePath) AndAlso File.Exists(state.GamePath) Then
            Return CompleteGameState(New GameInstallState With {
                .IsInstalled = True,
                .Source = GameSourceManual,
                .InstallPath = state.GamePath,
                .LaunchCommand = state.GamePath,
                .LastLaunchAt = state.LastGameLaunchAt,
                .Message = "Manual game path is configured."
            }, state)
        End If

        Dim steam = DetectSteamInstall()
        If steam.IsInstalled Then
            steam.LastLaunchAt = state.LastGameLaunchAt
            Return CompleteGameState(steam, state)
        End If

        Dim registry = DetectRegistryInstall()
        If registry.IsInstalled Then
            registry.LastLaunchAt = state.LastGameLaunchAt
            Return CompleteGameState(registry, state)
        End If

        Dim xbox = DetectXboxInstall()
        If xbox.IsInstalled Then
            xbox.LastLaunchAt = state.LastGameLaunchAt
            Return CompleteGameState(xbox, state)
        End If

        Return CompleteGameState(New GameInstallState With {
            .IsInstalled = False,
            .Message = "FH6 was not detected. Bind the executable or launch it from Steam/Xbox first."
        }, state)
    End Function

    Public Async Function BindManualPathAsync(path As String, Optional source As String = "") As Task
        Dim state = Await ManifestService.LoadStateAsync()
        state.GamePath = path
        If Not String.IsNullOrWhiteSpace(source) Then state.GameSourceOverride = NormalizeGameSource(source)
        Await ManifestService.SaveStateAsync(state)
    End Function

    Public Async Function SetManualSourceAsync(source As String) As Task
        Dim state = Await ManifestService.LoadStateAsync()
        state.GameSourceOverride = NormalizeGameSource(source)
        Await ManifestService.SaveStateAsync(state)
    End Function

    Public Async Function ClearManualOverrideAsync() As Task
        Dim state = Await ManifestService.LoadStateAsync()
        state.GamePath = ""
        state.GameSourceOverride = GameSourceAuto
        Await ManifestService.SaveStateAsync(state)
    End Function

    Public Async Function SetGameSavePathAsync(path As String) As Task
        If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException("Game save path cannot be empty.", NameOf(path))
        Dim state = Await ManifestService.LoadStateAsync()
        state.GameSavePathOverride = IO.Path.GetFullPath(path)
        Await ManifestService.SaveStateAsync(state)
    End Function

    Public Async Function ClearGameSavePathAsync() As Task
        Dim state = Await ManifestService.LoadStateAsync()
        state.GameSavePathOverride = ""
        Await ManifestService.SaveStateAsync(state)
    End Function

    Public Async Function WaitForGameExitAsync(Optional startTimeout As TimeSpan = Nothing) As Task(Of Boolean)
        If startTimeout = Nothing Then startTimeout = TimeSpan.FromMinutes(10)
        Dim started = DateTime.UtcNow
        Dim observedRunning = False
        Dim absentSince As Nullable(Of DateTime)
        While DateTime.UtcNow - started < startTimeout OrElse observedRunning
            Dim processes = Await Task.Run(Function() FindGameProcesses(GetBoundInstallPath()))
            Dim running = UpdateBoundProcesses(processes)
            If running Then
                observedRunning = True
                absentSince = Nothing
            ElseIf observedRunning Then
                If Not absentSince.HasValue Then absentSince = DateTime.UtcNow
                If DateTime.UtcNow - absentSince.Value >= TimeSpan.FromSeconds(10) Then Return True
            End If
            Await Task.Delay(TimeSpan.FromSeconds(5))
        End While
        Logger.Warn($"FH6 process binding timed out after {startTimeout}.")
        Return False
    End Function

    Public Function IsGameRunning() As Boolean
        Return FindGameProcesses(GetBoundInstallPath()).Count > 0
    End Function

    Public Async Function LaunchAsync(game As GameInstallState) As Task
        If Not game.IsInstalled Then Throw New InvalidOperationException("FH6 is not installed or bound.")
        Dim baseline = Process.GetProcesses().
            Select(Function(candidate)
                       Try
                           Return candidate.Id
                       Finally
                           candidate.Dispose()
                       End Try
                   End Function).
            ToHashSet()
        SyncLock ProcessBindingLock
            LaunchBaselineProcessIds = baseline
            BoundGameProcessIds.Clear()
            BoundGameInstallPath = If(game.InstallPath, "")
        End SyncLock
        Logger.Info($"FH6 launch requested through {game.Source}; captured {baseline.Count} existing process IDs.")
        Try
            Process.Start(New ProcessStartInfo With {.FileName = game.LaunchCommand, .UseShellExecute = True})
        Catch
            SyncLock ProcessBindingLock
                LaunchBaselineProcessIds.Clear()
                BoundGameProcessIds.Clear()
            End SyncLock
            Throw
        End Try
        Dim state = Await ManifestService.LoadStateAsync()
        state.LastGameLaunchAt = DateTimeOffset.Now.ToString("O")
        Await ManifestService.SaveStateAsync(state)
    End Function

    Private Function DetectSteamInstall() As GameInstallState
        Dim steamPath = TryReadRegistryString(Registry.CurrentUser, "Software\Valve\Steam", "SteamPath")
        If String.IsNullOrWhiteSpace(steamPath) Then steamPath = TryReadRegistryString(Registry.LocalMachine, "Software\WOW6432Node\Valve\Steam", "InstallPath")
        If String.IsNullOrWhiteSpace(steamPath) Then Return New GameInstallState

        Dim steamApps = Path.Combine(steamPath.Replace("/"c, "\"c), "steamapps")
        Dim manifests = New List(Of String)
        If Directory.Exists(steamApps) Then manifests.AddRange(Directory.GetFiles(steamApps, "appmanifest_*.acf"))
        Dim libraryFile = Path.Combine(steamApps, "libraryfolders.vdf")
        If File.Exists(libraryFile) Then
            For Each line In File.ReadAllLines(libraryFile)
                If line.Contains("path", StringComparison.OrdinalIgnoreCase) Then
                    Dim parts = line.Split(""""c, StringSplitOptions.RemoveEmptyEntries)
                    Dim candidate = parts.LastOrDefault()
                    If Not String.IsNullOrWhiteSpace(candidate) Then
                        Dim libApps = Path.Combine(candidate.Replace("\\", "\"), "steamapps")
                        If Directory.Exists(libApps) Then manifests.AddRange(Directory.GetFiles(libApps, "appmanifest_*.acf"))
                    End If
                End If
            Next
        End If

        For Each manifest In manifests.Distinct(StringComparer.OrdinalIgnoreCase)
            Dim text = File.ReadAllText(manifest)
            If text.Contains("Forza Horizon 6", StringComparison.OrdinalIgnoreCase) Then
                Dim appId = Path.GetFileNameWithoutExtension(manifest).Replace("appmanifest_", "")
                Return New GameInstallState With {
                    .IsInstalled = True,
                    .Source = "Steam",
                    .InstallPath = manifest,
                    .LaunchCommand = "steam://rungameid/" & appId,
                    .Message = "Steam app manifest detected."
                }
            End If
        Next

        Return New GameInstallState
    End Function

    Private Shared Function DetectManualOverride(state As ToolStateStore) As GameInstallState
        Dim source = NormalizeGameSource(state.GameSourceOverride)
        Select Case source
            Case GameSourceSteam
                If Not String.IsNullOrWhiteSpace(state.GamePath) AndAlso File.Exists(state.GamePath) Then
                    Return New GameInstallState With {
                        .IsInstalled = True,
                        .Source = GameSourceSteam,
                        .InstallPath = state.GamePath,
                        .LaunchCommand = state.GamePath,
                        .LastLaunchAt = state.LastGameLaunchAt,
                        .Message = "Steam version selected manually."
                    }
                End If
                Return New GameInstallState With {
                    .IsInstalled = False,
                    .Source = GameSourceSteam,
                    .InstallPath = state.GamePath,
                    .LastLaunchAt = state.LastGameLaunchAt,
                    .Message = "Steam version selected manually; bind the executable or let automatic Steam detection find the app manifest."
                }
            Case GameSourceXbox
                Dim installPath = If(String.IsNullOrWhiteSpace(state.GamePath), FindXboxInstallPath(), state.GamePath)
                Return New GameInstallState With {
                    .IsInstalled = True,
                    .Source = GameSourceXbox,
                    .InstallPath = installPath,
                    .LaunchCommand = "xbox://game/?title=Forza%20Horizon%206",
                    .LastLaunchAt = state.LastGameLaunchAt,
                    .Message = "Xbox version selected manually."
                }
            Case GameSourceManual
                If Not String.IsNullOrWhiteSpace(state.GamePath) AndAlso (File.Exists(state.GamePath) OrElse Directory.Exists(state.GamePath)) Then
                    Return New GameInstallState With {
                        .IsInstalled = True,
                        .Source = GameSourceManual,
                        .InstallPath = state.GamePath,
                        .LaunchCommand = state.GamePath,
                        .LastLaunchAt = state.LastGameLaunchAt,
                        .Message = "Manual game path is configured."
                    }
                End If
        End Select
        Return New GameInstallState
    End Function

    Private Function DetectRegistryInstall() As GameInstallState
        For Each root In {Registry.CurrentUser, Registry.LocalMachine}
            For Each subKey In {"Software\Microsoft\Windows\CurrentVersion\Uninstall", "Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"}
                Using key = root.OpenSubKey(subKey)
                    If key Is Nothing Then Continue For
                    For Each name In key.GetSubKeyNames()
                        Using app = key.OpenSubKey(name)
                            Dim display = TryCast(app?.GetValue("DisplayName"), String)
                            If String.IsNullOrWhiteSpace(display) OrElse Not display.Contains("Forza Horizon 6", StringComparison.OrdinalIgnoreCase) Then Continue For
                            Dim path = TryCast(app.GetValue("InstallLocation"), String)
                            If String.IsNullOrWhiteSpace(path) OrElse Not Directory.Exists(path) Then
                                path = FindXboxInstallPath()
                            End If
                            Return New GameInstallState With {
                                .IsInstalled = True,
                                .Source = "Xbox",
                                .InstallPath = If(path, ""),
                                .LaunchCommand = "xbox://game/?title=Forza%20Horizon%206",
                                .Message = "Xbox app registry entry detected."
                            }
                        End Using
                    Next
                End Using
            Next
        Next
        Return New GameInstallState
    End Function

    Private Shared Function DetectXboxInstall() As GameInstallState
        Dim installPath = FindXboxInstallPath()
        If String.IsNullOrWhiteSpace(installPath) Then Return New GameInstallState
        Return New GameInstallState With {
            .IsInstalled = True,
            .Source = "Xbox",
            .InstallPath = installPath,
            .LaunchCommand = "xbox://game/?title=Forza%20Horizon%206",
            .Message = "Xbox game install folder detected."
        }
    End Function

    Private Shared Function FindXboxInstallPath() As String
        For Each drive In DriveInfo.GetDrives()
            Try
                If drive.DriveType <> DriveType.Fixed OrElse Not drive.IsReady Then Continue For
                Dim candidate = Path.Combine(drive.RootDirectory.FullName, "XboxGames", "Forza Horizon 6")
                If Directory.Exists(candidate) Then Return candidate
            Catch
                ' Ignore drives that cannot be queried.
            End Try
        Next
        Return ""
    End Function

    Private Shared Function CompleteGameState(game As GameInstallState, state As ToolStateStore) As GameInstallState
        If Not String.IsNullOrWhiteSpace(state.GameSavePathOverride) Then
            game.SavePath = state.GameSavePathOverride
        End If
        Return game
    End Function

    Private Function GetBoundInstallPath() As String
        SyncLock ProcessBindingLock
            Return BoundGameInstallPath
        End SyncLock
    End Function

    Private Function UpdateBoundProcesses(processes As List(Of GameProcessInfo)) As Boolean
        SyncLock ProcessBindingLock
            Dim currentIds = processes.Select(Function(candidate) candidate.Id).ToHashSet()
            For Each candidate In processes
                If BoundGameProcessIds.Contains(candidate.Id) OrElse LaunchBaselineProcessIds.Contains(candidate.Id) Then Continue For
                BoundGameProcessIds.Add(candidate.Id)
                Logger.Info($"Bound FH6 process PID={candidate.Id}, name={candidate.Name}, path={If(candidate.ExecutablePath, "(unavailable)")}.")
            Next
            BoundGameProcessIds.RemoveWhere(Function(processId) Not currentIds.Contains(processId))
            Return BoundGameProcessIds.Count > 0
        End SyncLock
    End Function

    Private Shared Function FindGameProcesses(installPath As String) As List(Of GameProcessInfo)
        Dim result As New List(Of GameProcessInfo)
        Try
            For Each candidateProcess As Process In Process.GetProcesses()
                Try
                    Dim processName = candidateProcess.ProcessName
                    Dim executablePath As String = ""
                    Try
                        executablePath = candidateProcess.MainModule?.FileName
                    Catch
                        ' Protected or packaged processes may not expose their executable path.
                    End Try
                    If MatchesGameProcess(processName, executablePath, installPath) Then
                        result.Add(New GameProcessInfo With {
                            .Id = candidateProcess.Id,
                            .Name = processName,
                            .ExecutablePath = executablePath
                        })
                    End If
                Catch
                    ' The process may exit while it is being inspected.
                Finally
                    candidateProcess.Dispose()
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "FH6 process scan failed.")
        End Try
        Return result
    End Function

    Private Shared Function MatchesGameProcess(processName As String, executablePath As String, installPath As String) As Boolean
        Dim normalizedName = New String(If(processName, "").
            Where(Function(character) Char.IsLetterOrDigit(character)).
            Select(Function(character) Char.ToLowerInvariant(character)).
            ToArray())
        If normalizedName.Contains("forzahorizon6", StringComparison.Ordinal) Then Return True

        If String.IsNullOrWhiteSpace(executablePath) OrElse String.IsNullOrWhiteSpace(installPath) Then Return False
        Try
            Dim root = If(Directory.Exists(installPath), installPath, Path.GetDirectoryName(installPath))
            If String.IsNullOrWhiteSpace(root) Then Return False
            Dim normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim normalizedExecutable = Path.GetFullPath(executablePath)
            Return normalizedExecutable.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    Private Shared Function TryReadRegistryString(root As RegistryKey, path As String, name As String) As String
        Try
            Using key = root.OpenSubKey(path)
                Return TryCast(key?.GetValue(name), String)
            End Using
        Catch
            Return ""
        End Try
    End Function

    Public Shared Function NormalizeGameSource(value As String) As String
        Select Case If(value, "").Trim().ToLowerInvariant()
            Case "steam"
                Return GameSourceSteam
            Case "xbox", "store", "microsoft store", "microsoftstore"
                Return GameSourceXbox
            Case "manual"
                Return GameSourceManual
            Case Else
                Return GameSourceAuto
        End Select
    End Function
End Class

Friend Class GameProcessInfo
    Public Property Id As Integer
    Public Property Name As String = ""
    Public Property ExecutablePath As String = ""
End Class
