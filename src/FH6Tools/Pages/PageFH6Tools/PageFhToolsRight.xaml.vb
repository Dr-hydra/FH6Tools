Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports Microsoft.Win32

Public Class PageFhToolsRight
    Private ReadOnly ManifestService As New ToolManifestService
    Private ReadOnly RuntimeService As New ToolRuntimeService
    Private ReadOnly InstallService As New ToolInstallService
    Private ReadOnly GameService As New GameLaunchService
    Private ReadOnly GameBackupService As New GameBackupService
    Private ReadOnly UpdateService As New AppUpdateService
    Private ReadOnly ToolCardsSource As New ObservableCollection(Of ToolCardViewModel)
    Private ReadOnly InstalledToolCardsSource As New ObservableCollection(Of ToolCardViewModel)
    Private ReadOnly RuntimeRefreshTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(2)}
    Private CurrentTools As List(Of ToolManifestEntry) = New List(Of ToolManifestEntry)
    Private CurrentState As ToolStateStore = New ToolStateStore
    Private CurrentGame As GameInstallState = New GameInstallState
    Private ReadOnly PendingInstallTools As New Queue(Of DownloadTaskViewModel)
    Private IsDownloadQueueRunning As Boolean
    Private IsRuntimeRefreshRunning As Boolean
    Private RuntimeRefreshStarted As Boolean
    Private GameBackupMonitor As Task = Task.CompletedTask
    Private CurrentGameRunning As Boolean
    Private CurrentPage As FhShellPage = FhShellPage.Home

    Public ReadOnly Property GameSummary As String
        Get
            Return If(CurrentGame IsNot Nothing AndAlso CurrentGame.IsInstalled,
                      FhLanguage.Text("已通过 " & CurrentGame.Source & " 检测到游戏", "Detected via " & CurrentGame.Source),
                      FhLanguage.Text("未检测到游戏", "Not detected"))
        End Get
    End Property

    Public ReadOnly Property ToolsSummary As String
        Get
            Return FhLanguage.Text($"已加载 {ToolCardsSource.Count} 个工具", $"{ToolCardsSource.Count} tools loaded")
        End Get
    End Property

    Public ReadOnly Property GamePathSummary As String
        Get
            Return FhLanguage.Text("游戏路径：", "Game path: ") & If(String.IsNullOrWhiteSpace(CurrentGame.InstallPath), CurrentGame.Message, CurrentGame.InstallPath)
        End Get
    End Property

    Public ReadOnly Property IsGameInstalled As Boolean
        Get
            Return CurrentGame IsNot Nothing AndAlso CurrentGame.IsInstalled
        End Get
    End Property

    Public ReadOnly Property IsGameRunning As Boolean
        Get
            Return CurrentGameRunning
        End Get
    End Property

    Public Sub ShowGameBackupManager()
        Dim manager As New FormGameBackupManager(CurrentGame) With {.Owner = Window.GetWindow(Me)}
        manager.ShowDialog()
    End Sub

    Public Async Function InitializeAsync() As Task
        FhPaths.Ensure()
        DownloadToolCards.ItemsSource = ToolCardsSource
        InstalledToolCards.ItemsSource = InstalledToolCardsSource
        ConfigToolCards.ItemsSource = InstalledToolCardsSource
        RadioLanguageZh.SetChecked(Not FhLanguage.IsEnglish, False)
        RadioLanguageEn.SetChecked(FhLanguage.IsEnglish, False)
        RadioStartupOn.SetChecked(IsStartupEnabled(), False)
        RadioStartupOff.SetChecked(Not IsStartupEnabled(), False)
        Await LoadToolsFastAsync()
        Configure(CurrentPage)
        ApplyLanguage()
        Dim gameTask = RefreshGameAsync()
        Dim manifestTask = RefreshManifestAndToolsAsync()
        Await Task.WhenAll(gameTask, manifestTask)
        StartRuntimeRefresh()
    End Function

    Public Async Sub InitializeDeferred()
        Try
            Await InitializeAsync()
            Await CheckAppUpdateAsync(False)
        Catch ex As Exception
            Logger.Error(ex, "Deferred initialization failed.", LogBehavior.Toast)
        End Try
    End Sub

    Public Sub Configure(page As FhShellPage)
        CurrentPage = page
        PanHome.Visibility = If(page = FhShellPage.Home, Visibility.Visible, Visibility.Collapsed)
        PanInstalledTools.Visibility = If(page = FhShellPage.Tools, Visibility.Visible, Visibility.Collapsed)
        CardDownloads.Visibility = If(page = FhShellPage.Downloads, Visibility.Visible, Visibility.Collapsed)
        DownloadToolCards.Visibility = CardDownloads.Visibility
        CardConfig.Visibility = If(page = FhShellPage.Config, Visibility.Visible, Visibility.Collapsed)
        CardGameData.Visibility = If(page = FhShellPage.GameData, Visibility.Visible, Visibility.Collapsed)
        CardAbout.Visibility = If(page = FhShellPage.About, Visibility.Visible, Visibility.Collapsed)
        CardRuntimeInfo.Visibility = If(page = FhShellPage.RuntimeInfo, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub StartRuntimeRefresh()
        If RuntimeRefreshStarted Then Return
        RuntimeRefreshStarted = True
        AddHandler RuntimeRefreshTimer.Tick, AddressOf RuntimeRefreshTimer_Tick
        RuntimeRefreshTimer.Start()
    End Sub

    Private Async Sub RuntimeRefreshTimer_Tick(sender As Object, e As EventArgs)
        If CurrentPage <> FhShellPage.Home OrElse IsRuntimeRefreshRunning Then Return
        IsRuntimeRefreshRunning = True
        Try
            CurrentGameRunning = Await Task.Run(Function() GameService.IsGameRunning())
            FrmMain?.UpdateShellStatus(GameSummary, ToolsSummary)
            If CurrentTools.Count = 0 Then Return
            Dim statusTasks = CurrentTools.Select(Async Function(tool) New ToolCardViewModel(tool, Await RuntimeService.GetStatusAsync(tool), InstallService.IsUpdateAvailable(tool), GetToolState(tool.Id))).ToArray()
            Dim cards = Await Task.WhenAll(statusTasks)
            InstalledToolCardsSource.Clear()
            For Each card In cards
                If card.Status.IsInstalled Then InstalledToolCardsSource.Add(card)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "Automatic tool status refresh failed.")
        Finally
            IsRuntimeRefreshRunning = False
        End Try
    End Sub

    Public Sub ApplyLanguage()
        CardDownloads.Title = FhLanguage.Text("下载管理", "Downloads")
        BtnReloadManifest.Text = FhLanguage.Text("检查更新", "Check Updates")
        BtnChangeInstallRoot.Text = FhLanguage.Text("更改安装位置", "Change Install Location")
        BtnImportZip.Text = FhLanguage.Text("导入 ZIP", "Import ZIP")
        BtnImportFolder.Text = FhLanguage.Text("添加文件夹", "Add Folder")
        CardConfig.Title = FhLanguage.Text("常规设置", "General Settings")
        CardGameData.Title = FhLanguage.Text("游戏与数据", "Game and Data")
        LabLanguageTitle.Text = FhLanguage.Text("界面语言", "Interface Language")
        LabLanguageHint.Text = FhLanguage.Text("默认使用简体中文，切换后立即生效。", "Simplified Chinese is the default. Changes apply immediately.")
        BtnBindGame.Text = FhLanguage.Text("绑定游戏路径", "Bind Game Path")
        BtnRefreshGame.Text = FhLanguage.Text("重新检测游戏", "Detect Game Again")
        BtnOpenGameFolder.Text = FhLanguage.Text("打开游戏目录", "Open Game Folder")
        BtnOpenDataRoot.Text = FhLanguage.Text("打开数据目录", "Open Data Folder")
        CardAbout.Title = FhLanguage.Text("关于 FH6Tools", "About FH6Tools")
        CardRuntimeInfo.Title = FhLanguage.Text("运行时与安全", "Runtime and Safety")
        LabAboutDescription.Text = FhLanguage.Text("FH6Tools 是用于启动地平线 6 和管理社区工具的本地 Windows 工具中心。", "FH6Tools is a local Windows launcher and control center for Forza Horizon 6 community tools.")
        ItemAboutSafety.Title = FhLanguage.Text("安全策略", "Safety Policy")
        ItemAboutSafety.Info = FhLanguage.Text("违反项目安全策略的条目不会被安装或启动。", "Entries that violate the project safety policy are not installed or launched.")
        ItemAboutManifest.Title = FhLanguage.Text("工具清单", "Tool Manifest")
        ItemAboutManifest.Info = FhLanguage.Text("工具列表会在软件启动时动态更新。", "The tool list is refreshed dynamically when the app starts.")
        ItemAboutUpdateProtection.Title = FhLanguage.Text("更新数据保护", "Update Data Protection")
        ItemAboutUpdateProtection.Info = FhLanguage.Text("软件更新只覆盖程序文件，会保留配置、存档备份和已安装工具。",
                                                          "App updates replace only program files and preserve configuration, save backups, and installed tools.")
        SetUiCreditText()
        SetProjectAddressText()
        If String.IsNullOrWhiteSpace(LabAppUpdateStatus.Text) OrElse
           LabAppUpdateStatus.Text = "尚未检查软件更新。" OrElse
           LabAppUpdateStatus.Text = "App updates have not been checked yet." Then
            LabAppUpdateStatus.Text = FhLanguage.Text("尚未检查软件更新。", "App updates have not been checked yet.")
        End If
        ItemAboutRuntime.Title = FhLanguage.Text("共享运行时", "Shared Runtime")
        ItemAboutRuntime.Info = FhLanguage.Text(".NET 10 Desktop 与 ASP.NET Core Runtime 由所有托管程序共享。", ".NET 10 Desktop and ASP.NET Core Runtime are shared by all managed programs.")
        LabInstalledToolSummary.Text = FhLanguage.Text($"已安装工具：{InstalledToolCardsSource.Count} 个", $"Installed tools: {InstalledToolCardsSource.Count}")
        LabConfigToolSummary.Text = LabInstalledToolSummary.Text
        Configure(CurrentPage)
    End Sub

    Private Async Function RefreshGameAsync() As Task
        CurrentGame = Await GameService.DetectAsync()
        CurrentGameRunning = Await Task.Run(Function() GameService.IsGameRunning())
        BtnOpenGameFolder.IsEnabled = CurrentGame.IsInstalled AndAlso Not String.IsNullOrWhiteSpace(CurrentGame.InstallPath)
        LabGameDetectionStatus.Text = If(CurrentGame.IsInstalled,
                                         FhLanguage.Text($"已检测到 {CurrentGame.Source} 版本", $"Detected {CurrentGame.Source} version"),
                                         FhLanguage.Text("未检测到游戏", "Game not detected"))
        Configure(CurrentPage)
        FrmMain?.UpdateShellStatus(GameSummary, ToolsSummary)
    End Function

    Private Async Function RefreshToolsAsync() As Task
        CurrentTools = Await ManifestService.LoadToolsAsync()
        CurrentState = Await ManifestService.LoadStateAsync()
        ApplyRuntimeOverrides()
        Dim statusTasks = CurrentTools.Select(Async Function(tool) New ToolCardViewModel(tool, Await RuntimeService.GetStatusAsync(tool), InstallService.IsUpdateAvailable(tool), GetToolState(tool.Id))).ToArray()
        Dim cards = Await Task.WhenAll(statusTasks)
        ToolCardsSource.Clear()
        InstalledToolCardsSource.Clear()
        For Each card In cards
            ToolCardsSource.Add(card)
            If card.Status.IsInstalled Then InstalledToolCardsSource.Add(card)
        Next
        LabInstalledToolSummary.Text = FhLanguage.Text($"已安装工具：{InstalledToolCardsSource.Count} 个", $"Installed tools: {InstalledToolCardsSource.Count}")
        LabConfigToolSummary.Text = LabInstalledToolSummary.Text
        Configure(CurrentPage)
        FrmMain?.UpdateShellStatus(GameSummary, ToolsSummary)
    End Function

    Private Async Function LoadToolsFastAsync() As Task
        CurrentTools = Await ManifestService.LoadToolsAsync()
        CurrentState = Await ManifestService.LoadStateAsync()
        ApplyRuntimeOverrides()
        ToolCardsSource.Clear()
        InstalledToolCardsSource.Clear()
        For Each tool In CurrentTools
            Dim status As New ToolRuntimeStatus With {
                .ToolId = tool.Id,
                .IsInstalled = InstallService.IsInstalled(tool),
                .Message = If(InstallService.IsInstalled(tool), "ready", "not installed")
            }
            Dim card = New ToolCardViewModel(tool, status, InstallService.IsUpdateAvailable(tool), GetToolState(tool.Id))
            ToolCardsSource.Add(card)
            If status.IsInstalled Then InstalledToolCardsSource.Add(card)
        Next
        LabInstalledToolSummary.Text = FhLanguage.Text($"已安装工具：{InstalledToolCardsSource.Count} 个", $"Installed tools: {InstalledToolCardsSource.Count}")
        LabConfigToolSummary.Text = LabInstalledToolSummary.Text
    End Function

    Private Async Function RefreshManifestAndToolsAsync() As Task
        Await ManifestService.RefreshDynamicManifestAsync()
        Await RefreshToolsAsync()
    End Function

    Private Function GetToolFromSender(sender As Object) As ToolManifestEntry
        Dim element = TryCast(sender, FrameworkElement)
        Dim viewModel = TryCast(element?.DataContext, ToolCardViewModel)
        Return viewModel?.Tool
    End Function

    Private Async Function RunToolActionAsync(sender As Object, action As Func(Of ToolManifestEntry, Task), successMessage As String) As Task
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Dim portConflict As ToolPortConflictException = Nothing
        Try
            Await action(tool)
            Await RefreshToolsAsync()
            Hint(successMessage, HintType.Green)
        Catch ex As ToolPortConflictException
            portConflict = ex
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
        If portConflict IsNot Nothing Then Await ResolvePortConflictAsync(portConflict.Tool, portConflict.Port)
    End Function

    Private Async Function ResolvePortConflictAsync(tool As ToolManifestEntry, port As Integer) As Task
        Dim result = MessageBox.Show(
            $"Port {port} is already in use." & vbCrLf & vbCrLf &
            "Yes: reuse the existing service." & vbCrLf &
            "No: stop the process using this port." & vbCrLf &
            "Cancel: choose another port.",
            "Port conflict",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning)

        Try
            If result = MessageBoxResult.Yes Then
                Await RuntimeService.StartBackendAsync(tool, reuseExistingService:=True)
            ElseIf result = MessageBoxResult.No Then
                Await RuntimeService.StartBackendAsync(tool, stopPortOwner:=True)
            ElseIf result = MessageBoxResult.Cancel Then
                Dim input = Microsoft.VisualBasic.Interaction.InputBox("Enter a replacement local port.", "Port", port.ToString())
                Dim newPort As Integer
                If Integer.TryParse(input, newPort) AndAlso newPort > 0 AndAlso newPort <= 65535 Then
                    RuntimeService.SetPortOverride(tool, newPort)
                    Await RuntimeService.StartBackendAsync(tool)
                End If
            End If
            Await RefreshToolsAsync()
        Catch retryEx As Exception
            Hint(retryEx.Message, HintType.Red)
        End Try
    End Function

    Public Async Function LaunchCurrentGameAsync() As Task
        Try
            CurrentState = Await ManifestService.LoadStateAsync()
            Dim launchEntries = CurrentState.Tools.Where(Function(item) item.LaunchWithGame).ToList()
            If Not ConfirmLaunchConflicts(launchEntries) Then Return
            For Each entry In launchEntries
                Dim tool = CurrentTools.FirstOrDefault(Function(candidate) candidate.Id.Equals(entry.ToolId, StringComparison.OrdinalIgnoreCase))
                If tool Is Nothing OrElse Not InstallService.IsInstalled(tool) Then Continue For
                Dim portConflict As ToolPortConflictException = Nothing
                Try
                    Await RuntimeService.StartAllAsync(tool)
                Catch conflict As ToolPortConflictException
                    portConflict = conflict
                End Try
                If portConflict IsNot Nothing Then Await ResolvePortConflictAsync(portConflict.Tool, portConflict.Port)
            Next
            Try
                Await GameBackupService.BackupAsync(CurrentGame, "before-launch")
            Catch ex As Exception
                Logger.Warn(ex, "Pre-launch game save backup failed.")
                Hint(FhLanguage.Text("启动前存档备份失败，游戏仍会继续启动：", "Pre-launch save backup failed; the game will still launch: ") & ex.Message, HintType.Red)
            End Try
            Await GameService.LaunchAsync(CurrentGame)
            If GameBackupMonitor.IsCompleted Then GameBackupMonitor = BackupAfterGameExitAsync(CurrentGame)
            Await RefreshGameAsync()
            Hint(FhLanguage.Text("已请求启动地平线 6。", "FH6 launch requested."), HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Function

    Private Async Function BackupAfterGameExitAsync(game As GameInstallState) As Task
        Try
            If Not Await GameService.WaitForGameExitAsync() Then Return
            If Not Await GameBackupService.WaitForStableSaveAsync(game) Then Return
            Dim backupPath = Await GameBackupService.BackupAsync(game, "after-exit")
            If Not String.IsNullOrWhiteSpace(backupPath) Then
                Hint(FhLanguage.Text("游戏退出后的存档备份已创建。", "Post-exit save backup created."), HintType.Green)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "Post-exit game save backup failed.")
            Hint(FhLanguage.Text("游戏退出后的存档备份失败：", "Post-exit save backup failed: ") & ex.Message, HintType.Red)
        End Try
    End Function

    Private Function ConfirmLaunchConflicts(entries As List(Of ToolInstallState)) As Boolean
        Dim conflicts As New List(Of String)
        Dim runningIds = New HashSet(Of String)(InstalledToolCardsSource.Where(Function(card) card.Status.IsRunning).Select(Function(card) card.Tool.Id), StringComparer.OrdinalIgnoreCase)
        Dim relevantEntries = entries.Concat(CurrentState.Tools.Where(Function(item) runningIds.Contains(item.ToolId))).
            GroupBy(Function(item) item.ToolId, StringComparer.OrdinalIgnoreCase).Select(Function(candidateGroup) candidateGroup.First()).ToList()
        Dim servicePorts = relevantEntries.Select(Function(item)
                                                      Dim tool = CurrentTools.FirstOrDefault(Function(candidate) candidate.Id.Equals(item.ToolId, StringComparison.OrdinalIgnoreCase))
                                                      Return If(tool Is Nothing, 0, RuntimeService.GetConfiguredPort(tool))
                                                  End Function)
        For Each portGroup In servicePorts.Where(Function(port) port > 0).GroupBy(Function(port) port).Where(Function(candidate) candidate.Count > 1)
            conflicts.Add(FhLanguage.Text($"服务端口 {portGroup.Key} 被多个工具使用", $"Service port {portGroup.Key} is used by multiple tools"))
        Next
        Dim telemetryPorts = relevantEntries.Select(Function(item)
                                                        Dim tool = CurrentTools.FirstOrDefault(Function(candidate) candidate.Id.Equals(item.ToolId, StringComparison.OrdinalIgnoreCase))
                                                        Return If(tool Is Nothing, 0, tool.TelemetryPort)
                                                    End Function)
        For Each telemetryGroup In telemetryPorts.Where(Function(port) port > 0).GroupBy(Function(port) port).Where(Function(candidate) candidate.Count > 1)
            conflicts.Add(FhLanguage.Text($"游戏遥测端口 {telemetryGroup.Key} 被多个工具使用", $"Game telemetry port {telemetryGroup.Key} is used by multiple tools"))
        Next
        For Each hotkeyGroup In relevantEntries.
            Select(Function(item) CurrentTools.FirstOrDefault(Function(candidate) candidate.Id.Equals(item.ToolId, StringComparison.OrdinalIgnoreCase))).
            Where(Function(tool) tool IsNot Nothing).
            SelectMany(Function(tool) If(tool.Hotkeys, New List(Of ToolHotkeyDefinition)).Select(Function(hotkey) hotkey.DefaultValue)).
            Where(Function(value) Not String.IsNullOrWhiteSpace(value)).
            GroupBy(Function(value) value, StringComparer.OrdinalIgnoreCase).
            Where(Function(candidate) candidate.Count > 1)
            conflicts.Add(FhLanguage.Text($"热键 {hotkeyGroup.Key} 被多个工具使用", $"Hotkey {hotkeyGroup.Key} is used by multiple tools"))
        Next
        If conflicts.Count = 0 Then Return True
        Return MessageBox.Show(String.Join(vbCrLf, conflicts) & vbCrLf & vbCrLf & FhLanguage.Text("是否仍然继续？", "Continue anyway?"),
                               FhLanguage.Text("启动配置冲突", "Launch configuration conflict"),
                               MessageBoxButton.YesNo, MessageBoxImage.Warning) = MessageBoxResult.Yes
    End Function

    Private Function GetToolState(toolId As String) As ToolInstallState
        Dim state = CurrentState.Tools.FirstOrDefault(Function(item) item.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase))
        If state IsNot Nothing Then Return state
        Dim tool = CurrentTools.FirstOrDefault(Function(item) item.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase))
        state = New ToolInstallState With {
            .ToolId = toolId,
            .RunAsAdministrator = tool?.RequiresAdmin
        }
        CurrentState.Tools.Add(state)
        Return state
    End Function

    Private Sub ApplyRuntimeOverrides()
        For Each tool In CurrentTools
            Dim state = GetToolState(tool.Id)
            RuntimeService.SetRunAsAdministrator(tool, state.RunAsAdministrator)
        Next
    End Sub

    Private Async Sub ToolLaunchWithGame_Change(sender As Object, user As Boolean)
        If Not user Then Return
        Dim checkbox = TryCast(sender, MyCheckBox)
        Dim viewModel = TryCast(checkbox?.DataContext, ToolCardViewModel)
        If viewModel Is Nothing Then Return
        CurrentState = Await ManifestService.LoadStateAsync()
        GetToolState(viewModel.Tool.Id).LaunchWithGame = checkbox.Checked
        Await ManifestService.SaveStateAsync(CurrentState)
    End Sub

    Private Async Sub ToolRunAsAdministrator_Change(sender As Object, user As Boolean)
        If Not user Then Return
        Dim checkbox = TryCast(sender, MyCheckBox)
        Dim viewModel = TryCast(checkbox?.DataContext, ToolCardViewModel)
        If viewModel Is Nothing Then Return
        CurrentState = Await ManifestService.LoadStateAsync()
        GetToolState(viewModel.Tool.Id).RunAsAdministrator = checkbox.Checked
        RuntimeService.SetRunAsAdministrator(viewModel.Tool, checkbox.Checked)
        Await ManifestService.SaveStateAsync(CurrentState)
    End Sub

    Private Async Sub BtnBindGame_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnBindGame.Click
        Dim dialog As New OpenFileDialog With {.Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*", .Title = "Bind FH6 executable"}
        If dialog.ShowDialog() Then
            Await GameService.BindManualPathAsync(dialog.FileName)
            Await RefreshGameAsync()
        End If
    End Sub

    Private Async Sub BtnRefreshGame_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRefreshGame.Click
        Await RefreshGameAsync()
    End Sub

    Private Sub BtnOpenGameFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenGameFolder.Click
        Try
            Dim path As String = If(Directory.Exists(CurrentGame.InstallPath), CurrentGame.InstallPath, IO.Path.GetDirectoryName(CurrentGame.InstallPath))
            If Not String.IsNullOrWhiteSpace(path) AndAlso Directory.Exists(path) Then Process.Start(New ProcessStartInfo With {.FileName = path, .UseShellExecute = True})
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub BtnReloadManifest_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnReloadManifest.Click
        Await RefreshManifestAndToolsAsync()
        LabDownloadStatus.Text = "Manifest reloaded from " & FhPaths.ManifestPath
    End Sub

    Private Sub BtnChangeInstallRoot_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnChangeInstallRoot.Click
        Dim dialog As New OpenFolderDialog With {.Title = FhLanguage.Text("选择工具安装位置", "Choose tool install location"), .InitialDirectory = FhPaths.ToolsRoot}
        If dialog.ShowDialog() Then
            FhPaths.SetToolsRoot(dialog.FolderName)
            LabDownloadStatus.Text = FhLanguage.Text("工具安装位置已更改为：", "Tool install location changed to: ") & dialog.FolderName
        End If
    End Sub

    Private Async Sub BtnImportZip_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnImportZip.Click
        Dim dialog As New OpenFileDialog With {.Title = FhLanguage.Text("导入工具压缩包", "Import tool archive"), .Filter = "ZIP archive (*.zip)|*.zip"}
        If Not dialog.ShowDialog() Then Return
        Try
            Dim executable = Await InstallService.ImportZipAsync(dialog.FileName, Threading.CancellationToken.None)
            Await ManifestService.AddLocalToolAsync(executable)
            Await RefreshToolsAsync()
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub BtnImportFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnImportFolder.Click
        Dim dialog As New OpenFolderDialog With {.Title = FhLanguage.Text("添加本地工具文件夹", "Add local tool folder")}
        If Not dialog.ShowDialog() Then Return
        Try
            Dim executable = Await InstallService.ImportFolderAsync(dialog.FolderName, Threading.CancellationToken.None)
            Await ManifestService.AddLocalToolAsync(executable)
            Await RefreshToolsAsync()
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub BtnOpenAppRoot_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenAppRoot.Click
        Process.Start(New ProcessStartInfo With {.FileName = AppContext.BaseDirectory, .UseShellExecute = True})
    End Sub

    Private Sub BtnOpenDataRoot_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenDataRoot.Click
        FhPaths.Ensure()
        Process.Start(New ProcessStartInfo With {.FileName = FhPaths.AppDataRoot, .UseShellExecute = True})
    End Sub

    Private Async Sub RadioLanguage_Check(sender As Object, e As RouteEventArgs) Handles RadioLanguageZh.Check, RadioLanguageEn.Check
        If Not IsLoaded Then Return
        Dim radio = TryCast(sender, FrameworkElement)
        Dim language = CStr(radio?.Tag)
        If String.Equals(language, FhLanguage.Current, StringComparison.OrdinalIgnoreCase) Then Return
        FhLanguage.SetLanguage(language)
        FrmMain?.ApplyLanguage()
        Await RefreshToolsAsync()
    End Sub

    Private Sub RadioStartup_Check(sender As Object, e As RouteEventArgs) Handles RadioStartupOn.Check, RadioStartupOff.Check
        If Not IsLoaded Then Return
        SetStartupEnabled(ReferenceEquals(sender, RadioStartupOn))
    End Sub

    Private Shared Function IsStartupEnabled() As Boolean
        Try
            Using key = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Run")
                Return key?.GetValue("FH6Tools") IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Shared Sub SetStartupEnabled(enabled As Boolean)
        Try
            Using key = Registry.CurrentUser.CreateSubKey("Software\Microsoft\Windows\CurrentVersion\Run")
                If enabled Then
                    key.SetValue("FH6Tools", """" & Environment.ProcessPath & """")
                Else
                    key.DeleteValue("FH6Tools", False)
                End If
            End Using
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub BtnCheckAppUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCheckAppUpdate.Click
        Await CheckAppUpdateAsync(True)
    End Sub

    Private Async Function CheckAppUpdateAsync(interactive As Boolean) As Task
        LabAppUpdateStatus.Text = FhLanguage.Text("正在检查软件更新……", "Checking for app updates...")
        BtnCheckAppUpdate.IsEnabled = False
        Try
            Dim update = Await UpdateService.CheckAsync()
            If Not update.ReleaseAvailable Then
                LabAppUpdateStatus.Text = FhLanguage.Text("当前没有已发布版本可供检查。", "No published release is available.")
                If interactive Then MessageBox.Show(LabAppUpdateStatus.Text, "FH6Tools", MessageBoxButton.OK, MessageBoxImage.Information)
                Return
            End If
            If Not update.UpdateAvailable Then
                LabAppUpdateStatus.Text = FhLanguage.Text($"当前已是最新版本（{update.CurrentVersion}）。", $"FH6Tools is up to date ({update.CurrentVersion}).")
                If interactive Then MessageBox.Show(LabAppUpdateStatus.Text, "FH6Tools", MessageBoxButton.OK, MessageBoxImage.Information)
                Return
            End If

            LabAppUpdateStatus.Text = FhLanguage.Text($"发现新版本 {update.LatestVersion}，当前版本为 {update.CurrentVersion}。",
                                                      $"Version {update.LatestVersion} is available. Current version: {update.CurrentVersion}.")
            If Not interactive Then Return
            If String.IsNullOrWhiteSpace(update.ZipDownloadUrl) Then
                MessageBox.Show(FhLanguage.Text("发现新版本，但该版本没有 ZIP 更新包。将打开发布页面。",
                                                "A new version is available, but it has no ZIP update package. The release page will open."),
                                "FH6Tools", MessageBoxButton.OK, MessageBoxImage.Information)
                Process.Start(New ProcessStartInfo With {.FileName = update.ReleaseUrl, .UseShellExecute = True})
                Return
            End If
            Dim confirm = MessageBox.Show(
                FhLanguage.Text("是否下载并安装更新？更新只覆盖程序文件，FH6ToolsData 中的配置、存档备份和已安装工具将被保留。",
                                "Download and install the update? Only app files are replaced; configuration, save backups, and installed tools in FH6ToolsData are preserved."),
                "FH6Tools", MessageBoxButton.YesNo, MessageBoxImage.Question)
            If confirm <> MessageBoxResult.Yes Then Return

            Dim progress As New Progress(Of ToolDownloadProgress)(
                Sub(value)
                    LabAppUpdateStatus.Text = FhLanguage.Text($"正在下载更新：{value.Fraction:P0}", $"Downloading update: {value.Fraction:P0}")
                End Sub)
            Await UpdateService.PrepareAndLaunchUpdateAsync(update, progress)
            LabAppUpdateStatus.Text = FhLanguage.Text("更新已准备完成，正在重新启动。", "Update prepared. Restarting...")
            System.Windows.Application.Current.Shutdown()
        Catch ex As Exception
            ' Startup update checks are intentionally silent on network errors.
            LabAppUpdateStatus.Text = FhLanguage.Text("检查更新失败：", "Update check failed: ") & ex.Message
            If interactive Then MessageBox.Show(LabAppUpdateStatus.Text, "FH6Tools", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            BtnCheckAppUpdate.IsEnabled = True
        End Try
    End Function

    Private Sub SetUiCreditText()
        LabUiCredit.Inlines.Clear()
        LabUiCredit.Inlines.Add(New Run(FhLanguage.Text("本项目的 UI 设计借鉴了 ", "The UI design of this project draws inspiration from ")))
        Dim link As New Hyperlink(New Run("Meloong-Git/PCL")) With {.NavigateUri = New Uri("https://github.com/Meloong-Git/PCL")}
        AddHandler link.RequestNavigate, AddressOf UiCredit_RequestNavigate
        LabUiCredit.Inlines.Add(link)
        LabUiCredit.Inlines.Add(New Run("。"))
    End Sub

    Private Sub UiCredit_RequestNavigate(sender As Object, e As Navigation.RequestNavigateEventArgs)
        Process.Start(New ProcessStartInfo With {.FileName = e.Uri.AbsoluteUri, .UseShellExecute = True})
        e.Handled = True
    End Sub

    Private Sub SetProjectAddressText()
        LabProjectAddress.Inlines.Clear()
        LabProjectAddress.Inlines.Add(New Run(FhLanguage.Text("项目地址：", "Project: ")))
        Dim link As New Hyperlink(New Run("Dr-hydra/FH6Tools")) With {.NavigateUri = New Uri("https://github.com/Dr-hydra/FH6Tools")}
        AddHandler link.RequestNavigate, AddressOf ProjectAddress_RequestNavigate
        LabProjectAddress.Inlines.Add(link)
    End Sub

    Private Sub ProjectAddress_RequestNavigate(sender As Object, e As Navigation.RequestNavigateEventArgs)
        Process.Start(New ProcessStartInfo With {.FileName = e.Uri.AbsoluteUri, .UseShellExecute = True})
        e.Handled = True
    End Sub

    Private Async Sub ToolStartAll_Click(sender As Object, e As MouseButtonEventArgs)
        Await RunToolActionAsync(sender, Async Function(tool)
                                           Await RuntimeService.StartAllAsync(tool)
                                       End Function, "Tool started.")
    End Sub

    Private Async Sub IconToolStartAll_Click(sender As Object, e As EventArgs)
        Await RunToolActionAsync(sender, Async Function(tool)
                                           Await RuntimeService.StartAllAsync(tool)
                                       End Function, "Tool started.")
    End Sub

    Private Async Sub ToolStartBackend_Click(sender As Object, e As MouseButtonEventArgs)
        Await RunToolActionAsync(sender, Async Function(tool)
                                           Await RuntimeService.StartBackendAsync(tool)
                                       End Function, "Backend started.")
    End Sub

    Private Async Sub ToolOpenFrontend_Click(sender As Object, e As MouseButtonEventArgs)
        Await RunToolActionAsync(sender, Async Function(tool)
                                           Await RuntimeService.StartFrontendAsync(tool)
                                       End Function, "Frontend opened.")
    End Sub

    Private Async Sub ToolStop_Click(sender As Object, e As MouseButtonEventArgs)
        Await RunToolActionAsync(sender, Function(tool)
                                           RuntimeService.StopTool(tool)
                                           Return Task.CompletedTask
                                       End Function, "Tool stopped.")
    End Sub

    Private Async Sub IconToolStop_Click(sender As Object, e As EventArgs)
        Await RunToolActionAsync(sender, Function(tool)
                                           RuntimeService.StopTool(tool)
                                           Return Task.CompletedTask
                                       End Function, "Tool stopped.")
    End Sub

    Private Async Sub ToolHealthCheck_Click(sender As Object, e As MouseButtonEventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Await RefreshToolsAsync()
        Hint(FhLanguage.Text("健康状态检测已完成。", "Health check completed."), HintType.Green)
    End Sub

    Private Async Sub IconToolHealthCheck_Click(sender As Object, e As EventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Await RefreshToolsAsync()
        Hint(FhLanguage.Text("健康状态检测已完成。", "Health check completed."), HintType.Green)
    End Sub

    Private Async Sub ToolCheckUpdate_Click(sender As Object, e As EventArgs)
        Await RefreshManifestAndToolsAsync()
        Hint(FhLanguage.Text("工具更新状态已刷新。", "Tool update status refreshed."), HintType.Green)
    End Sub

    Private Async Sub ToolInstall_Click(sender As Object, e As MouseButtonEventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Dim task = New DownloadTaskViewModel(tool)
        PendingInstallTools.Enqueue(task)
        LabDownloadStatus.Text = $"Queued {tool.Name}. Pending jobs: {PendingInstallTools.Count}"
        Await ProcessInstallQueueAsync()
    End Sub

    Private Async Sub IconToolInstall_Click(sender As Object, e As EventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Dim task = New DownloadTaskViewModel(tool)
        PendingInstallTools.Enqueue(task)
        LabDownloadStatus.Text = $"Queued {tool.Name}. Pending jobs: {PendingInstallTools.Count}"
        Await ProcessInstallQueueAsync()
    End Sub

    Private Sub ToolOpenProject_Click(sender As Object, e As EventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing OrElse String.IsNullOrWhiteSpace(tool.Homepage) Then Return
        Process.Start(New ProcessStartInfo With {.FileName = tool.Homepage, .UseShellExecute = True})
    End Sub

    Private Async Function ProcessInstallQueueAsync() As Task
        If IsDownloadQueueRunning Then Return
        IsDownloadQueueRunning = True
        Try
            While PendingInstallTools.Count > 0
                Dim downloadTask = PendingInstallTools.Dequeue()
                Dim tool = downloadTask.Tool
                downloadTask.StatusText = FhLanguage.Text("正在下载", "Downloading")
                DownloadProgressBar.Value = 0
                Try
                    Dim progress As New Progress(Of ToolDownloadProgress)(Sub(value)
                                                                            downloadTask.UpdateProgress(value)
                                                                            DownloadProgressBar.Value = downloadTask.Percentage
                                                                            LabDownloadStatus.Text = $"{FhLanguage.Text("正在安装", "Installing")} {tool.Name}：{downloadTask.ProgressText} · {FhLanguage.Text("等待", "Pending")} {PendingInstallTools.Count}"
                                                                        End Sub)
                    Dim installPath = Await InstallService.DownloadAndInstallAsync(tool, progress, Threading.CancellationToken.None)
                    downloadTask.StatusText = FhLanguage.Text("已完成", "Completed")
                    DownloadProgressBar.Value = 100
                    LabDownloadStatus.Text = $"Installed {tool.Name} to {installPath}"
                    Await RefreshToolsAsync()
                Catch ex As Exception
                    downloadTask.StatusText = FhLanguage.Text("失败：", "Failed: ") & ex.Message
                    DownloadProgressBar.Value = 0
                    LabDownloadStatus.Text = $"Install failed for {tool.Name}: {ex.Message}"
                    Hint(ex.Message, HintType.Red)
                End Try
            End While
            LabDownloadStatus.Text &= If(PendingInstallTools.Count = 0, " Queue idle.", "")
        Finally
            IsDownloadQueueRunning = False
        End Try
    End Function

    Private Sub ToolOpenFolder_Click(sender As Object, e As MouseButtonEventArgs)
        OpenToolFolder(sender)
    End Sub

    Private Sub IconToolOpenFolder_Click(sender As Object, e As EventArgs)
        OpenToolFolder(sender)
    End Sub

    Private Sub HeaderToolOpenFolder_Click(sender As Object, e As RouteEventArgs)
        OpenToolFolder(sender)
    End Sub

    Private Async Sub HeaderToolUninstall_Click(sender As Object, e As RouteEventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        If MessageBox.Show(FhLanguage.Text($"确定卸载 {tool.Name}？", $"Uninstall {tool.Name}?"), "FH6Tools", MessageBoxButton.YesNo, MessageBoxImage.Warning) <> MessageBoxResult.Yes Then Return
        Try
            RuntimeService.StopTool(tool)
            InstallService.Uninstall(tool)
            Await RefreshToolsAsync()
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub ConfigToolCard_Loaded(sender As Object, e As RoutedEventArgs)
        Dim card = TryCast(sender, MyCard)
        If card Is Nothing Then Return
        Dim content = card.Children.OfType(Of StackPanel)().FirstOrDefault()
        If content Is Nothing Then Return
        card.SwapControl = content
        card.UseAnimation = False
        SynchronizeConfigToolCardHeight(card)
    End Sub

    Private Sub ConfigToolCard_Swap(sender As Object, e As RouteEventArgs)
        Dim card = TryCast(sender, MyCard)
        If card IsNot Nothing Then SynchronizeConfigToolCardHeight(card)
    End Sub

    Private Shared Sub SynchronizeConfigToolCardHeight(card As MyCard)
        If card.IsSwapped Then
            card.SwapControl.Visibility = Visibility.Collapsed
            card.MinHeight = MyCard.SwapedHeight
            card.MaxHeight = MyCard.SwapedHeight
            card.Height = MyCard.SwapedHeight
        Else
            card.MinHeight = 0
            card.MaxHeight = Double.PositiveInfinity
            card.SwapControl.Visibility = Visibility.Visible
            card.Height = Double.NaN
        End If
        card.InvalidateMeasure()
        card.InvalidateArrange()
    End Sub

    Private Sub OpenToolFolder(sender As Object)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Try
            Dim root = RuntimeService.GetToolRoot(tool)
            Dim folder = If(File.Exists(root), Path.GetDirectoryName(root), root)
            If Directory.Exists(folder) Then
                Process.Start(New ProcessStartInfo With {.FileName = folder, .UseShellExecute = True})
            Else
                Hint("Tool folder does not exist yet.", HintType.Blue)
            End If
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

End Class

Public Class ToolCardViewModel
    Public ReadOnly Property Tool As ToolManifestEntry
    Public ReadOnly Property Status As ToolRuntimeStatus
    Public ReadOnly Property UpdateAvailable As Boolean
    Public ReadOnly Property LaunchWithGame As Boolean
    Public ReadOnly Property RunAsAdministrator As Boolean
    Private ReadOnly InstallLocation As String
    Private ReadOnly InstalledVersion As String
    Private ReadOnly InstalledAt As Nullable(Of DateTime)

    Public Sub New(tool As ToolManifestEntry, status As ToolRuntimeStatus, Optional updateAvailable As Boolean = False, Optional state As ToolInstallState = Nothing)
        Me.Tool = tool
        Me.Status = status
        Me.UpdateAvailable = updateAvailable
        Me.LaunchWithGame = state?.LaunchWithGame
        Me.RunAsAdministrator = If(state Is Nothing, tool.RequiresAdmin, state.RunAsAdministrator)
        Dim installService As New ToolInstallService
        InstallLocation = installService.GetInstallLocation(tool)
        InstalledVersion = installService.GetInstalledVersion(tool)
        InstalledAt = installService.GetInstalledAt(tool)
    End Sub

    Public ReadOnly Property DisplayName As String
        Get
            Return Tool.Name
        End Get
    End Property

    Public ReadOnly Property HasBackend As Boolean
        Get
            Return Tool.Backend IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property BackendVisibility As Visibility
        Get
            Return If(HasBackend, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property InstallLocationLine As String
        Get
            Return FhLanguage.Text("安装位置：", "Install location: ") & InstallLocation
        End Get
    End Property

    Public ReadOnly Property InstalledVersionLine As String
        Get
            Return FhLanguage.Text("安装版本：", "Installed version: ") & InstalledVersion
        End Get
    End Property

    Public ReadOnly Property InstallTimeLine As String
        Get
            Dim value = If(InstalledAt.HasValue, InstalledAt.Value.ToString("yyyy-MM-dd HH:mm:ss"), "-")
            Return FhLanguage.Text("安装时间：", "Installed at: ") & value
        End Get
    End Property

    Public ReadOnly Property RuntimeDisplayName As String
        Get
            Return $"{Tool.Name}  ·  {FhLanguage.Text(If(Status.IsRunning, "正在运行", "未运行"), If(Status.IsRunning, "Running", "Not running"))}"
        End Get
    End Property

    Public ReadOnly Property RuntimeStateText As String
        Get
            Return FhLanguage.Text(If(Status.IsRunning, "正在运行", "未运行"), If(Status.IsRunning, "Running", "Not running"))
        End Get
    End Property

    Public ReadOnly Property RuntimeSummaryLine As String
        Get
            Dim parts As New List(Of String) From {FhLanguage.Text("版本 ", "Version ") & If(Tool.Version, "-")}
            If Status.Port > 0 Then parts.Add(FhLanguage.Text("监听端口 ", "Port ") & Status.Port)
            If Tool.TelemetryPort > 0 Then parts.Add(FhLanguage.Text("游戏遥测端口 ", "Telemetry ") & Tool.TelemetryPort)
            If Tool.Hotkeys IsNot Nothing Then
                parts.AddRange(Tool.Hotkeys.Where(Function(item) Not String.IsNullOrWhiteSpace(item.DefaultValue)).
                    Select(Function(item) $"{item.DefaultValue}: {item.Description}"))
            End If
            If Not String.IsNullOrWhiteSpace(Description) Then parts.Add(Description.ReplaceLineEndings(" "))
            Return String.Join("  ·  ", parts)
        End Get
    End Property

    Public ReadOnly Property DownloadStateText As String
        Get
            If ToolInstallService.IsBlocked(Tool) Then Return FhLanguage.Text("不可下载", "Unavailable")
            If UpdateAvailable Then Return FhLanguage.Text("有可用更新", "Update available")
            If Status.IsInstalled Then Return FhLanguage.Text("已下载，当前为最新版本", "Downloaded, up to date")
            If String.Equals(Tool.OnlineStatus, "unavailable", StringComparison.OrdinalIgnoreCase) Then Return FhLanguage.Text("网络错误或项目不存在", "Network error or project unavailable")
            Return FhLanguage.Text("未下载", "Not downloaded")
        End Get
    End Property

    Public ReadOnly Property DownloadStateBrush As Brush
        Get
            Dim unavailable = String.Equals(Tool.OnlineStatus, "unavailable", StringComparison.OrdinalIgnoreCase) AndAlso Not Status.IsInstalled
            Dim key = If(ToolInstallService.IsBlocked(Tool) OrElse unavailable, "ColorBrushRedLight",
                         If(UpdateAvailable, "ColorBrush4",
                            If(Status.IsInstalled, "ColorBrush3", "ColorBrushGray3")))
            Return TryCast(System.Windows.Application.Current.TryFindResource(key), Brush)
        End Get
    End Property

    Public ReadOnly Property Description As String
        Get
            If FhLanguage.IsEnglish Then Return If(String.IsNullOrWhiteSpace(Tool.Description), Tool.Category, Tool.Description)
            If Not String.IsNullOrWhiteSpace(Tool.DescriptionZh) Then Return Tool.DescriptionZh
            Select Case Tool.Id.ToLowerInvariant()
                Case "omnimix-vbnet-frontend"
                    Return "可以自定义的《地平线 6》音乐电台，支持网易云音乐、QQ 音乐和哔哩哔哩歌曲导入。"
                Case "fh6-auction-house-sniper"
                    Return "用于监控《极限竞速：地平线 6》拍卖行的工具。"
                Case "fh-language-combo-tool"
                    Return "用于调整《极限竞速》系列游戏语言组合的工具。"
                Case "fh6farm"
                    Return "用于获取超级抽奖的辅助工具。"
                Case "fh6-virtual-tcu-backend"
                    Return "虚拟变速箱控制单元，提供浏览器仪表盘及本地 WebSocket/HTTP 服务。"
                Case "horizon-haptics"
                    Return "通过 FH6 Data Out 遥测为 DualSense 控制器提供自适应扳机与触觉反馈。"
                Case "forza-painter-fh6"
                    Return "将图片转换为 FH6 涂装绘制数据，并提供可配置的几何参数。"
                Case Else
                    If String.Equals(Tool.Source, "local", StringComparison.OrdinalIgnoreCase) Then Return "用户手动添加的本地工具。"
                    Return $"社区工具：{Tool.Name}"
            End Select
        End Get
    End Property

    Public ReadOnly Property DownloadDetailLine As String
        Get
            Return $"{Description.ReplaceLineEndings(" ")}  ·  {FhLanguage.Text("版本 ", "Version ")}{If(Tool.Version, "-")}"
        End Get
    End Property

    Public ReadOnly Property StatusLine As String
        Get
            Return FhLanguage.Text($"状态：{TranslateStatus(Status.Message)} | 类型：{Tool.ToolType}", $"Status: {Status.Message} | Type: {Tool.ToolType}")
        End Get
    End Property

    Public ReadOnly Property HealthLine As String
        Get
            If Status.HealthOk Then Return FhLanguage.Text("健康状态：正常", "Health: healthy")
            If Status.IsRunning Then Return FhLanguage.Text("健康状态：进程运行中，健康检查未通过", "Health: process running, health check not passed")
            Return FhLanguage.Text("健康状态：未运行", "Health: not running")
        End Get
    End Property

    Public ReadOnly Property StartAllText As String
        Get
            Return FhLanguage.Text("全部启动", "Start All")
        End Get
    End Property

    Public ReadOnly Property StartText As String
        Get
            Return FhLanguage.Text("启动", "Start")
        End Get
    End Property

    Public ReadOnly Property BackendText As String
        Get
            Return FhLanguage.Text("启动后端", "Start Backend")
        End Get
    End Property

    Public ReadOnly Property FrontendText As String
        Get
            Return FhLanguage.Text("打开前端", "Open Frontend")
        End Get
    End Property

    Public ReadOnly Property StopText As String
        Get
            Return FhLanguage.Text("停止", "Stop")
        End Get
    End Property

    Public ReadOnly Property InstallText As String
        Get
            If UpdateAvailable Then Return FhLanguage.Text("更新", "Update")
            If Status.IsInstalled Then Return FhLanguage.Text("重新下载", "Download Again")
            Return FhLanguage.Text("下载", "Download")
        End Get
    End Property

    Public ReadOnly Property FolderText As String
        Get
            Return FhLanguage.Text("打开文件夹", "Open Folder")
        End Get
    End Property

    Public ReadOnly Property HealthCheckText As String
        Get
            Return FhLanguage.Text("检测健康状态", "Check Health")
        End Get
    End Property

    Private Shared Function TranslateStatus(value As String) As String
        Select Case If(value, "").Trim().ToLowerInvariant()
            Case "not installed"
                Return "未安装"
            Case "blocked by safety policy"
                Return "已被安全策略阻止"
            Case "running and healthy"
                Return "运行正常"
            Case "process running"
                Return "进程正在运行"
            Case "port occupied"
                Return "端口已占用"
            Case "ready"
                Return "就绪"
            Case Else
                Return value
        End Select
    End Function
End Class

Public Class DownloadTaskViewModel
    Implements INotifyPropertyChanged

    Public ReadOnly Property Tool As ToolManifestEntry
    Private _percentage As Double
    Private _progressText As String = "0% · 0 B / -- · 0 B/s"
    Private _statusText As String
    Private _lastSpeed As Double

    Public Sub New(tool As ToolManifestEntry)
        Me.Tool = tool
        _statusText = FhLanguage.Text("等待中", "Queued")
    End Sub

    Public ReadOnly Property Name As String
        Get
            Return Tool.Name
        End Get
    End Property

    Public Property Percentage As Double
        Get
            Return _percentage
        End Get
        Private Set(value As Double)
            _percentage = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property ProgressText As String
        Get
            Return _progressText
        End Get
        Private Set(value As String)
            _progressText = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property StatusText As String
        Get
            Return _statusText
        End Get
        Set(value As String)
            _statusText = value
            OnPropertyChanged()
        End Set
    End Property

    Public Sub UpdateProgress(progress As ToolDownloadProgress)
        Percentage = Math.Max(0, Math.Min(100, progress.Fraction * 100))
        If progress.BytesPerSecond > 0 Then _lastSpeed = progress.BytesPerSecond
        Dim totalText = If(progress.TotalBytes > 0, FormatSize(progress.TotalBytes), "--")
        ProgressText = $"{Math.Round(Percentage)}% · {FormatSize(progress.BytesReceived)} / {totalText} · {FormatSpeed(_lastSpeed)}"
    End Sub

    Private Shared Function FormatSpeed(bytesPerSecond As Double) As String
        Return FormatSize(bytesPerSecond) & "/s"
    End Function

    Private Shared Function FormatSize(bytes As Double) As String
        If bytes >= 1024 * 1024 * 1024 Then Return $"{bytes / 1024 / 1024 / 1024:0.00} GB"
        If bytes >= 1024 * 1024 Then Return $"{bytes / 1024 / 1024:0.0} MB"
        If bytes >= 1024 Then Return $"{bytes / 1024:0.0} KB"
        Return $"{bytes:0} B"
    End Function

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Private Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    End Sub
End Class
