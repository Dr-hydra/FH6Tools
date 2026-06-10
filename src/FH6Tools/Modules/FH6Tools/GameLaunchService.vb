Imports Microsoft.Win32

Public Class GameLaunchService
    Private ReadOnly ManifestService As New ToolManifestService

    Public Async Function DetectAsync() As Task(Of GameInstallState)
        Dim state = Await ManifestService.LoadStateAsync()
        If Not String.IsNullOrWhiteSpace(state.GamePath) AndAlso File.Exists(state.GamePath) Then
            Return New GameInstallState With {
                .IsInstalled = True,
                .Source = "Manual",
                .InstallPath = state.GamePath,
                .LaunchCommand = state.GamePath,
                .LastLaunchAt = state.LastGameLaunchAt,
                .Message = "Manual game path is configured."
            }
        End If

        Dim steam = DetectSteamInstall()
        If steam.IsInstalled Then
            steam.LastLaunchAt = state.LastGameLaunchAt
            Return steam
        End If

        Dim microsoftStore = DetectMicrosoftStoreInstall()
        If microsoftStore.IsInstalled Then
            microsoftStore.LastLaunchAt = state.LastGameLaunchAt
            Return microsoftStore
        End If

        Dim registry = DetectRegistryInstall()
        If registry.IsInstalled Then
            registry.LastLaunchAt = state.LastGameLaunchAt
            Return registry
        End If

        Return New GameInstallState With {
            .IsInstalled = False,
            .Message = "FH6 was not detected. Bind the executable or launch it from Steam/Xbox first."
        }
    End Function

    Public Async Function BindManualPathAsync(path As String) As Task
        Dim state = Await ManifestService.LoadStateAsync()
        state.GamePath = path
        Await ManifestService.SaveStateAsync(state)
    End Function

    Public Async Function WaitForGameExitAsync(Optional startTimeout As TimeSpan = Nothing) As Task(Of Boolean)
        If startTimeout = Nothing Then startTimeout = TimeSpan.FromMinutes(10)
        Dim started = DateTime.UtcNow
        Dim observedRunning = False
        Dim absentSince As Nullable(Of DateTime)
        While DateTime.UtcNow - started < startTimeout OrElse observedRunning
            Dim running = Await Task.Run(AddressOf IsGameProcessRunning)
            If running Then
                observedRunning = True
                absentSince = Nothing
            ElseIf observedRunning Then
                If Not absentSince.HasValue Then absentSince = DateTime.UtcNow
                If DateTime.UtcNow - absentSince.Value >= TimeSpan.FromSeconds(10) Then Return True
            End If
            Await Task.Delay(TimeSpan.FromSeconds(5))
        End While
        Return False
    End Function

    Public Function IsGameRunning() As Boolean
        Return IsGameProcessRunning()
    End Function

    Public Async Function LaunchAsync(game As GameInstallState) As Task
        If Not game.IsInstalled Then Throw New InvalidOperationException("FH6 is not installed or bound.")
        Process.Start(New ProcessStartInfo With {.FileName = game.LaunchCommand, .UseShellExecute = True})
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

    Private Shared Function DetectMicrosoftStoreInstall() As GameInstallState
        Dim savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Packages", "Microsoft.624F8B84B80_8wekyb3d8bbwe", "SystemAppData", "wgs")
        If Not Directory.Exists(savePath) Then Return New GameInstallState
        Return New GameInstallState With {
            .IsInstalled = True,
            .Source = "Microsoft Store",
            .InstallPath = Path.GetDirectoryName(Path.GetDirectoryName(savePath)),
            .LaunchCommand = "xbox://game/?title=Forza%20Horizon%206",
            .Message = "Microsoft Store save data detected."
        }
    End Function

    Private Shared Function IsGameProcessRunning() As Boolean
        Try
            Dim found = False
            For Each candidateProcess As Process In Process.GetProcesses()
                Try
                    found = found OrElse candidateProcess.ProcessName.Contains("ForzaHorizon6", StringComparison.OrdinalIgnoreCase)
                Catch
                    ' Some protected processes do not expose their names.
                Finally
                    candidateProcess.Dispose()
                End Try
            Next
            Return found
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
End Class
