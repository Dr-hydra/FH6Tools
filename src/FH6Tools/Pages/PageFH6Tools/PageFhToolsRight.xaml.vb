Imports System.Collections.ObjectModel
Imports Microsoft.Win32

Public Class PageFhToolsRight

    Private ReadOnly ManifestService As New ToolManifestService
    Private ReadOnly RuntimeService As New ToolRuntimeService
    Private ReadOnly InstallService As New ToolInstallService
    Private ReadOnly GameService As New GameLaunchService
    Private ReadOnly GameBackupService As New GameBackupService
    Private ReadOnly ConfigService As New ConfigSnapshotService
    Private ReadOnly ToolCardsSource As New ObservableCollection(Of ToolCardViewModel)
    Private ReadOnly InstalledToolCardsSource As New ObservableCollection(Of ToolCardViewModel)
    Private CurrentTools As List(Of ToolManifestEntry) = New List(Of ToolManifestEntry)
    Private CurrentGame As GameInstallState = New GameInstallState
    Private ActiveDownloadCancellation As Threading.CancellationTokenSource
    Private ReadOnly PendingInstallTools As New Queue(Of ToolManifestEntry)
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
        ComboLanguage.SelectedIndex = If(FhLanguage.IsEnglish, 1, 0)
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
        BtnReloadManifest.Text = FhLanguage.Text("重新加载工具清单", "Reload Manifest")
        BtnReloadManifest2.Text = FhLanguage.Text("刷新工具状态", "Refresh Tool Status")
        BtnOpenDownloads.Text = FhLanguage.Text("打开下载目录", "Open Downloads")
        BtnCancelDownload.Text = FhLanguage.Text("取消当前任务", "Cancel Current Job")
        BtnCancelDownload2.Text = FhLanguage.Text("取消全部任务", "Cancel All Jobs")
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
        Configure(CurrentPage)
        FrmMain?.UpdateShellStatus(GameSummary, ToolsSummary)
    End Function

    Private Async Function RefreshToolsAsync() As Task
        CurrentTools = Await ManifestService.LoadToolsAsync()
        Dim statusTasks = CurrentTools.Select(Async Function(tool) New ToolCardViewModel(tool, Await RuntimeService.GetStatusAsync(tool), InstallService.IsUpdateAvailable(tool))).ToArray()
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
        ToolCardsSource.Clear()
        InstalledToolCardsSource.Clear()
        For Each tool In CurrentTools
            Dim status As New ToolRuntimeStatus With {
                .ToolId = tool.Id,
                .IsInstalled = InstallService.IsInstalled(tool),
                .Message = If(InstallService.IsInstalled(tool), "ready", "not installed")
            }
            Dim card = New ToolCardViewModel(tool, status, InstallService.IsUpdateAvailable(tool))
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
            Await GameService.LaunchAsync(CurrentGame)
            Await RefreshGameAsync()
            Hint(FhLanguage.Text("已请求启动地平线 6。", "FH6 launch requested."), HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Function

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

    Private Async Sub BtnReloadManifest_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnReloadManifest.Click, BtnReloadManifest2.Click
        Await RefreshManifestAndToolsAsync()
        LabDownloadStatus.Text = "Manifest reloaded from " & FhPaths.ManifestPath
    End Sub

    Private Sub BtnCancelDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCancelDownload.Click, BtnCancelDownload2.Click
        If ActiveDownloadCancellation IsNot Nothing Then
            ActiveDownloadCancellation.Cancel()
            LabDownloadStatus.Text = "Download cancellation requested."
        Else
            LabDownloadStatus.Text = "No active download job to cancel."
        End If
    End Sub

    Private Sub BtnOpenDownloads_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenDownloads.Click
        FhPaths.Ensure()
        Process.Start(New ProcessStartInfo With {.FileName = FhPaths.DownloadRoot, .UseShellExecute = True})
    End Sub

    Private Sub BtnOpenConfigRoot_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenConfigRoot.Click
        FhPaths.Ensure()
        Process.Start(New ProcessStartInfo With {.FileName = FhPaths.ConfigRoot, .UseShellExecute = True})
    End Sub

    Private Sub BtnOpenDataRoot_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenDataRoot.Click
        FhPaths.Ensure()
        Process.Start(New ProcessStartInfo With {.FileName = FhPaths.AppDataRoot, .UseShellExecute = True})
    End Sub

    Private Async Sub ComboLanguage_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboLanguage.SelectionChanged
        If Not IsLoaded OrElse ComboLanguage.SelectedIndex < 0 Then Return
        Dim language = If(ComboLanguage.SelectedIndex = 1, "en-US", "zh-CN")
        If String.Equals(language, FhLanguage.Current, StringComparison.OrdinalIgnoreCase) Then Return
        FhLanguage.SetLanguage(language)
        FrmMain?.ApplyLanguage()
        Await RefreshToolsAsync()
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
        PendingInstallTools.Enqueue(tool)
        LabDownloadStatus.Text = $"Queued {tool.Name}. Pending jobs: {PendingInstallTools.Count}"
        Await ProcessInstallQueueAsync()
    End Sub

    Private Async Sub IconToolInstall_Click(sender As Object, e As EventArgs)
        Dim tool = GetToolFromSender(sender)
        If tool Is Nothing Then Return
        PendingInstallTools.Enqueue(tool)
        LabDownloadStatus.Text = $"Queued {tool.Name}. Pending jobs: {PendingInstallTools.Count}"
        Await ProcessInstallQueueAsync()
    End Sub

    Private Async Function ProcessInstallQueueAsync() As Task
        If IsDownloadQueueRunning Then Return
        IsDownloadQueueRunning = True
        Try
            While PendingInstallTools.Count > 0
                Dim tool = PendingInstallTools.Dequeue()
                ActiveDownloadCancellation = New Threading.CancellationTokenSource()
                Try
                    Dim progress As New Progress(Of Double)(Sub(value)
                                                                LabDownloadStatus.Text = $"Installing {tool.Name}: {Math.Round(value * 100)}% | Pending: {PendingInstallTools.Count}"
                                                            End Sub)
                    Dim installPath = Await InstallService.DownloadAndInstallAsync(tool, progress, ActiveDownloadCancellation.Token)
                    LabDownloadStatus.Text = $"Installed {tool.Name} to {installPath}"
                    Await RefreshToolsAsync()
                Catch ex As OperationCanceledException
                    LabDownloadStatus.Text = $"Cancelled install for {tool.Name}."
                    PendingInstallTools.Clear()
                Catch ex As Exception
                    LabDownloadStatus.Text = $"Install failed for {tool.Name}: {ex.Message}"
                    Hint(ex.Message, HintType.Red)
                Finally
                    ActiveDownloadCancellation.Dispose()
                    ActiveDownloadCancellation = Nothing
                End Try
            End While
            If ActiveDownloadCancellation Is Nothing Then LabDownloadStatus.Text &= If(PendingInstallTools.Count = 0, " Queue idle.", "")
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
                Directory.CreateDirectory(configPath)
                Process.Start(New ProcessStartInfo With {.FileName = configPath, .UseShellExecute = True})
                LabConfigStatus.Text = "Opened config directory: " & configPath
                Return
            End If
            Directory.CreateDirectory(Path.GetDirectoryName(configPath))
            If Not File.Exists(configPath) Then File.WriteAllText(configPath, "{}")
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
        If tool.ConfigFiles Is Nothing OrElse tool.ConfigFiles.Count = 0 Then
            Hint("This tool has no config files declared.", HintType.Blue)
            Return Nothing
        End If
        If tool.ConfigFiles.Count > 1 Then
            Dim selected = MyMsgBoxSelect(tool.ConfigFiles.Select(Function(config) config.Name), "Select config")
            If selected < 0 OrElse selected >= tool.ConfigFiles.Count Then Return Nothing
            Return tool.ConfigFiles(selected)
        End If
        Return tool.ConfigFiles(0)
    End Function

End Class

Public Class ToolCardViewModel
    Public ReadOnly Property Tool As ToolManifestEntry
    Public ReadOnly Property Status As ToolRuntimeStatus
    Public ReadOnly Property UpdateAvailable As Boolean

    Public Sub New(tool As ToolManifestEntry, status As ToolRuntimeStatus, Optional updateAvailable As Boolean = False)
        Me.Tool = tool
        Me.Status = status
        Me.UpdateAvailable = updateAvailable
    End Sub

    Public ReadOnly Property DisplayName As String
        Get
            Return Tool.Name
        End Get
    End Property

    Public ReadOnly Property DownloadStateText As String
        Get
            If ToolInstallService.IsBlocked(Tool) Then Return FhLanguage.Text("不可下载", "Unavailable")
            If UpdateAvailable Then Return FhLanguage.Text("有可用更新", "Update available")
            If Status.IsInstalled Then Return FhLanguage.Text("已下载，当前为最新版本", "Downloaded, up to date")
            Return FhLanguage.Text("未下载", "Not downloaded")
        End Get
    End Property

    Public ReadOnly Property DownloadStateBrush As Brush
        Get
            Dim key = If(ToolInstallService.IsBlocked(Tool), "ColorBrushRedLight",
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
            Dim count = If(Tool.ConfigFiles Is Nothing, 0, Tool.ConfigFiles.Count)
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
