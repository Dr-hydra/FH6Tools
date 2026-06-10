Imports System.Windows.Interop

Public Class FormMain

    Private ReadOnly PageHost As New FhShellHost()
    Private PageNav As PageFhToolsLeft
    Private PageMain As PageFhToolsRight
    Private IsSizeSaveable As Boolean = False
    Private IsPageChanging As Boolean

    Public PageLeft As MyPageLeft
    Public PageRight As MyPageRight
    Public Property Hidden As Boolean

    Public Sub New()
        ApplicationStartTick = GetTimeMs()
        FrmMain = Me
        ThemeCheckAll(False)
        ThemeRefresh(Settings.Get(Of Integer)("UiLauncherTheme"))

        PageNav = New PageFhToolsLeft()
        PageMain = New PageFhToolsRight()

        InitializeComponent()
        Opacity = 0

        PanMainLeft.Child = PageNav
        PageLeft = PageNav
        PanMainRight.Child = PageMain
        PageRight = PageMain
        PageHost.CurrentPage = FhShellPage.Home
        PageNav.Configure(FhShellPage.Home)
        PageMain.Configure(FhShellPage.Home)
        PageMain.PageState = MyPageRight.PageStates.ContentStay
    End Sub

    Private Sub FormMain_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Handle = New WindowInteropHelper(Me).Handle
        UpdateBackgroundAndTitleBar()
        BtnExtraBack.ShowCheck = AddressOf BtnExtraBack_ShowCheck

        Dim Resizer As New MyResizer(Me)
        Resizer.addResizerDown(ResizerB)
        Resizer.addResizerLeft(ResizerL)
        Resizer.addResizerLeftDown(ResizerLB)
        Resizer.addResizerLeftUp(ResizerLT)
        Resizer.addResizerRight(ResizerR)
        Resizer.addResizerRightDown(ResizerRB)
        Resizer.addResizerRightUp(ResizerRT)
        Resizer.addResizerUp(ResizerT)

        ThemeRefreshMain()
        BtnTitleSelect0.SetChecked(True, False, False)
        Height = Math.Max(Settings.Get(Of Integer)("WindowHeight"), MinHeight)
        Width = Math.Max(Settings.Get(Of Integer)("WindowWidth"), MinWidth)
        Top = (GetWPFSize(System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Height) - Height) / 2
        Left = (GetWPFSize(System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Width) - Width) / 2
        IsSizeSaveable = True
        ShowWindowToTop()

        AniStart({
            AaCode(Sub() AniControlEnabled = 0, 50),
            AaOpacity(Me, Settings.Get(Of Integer)("UiLauncherTransparent") / 1000 + 0.4, 250, 100),
            AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, 600, 100, New AniEaseOutBack(AniEasePower.Weak)),
            AaDouble(Sub(i) TransformRotate.Angle += i, -TransformRotate.Angle, 500, 100, New AniEaseOutBack(AniEasePower.Weak)),
            AaCode(Sub()
                       PanBack.RenderTransform = Nothing
                       PageNav.TriggerShowAnimation()
                       Logger.Info("FH6Tools loaded.")
                   End Sub, , True)
        }, "Form Show")

        Hint("FH6Tools control center loaded.", HintType.Green)
        FhPaths.Ensure()
        PageMain.InitializeDeferred()
    End Sub

    Public Shared Sub UpdateBackgroundAndTitleBar(value)
        If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
        FrmMain.UpdateBackgroundAndTitleBar()
    End Sub

    Public Sub UpdateBackgroundAndTitleBar()
        ShapeTitleLogo.Visibility = Visibility.Collapsed
        LabTitleLogo.Visibility = Visibility.Visible
        LabTitleStatus.Visibility = Visibility.Visible
        ImageTitleLogo.Visibility = Visibility.Collapsed
        PanTitleSelect.Visibility = Visibility.Visible
        LabTitleLogo.Text = "FH6Tools"
        LabTitleStatus.Text = FhLanguage.Text("地平线 6 工具中心", "Forza Horizon 6 Tool Center")
        PanTitleMain.ColumnDefinitions(0).Width = New GridLength(1, GridUnitType.Star)
        ApplyLanguage()
    End Sub

    Public Sub ShowGameBackupManager()
        PageMain?.ShowGameBackupManager()
    End Sub

    Private Sub BtnTitleClose_Click(sender As Object, e As EventArgs) Handles BtnTitleClose.Click
        System.Windows.Application.Current.Shutdown()
    End Sub

    Private Sub FormMain_Closed(sender As Object, e As EventArgs) Handles Me.Closed
        If System.Windows.Application.Current IsNot Nothing Then
            System.Windows.Application.Current.Shutdown()
        End If
    End Sub

    Private Sub BtnTitleMin_Click(sender As Object, e As EventArgs) Handles BtnTitleMin.Click
        WindowState = WindowState.Minimized
    End Sub

    Private Sub FormDragMove(sender As Object, e As MouseButtonEventArgs) Handles PanTitle.MouseLeftButtonDown, PanMsg.MouseLeftButtonDown
        If e.ClickCount >= 2 Then
            WindowState = If(WindowState = WindowState.Maximized, WindowState.Normal, WindowState.Maximized)
        ElseIf sender.IsMouseDirectlyOver Then
            DragMove()
        End If
    End Sub

    Private Sub FormMain_DragEnter(sender As Object, e As DragEventArgs) Handles Me.DragEnter, Me.DragOver
        e.Handled = True
        e.Effects = If(FhFileDropService.HasFileDrop(e), DragDropEffects.Copy, DragDropEffects.None)
    End Sub

    Private Async Sub FormMain_Drop(sender As Object, e As DragEventArgs) Handles Me.Drop
        Dim files = FhFileDropService.GetDroppedFiles(e)
        If files.Count = 0 Then Return
        Await AddDroppedLocalToolsAsync(files)
    End Sub

    Private Async Function AddDroppedLocalToolsAsync(files As IEnumerable(Of String)) As Task
        Dim service As New ToolManifestService()
        Dim added As Integer = 0
        For Each path In files.Where(Function(filePath) IO.File.Exists(filePath) AndAlso IO.Path.GetExtension(filePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            Await service.AddLocalToolAsync(path)
            added += 1
        Next
        If added > 0 Then
            Hint($"Added {added} local tool(s).", HintType.Green)
            Await PageMain.InitializeAsync()
            UpdateShellStatus(PageMain.GameSummary, PageMain.ToolsSummary)
        Else
            Hint("Drop executable files to add local tools.", HintType.Blue)
        End If
    End Function

    Private Sub FormMain_SizeChanged() Handles Me.SizeChanged, Me.Loaded
        If IsSizeSaveable Then
            Settings.Set("WindowHeight", CInt(Height))
            Settings.Set("WindowWidth", CInt(Width))
        End If
        RectForm.Rect = New Rect(0, 0, BorderForm.ActualWidth, BorderForm.ActualHeight)
        PanForm.Width = BorderForm.ActualWidth + 0.001
        PanForm.Height = BorderForm.ActualHeight + 0.001
        PanMain.Width = PanForm.Width
        PanMain.Height = Math.Max(0, PanForm.Height - PanTitle.ActualHeight)
        If WindowState = WindowState.Maximized Then WindowState = WindowState.Normal
    End Sub

    Private Sub BtnTitleSelect_Click(sender As MyRadioButton, raiseByMouse As Boolean) Handles BtnTitleSelect0.Check, BtnTitleSelect1.Check, BtnTitleSelect2.Check, BtnTitleSelect3.Check
        PageChange(CType(Val(sender.Tag), FhShellPage))
    End Sub

    Public Sub PageChange(page As FhShellPage)
        PageChange(page, True)
    End Sub

    Private Sub PageChange(page As FhShellPage, animated As Boolean)
        If IsPageChanging OrElse PageHost.CurrentPage = page AndAlso animated Then Return
        IsPageChanging = True
        Try
            PageHost.LastPage = PageHost.CurrentPage
            PageHost.CurrentPage = page
            PageNav.Configure(page)
            PageMain.Configure(page)
            If Not ReferenceEquals(PanMainRight.Child, PageMain) Then PanMainRight.Child = PageMain
            PageRight = PageMain
        Finally
            IsPageChanging = False
        End Try
    End Sub

    Private Function GetRightPage(page As FhShellPage) As PageFhToolsRight
        Return PageMain
    End Function

    Private Sub PageChangeAnim(target As MyPageRight)
        If target Is Nothing Then Return
        AniStop("FrmMain PageChangeRight")
        AniControlEnabled += 1
        If PanMainRight.Child IsNot Nothing AndAlso TypeOf PanMainRight.Child Is MyPageRight Then
            CType(PanMainRight.Child, MyPageRight).PageOnExit()
        End If
        AniControlEnabled -= 1
        AniStart({
            AaCode(Sub()
                       AniControlEnabled += 1
                       If PanMainRight.Child IsNot Nothing AndAlso TypeOf PanMainRight.Child Is MyPageRight Then
                           CType(PanMainRight.Child, MyPageRight).PageOnForceExit()
                       End If
                       PanMainRight.Child = target
                       PageRight = target
                       target.Opacity = 0
                       AniControlEnabled -= 1
                       BtnExtraBack.ShowRefresh()
                   End Sub, 110),
            AaCode(Sub()
                       target.Opacity = 1
                       target.PageOnEnter()
                   End Sub, 30, True)
        }, "FrmMain PageChangeRight")
    End Sub

    Private Sub PanMainLeft_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles PanMainLeft.SizeChanged
        If Not e.WidthChanged Then Return
        RectLeftBackground.Width = e.NewSize.Width
        RectLeftShadow.Opacity = If(e.NewSize.Width > 0, 1, 0)
    End Sub

    Public Sub UpdateShellStatus(gameText As String, toolText As String)
        PageNav?.UpdateStatus(PageMain?.GameSummary, toolText, PageMain IsNot Nothing AndAlso PageMain.IsGameInstalled)
    End Sub

    Public Async Function LaunchGameFromSidebarAsync() As Task
        If PageMain IsNot Nothing Then Await PageMain.LaunchCurrentGameAsync()
    End Function

    Public Sub ApplyLanguage()
        BtnTitleSelect0.Text = FhLanguage.Text("启动台", "Launcher")
        BtnTitleSelect1.Text = FhLanguage.Text("工具", "Tools")
        BtnTitleSelect2.Text = FhLanguage.Text("设置", "Settings")
        BtnTitleSelect3.Text = FhLanguage.Text("关于", "About")
        LabTitleStatus.Text = FhLanguage.Text("地平线 6 工具中心", "Forza Horizon 6 Tool Center")
        PageNav?.ApplyLanguage()
        PageMain?.ApplyLanguage()
    End Sub

    Public Sub ShowWindowToTop()
        Visibility = Visibility.Visible
        ShowInTaskbar = True
        WindowState = WindowState.Normal
        Topmost = True
        Topmost = False
        Activate()
        Focus()
    End Sub

    Public Sub BackToTop() Handles BtnExtraBack.Click
        Dim scroll = FhShellNavigation.GetActiveScroll(PanMainRight.Child)
        If scroll IsNot Nothing Then scroll.PerformVerticalOffsetDelta(-scroll.VerticalOffset)
    End Sub

    Private Function BtnExtraBack_ShowCheck() As Boolean
        Dim scroll = FhShellNavigation.GetActiveScroll(PanMainRight.Child)
        Return scroll IsNot Nothing AndAlso scroll.Visibility = Visibility.Visible AndAlso scroll.VerticalOffset > Height + If(BtnExtraBack.Show, 0, 700)
    End Function

    Public Sub DragDoing()
    End Sub

    Public Sub DragStop()
    End Sub

    Public Sub DragTick()
    End Sub

    Public Sub SliderDrag_Finish()
    End Sub

    Public Shared Sub EndProgramForce(returnValue As ProcessReturnValues)
        Environment.Exit(CInt(returnValue))
    End Sub

End Class
