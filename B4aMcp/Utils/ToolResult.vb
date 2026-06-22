Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Namespace Utils
    ''' <summary>
    ''' Uniform response envelope returned by every MCP tool, so callers can detect
    ''' success/failure the same way regardless of which tool produced the result:
    '''   success: { "ok": true,  "data": {...}, "warnings": [...] }   (warnings omitted when empty)
    '''   failure: { "ok": false, "error": "message" }
    ''' </summary>
    Public Class ToolResult

        ''' <summary>Success envelope. <paramref name="data"/> is any JSON-serializable object (anonymous types welcome).</summary>
        Public Shared Function Ok(Optional data As Object = Nothing, Optional warnings As IEnumerable(Of String) = Nothing) As String
            Dim o As New JObject()
            o("ok") = True
            Dim token As JToken
            If data Is Nothing Then
                token = JValue.CreateNull()
            ElseIf TypeOf data Is JToken Then
                token = DirectCast(data, JToken)   ' already-parsed JSON (e.g. a layout) — use as-is, don't re-encode
            Else
                token = JToken.FromObject(data)
            End If
            o("data") = token
            If warnings IsNot Nothing Then
                Dim list = warnings.Where(Function(w) Not String.IsNullOrEmpty(w)).ToList()
                If list.Count > 0 Then o("warnings") = New JArray(list)
            End If
            Return o.ToString(Formatting.Indented)
        End Function

        ''' <summary>Convenience success envelope carrying a single human-readable message in data.message.</summary>
        Public Shared Function Message(text As String, Optional warnings As IEnumerable(Of String) = Nothing) As String
            Return Ok(New With {.message = text}, warnings)
        End Function

        ''' <summary>
        ''' Failure envelope. The message should describe what went wrong (no "Error:" prefix needed).
        ''' Optional <paramref name="data"/> attaches context (e.g. a build log) to the failure.
        ''' </summary>
        Public Shared Function Fail(message As String, Optional data As Object = Nothing) As String
            Dim o As New JObject()
            o("ok") = False
            o("error") = message
            If data IsNot Nothing Then o("data") = JToken.FromObject(data)
            Return o.ToString(Formatting.Indented)
        End Function

    End Class
End Namespace
