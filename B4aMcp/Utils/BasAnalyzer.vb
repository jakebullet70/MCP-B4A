Imports System.Text.RegularExpressions

Namespace Utils
    ''' <summary>
    ''' Lightweight structural parser for B4X (.bas) source modules.
    ''' B4A has no separate Function keyword — every routine is a Sub, optionally
    ''' with an "As ReturnType" suffix. Matching is case-insensitive because B4A
    ''' itself is case-insensitive.
    ''' </summary>
    Public Class BasAnalyzer

        ' Public/Private Sub Name(params) [As ReturnType]
        Private Shared ReadOnly SubRx As New Regex(
            "^\s*(?<vis>Public\s+|Private\s+)?Sub\s+(?<name>[A-Za-z_]\w*)\s*(\((?<params>[^)]*)\))?\s*(As\s+(?<ret>[A-Za-z_]\w*))?\s*$",
            RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        ' Type Name(field As T, ...)
        Private Shared ReadOnly TypeRx As New Regex(
            "^\s*Type\s+(?<name>[A-Za-z_]\w*)\s*\((?<fields>.*)\)\s*$",
            RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        ' #Region <title>  /  #End Region
        Private Shared ReadOnly RegionRx As New Regex("^\s*#Region\s*(?<title>.*)$", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
        Private Shared ReadOnly EndRegionRx As New Regex("^\s*#End\s+Region\b", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        ' [Public|Private|Dim|Global] [Const] <name> As <type>  (globals inside *_Globals subs)
        Private Shared ReadOnly DeclRx As New Regex(
            "^\s*(Public|Private|Dim|Global)\s+(Const\s+)?(?<name>[A-Za-z_]\w*)\b",
            RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        Public Class SubInfo
            Public Property Name As String
            Public Property Visibility As String
            Public Property Params As String
            Public Property ReturnType As String
            Public Property StartLine As Integer
            Public Property EndLine As Integer
        End Class

        Public Class OutlineResult
            Public Property Subs As New List(Of SubInfo)
            Public Property Types As New List(Of Object)
            Public Property Regions As New List(Of Object)
            Public Property Globals As New List(Of Object)
            Public Property LineCount As Integer
        End Class

        Public Shared Function Outline(content As String) As OutlineResult
            Dim lines = content.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim result As New OutlineResult With {.LineCount = lines.Length}

            Dim current As SubInfo = Nothing
            Dim inGlobals As Boolean = False
            Dim regionStack As New Stack(Of (Title As String, Line As Integer))

            For i = 0 To lines.Length - 1
                Dim line = lines(i)
                Dim lineNo = i + 1
                Dim trimmed = line.TrimStart()

                ' Regions
                Dim rm = RegionRx.Match(line)
                If rm.Success Then
                    regionStack.Push((rm.Groups("title").Value.Trim(), lineNo))
                    Continue For
                End If
                If EndRegionRx.IsMatch(line) AndAlso regionStack.Count > 0 Then
                    Dim r = regionStack.Pop()
                    result.Regions.Add(New With {.title = r.Title, .startLine = r.Line, .endLine = lineNo})
                    Continue For
                End If

                ' End Sub closes the current sub
                If current IsNot Nothing AndAlso Regex.IsMatch(trimmed, "^End\s+Sub\b", RegexOptions.IgnoreCase) Then
                    current.EndLine = lineNo
                    result.Subs.Add(current)
                    inGlobals = False
                    current = Nothing
                    Continue For
                End If

                ' Sub definitions (only when not already inside a sub)
                If current Is Nothing Then
                    Dim sm = SubRx.Match(line)
                    If sm.Success Then
                        Dim name = sm.Groups("name").Value
                        current = New SubInfo With {
                            .Name = name,
                            .Visibility = sm.Groups("vis").Value.Trim().ToLowerInvariant(),
                            .Params = sm.Groups("params").Value.Trim(),
                            .ReturnType = sm.Groups("ret").Value.Trim(),
                            .StartLine = lineNo
                        }
                        inGlobals = name.EndsWith("_Globals", StringComparison.OrdinalIgnoreCase) OrElse
                                    name.Equals("Globals", StringComparison.OrdinalIgnoreCase)
                        Continue For
                    End If

                    ' Type declarations (module level)
                    Dim tm = TypeRx.Match(line)
                    If tm.Success Then
                        result.Types.Add(New With {.name = tm.Groups("name").Value, .fields = tm.Groups("fields").Value.Trim(), .line = lineNo})
                        Continue For
                    End If
                End If

                ' Globals captured from inside *_Globals / Globals subs
                If inGlobals Then
                    Dim dm = DeclRx.Match(line)
                    If dm.Success Then
                        result.Globals.Add(New With {.name = dm.Groups("name").Value, .line = lineNo, .declaration = trimmed})
                    End If
                End If
            Next

            ' Unclosed sub at EOF
            If current IsNot Nothing Then
                current.EndLine = lines.Length
                result.Subs.Add(current)
            End If

            Return result
        End Function

        ''' <summary>
        ''' Returns 1-based line numbers where <paramref name="symbol"/> appears as a whole
        ''' word (case-insensitive). Used for reference scanning.
        ''' </summary>
        Public Shared Function FindReferenceLines(content As String, symbol As String) As List(Of Integer)
            Dim hits As New List(Of Integer)
            Dim rx As New Regex("\b" & Regex.Escape(symbol) & "\b", RegexOptions.IgnoreCase)
            Dim lines = content.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            For i = 0 To lines.Length - 1
                If rx.IsMatch(lines(i)) Then hits.Add(i + 1)
            Next
            Return hits
        End Function

    End Class
End Namespace
