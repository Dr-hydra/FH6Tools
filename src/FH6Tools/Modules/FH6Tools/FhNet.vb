Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography

Public Class FhNet
    Private Shared ReadOnly Client As New HttpClient

    Public Async Function DownloadFileAsync(url As String, targetPath As String, expectedSha256 As String, progress As IProgress(Of ToolDownloadProgress), cancellationToken As Threading.CancellationToken) As Task(Of String)
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath))
        Dim tempPath = targetPath & ".part"
        Dim existingLength As Long = If(File.Exists(tempPath), New FileInfo(tempPath).Length, 0)

        Using request As New HttpRequestMessage(HttpMethod.Get, url)
            If existingLength > 0 Then request.Headers.Range = New RangeHeaderValue(existingLength, Nothing)
            Using response = Await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                If existingLength > 0 AndAlso response.StatusCode <> Net.HttpStatusCode.PartialContent Then
                    existingLength = 0
                    File.Delete(tempPath)
                End If
                response.EnsureSuccessStatusCode()
                Dim total = If(response.Content.Headers.ContentLength.HasValue, response.Content.Headers.ContentLength.Value + existingLength, -1)
                Using source = Await response.Content.ReadAsStreamAsync(cancellationToken)
                    Using target = New FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None)
                        Dim buffer(81919) As Byte
                        Dim readTotal = existingLength
                        Dim stopwatch = Diagnostics.Stopwatch.StartNew()
                        Dim sampleBytes = readTotal
                        Dim sampleElapsed = stopwatch.Elapsed
                        Do
                            Dim read = Await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            If read = 0 Then Exit Do
                            Await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            readTotal += read
                            Dim elapsed = stopwatch.Elapsed
                            If elapsed - sampleElapsed >= TimeSpan.FromMilliseconds(250) Then
                                Dim seconds = (elapsed - sampleElapsed).TotalSeconds
                                progress?.Report(New ToolDownloadProgress With {
                                    .Fraction = If(total > 0, readTotal / CDbl(total), 0),
                                    .BytesReceived = readTotal,
                                    .TotalBytes = total,
                                    .BytesPerSecond = (readTotal - sampleBytes) / seconds
                                })
                                sampleBytes = readTotal
                                sampleElapsed = elapsed
                            End If
                        Loop
                    End Using
                End Using
            End Using
        End Using

        If Not String.IsNullOrWhiteSpace(expectedSha256) Then
            Dim hash = Await ComputeSha256Async(tempPath)
            If Not String.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException($"SHA256 mismatch. Expected {expectedSha256}, got {hash}.")
            End If
        End If

        If File.Exists(targetPath) Then File.Delete(targetPath)
        File.Move(tempPath, targetPath)
        progress?.Report(New ToolDownloadProgress With {.Fraction = 1, .BytesReceived = New FileInfo(targetPath).Length, .TotalBytes = New FileInfo(targetPath).Length})
        Return targetPath
    End Function

    Public Shared Async Function ComputeSha256Async(filePath As String) As Task(Of String)
        Using stream = File.OpenRead(filePath)
            Dim bytes = Await SHA256.HashDataAsync(stream)
            Return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()
        End Using
    End Function
End Class
