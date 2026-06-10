Imports System.Net
Imports System.Net.Http
Imports System.Net.NetworkInformation

Public Class ToolRuntimeService
    Private ReadOnly InstallService As New ToolInstallService
    Private ReadOnly BackendProcesses As New Dictionary(Of String, Process)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly FrontendProcesses As New Dictionary(Of String, Process)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly PortOverrides As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(2)}

    Public Async Function GetStatusAsync(tool As ToolManifestEntry) As Task(Of ToolRuntimeStatus)
        Dim status As New ToolRuntimeStatus With {
            .ToolId = tool.Id,
            .IsInstalled = InstallService.IsInstalled(tool),
            .Port = GetConfiguredPort(tool)
        }
        If ToolInstallService.IsBlocked(tool) Then
            status.Message = "blocked by safety policy"
            Return status
        End If
        status.BackendProcessAlive = IsProcessAlive(BackendProcesses, tool.Id)
        status.FrontendProcessAlive = IsProcessAlive(FrontendProcesses, tool.Id)
        status.PortListening = status.Port > 0 AndAlso IsPortListening(status.Port)
        status.HealthOk = Await CheckHealthAsync(tool, status.Port)
        If status.HealthOk Then status.PortListening = True
        status.Message = BuildStatusMessage(tool, status)
        Return status
    End Function

    Public Async Function StartAllAsync(tool As ToolManifestEntry) As Task(Of ToolRuntimeStatus)
        EnsureAllowed(tool)
        Select Case NormalizeType(tool.ToolType)
            Case "backendOnly"
                Await StartBackendAsync(tool)
            Case "frontendBackend"
                Await StartBackendAsync(tool)
                Await StartFrontendAsync(tool)
            Case Else
                Await StartSingleAsync(tool)
        End Select
        Return Await GetStatusAsync(tool)
    End Function

    Public Function StartSingleAsync(tool As ToolManifestEntry) As Task
        EnsureAllowed(tool)
        Dim endpoint = tool.[Single]
        If endpoint Is Nothing Then Throw New InvalidOperationException("Tool has no single-process definition.")
        StartEndpoint(tool, endpoint, FrontendProcesses, 0)
        Return Task.CompletedTask
    End Function

    Public Async Function StartBackendAsync(tool As ToolManifestEntry, Optional reuseExistingService As Boolean = False, Optional stopPortOwner As Boolean = False) As Task
        EnsureAllowed(tool)
        Dim endpoint = If(tool.Backend, tool.[Single])
        If endpoint Is Nothing Then Throw New InvalidOperationException("Tool has no backend definition.")
        Dim port = GetConfiguredPort(tool)
        If port > 0 AndAlso IsPortListening(port) AndAlso Not Await CheckHealthAsync(tool, port) Then
            If reuseExistingService Then Return
            If stopPortOwner AndAlso TryStopProcessOnPort(port) Then
                Await Task.Delay(500)
            End If
            If IsPortListening(port) Then Throw New ToolPortConflictException(tool, port)
        End If
        StartEndpoint(tool, endpoint, BackendProcesses, port)
    End Function

    Public Async Function StartFrontendAsync(tool As ToolManifestEntry) As Task
        EnsureAllowed(tool)
        Dim endpoint = If(tool.Frontend, tool.[Single])
        If endpoint Is Nothing Then Throw New InvalidOperationException("Tool has no frontend definition.")
        Dim port = GetConfiguredPort(tool)
        If NormalizeType(tool.ToolType) = "frontendBackend" AndAlso tool.Backend IsNot Nothing Then
            Await WaitForBackendAsync(tool, port)
        End If
        If Not String.IsNullOrWhiteSpace(endpoint.Url) Then
            Process.Start(New ProcessStartInfo With {.FileName = ApplyPort(endpoint.Url, port), .UseShellExecute = True})
            Return
        End If
        StartEndpoint(tool, endpoint, FrontendProcesses, port)
    End Function

    Public Function StopTool(tool As ToolManifestEntry) As Boolean
        Dim stopped = StopProcess(BackendProcesses, tool.Id)
        stopped = StopProcess(FrontendProcesses, tool.Id) OrElse stopped
        Return stopped
    End Function

    Public Function GetConfiguredPort(tool As ToolManifestEntry) As Integer
        If PortOverrides.ContainsKey(tool.Id) Then Return PortOverrides(tool.Id)
        If tool.Backend IsNot Nothing AndAlso tool.Backend.DefaultPort > 0 Then Return tool.Backend.DefaultPort
        If tool.[Single] IsNot Nothing AndAlso tool.[Single].DefaultPort > 0 Then Return tool.[Single].DefaultPort
        Return 0
    End Function

    Public Sub SetPortOverride(tool As ToolManifestEntry, port As Integer)
        If port > 0 Then PortOverrides(tool.Id) = port
    End Sub

    Public Function GetToolRoot(tool As ToolManifestEntry) As String
        Return InstallService.GetInstallPath(tool)
    End Function

    Private Sub StartEndpoint(tool As ToolManifestEntry, endpoint As ToolEndpointDefinition, store As Dictionary(Of String, Process), port As Integer)
        If IsProcessAlive(store, tool.Id) Then Return
        Dim basePath = If(tool.InstallType = "local", "", InstallService.GetInstallPath(tool))
        Dim executable = ToolInstallService.ResolveInstalledExecutable(endpoint, basePath)
        If Not File.Exists(executable) Then Throw New FileNotFoundException("Executable not found.", executable)
        Dim workingDirectory = If(String.IsNullOrWhiteSpace(endpoint.WorkingDirectory),
                                  Path.GetDirectoryName(executable),
                                  FhPaths.ExpandPath(endpoint.WorkingDirectory, basePath))
        Dim info As New ProcessStartInfo With {
            .FileName = executable,
            .WorkingDirectory = workingDirectory,
            .UseShellExecute = False
        }
        ApplySharedRuntimeEnvironment(info)
        ApplyEnvironment(endpoint, info, basePath)
        For Each argument In endpoint.Arguments
            info.ArgumentList.Add(ApplyPort(argument, port))
        Next
        Dim process As Process = Process.Start(info)
        If process IsNot Nothing Then store(tool.Id) = process
    End Sub

    Private Async Function WaitForBackendAsync(tool As ToolManifestEntry, port As Integer) As Task
        For i = 0 To 20
            If Await CheckHealthAsync(tool, port) Then Return
            Await Task.Delay(250)
        Next
    End Function

    Private Async Function CheckHealthAsync(tool As ToolManifestEntry, port As Integer) As Task(Of Boolean)
        If tool.Backend Is Nothing OrElse String.IsNullOrWhiteSpace(tool.Backend.HealthUrl) Then Return False
        Try
            Using response = Await Client.GetAsync(ApplyPort(tool.Backend.HealthUrl, port))
                Return response.IsSuccessStatusCode
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Shared Function IsPortListening(port As Integer) As Boolean
        If IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().
            Any(Function(ep) ep.Port = port AndAlso (IPAddress.IsLoopback(ep.Address) OrElse ep.Address.Equals(IPAddress.Any) OrElse ep.Address.Equals(IPAddress.IPv6Any)))
            Return True
        End If

        Try
            Using client As New Net.Sockets.TcpClient()
                Dim connect = client.ConnectAsync(IPAddress.Loopback, port)
                Return connect.Wait(TimeSpan.FromMilliseconds(500)) AndAlso client.Connected
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Shared Function IsProcessAlive(store As Dictionary(Of String, Process), id As String) As Boolean
        If Not store.ContainsKey(id) Then Return False
        Try
            Return Not store(id).HasExited
        Catch
            Return False
        End Try
    End Function

    Private Shared Function StopProcess(store As Dictionary(Of String, Process), id As String) As Boolean
        If Not store.ContainsKey(id) Then Return False
        Try
            If Not store(id).HasExited Then store(id).Kill(True)
            store.Remove(id)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared Function TryStopProcessOnPort(port As Integer) As Boolean
        Try
            Dim info As New ProcessStartInfo With {
                .FileName = "netstat.exe",
                .Arguments = "-ano -p tcp",
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True
            }
            Using process As Process = Process.Start(info)
                Dim output = process.StandardOutput.ReadToEnd()
                process.WaitForExit(3000)
                For Each line In output.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim parts = line.Split({" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                    If parts.Length < 5 Then Continue For
                    If Not parts(1).EndsWith(":" & port.ToString(Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim pid As Integer
                    If Integer.TryParse(parts(4), pid) AndAlso pid > 0 Then
                        Dim owner = Process.GetProcessById(pid)
                        owner.Kill(True)
                        Return True
                    End If
                Next
            End Using
        Catch
        End Try
        Return False
    End Function

    Private Shared Function BuildStatusMessage(tool As ToolManifestEntry, status As ToolRuntimeStatus) As String
        If Not status.IsInstalled Then Return "not installed"
        If status.HealthOk Then Return "running and healthy"
        If status.BackendProcessAlive OrElse status.FrontendProcessAlive Then Return "process running"
        If status.PortListening Then Return "port occupied"
        Return "ready"
    End Function

    Private Shared Sub EnsureAllowed(tool As ToolManifestEntry)
        If ToolInstallService.IsBlocked(tool) Then Throw New InvalidOperationException("This tool is blocked by the FH6Tools safety policy.")
    End Sub

    Private Shared Sub ApplyEnvironment(endpoint As ToolEndpointDefinition, info As ProcessStartInfo, toolRoot As String)
        If endpoint.EnvironmentVariables Is Nothing Then Return
        For Each pair In endpoint.EnvironmentVariables
            info.Environment(pair.Key) = FhPaths.ApplyTokens(pair.Value, toolRoot)
        Next
    End Sub

    Private Shared Sub ApplySharedRuntimeEnvironment(info As ProcessStartInfo)
        Dim dotnetRoot = FhPaths.SharedDotNetRoot
        info.Environment("DOTNET_ROOT") = dotnetRoot
        info.Environment("DOTNET_ROOT_X64") = dotnetRoot
        info.Environment("DOTNET_MULTILEVEL_LOOKUP") = "0"

        Dim currentPath As String = Nothing
        If info.Environment.TryGetValue("PATH", currentPath) AndAlso Not String.IsNullOrWhiteSpace(currentPath) Then
            info.Environment("PATH") = dotnetRoot & Path.PathSeparator & currentPath
        Else
            info.Environment("PATH") = dotnetRoot
        End If
    End Sub

    Private Shared Function NormalizeType(value As String) As String
        Return If(value, "single").Trim()
    End Function

    Private Shared Function ApplyPort(value As String, port As Integer) As String
        Return If(value, "").Replace("{port}", port.ToString(Globalization.CultureInfo.InvariantCulture))
    End Function

End Class
