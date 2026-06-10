Imports System.Globalization
Imports System.Runtime.CompilerServices

Public Module ModBase

    Public Const VersionBaseName As String = "1.0.0"
    Public Const VersionCode As Integer = 1
    Public Const VersionDisplay As String = "UI Kit 1.0.0"
    Public Const CommitHash As String = ""
    Public Const BuildTypeDisplay As String = "UI Kit"
    Public Const VersionBranchMain As String = "main"

    Public Enum BuildTypes
        Debug = 100
        Release = 50
        Snapshot = 0
    End Enum

    Public Const BuildType As BuildTypes = BuildTypes.Release
    Public ReadOnly Property ModeDebug As Boolean
        Get
            Return Settings.Get(Of Boolean)("SystemDebugMode")
        End Get
    End Property

    Public Enum ProcessReturnValues
        Success = 0
        Cancel = 1
        Exception = 2
        Fail = 3
        TaskDone = 4
    End Enum

    Public Handle As IntPtr
    Public PathExe As String = If(Environment.ProcessPath, AppDomain.CurrentDomain.BaseDirectory & AppDomain.CurrentDomain.FriendlyName)
    Public PathExeFolder As String = AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c) & "\"
    Public PathImage As String = "pack://application:,,,/FH6Tools;component/Images/"
    Public PathTemp As String = IO.Path.Combine(IO.Path.GetTempPath(), "FH6Tools") & "\"
    Public PathAppdata As String = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FH6Tools") & "\"
    Public PathPure As String = PathTemp
    Public Lang As String = "zh_CN"
    Public ApplicationStartTick As Long = GetTimeMs()
    Public ApplicationOpenTime As Date = Date.Now
    Public Identify As String = Guid.NewGuid().ToString("N").Substring(0, 8)
    Public IsProgramEnding As Boolean = False
    Public Is32BitSystem As Boolean = Not Environment.Is64BitOperatingSystem
    Public IsGBKEncoding As Boolean = Encoding.Default.CodePage = 936
    Public OsDrive As String = IO.Path.GetPathRoot(Environment.SystemDirectory)
    Public ReadOnly DPI As Integer = CInt(System.Drawing.Graphics.FromHwnd(IntPtr.Zero).DpiX)
    Public DragControl As Object

    Private UuidCounter As Integer
    Private ReadOnly RandomSource As New Random()

    Public Function GetUuid() As Integer
        Return Threading.Interlocked.Increment(UuidCounter)
    End Function

    Public Function GetTimeMs() As Long
        Return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    End Function

    Public Function RandomInteger(min As Integer, max As Integer) As Integer
        SyncLock RandomSource
            Return RandomSource.Next(min, max + 1)
        End SyncLock
    End Function

    Public Function GetWPFSize(PixelSize As Double) As Double
        Return PixelSize * 96 / DPI
    End Function

    Public Function GetPixelSize(WPFSize As Double) As Double
        Return WPFSize / 96 * DPI
    End Function

    Public Function Val(Str As Object) As Double
        If Str Is Nothing Then Return 0
        Return Conversion.Val(Str.ToString())
    End Function

    Public Sub RunInUiWait(Action As Action)
        If Application.Current Is Nothing OrElse Application.Current.Dispatcher.CheckAccess() Then
            Action()
        Else
            Application.Current.Dispatcher.Invoke(Action)
        End If
    End Sub

    Public Function RunInUiWait(Of T)(Action As Func(Of T)) As T
        If Application.Current Is Nothing OrElse Application.Current.Dispatcher.CheckAccess() Then
            Return Action()
        End If
        Return Application.Current.Dispatcher.Invoke(Action)
    End Function

    Public Sub RunInUi(Action As Action, Optional ForceWaitUntilLoaded As Boolean = False)
        If Application.Current Is Nothing OrElse Application.Current.Dispatcher.CheckAccess() Then
            Action()
        ElseIf ForceWaitUntilLoaded Then
            Application.Current.Dispatcher.Invoke(Action)
        Else
            Application.Current.Dispatcher.BeginInvoke(Action)
        End If
    End Sub

    Public Function RunInUi() As Boolean
        Return Application.Current Is Nothing OrElse Application.Current.Dispatcher.CheckAccess()
    End Function

    Public Sub RunInThread(Action As Action)
        Dim thread As New Threading.Thread(Sub() Action()) With {.IsBackground = True}
        thread.Start()
    End Sub

    Public Function RunInNewThread(Action As Action, Optional Name As String = Nothing, Optional Priority As Threading.ThreadPriority = Threading.ThreadPriority.Normal) As Threading.Thread
        Dim thread As New Threading.Thread(Sub()
            Try
                Action()
            Catch ex As Exception
                Logger.Warn(ex, "Background action failed")
            End Try
        End Sub)
        thread.IsBackground = True
        If Not String.IsNullOrWhiteSpace(Name) Then thread.Name = Name
        thread.Priority = Priority
        thread.Start()
        Return thread
    End Function

    Public Function CTypeDynamic(Value As Object, TargetType As Type) As Object
        If Value Is Nothing Then Return Nothing
        If TargetType.IsEnum Then Return [Enum].Parse(TargetType, Value.ToString(), True)
        Return Convert.ChangeType(Value, TargetType, CultureInfo.InvariantCulture)
    End Function

    Public Sub OpenWebsite(Url As String)
        Process.Start(New ProcessStartInfo(Url) With {.UseShellExecute = True})
    End Sub

    Public Sub OpenExplorer(Path As String)
        Process.Start(New ProcessStartInfo(Path) With {.UseShellExecute = True})
    End Sub

    Public Sub NetDownloadByLoader(urls As IEnumerable(Of String), targetPath As String, Optional SimulateBrowserHeaders As Boolean = False)
        Directory.CreateDirectory(IO.Path.GetDirectoryName(targetPath))
        Using client As New HttpClient()
            If SimulateBrowserHeaders Then client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 FH6Tools")
            Dim lastError As Exception = Nothing
            For Each url In urls
                Try
                    Dim bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult()
                    File.WriteAllBytes(targetPath, bytes)
                    Return
                Catch ex As Exception
                    lastError = ex
                End Try
            Next
            Throw New IOException("Download failed.", lastError)
        End Using
    End Sub

    Public Function ReadReg(Key As String, Optional DefaultValue As String = "") As String
        Return DefaultValue
    End Function

    Public Sub WriteReg(Key As String, Value As String)
    End Sub

    Public Function ReadIni(Section As String, Key As String, Optional DefaultValue As String = "") As String
        Return DefaultValue
    End Function

    Public Sub WriteIni(Section As String, Key As String, Value As String)
    End Sub

    Public Sub DeleteIniKey(Section As String, Key As String)
    End Sub

    Public Function HasIniKey(Section As String, Key As String) As Boolean
        Return False
    End Function

    Public Function HasReg(Key As String) As Boolean
        Return False
    End Function

    Public Sub CheckPermissionWithException(Folder As String)
        Directory.CreateDirectory(Folder)
    End Sub

    Public Function CheckPermission(Folder As String) As Boolean
        Try
            Directory.CreateDirectory(Folder)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Sub ExtractResources(FilePath As String, ResourceName As String)
    End Sub

    Public Class Logo
        Public Const IconButtonSetup As String = "M651.946667 1001.813333c-22.186667 0-42.666667-10.24-61.44-27.306666-23.893333-23.893333-49.493333-35.84-75.093334-35.84-29.013333 0-56.32 11.946667-73.386666 30.72v3.413333c-17.066667 17.066667-42.666667 27.306667-66.56 27.306667h-6.826667c-6.826667 0-11.946667-1.706667-15.36-1.706667l-6.826667-1.706667c-64.853333-20.48-121.173333-54.613333-168.96-98.986666-29.013333-23.893333-37.546667-63.146667-25.6-95.573334 8.533333-23.893333 5.12-51.2-10.24-75.093333-15.36-27.306667-34.133333-40.96-59.733333-47.786667h-1.706667l-5.12-1.706666c-35.84-8.533333-61.44-34.133333-66.56-69.973334C1.706667 575.146667 0 537.6 0 512c0-32.426667 3.413333-63.146667 8.533333-93.866667v-6.826666l3.413334-8.533334c10.24-23.893333 23.893333-40.96 44.373333-51.2 5.12-3.413333 11.946667-6.826667 20.48-8.533333 27.306667-8.533333 51.2-25.6 63.146667-44.373333 13.653333-23.893333 17.066667-52.906667 10.24-81.92-11.946667-34.133333 0-71.68 30.72-93.866667 44.373333-37.546667 97.28-68.266667 158.72-93.866667l3.413333-1.706666c44.373333-13.653333 75.093333 3.413333 92.16 20.48 23.893333 23.893333 49.493333 35.84 75.093333 35.84 30.72 0 56.32-10.24 71.68-30.72l3.413334-3.413334c27.306667-27.306667 63.146667-35.84 93.866666-22.186666 63.146667 22.186667 117.76 54.613333 165.546667 97.28 29.013333 23.893333 37.546667 63.146667 25.6 95.573333-8.533333 23.893333-5.12 51.2 10.24 75.093333 15.36 27.306667 34.133333 40.96 59.733333 47.786667h1.706667l5.12 1.706667c35.84 8.533333 61.44 34.133333 66.56 71.68 6.826667 30.72 10.24 63.146667 11.946667 93.866666v3.413334c0 32.426667-3.413333 63.146667-8.533334 93.866666v6.826667l-3.413333 8.533333c-10.24 23.893333-23.893333 40.96-44.373333 51.2-5.12 3.413333-11.946667 6.826667-20.48 8.533334-27.306667 8.533333-51.2 25.6-63.146667 46.08-13.653333 23.893333-17.066667 52.906667-10.24 81.92 11.946667 35.84-1.706667 75.093333-30.72 95.573333-44.373333 35.84-95.573333 66.56-157.013333 92.16-15.36 3.413333-27.306667 3.413333-35.84 3.413333z"
    End Class

    Public Class MyColor
        Public A As Double = 255
        Public R As Double
        Public G As Double
        Public B As Double

        Public Sub New()
        End Sub

        Public Sub New(value As String)
            Me.New(CType(ColorConverter.ConvertFromString(value), Color))
        End Sub

        Public Sub New(gray As Double)
            Me.New(255, gray, gray, gray)
        End Sub

        Public Sub New(r As Double, g As Double, b As Double)
            Me.New(255, r, g, b)
        End Sub

        Public Sub New(a As Double, r As Double, g As Double, b As Double)
            Me.A = a
            Me.R = r
            Me.G = g
            Me.B = b
        End Sub

        Public Sub New(a As Double, color As MyColor)
            Me.New(a, color.R, color.G, color.B)
        End Sub

        Public Sub New(color As Color)
            Me.New(color.A, color.R, color.G, color.B)
        End Sub

        Public Sub New(brush As Brush)
            If TypeOf brush Is SolidColorBrush Then
                Dim c = DirectCast(brush, SolidColorBrush).Color
                A = c.A : R = c.R : G = c.G : B = c.B
            End If
        End Sub

        Public Shared Widening Operator CType(str As String) As MyColor
            Return New MyColor(CType(ColorConverter.ConvertFromString(str), Color))
        End Operator

        Public Shared Widening Operator CType(color As Color) As MyColor
            Return New MyColor(color)
        End Operator

        Public Shared Widening Operator CType(color As MyColor) As Color
            Return System.Windows.Media.Color.FromArgb(ToByte(color.A), ToByte(color.R), ToByte(color.G), ToByte(color.B))
        End Operator

        Public Shared Widening Operator CType(brush As Brush) As MyColor
            Return New MyColor(brush)
        End Operator

        Public Shared Widening Operator CType(color As MyColor) As SolidColorBrush
            Return New SolidColorBrush(CType(color, Color))
        End Operator

        Public Shared Widening Operator CType(color As MyColor) As Brush
            Return New SolidColorBrush(CType(color, Color))
        End Operator

        Public Shared Operator +(a As MyColor, b As MyColor) As MyColor
            Return New MyColor(a.A + b.A, a.R + b.R, a.G + b.G, a.B + b.B)
        End Operator

        Public Shared Operator -(a As MyColor, b As MyColor) As MyColor
            Return New MyColor(a.A - b.A, a.R - b.R, a.G - b.G, a.B - b.B)
        End Operator

        Public Shared Operator *(a As MyColor, value As Double) As MyColor
            Return New MyColor(a.A * value, a.R * value, a.G * value, a.B * value)
        End Operator

        Public Function FromHSL2(h As Double, s As Double, l As Double) As MyColor
            s /= 100
            l /= 100
            Dim c = (1 - Math.Abs(2 * l - 1)) * s
            Dim x = c * (1 - Math.Abs((h / 60) Mod 2 - 1))
            Dim m = l - c / 2
            Dim rr As Double, gg As Double, bb As Double
            Select Case CInt(Math.Floor(h / 60)) Mod 6
                Case 0 : rr = c : gg = x
                Case 1 : rr = x : gg = c
                Case 2 : gg = c : bb = x
                Case 3 : gg = x : bb = c
                Case 4 : rr = x : bb = c
                Case Else : rr = c : bb = x
            End Select
            Return New MyColor(255, (rr + m) * 255, (gg + m) * 255, (bb + m) * 255)
        End Function

        Private Shared Function ToByte(value As Double) As Byte
            Return CByte(Math.Max(0, Math.Min(255, Math.Round(value))))
        End Function
    End Class

    Public NotInheritable Class RouteEventArgs
        Inherits EventArgs

        Public Property RaiseByMouse As Boolean
        Public Property Handled As Boolean

        Public Sub New(Optional raiseByMouse As Boolean = False)
            Me.RaiseByMouse = raiseByMouse
        End Sub
    End Class

    <Extension>
    Public Function IsInstanceOfGenericType(genericType As Type, obj As Object) As Boolean
        Dim type = If(TryCast(obj, Type), obj?.GetType())
        While type IsNot Nothing
            If type.IsGenericType AndAlso type.GetGenericTypeDefinition() Is genericType Then Return True
            type = type.BaseType
        End While
        Return False
    End Function

End Module

Public Class Logo
    Public Const IconButtonSetup As String = ModBase.Logo.IconButtonSetup
End Class
