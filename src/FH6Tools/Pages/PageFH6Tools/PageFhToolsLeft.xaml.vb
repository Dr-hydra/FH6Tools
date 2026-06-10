Public Class PageFhToolsLeft

    Private CurrentPage As FhShellPage = FhShellPage.Home

    Public Sub Configure(page As FhShellPage)
        CurrentPage = page
        Width = If(page = FhShellPage.Home, 190, 150)
        PanLauncher.Visibility = If(page = FhShellPage.Home, Visibility.Visible, Visibility.Collapsed)
        PanNavigation.Visibility = If(page = FhShellPage.Home, Visibility.Collapsed, Visibility.Visible)
        ItemTools.Visibility = If(page = FhShellPage.Tools OrElse page = FhShellPage.Downloads, Visibility.Visible, Visibility.Collapsed)
        ItemDownloads.Visibility = ItemTools.Visibility
        ItemConfig.Visibility = If(page = FhShellPage.Config OrElse page = FhShellPage.GameData, Visibility.Visible, Visibility.Collapsed)
        ItemGameData.Visibility = ItemConfig.Visibility
        ItemAbout.Visibility = If(page = FhShellPage.About OrElse page = FhShellPage.RuntimeInfo, Visibility.Visible, Visibility.Collapsed)
        ItemRuntimeInfo.Visibility = ItemAbout.Visibility
        SetChecked(page)
    End Sub

    Public Sub ApplyLanguage()
        ItemTools.Title = FhLanguage.Text("已安装工具", "Installed Tools")
        ItemDownloads.Title = FhLanguage.Text("下载", "Downloads")
        ItemConfig.Title = FhLanguage.Text("常规设置", "General Settings")
        ItemGameData.Title = FhLanguage.Text("游戏与数据", "Game and Data")
        ItemAbout.Title = FhLanguage.Text("关于 FH6Tools", "About FH6Tools")
        ItemRuntimeInfo.Title = FhLanguage.Text("运行时与安全", "Runtime and Safety")
        BtnLaunchGame.Text = FhLanguage.Text("启动地平线 6", "Launch Forza Horizon 6")
    End Sub

    Public Sub UpdateStatus(gameText As String, toolText As String, gameInstalled As Boolean)
        LabGamePath.Text = gameText
        LabGamePath.ToolTip = gameText
        BtnLaunchGame.IsEnabled = gameInstalled
    End Sub

    Private Async Sub BtnLaunchGame_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLaunchGame.Click
        If FrmMain IsNot Nothing Then Await FrmMain.LaunchGameFromSidebarAsync()
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
            Case Else
                Return
        End Select
        target.SetChecked(True, False, False)
    End Sub

    Private Sub NavItem_Check(sender As Object, e As RouteEventArgs) Handles ItemTools.Check, ItemDownloads.Check, ItemConfig.Check, ItemGameData.Check, ItemAbout.Check, ItemRuntimeInfo.Check
        Dim item = TryCast(sender, FrameworkElement)
        If item Is Nothing OrElse item.Tag Is Nothing OrElse FrmMain Is Nothing Then Return
        Dim page = CType(Val(item.Tag), FhShellPage)
        If page = CurrentPage Then Return
        FrmMain.PageChange(page)
    End Sub

End Class
