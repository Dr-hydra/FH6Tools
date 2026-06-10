Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports Microsoft.Win32

Public Class PageFhToolsRight
    Private Shared ReadOnly AppUpdateClient As New HttpClient With {.Timeout = TimeSpan.FromSeconds(5)}

    Private ReadOnly ManifestService As New ToolManifestService
    Private ReadOnly RuntimeService As New ToolRuntimeService
    Private ReadOnly InstallService As New ToolInstallService
    Private ReadOnly GameService As New GameLaunchService
    Private ReadOnly GameBackupService As New GameBackupService
    Private ReadOnly ConfigService As New ConfigSnapshotService
    Private ReadOnly ToolCardsSource As New ObservableCollection(Of ToolCardViewModel)
    Private ReadOnly InstalledToolCardsSource As New ObservableCollection(Of ToolCardViewModel)
    Private ReadOnly DownloadTasksSource As New ObservableCollection(Of DownloadTaskViewModel)
    Private CurrentTools As List(Of ToolManifestEntry) = New List(Of ToolManifestEntry)
    Private CurrentState As ToolStateStore = New ToolStateStore
    Private CurrentGame As GameInstallState = New GameInstallState
    Private ReadOnly PendingInstallTools As New Queue(Of DownloadTaskViewModel)
    Private IsDownloadQueueRunning As Boolean
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

    Public Async Function InitializeAsync() As Task
        FhPaths.Ensure()
        DownloadToolCards.ItemsSource = ToolCardsSource
        InstalledToolCards.ItemsSource = InstalledToolCardsSource
        ConfigToolCards.ItemsSource = InstalledToolCardsSource
        DownloadTasks.ItemsSource = DownloadTasksSource
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

    Public Sub ApplyLanguage()
        CardDownloads.Title = FhLanguage.Text("下载管理", "Downloads")
        LabDownloadDescription.Text = FhLanguage.Text("下载与安装任务保存在软件旁的 FH6ToolsData 目录。", "Downloads and install jobs are stored in FH6ToolsData beside the app.")
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
        BtnBackupGameData.Text = FhLanguage.Text("备份游戏数据", "Back Up Game Data")
        BtnOpenGameBackups.Text = FhLanguage.Text("打开游戏备份", "Open Game Backups")
        BtnOpenConfigRoot.Text = FhLanguage.Text("打开配置目录", "Open Config Folder")
        BtnOpenDataRoot.Text = FhLanguage.Text("打开数据目录", "Open Data Folder")
        CardAbout.Title = FhLanguage.Text("关于 FH6Tools", "About FH6Tools")
        CardRuntimeInfo.Title = FhLanguage.Text("运行时与安全", "Runtime and Safety")
        LabAboutDescription.Text = FhLanguage.Text("FH6Tools 是用于启动地平线 6 和管理社区工具的本地 Windows 工具中心。", "FH6Tools is a local Windows launcher and control center for Forza Horizon 6 community tools.")
        ItemAboutSafety.Title = FhLanguage.Text("安全策略", "Safety Policy")
        ItemAboutSafety.Info = FhLanguage.Text("违反项目安全策略的条目不会被安装或启动。", "Entries that violate the project safety policy are not installed or launched.")
        ItemAboutManifest.Title = FhLanguage.Text("工具清单", "Tool Manifest")
        ItemAboutManifest.Info = FhLanguage.Text("工具列表会在软件启动时动态更新。", "The tool list is refreshed dynamically when the app starts.")
        ItemAboutRuntime.Title = FhLanguage.Text("共享运行时", "Shared Runtime")
        ItemAboutRuntime.Info = FhLanguage.Text(".NET 10 Desktop 与 ASP.NET Core Runtime 由所有托管程序共享。", ".NET 10 Desktop and ASP.NET Core Runtime are shared by all managed programs.")
        LabInstalledToolSummary.Text = FhLanguage.Text($"已安装工具：{InstalledToolCardsSource.Count} 个", $"Installed tools: {InstalledToolCardsSource.Count}")
        LabConfigToolSummary.Text = LabInstalledToolSummary.Text
        Configure(CurrentPage)
    End Sub

    Private Async Function RefreshGameAsync() As Task
        CurrentGame = Await GameService.DetectAsync()
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
            Await GameService.LaunchAsync(CurrentGame)
            Await RefreshGameAsync()
            Hint(FhLanguage.Text("已请求启动地平线 6。", "FH6 launch requested."), HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Function

    Private Function ConfirmLaunchConflicts(entries As List(Of ToolInstallState)) As Boolean
        Dim conflicts As New List(Of String)
        Dim runningIds = New HashSet(Of String)(InstalledToolCardsSource.Where(Function(card) card.Status.IsRunning).Select(Function(card) card.Tool.Id), StringComparer.OrdinalIgnoreCase)
        Dim relevantEntries = entries.Concat(CurrentState.Tools.Where(Function(item) runningIds.Contains(item.ToolId))).
            GroupBy(Function(item) item.ToolId, StringComparer.OrdinalIgnoreCase).Select(Function(candidateGroup) candidateGroup.First()).ToList()
        Dim servicePorts = relevantEntries.Select(Function(item)
                                                      Dim tool = CurrentTools.FirstOrDefault(Function(candidate) candidate.Id.Equals(item.ToolId, StringComparison.OrdinalIgnoreCase))
                                                      Return If(item.Port > 0, item.Port, If(tool Is Nothing, 0, RuntimeService.GetConfiguredPort(tool)))
                                                  End Function)
        For Each portGroup In servicePorts.Where(Function(port) port > 0).GroupBy(Function(port) port).Where(Function(candidate) candidate.Count > 1)
            conflicts.Add(FhLanguage.Text($"服务端口 {portGroup.Key} 被多个工具使用", $"Service port {portGroup.Key} is used by multiple tools"))
        Next
        Dim telemetryPorts = relevantEntries.Select(Function(item)
                                                        Dim tool = CurrentTools.FirstOrDefault(Function(candidate) candidate.Id.Equals(item.ToolId, StringComparison.OrdinalIgnoreCase))
                                                        Return If(item.TelemetryPort > 0, item.TelemetryPort, If(tool Is Nothing, 0, tool.TelemetryPort))
                                                    End Function)
        For Each telemetryGroup In telemetryPorts.Where(Function(port) port > 0).GroupBy(Function(port) port).Where(Function(candidate) candidate.Count > 1)
            conflicts.Add(FhLanguage.Text($"游戏遥测端口 {telemetryGroup.Key} 被多个工具使用", $"Game telemetry port {telemetryGroup.Key} is used by multiple tools"))
        Next
        For Each hotkeyGroup In relevantEntries.SelectMany(Function(item) If(item.Hotkeys, New Dictionary(Of String, String)).Values).
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
            .ListenAddress = "127.0.0.1",
            .Port = If(tool Is Nothing, 0, RuntimeService.GetConfiguredPort(tool)),
            .TelemetryPort = If(tool Is Nothing, 0, tool.TelemetryPort),
            .RunAsAdministrator = tool?.RequiresAdmin
        }
        If tool?.Hotkeys IsNot Nothing Then
            state.Hotkeys = tool.Hotkeys.
                Where(Function(item) Not String.IsNullOrWhiteSpace(item.Name) AndAlso Not String.IsNullOrWhiteSpace(item.DefaultValue)).
                ToDictionary(Function(item) item.Name, Function(item) item.DefaultValue, StringComparer.OrdinalIgnoreCase)
        End If
        CurrentState.Tools.Add(state)
        Return state
    End Function

    Private Sub ApplyRuntimeOverrides()
        For Each tool In CurrentTools
            Dim state = GetToolState(tool.Id)
            RuntimeService.SetRunAsAdministrator(tool, state.RunAsAdministrator)
            RuntimeService.SetBackendOnly(tool, state.BackendOnly)
            If state.Port > 0 Then RuntimeService.SetPortOverride(tool, state.Port)
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

    Private Async Sub ToolBackendOnly_Change(sender As Object, user As Boolean)
        If Not user Then Return
        Dim checkbox = TryCast(sender, MyCheckBox)
        Dim viewModel = TryCast(checkbox?.DataContext, ToolCardViewModel)
        If viewModel Is Nothing Then Return
        CurrentState = Await ManifestService.LoadStateAsync()
        GetToolState(viewModel.Tool.Id).BackendOnly = checkbox.Checked
        RuntimeService.SetBackendOnly(viewModel.Tool, checkbox.Checked)
        Await ManifestService.SaveStateAsync(CurrentState)
    End Sub

    Private Async Sub ToolSaveRuntimeConfig_Click(sender As Object, e As MouseButtonEventArgs)
        Dim viewModel = TryCast(TryCast(sender, FrameworkElement)?.DataContext, ToolCardViewModel)
        If viewModel Is Nothing Then Return
        Dim port As Integer
        If viewModel.HasPort AndAlso (Not Integer.TryParse(viewModel.PortText, port) OrElse port < 1 OrElse port > 65535) Then
            Hint(FhLanguage.Text("服务监听端口必须在 1 到 65535 之间。", "Service port must be between 1 and 65535."), HintType.Red)
            Return
        End If
        CurrentState = Await ManifestService.LoadStateAsync()
        Dim state = GetToolState(viewModel.Tool.Id)
        state.ListenAddress = viewModel.ListenAddress.Trim()
        state.Port = If(viewModel.HasPort, port, 0)
        state.TelemetryPort = viewModel.Tool.TelemetryPort
        state.Hotkeys = viewModel.ParseHotkeys()
        If state.Port > 0 Then RuntimeService.SetPortOverride(viewModel.Tool, state.Port)
        Await ManifestService.SaveStateAsync(CurrentState)
        Hint(FhLanguage.Text("运行配置已保存。", "Runtime configuration saved."), HintType.Green)
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

    Private Sub BtnBackupGameData_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnBackupGameData.Click
        Try
            Dim dataRoot = GameBackupService.ChooseDataRoot(Window.GetWindow(Me))
            If String.IsNullOrWhiteSpace(dataRoot) Then Return
            Dim backupPath = GameBackupService.BackupFolder(dataRoot)
            LabConfigStatus.Text = FhLanguage.Text("游戏数据备份已创建：", "Game data backup created: ") & backupPath
            Hint("FH6 data backup created.", HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub BtnOpenGameBackups_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenGameBackups.Click
        Try
            Directory.CreateDirectory(GameBackupService.BackupRoot)
            Process.Start(New ProcessStartInfo With {.FileName = GameBackupService.BackupRoot, .UseShellExecute = True})
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

    Private Sub BtnOpenConfigRoot_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenConfigRoot.Click
        FhPaths.Ensure()
        Process.Start(New ProcessStartInfo With {.FileName = FhPaths.ConfigRoot, .UseShellExecute = True})
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

    Private Sub BtnOpenProject_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenProject.Click
        Process.Start(New ProcessStartInfo With {.FileName = "https://github.com/Dr-hydra/FH6Tools", .UseShellExecute = True})
    End Sub

    Private Async Sub BtnCheckAppUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCheckAppUpdate.Click
        Await CheckAppUpdateAsync(True)
    End Sub

    Private Shared Async Function CheckAppUpdateAsync(showNoUpdate As Boolean) As Task
        Try
            Using request As New HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Dr-hydra/FH6Tools/releases/latest")
                request.Headers.UserAgent.ParseAdd("FH6Tools")
                Using response = Await AppUpdateClient.SendAsync(request)
                    If Not response.IsSuccessStatusCode Then
                        If showNoUpdate Then Hint(FhLanguage.Text("当前没有可检查的已发布版本。", "No published release is available to check."), HintType.Blue)
                        Return
                    End If
                    Using document = System.Text.Json.JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                        Dim latest = document.RootElement.GetProperty("tag_name").GetString()
                        Dim current = Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                        If Not String.Equals(latest?.TrimStart("v"c), current, StringComparison.OrdinalIgnoreCase) Then
                            Hint(FhLanguage.Text($"发现新版本：{latest}", $"New version available: {latest}"), HintType.Green)
                        ElseIf showNoUpdate Then
                            Hint(FhLanguage.Text("当前已是最新版本。", "FH6Tools is up to date."), HintType.Green)
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' Startup update checks are intentionally silent on network errors.
            If showNoUpdate Then Hint(FhLanguage.Text("检查更新失败：", "Update check failed: ") & ex.Message, HintType.Red)
        End Try
    End Function

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
        DownloadTasksSource.Add(task)
        PendingInstallTools.Enqueue(task)
        LabDownloadStatus.Text = $"Queued {tool.Name}. Pending jobs: {PendingInstallTools.Count}"
        Await ProcessInstallQueueAsync()
    End Sub

    Private Async Sub IconToolInstall_Click(sender As Object, e As EventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Dim task = New DownloadTaskViewModel(tool)
        DownloadTasksSource.Add(task)
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
                Try
                    Dim progress As New Progress(Of ToolDownloadProgress)(Sub(value)
                                                                            downloadTask.UpdateProgress(value)
                                                                            LabDownloadStatus.Text = $"Installing {tool.Name}: {Math.Round(value.Fraction * 100)}% | Pending: {PendingInstallTools.Count}"
                                                                        End Sub)
                    Dim installPath = Await InstallService.DownloadAndInstallAsync(tool, progress, Threading.CancellationToken.None)
                    downloadTask.StatusText = FhLanguage.Text("已完成", "Completed")
                    LabDownloadStatus.Text = $"Installed {tool.Name} to {installPath}"
                    Await RefreshToolsAsync()
                Catch ex As Exception
                    downloadTask.StatusText = FhLanguage.Text("失败：", "Failed: ") & ex.Message
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

    Private Sub HeaderToolToggleCollapse_Click(sender As Object, e As RouteEventArgs)
        Dim current As DependencyObject = TryCast(sender, DependencyObject)
        While current IsNot Nothing AndAlso Not TypeOf current Is MyCard
            current = VisualTreeHelper.GetParent(current)
        End While
        Dim card = TryCast(current, MyCard)
        If card IsNot Nothing Then card.IsSwapped = Not card.IsSwapped
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

    Private Sub ToolOpenConfig_Click(sender As Object, e As MouseButtonEventArgs)
        OpenToolConfig(sender)
    End Sub

    Private Sub IconToolOpenConfig_Click(sender As Object, e As EventArgs)
        OpenToolConfig(sender)
    End Sub

    Private Sub OpenToolConfig(sender As Object)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Try
            Dim config = SelectConfig(tool)
            If config Is Nothing Then Return
            Dim configPath = ConfigService.ResolveConfigPath(tool, config)
            If ConfigSnapshotService.IsDirectoryConfig(config, configPath) Then
                If Not Directory.Exists(configPath) Then
                    Hint(FhLanguage.Text("配置目录不存在。", "Config directory does not exist."), HintType.Blue)
                    Return
                End If
                Process.Start(New ProcessStartInfo With {.FileName = configPath, .UseShellExecute = True})
                LabConfigStatus.Text = "Opened config directory: " & configPath
                Return
            End If
            If Not File.Exists(configPath) Then
                Hint(FhLanguage.Text("配置文件不存在。", "Config file does not exist."), HintType.Blue)
                Return
            End If
            Process.Start(New ProcessStartInfo With {.FileName = configPath, .UseShellExecute = True})
            LabConfigStatus.Text = "Opened config: " & configPath
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub ToolBackupConfig_Click(sender As Object, e As MouseButtonEventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Try
            Dim config = SelectConfig(tool)
            If config Is Nothing Then Return
            Dim snapshot = ConfigService.Backup(tool, config)
            LabConfigStatus.Text = "Created snapshot: " & snapshot
            Hint("Config snapshot created.", HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub ToolRestoreConfig_Click(sender As Object, e As MouseButtonEventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Try
            Dim config = SelectConfig(tool)
            If config Is Nothing Then Return
            Dim snapshots = ConfigService.ListSnapshots(tool)
            If snapshots.Count = 0 Then
                Hint("No snapshots found for this tool.", HintType.Blue)
                Return
            End If
            Dim snapshot = snapshots(0)
            If snapshots.Count > 1 Then
                Dim selected = MyMsgBoxSelect(snapshots.Select(Function(path) IO.Path.GetFileName(path)), "Restore snapshot")
                If selected >= 0 AndAlso selected < snapshots.Count Then snapshot = snapshots(selected)
            End If
            Dim target = ConfigService.ResolveConfigPath(tool, config)
            ConfigService.Restore(snapshot, target)
            LabConfigStatus.Text = "Restored " & IO.Path.GetFileName(snapshot) & " to " & target
            Hint("Config restored.", HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub ToolOpenLogs_Click(sender As Object, e As MouseButtonEventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        Try
            Dim logConfig = If(tool.ConfigFiles, New List(Of ToolConfigFileEntry)).
                FirstOrDefault(Function(config) config.Name.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0)
            If logConfig IsNot Nothing Then
                Dim logPath = ConfigService.ResolveConfigPath(tool, logConfig)
                Directory.CreateDirectory(logPath)
                Process.Start(New ProcessStartInfo With {.FileName = logPath, .UseShellExecute = True})
                Return
            End If
            Dim toolLogRoot = IO.Path.Combine(FhPaths.AppDataRoot, "logs", tool.Id)
            Directory.CreateDirectory(toolLogRoot)
            Process.Start(New ProcessStartInfo With {.FileName = toolLogRoot, .UseShellExecute = True})
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Function SelectConfig(tool As ToolManifestEntry) As ToolConfigFileEntry
        Dim existingConfigs = If(tool.ConfigFiles, New List(Of ToolConfigFileEntry)).
            Where(Function(config)
                      Dim path = ConfigService.ResolveConfigPath(tool, config)
                      Return File.Exists(path) OrElse Directory.Exists(path)
                  End Function).ToList()
        If existingConfigs.Count = 0 Then
            Hint(FhLanguage.Text("未找到该工具的现有配置文件。", "No existing config files were found for this tool."), HintType.Blue)
            Return Nothing
        End If
        If existingConfigs.Count > 1 Then
            Dim selected = MyMsgBoxSelect(existingConfigs.Select(Function(config) config.Name), FhLanguage.Text("选择配置", "Select config"))
            If selected < 0 OrElse selected >= existingConfigs.Count Then Return Nothing
            Return existingConfigs(selected)
        End If
        Return existingConfigs(0)
    End Function

End Class

Public Class ToolCardViewModel
    Public ReadOnly Property Tool As ToolManifestEntry
    Public ReadOnly Property Status As ToolRuntimeStatus
    Public ReadOnly Property UpdateAvailable As Boolean
    Public ReadOnly Property LaunchWithGame As Boolean
    Public ReadOnly Property RunAsAdministrator As Boolean
    Public ReadOnly Property BackendOnly As Boolean
    Public Property ListenAddress As String
    Public Property PortText As String
    Public Property HotkeyText As String
    Private ReadOnly ExistingConfigCountValue As Integer

    Public Sub New(tool As ToolManifestEntry, status As ToolRuntimeStatus, Optional updateAvailable As Boolean = False, Optional state As ToolInstallState = Nothing)
        Me.Tool = tool
        Me.Status = status
        Me.UpdateAvailable = updateAvailable
        Me.LaunchWithGame = state?.LaunchWithGame
        Me.RunAsAdministrator = If(state Is Nothing, tool.RequiresAdmin, state.RunAsAdministrator)
        Me.BackendOnly = state?.BackendOnly
        Me.ListenAddress = If(String.IsNullOrWhiteSpace(state?.ListenAddress), "127.0.0.1", state.ListenAddress)
        Me.PortText = If(If(state?.Port, 0) > 0, state.Port.ToString(), Status.Port.ToString())
        Dim configuredHotkeys = If(state?.Hotkeys, New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase))
        If configuredHotkeys.Count = 0 AndAlso tool.Hotkeys IsNot Nothing Then
            configuredHotkeys = tool.Hotkeys.ToDictionary(Function(item) item.Name, Function(item) item.DefaultValue, StringComparer.OrdinalIgnoreCase)
        End If
        Me.HotkeyText = String.Join("; ", configuredHotkeys.Select(Function(item) $"{item.Key}={item.Value}"))
        ExistingConfigCountValue = If(tool.ConfigFiles, New List(Of ToolConfigFileEntry)).
            Where(Function(config)
                      Dim path = (New ConfigSnapshotService).ResolveConfigPath(tool, config)
                      Return File.Exists(path) OrElse Directory.Exists(path)
                  End Function).Count()
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

    Public ReadOnly Property HasPort As Boolean
        Get
            Return Status.Port > 0 OrElse Tool.Backend?.DefaultPort > 0 OrElse Tool.[Single]?.DefaultPort > 0
        End Get
    End Property

    Public ReadOnly Property HasHotkeys As Boolean
        Get
            Return Tool.Hotkeys IsNot Nothing AndAlso Tool.Hotkeys.Count > 0
        End Get
    End Property

    Public ReadOnly Property ExistingConfigCount As Integer
        Get
            Return ExistingConfigCountValue
        End Get
    End Property

    Public ReadOnly Property PortVisibility As Visibility
        Get
            Return If(HasPort, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property TelemetryVisibility As Visibility
        Get
            Return If(Tool.TelemetryPort > 0, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property HotkeyVisibility As Visibility
        Get
            Return If(HasHotkeys, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property BackendVisibility As Visibility
        Get
            Return If(HasBackend, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property ConfigVisibility As Visibility
        Get
            Return If(ExistingConfigCount > 0, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property RuntimeConfigVisibility As Visibility
        Get
            Return If(HasPort OrElse HasHotkeys, Visibility.Visible, Visibility.Collapsed)
        End Get
    End Property

    Public ReadOnly Property TelemetryPortLine As String
        Get
            If Tool.TelemetryPort <= 0 Then Return FhLanguage.Text("游戏遥测端口：无", "Game telemetry port: none")
            Return FhLanguage.Text($"游戏遥测端口：{Tool.TelemetryPort}", $"Game telemetry port: {Tool.TelemetryPort}")
        End Get
    End Property

    Public Function ParseHotkeys() As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each part In If(HotkeyText, "").Split(";"c)
            Dim pair = part.Split({"="c}, 2)
            If pair.Length = 2 AndAlso Not String.IsNullOrWhiteSpace(pair(0)) AndAlso Not String.IsNullOrWhiteSpace(pair(1)) Then
                result(pair(0).Trim()) = pair(1).Trim()
            End If
        Next
        Return result
    End Function

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
            If Not String.IsNullOrWhiteSpace(Tool.Description) Then parts.Add(Tool.Description.ReplaceLineEndings(" "))
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
            Return If(String.IsNullOrWhiteSpace(Tool.Description), Tool.Category, Tool.Description)
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

    Public ReadOnly Property PortLine As String
        Get
            If Status.Port <= 0 Then Return FhLanguage.Text("端口：无", "Port: none")
            Return FhLanguage.Text($"端口：{Status.Port} | 监听：{Status.PortListening} | 健康：{Status.HealthOk}", $"Port: {Status.Port} | Listening: {Status.PortListening} | Health: {Status.HealthOk}")
        End Get
    End Property

    Public ReadOnly Property ConfigLine As String
        Get
            Dim count = ExistingConfigCount
            Return FhLanguage.Text($"配置文件：{count} | 风险：{Tool.RiskLevel}", $"Config files: {count} | Risk: {Tool.RiskLevel}")
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

    Public ReadOnly Property ConfigText As String
        Get
            Return FhLanguage.Text("打开配置", "Open Config")
        End Get
    End Property

    Public ReadOnly Property BackupText As String
        Get
            Return FhLanguage.Text("备份配置", "Back Up Config")
        End Get
    End Property

    Public ReadOnly Property RestoreText As String
        Get
            Return FhLanguage.Text("恢复配置", "Restore Config")
        End Get
    End Property

    Public ReadOnly Property LogsText As String
        Get
            Return FhLanguage.Text("打开日志", "Open Logs")
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
    Private _progressText As String = "0% · 0 B/s"
    Private _statusText As String

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
        ProgressText = $"{Math.Round(Percentage)}% · {FormatSpeed(progress.BytesPerSecond)}"
    End Sub

    Private Shared Function FormatSpeed(bytesPerSecond As Double) As String
        If bytesPerSecond >= 1024 * 1024 Then Return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s"
        If bytesPerSecond >= 1024 Then Return $"{bytesPerSecond / 1024:0.0} KB/s"
        Return $"{bytesPerSecond:0} B/s"
    End Function

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Private Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    End Sub
End Class
