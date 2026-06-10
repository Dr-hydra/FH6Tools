Imports System.Text.Json.Serialization

Public Class ToolManifest
    Public Property SchemaVersion As Integer = 1
    Public Property UpdatedAt As String = ""
    Public Property Tools As List(Of ToolManifestEntry) = New List(Of ToolManifestEntry)
End Class

Public Class ToolManifestEntry
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Version As String = ""
    Public Property Category As String = ""
    Public Property Description As String = ""
    Public Property DescriptionZh As String = ""
    Public Property Homepage As String = ""
    Public Property DownloadUrl As String = ""
    Public Property Sha256 As String = ""
    Public Property InstallType As String = ""
    Public Property ToolType As String = "single"
    Public Property Backend As ToolEndpointDefinition
    Public Property Frontend As ToolEndpointDefinition
    Public Property [Single] As ToolEndpointDefinition
    Public Property ConfigFiles As List(Of ToolConfigFileEntry) = New List(Of ToolConfigFileEntry)
    Public Property TelemetryPort As Integer
    Public Property Hotkeys As List(Of ToolHotkeyDefinition) = New List(Of ToolHotkeyDefinition)
    Public Property RequiresAdmin As Boolean
    Public Property RiskLevel As String = "normal"
    Public Property Notes As String = ""
    Public Property OnlineStatus As String = "unknown"
    <JsonIgnore>
    Public Property Source As String = "curated"
End Class

Public Class ToolEndpointDefinition
    Public Property Executable As String = ""
    Public Property WorkingDirectory As String = ""
    Public Property DefaultPort As Integer
    Public Property HealthUrl As String = ""
    Public Property Url As String = ""
    Public Property Arguments As List(Of String) = New List(Of String)
    Public Property EnvironmentVariables As Dictionary(Of String, String) = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
End Class

Public Class ToolConfigFileEntry
    Public Property Name As String = ""
    Public Property Path As String = ""
    Public Property Kind As String = "file"
End Class

Public Class ToolHotkeyDefinition
    Public Property Name As String = ""
    Public Property DefaultValue As String = ""
    Public Property Description As String = ""
End Class

Public Class LocalToolStore
    Public Property Tools As List(Of ToolManifestEntry) = New List(Of ToolManifestEntry)
End Class

Public Class ToolInstallState
    Public Property ToolId As String = ""
    Public Property InstallPath As String = ""
    Public Property Port As Integer
    Public Property ListenAddress As String = "127.0.0.1"
    Public Property TelemetryPort As Integer
    Public Property Hotkeys As Dictionary(Of String, String) = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Public Property LastLaunchAt As String = ""
    Public Property LaunchWithGame As Boolean
    Public Property RunAsAdministrator As Boolean
    Public Property BackendOnly As Boolean
End Class

Public Class ToolDownloadProgress
    Public Property Fraction As Double
    Public Property BytesReceived As Long
    Public Property TotalBytes As Long
    Public Property BytesPerSecond As Double
End Class

Public Class ToolStateStore
    Public Property Tools As List(Of ToolInstallState) = New List(Of ToolInstallState)
    Public Property GamePath As String = ""
    Public Property LastGameLaunchAt As String = ""
End Class

Public Class ToolRuntimeStatus
    Public Property ToolId As String = ""
    Public Property IsInstalled As Boolean
    Public Property BackendProcessAlive As Boolean
    Public Property FrontendProcessAlive As Boolean
    Public Property PortListening As Boolean
    Public Property HealthOk As Boolean
    Public Property Port As Integer
    Public Property Message As String = ""

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return BackendProcessAlive OrElse FrontendProcessAlive OrElse HealthOk
        End Get
    End Property
End Class

Public Class ToolPortConflictException
    Inherits Exception

    Public ReadOnly Property Tool As ToolManifestEntry
    Public ReadOnly Property Port As Integer

    Public Sub New(tool As ToolManifestEntry, port As Integer)
        MyBase.New($"Port {port} is already in use.")
        Me.Tool = tool
        Me.Port = port
    End Sub
End Class

Public Class GameInstallState
    Public Property IsInstalled As Boolean
    Public Property Source As String = ""
    Public Property DisplayName As String = "Forza Horizon 6"
    Public Property InstallPath As String = ""
    Public Property LaunchCommand As String = ""
    Public Property LastLaunchAt As String = ""
    Public Property Message As String = ""
End Class
