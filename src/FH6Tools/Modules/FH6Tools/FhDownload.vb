Public Class FhDownload
    Public Property Tool As ToolManifestEntry
    Public Property TargetPath As String = ""
    Public Property Task As FhTask(Of String)

    Public Sub New(tool As ToolManifestEntry)
        Me.Tool = tool
        Me.Task = New FhTask(Of String)("Download " & tool.Id)
    End Sub
End Class
