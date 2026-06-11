Public Enum FhShellPage
    Home = 0
    Tools = 1
    Downloads = 2
    Config = 3
    About = 4
    GameData = 5
    RuntimeInfo = 6
    Guide = 7
End Enum

Public Class FhShellHost
    Public Property CurrentPage As FhShellPage = FhShellPage.Home
    Public Property LastPage As FhShellPage = FhShellPage.Home
End Class

Public Module FhShellText
    Public Function GetPageTitle(page As FhShellPage) As String
        Select Case page
            Case FhShellPage.Guide
                Return FhLanguage.Text("攻略", "Guide")
            Case FhShellPage.Tools
                Return FhLanguage.Text("工具", "Tools")
            Case FhShellPage.Downloads
                Return FhLanguage.Text("下载管理", "Downloads")
            Case FhShellPage.Config
                Return FhLanguage.Text("常规设置", "General Settings")
            Case FhShellPage.About
                Return FhLanguage.Text("关于 FH6Tools", "About FH6Tools")
            Case FhShellPage.GameData
                Return FhLanguage.Text("游戏与数据", "Game and Data")
            Case FhShellPage.RuntimeInfo
                Return FhLanguage.Text("运行时与安全", "Runtime and Safety")
            Case Else
                Return FhLanguage.Text("启动台", "Launcher")
        End Select
    End Function

    Public Function GetPageSubtitle(page As FhShellPage) As String
        Select Case page
            Case FhShellPage.Guide
                Return FhLanguage.Text("查看每周季节赛调校图流与社区攻略。", "View the weekly season tune sheet and community guide.")
            Case FhShellPage.Tools
                Return FhLanguage.Text("管理精选与本地 Mod 工具。", "Manage curated and local mod tools.")
            Case FhShellPage.Downloads
                Return FhLanguage.Text("管理工具下载、安装队列与任务。", "Manage downloads, install queues, and jobs.")
            Case FhShellPage.Config
                Return FhLanguage.Text("管理工具配置、备份与界面设置。", "Manage tool configuration, backups, and interface settings.")
            Case FhShellPage.About
                Return FhLanguage.Text("查看项目信息与安全说明。", "View project information and safety notes.")
            Case FhShellPage.GameData
                Return FhLanguage.Text("管理游戏路径、数据与备份。", "Manage game paths, data, and backups.")
            Case FhShellPage.RuntimeInfo
                Return FhLanguage.Text("查看共享运行时与安全策略。", "View shared runtime and safety policy.")
            Case Else
                Return FhLanguage.Text("启动极限竞速：地平线 6。", "Launch Forza Horizon 6.")
        End Select
    End Function
End Module

Public Module FhShellNavigation
    Public Function GetActiveScroll(child As Object) As MyScrollViewer
        If child Is Nothing OrElse TypeOf child IsNot MyPageRight Then Return Nothing
        Dim page As MyPageRight = child
        If String.IsNullOrWhiteSpace(page.PanScroll) Then Return Nothing
        Return TryCast(page.FindName(page.PanScroll), MyScrollViewer)
    End Function
End Module

Public Module FhFileDropService
    Public Function HasFileDrop(e As DragEventArgs) As Boolean
        Return e IsNot Nothing AndAlso e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop)
    End Function

    Public Function GetDroppedFiles(e As DragEventArgs) As List(Of String)
        If Not HasFileDrop(e) Then Return New List(Of String)
        Dim raw = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If raw Is Nothing Then Return New List(Of String)
        Return raw.Where(Function(path) Not String.IsNullOrWhiteSpace(path)).ToList()
    End Function
End Module
