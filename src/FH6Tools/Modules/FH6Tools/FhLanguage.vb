Public Module FhLanguage
    Private Const DefaultLanguage As String = "zh-CN"

    Public ReadOnly Property Current As String
        Get
            Dim value = Settings.Get("UiLanguage")
            Return If(String.IsNullOrWhiteSpace(value), DefaultLanguage, value)
        End Get
    End Property

    Public ReadOnly Property IsEnglish As Boolean
        Get
            Return Current.StartsWith("en", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Public Function Text(chinese As String, english As String) As String
        Return If(IsEnglish, english, chinese)
    End Function

    Public Sub Load()
        Try
            Dim path = GetLanguagePath()
            If File.Exists(path) Then
                Dim value = File.ReadAllText(path).Trim()
                If value = "zh-CN" OrElse value = "en-US" Then Settings.Set("UiLanguage", value)
            End If
        Catch
            Settings.Set("UiLanguage", DefaultLanguage)
        End Try
    End Sub

    Public Sub SetLanguage(value As String)
        Dim normalized = If(value.StartsWith("en", StringComparison.OrdinalIgnoreCase), "en-US", DefaultLanguage)
        Settings.Set("UiLanguage", normalized)
        FhPaths.Ensure()
        File.WriteAllText(GetLanguagePath(), normalized)
    End Sub

    Private Function GetLanguagePath() As String
        Return Path.Combine(FhPaths.AppDataRoot, "ui-language.txt")
    End Function
End Module
