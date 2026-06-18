Imports Microsoft.Win32
Imports System.Windows.Media.Imaging

Public Class PageFhToolsLeft

    Private CurrentPage As FhShellPage = FhShellPage.Home

    Public Sub Configure(page As FhShellPage)
        CurrentPage = page
        Width = If(page = FhShellPage.Home, 190, If(page = FhShellPage.Guide, 0, 150))
        PanLauncher.Visibility = If(page = FhShellPage.Home, Visibility.Visible, Visibility.Collapsed)
        PanNavigation.Visibility = If(page = FhShellPage.Home OrElse page = FhShellPage.Guide, Visibility.Collapsed, Visibility.Visible)
        ItemTools.Visibility = If(page = FhShellPage.Tools OrElse page = FhShellPage.Downloads, Visibility.Visible, Visibility.Collapsed)
        ItemDownloads.Visibility = ItemTools.Visibility
        ItemConfig.Visibility = If(page = FhShellPage.Config OrElse page = FhShellPage.GameData OrElse page = FhShellPage.Personalization, Visibility.Visible, Visibility.Collapsed)
        ItemGameData.Visibility = ItemConfig.Visibility
        ItemPersonalization.Visibility = ItemConfig.Visibility
        ItemAbout.Visibility = If(page = FhShellPage.About OrElse page = FhShellPage.RuntimeInfo, Visibility.Visible, Visibility.Collapsed)
        ItemRuntimeInfo.Visibility = ItemAbout.Visibility
        SetChecked(page)
    End Sub

    Public Sub ApplyLanguage()
        ItemTools.Title = FhLanguage.Text("已安装工具", "Installed Tools")
        ItemDownloads.Title = FhLanguage.Text("下载", "Downloads")
        ItemConfig.Title = FhLanguage.Text("常规设置", "General Settings")
        ItemGameData.Title = FhLanguage.Text("游戏与数据", "Game and Data")
        ItemPersonalization.Title = FhLanguage.Text("个性化设置", "Personalization")
        ItemAbout.Title = FhLanguage.Text("关于 FH6Tools", "About FH6Tools")
        ItemRuntimeInfo.Title = FhLanguage.Text("运行时与安全", "Runtime and Safety")
        BtnLaunchGame.Text = FhLanguage.Text("启动地平线 6", "Launch Forza Horizon 6")
        BtnOpenGameBackups.Text = FhLanguage.Text("查看存档备份", "View Save Backups")
    End Sub

    Public Sub UpdateStatus(gameText As String, toolText As String, gameInstalled As Boolean, gameRunning As Boolean)
        LabGamePlatform.Text = gameText
        LabGamePlatform.ToolTip = gameText
        LabGameRuntimeState.Text = FhLanguage.Text(If(gameRunning, "游戏正在运行", "游戏未运行"), If(gameRunning, "Game running", "Game not running"))
        LabGameRuntimeState.Foreground = TryCast(System.Windows.Application.Current.TryFindResource(If(gameRunning, "ColorBrush3", "ColorBrushGray3")), Brush)
        BtnLaunchGame.IsEnabled = gameInstalled
    End Sub

    Private Async Sub BtnLaunchGame_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLaunchGame.Click
        If FrmMain IsNot Nothing Then Await FrmMain.LaunchGameFromSidebarAsync()
    End Sub

    Private Sub BtnOpenGameBackups_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenGameBackups.Click
        FrmMain?.ShowGameBackupManager()
    End Sub

    Private Sub SetChecked(page As FhShellPage)
        Dim target As MyListItem = Nothing
        Select Case page
            Case FhShellPage.Tools
                target = ItemTools
            Case FhShellPage.Downloads
                target = ItemDownloads
            Case FhShellPage.Config
                target = ItemConfig
            Case FhShellPage.GameData
                target = ItemGameData
            Case FhShellPage.About
                target = ItemAbout
            Case FhShellPage.RuntimeInfo
                target = ItemRuntimeInfo
            Case FhShellPage.Personalization
                target = ItemPersonalization
            Case Else
                Return
        End Select
        target.SetChecked(True, False, False)
    End Sub

    Private Sub NavItem_Check(sender As Object, e As RouteEventArgs) Handles ItemTools.Check, ItemDownloads.Check, ItemConfig.Check, ItemGameData.Check, ItemPersonalization.Check, ItemAbout.Check, ItemRuntimeInfo.Check
        Dim item = TryCast(sender, FrameworkElement)
        If item Is Nothing OrElse item.Tag Is Nothing OrElse FrmMain Is Nothing Then Return
        Dim page = CType(Val(item.Tag), FhShellPage)
        If page = CurrentPage Then Return
        FrmMain.PageChange(page)
    End Sub

    Private Sub PageFhToolsLeft_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        LoadGameIcon()
    End Sub

    Public Sub LoadGameIcon()
        If ImgGameIcon Is Nothing OrElse GridGameIcon Is Nothing Then Return
        Dim path = Settings.Get(Of String)("GameIconPath")
        If Not String.IsNullOrWhiteSpace(path) AndAlso IO.File.Exists(path) Then
            GridGameIcon.Margin = New Thickness(0)
            ImgGameIcon.Stretch = Stretch.UniformToFill
            ImgGameIcon.Source = path
        Else
            GridGameIcon.Margin = New Thickness(10)
            ImgGameIcon.Stretch = Stretch.Uniform
            ImgGameIcon.Source = "pack://application:,,,/FH6Tools;component/Images/Logos/forza-horizon-6-logo.png"
        End If
    End Sub

    Private Sub BorderGameIcon_Click(sender As Object, e As MouseButtonEventArgs)
        Dim openFileDialog As New OpenFileDialog()
        openFileDialog.Filter = "图像文件 (*.jpg;*.jpeg;*.png;*.bmp;*.ico)|*.jpg;*.jpeg;*.png;*.bmp;*.ico|所有文件 (*.*)|*.*"
        If openFileDialog.ShowDialog() = True Then
            Settings.Set("GameIconPath", openFileDialog.FileName)
            LoadGameIcon()
            TryCast(FrmMain?.PageRight, PageFhToolsRight)?.UpdatePersonalizationLabels()
        End If
    End Sub

    Private Sub BorderGameIcon_RightClick(sender As Object, e As MouseButtonEventArgs)
        Settings.Set("GameIconPath", "")
        LoadGameIcon()
        TryCast(FrmMain?.PageRight, PageFhToolsRight)?.UpdatePersonalizationLabels()
    End Sub

End Class
