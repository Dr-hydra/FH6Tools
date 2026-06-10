Public Class FormGameBackupManager
    Private ReadOnly Game As GameInstallState
    Private ReadOnly BackupService As New GameBackupService
    Private ReadOnly LaunchService As New GameLaunchService

    Public Sub New(game As GameInstallState)
        Me.Game = game
        InitializeComponent()
        ApplyLanguage()
        RefreshBackups()
    End Sub

    Private Sub ApplyLanguage()
        Title = FhLanguage.Text("存档备份管理", "Save Backup Manager")
        LabWindowTitle.Text = Title
        LabTitle.Text = Title
        LabHint.Text = FhLanguage.Text(
            "选择备份后可以恢复。恢复前会自动备份当前存档，请确保游戏已经完全退出。",
            "Select a backup to restore. The current save is backed up first. Make sure the game is fully closed.")
        BtnRefresh.Text = FhLanguage.Text("刷新", "Refresh")
        BtnOpenFolder.Text = FhLanguage.Text("打开备份目录", "Open Backup Folder")
        BtnRestore.Text = FhLanguage.Text("恢复所选存档", "Restore Selected Save")
        ColCreatedAt.Header = FhLanguage.Text("备份时间", "Backup Time")
        ColPlatform.Header = FhLanguage.Text("平台", "Platform")
        ColType.Header = FhLanguage.Text("类型", "Type")
        ColSize.Header = FhLanguage.Text("大小", "Size")
        ColName.Header = FhLanguage.Text("目录名", "Folder")
    End Sub

    Private Sub PanTitle_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If e.LeftButton = MouseButtonState.Pressed Then DragMove()
    End Sub

    Private Sub BtnClose_Click(sender As Object, e As EventArgs) Handles BtnClose.Click
        Close()
    End Sub

    Private Sub RefreshBackups()
        Dim backups = BackupService.GetBackups()
        GridBackups.ItemsSource = backups
        LabStatus.Text = FhLanguage.Text($"共 {backups.Count} 个备份", $"{backups.Count} backups")
        BtnRestore.IsEnabled = backups.Count > 0
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRefresh.Click
        RefreshBackups()
    End Sub

    Private Sub BtnOpenFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenFolder.Click
        Try
            Directory.CreateDirectory(BackupService.BackupRoot)
            Process.Start(New ProcessStartInfo With {.FileName = BackupService.BackupRoot, .UseShellExecute = True})
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub BtnRestore_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRestore.Click
        Dim selected = TryCast(GridBackups.SelectedItem, GameSaveBackupInfo)
        If selected Is Nothing Then
            Hint(FhLanguage.Text("请先选择一个存档备份。", "Select a save backup first."), HintType.Blue)
            Return
        End If
        If LaunchService.IsGameRunning() Then
            MessageBox.Show(FhLanguage.Text("游戏仍在运行，无法恢复存档。请完全退出游戏后重试。",
                                            "The game is still running. Close it completely before restoring."),
                            Title, MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If
        If MessageBox.Show(
            FhLanguage.Text($"确定恢复 {selected.CreatedAtText} 的存档吗？恢复前会自动备份当前存档。恢复后 Xbox 云存档仍可能覆盖本地存档。",
                            $"Restore the save from {selected.CreatedAtText}? The current save will be backed up first. Xbox cloud sync may still overwrite the restored local save."),
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) <> MessageBoxResult.Yes Then Return

        BtnRestore.IsEnabled = False
        LabStatus.Text = FhLanguage.Text("正在备份当前存档并恢复所选存档……", "Backing up the current save and restoring the selected backup...")
        Try
            Dim safetyBackup = Await BackupService.RestoreAsync(Game, selected.Path)
            RefreshBackups()
            LabStatus.Text = If(String.IsNullOrWhiteSpace(safetyBackup),
                                FhLanguage.Text("恢复完成；恢复前没有检测到可备份的当前存档。", "Restore completed; no current save was found to back up."),
                                FhLanguage.Text("恢复完成，当前存档已创建恢复前保护备份。", "Restore completed and a safety backup was created."))
            Hint(FhLanguage.Text("存档恢复完成。", "Save restore completed."), HintType.Green)
        Catch ex As Exception
            LabStatus.Text = FhLanguage.Text("恢复失败：", "Restore failed: ") & ex.Message
            Hint(ex.Message, HintType.Red)
        Finally
            BtnRestore.IsEnabled = True
        End Try
    End Sub
End Class
