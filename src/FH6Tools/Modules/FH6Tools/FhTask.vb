Public Enum FhTaskState
    Waiting
    Running
    Finished
    Failed
    Cancelled
End Enum

Public Class FhTask(Of T)
    Public Property Name As String
    Public Property State As FhTaskState = FhTaskState.Waiting
    Public Property Progress As Double
    Public Property Output As T
    Public Property [Error] As Exception

    Public Sub New(name As String)
        Me.Name = name
    End Sub

    Public Async Function RunAsync(action As Func(Of IProgress(Of Double), Threading.CancellationToken, Task(Of T)), cancellationToken As Threading.CancellationToken) As Task
        Try
            State = FhTaskState.Running
            Dim progressSink As New Progress(Of Double)(Sub(value) Progress = Math.Max(0, Math.Min(1, value)))
            Output = Await action(progressSink, cancellationToken)
            State = If(cancellationToken.IsCancellationRequested, FhTaskState.Cancelled, FhTaskState.Finished)
        Catch ex As OperationCanceledException
            State = FhTaskState.Cancelled
            [Error] = ex
        Catch ex As Exception
            State = FhTaskState.Failed
            [Error] = ex
        End Try
    End Function
End Class
