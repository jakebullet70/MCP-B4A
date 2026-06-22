Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.IO
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class BasTools

        Private Shared Function IsB4aSource(path As String) As Boolean
            Return path.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) OrElse
                   path.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase)
        End Function

        <McpServerTool, Description("Reads a B4A source file (.bas or .b4a) and returns its full content with line numbers")>
        Public Shared Function B4aReadBas(
            <Description("Full path to the .bas or .b4a file")> basPath As String
        ) As String
            If Not File.Exists(basPath) Then Return ToolResult.Fail($"File not found: {basPath}")
            If Not IsB4aSource(basPath) Then
                Return ToolResult.Fail("File must have .bas or .b4a extension")
            End If
            Try
                Dim lines = File.ReadAllLines(basPath)
                Dim sb As New Text.StringBuilder()
                For i = 0 To lines.Length - 1
                    sb.AppendLine($"{i + 1,5}| {lines(i)}")
                Next
                Return ToolResult.Ok(sb.ToString())
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description("Performs a search-and-replace edit on a B4A source file (.bas or .b4a). Creates .bak backup first. The old_text must match exactly (including indentation).")>
        Public Shared Function B4aEditBas(
            <Description("Full path to the .bas or .b4a file")> basPath As String,
            <Description("Exact text to find (must match including whitespace/indentation)")> old_text As String,
            <Description("Replacement text")> new_text As String,
            <Description("If true, replace ALL occurrences; if false (default), the old_text must be unique")> Optional replace_all As Boolean = False
        ) As String
            If Not File.Exists(basPath) Then Return ToolResult.Fail($"File not found: {basPath}")
            If Not IsB4aSource(basPath) Then
                Return ToolResult.Fail("File must have .bas or .b4a extension")
            End If
            If String.IsNullOrEmpty(old_text) Then Return ToolResult.Fail("old_text cannot be empty")
            If old_text = new_text Then Return ToolResult.Fail("old_text and new_text are identical")

            Try
                Dim content = File.ReadAllText(basPath)

                ' Normalise line endings for matching
                Dim normContent = content.Replace(vbCrLf, vbLf)
                Dim normOld = old_text.Replace(vbCrLf, vbLf)
                Dim normNew = new_text.Replace(vbCrLf, vbLf)

                Dim count = CountOccurrences(normContent, normOld)

                If count = 0 Then
                    Return ToolResult.Fail("old_text not found in file. Make sure whitespace and indentation match exactly.")
                End If

                If Not replace_all AndAlso count > 1 Then
                    Return ToolResult.Fail($"old_text found {count} times. Provide more context to make it unique, or set replace_all=true.")
                End If

                ' Create backup
                File.Copy(basPath, basPath & ".bak", overwrite:=True)

                ' Apply replacement (preserve original line endings)
                Dim result As String
                If replace_all Then
                    result = normContent.Replace(normOld, normNew)
                Else
                    Dim idx = normContent.IndexOf(normOld, StringComparison.Ordinal)
                    result = normContent.Substring(0, idx) & normNew & normContent.Substring(idx + normOld.Length)
                End If

                ' Restore CRLF if original used it
                If content.Contains(vbCrLf) Then
                    result = result.Replace(vbLf, vbCrLf)
                End If

                File.WriteAllText(basPath, result)

                Dim replacements = If(replace_all, count, 1)
                Return ToolResult.Message($"{replacements} replacement(s) made. Backup saved as {basPath}.bak")
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Applies an ordered list of search-and-replace edits to a B4A source file in a single " &
            "transaction with one .bak backup. If ANY edit fails to match (or is ambiguous), NOTHING " &
            "is written and the failing edit index is reported. " &
            "edits is a JSON array: [{""old_text"":""..."",""new_text"":""..."",""replace_all"":false}, ...]. " &
            "Edits are applied in order against the running result, so a later edit can target text an " &
            "earlier edit produced.")>
        Public Shared Function B4aMultiEditBas(
            <Description("Full path to the .bas or .b4a file")> basPath As String,
            <Description("JSON array of edit objects, each with old_text, new_text, and optional replace_all (default false)")> edits As String
        ) As String
            If Not File.Exists(basPath) Then Return ToolResult.Fail($"File not found: {basPath}")
            If Not IsB4aSource(basPath) Then Return ToolResult.Fail("File must have .bas or .b4a extension")

            Dim ops As JArray
            Try
                ops = JArray.Parse(edits)
            Catch ex As Exception
                Return ToolResult.Fail($"edits is not a valid JSON array — {ex.Message}")
            End Try
            If ops.Count = 0 Then Return ToolResult.Fail("edits array is empty")

            Try
                Dim content = File.ReadAllText(basPath)
                Dim usedCrLf = content.Contains(vbCrLf)
                Dim work = content.Replace(vbCrLf, vbLf)

                ' Validate + apply every edit in memory first (transactional)
                For i = 0 To ops.Count - 1
                    Dim op = TryCast(ops(i), JObject)
                    If op Is Nothing Then Return ToolResult.Fail($"edit #{i + 1} is not an object")
                    Dim oldText = If(op("old_text")?.ToString(), Nothing)
                    Dim newText = If(op("new_text")?.ToString(), "")
                    Dim replaceAll = op("replace_all") IsNot Nothing AndAlso op("replace_all").ToObject(Of Boolean)()

                    If String.IsNullOrEmpty(oldText) Then Return ToolResult.Fail($"edit #{i + 1} has empty old_text")
                    Dim normOld = oldText.Replace(vbCrLf, vbLf)
                    Dim normNew = newText.Replace(vbCrLf, vbLf)

                    Dim count = CountOccurrences(work, normOld)
                    If count = 0 Then Return ToolResult.Fail($"edit #{i + 1} old_text not found (whitespace/indentation must match exactly). No changes written.")
                    If Not replaceAll AndAlso count > 1 Then Return ToolResult.Fail($"edit #{i + 1} old_text found {count} times — add context or set replace_all=true. No changes written.")

                    If replaceAll Then
                        work = work.Replace(normOld, normNew)
                    Else
                        Dim idx = work.IndexOf(normOld, StringComparison.Ordinal)
                        work = work.Substring(0, idx) & normNew & work.Substring(idx + normOld.Length)
                    End If
                Next

                If usedCrLf Then work = work.Replace(vbLf, vbCrLf)
                File.Copy(basPath, basPath & ".bak", overwrite:=True)
                File.WriteAllText(basPath, work)
                Return ToolResult.Message($"{ops.Count} edit(s) applied. Backup saved as {basPath}.bak")
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Creates a new B4A source module (.bas) with the correct header and boilerplate for the given type, " &
            "and optionally registers it in a .b4a project (bumps NumberOfModules + adds a Module entry, with a .bak backup). " &
            "moduleType: 'class', 'code' (StandardModule), 'activity', or 'service'.")>
        Public Shared Function B4aCreateModule(
            <Description("Full path for the new .bas file (e.g. C:\proj\src\MyClass.bas)")> modulePath As String,
            <Description("Module type: class | code | activity | service")> Optional moduleType As String = "class",
            <Description("Optional .b4a project file to register the module in")> Optional projectPath As String = "",
            <Description("B4A module Group name (default 'Default Group')")> Optional group As String = "Default Group",
            <Description("Overwrite if the file already exists (default false)")> Optional overwrite As Boolean = False
        ) As String
            If Not modulePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) Then
                Return ToolResult.Fail("modulePath must end with .bas")
            End If
            If File.Exists(modulePath) AndAlso Not overwrite Then
                Return ToolResult.Fail($"File already exists: {modulePath} (set overwrite=true to replace)")
            End If

            Dim typeName As String
            Dim body As String
            Select Case moduleType.Trim().ToLowerInvariant()
                Case "code", "codemodule", "standard", "standardmodule"
                    typeName = "StandardModule"
                    body = "Sub Process_Globals" & vbCrLf & vbCrLf & "End Sub" & vbCrLf
                Case "activity"
                    typeName = "Activity"
                    body = "Sub Process_Globals" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Globals" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Activity_Create(FirstTime As Boolean)" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Activity_Resume" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Activity_Pause(UserClosed As Boolean)" & vbCrLf & vbCrLf & "End Sub" & vbCrLf
                Case "service"
                    typeName = "Service"
                    body = "Sub Process_Globals" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Service_Create" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Service_Start(StartingIntent As Intent)" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Sub Service_Destroy" & vbCrLf & vbCrLf & "End Sub" & vbCrLf
                Case "class", ""
                    typeName = "Class"
                    body = "Sub Class_Globals" & vbCrLf & vbCrLf & "End Sub" & vbCrLf & vbCrLf &
                           "Public Sub Initialize" & vbCrLf & vbCrLf & "End Sub" & vbCrLf
                Case Else
                    Return ToolResult.Fail($"unknown moduleType '{moduleType}'. Use class | code | activity | service.")
            End Select

            Try
                Dim header = $"B4A=true{vbCrLf}Group={group}{vbCrLf}ModulesStructureVersion=1{vbCrLf}Type={typeName}{vbCrLf}Version=8.5{vbCrLf}@EndOfDesignText@{vbCrLf}"
                Dim dir = Path.GetDirectoryName(modulePath)
                If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
                File.WriteAllText(modulePath, header & body)

                Dim registration As String = "not registered (no projectPath given — add it via the B4A IDE or pass projectPath)"
                If Not String.IsNullOrEmpty(projectPath) Then
                    registration = RegisterModuleInProject(projectPath, modulePath)
                End If

                Return ToolResult.Message($"created {typeName} module at {modulePath}. {registration}")
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        ''' <summary>
        ''' Adds a Module{N} entry to a .b4a project and bumps NumberOfModules.
        ''' Module path is stored relative to the project dir when possible (B4A's |relative| form).
        ''' </summary>
        Private Shared Function RegisterModuleInProject(projectPath As String, modulePath As String) As String
            If Not File.Exists(projectPath) Then Return $"WARNING: project not found, module file created but not registered: {projectPath}"
            If Not projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then Return "WARNING: projectPath is not a .b4a file — module not registered"

            Dim projDir = Path.GetDirectoryName(projectPath)
            Dim moduleNameNoExt = Path.GetFileNameWithoutExtension(modulePath)

            ' Build the |relative| reference (without .bas), backslash-separated, when the module sits under the project dir.
            Dim entry As String
            Dim fullModuleDir = Path.GetDirectoryName(Path.GetFullPath(modulePath))
            Dim fullProjDir = Path.GetFullPath(projDir)
            If fullModuleDir.StartsWith(fullProjDir, StringComparison.OrdinalIgnoreCase) Then
                Dim rel = Path.Combine(fullModuleDir.Substring(fullProjDir.Length).TrimStart(Path.DirectorySeparatorChar, "/"c), moduleNameNoExt)
                rel = rel.Replace("/"c, "\"c)
                entry = $"|relative|{rel}"
            Else
                entry = $"|absolute|{Path.Combine(fullModuleDir, moduleNameNoExt)}"
            End If

            Dim lines = File.ReadAllLines(projectPath).ToList()
            Dim sepIdx = lines.FindIndex(Function(l) l.Trim() = "@EndOfDesignText@")
            Dim headerEnd = If(sepIdx < 0, lines.Count, sepIdx)

            ' Find NumberOfModules and the highest Module index in the header
            Dim numIdx = -1
            Dim count = 0
            Dim maxIdx = 0
            For i = 0 To headerEnd - 1
                Dim t = lines(i).Trim()
                If t.StartsWith("NumberOfModules=", StringComparison.OrdinalIgnoreCase) Then
                    numIdx = i
                    Integer.TryParse(t.Substring(t.IndexOf("="c) + 1).Trim(), count)
                ElseIf t.StartsWith("Module", StringComparison.OrdinalIgnoreCase) AndAlso t.Contains("=") Then
                    Dim key = t.Substring(0, t.IndexOf("="c))
                    Dim n As Integer
                    If Integer.TryParse(key.Substring("Module".Length), n) Then maxIdx = Math.Max(maxIdx, n)
                End If
            Next

            ' Guard against accidentally re-registering the same module
            Dim alreadyKey = $"={entry}"
            If lines.Take(headerEnd).Any(Function(l) l.Trim().EndsWith(alreadyKey, StringComparison.OrdinalIgnoreCase)) Then
                Return "already registered in project (skipped)"
            End If

            File.Copy(projectPath, projectPath & ".bak", overwrite:=True)

            Dim newIdx = maxIdx + 1
            ' Insert the new Module line right after the last header line (before separator)
            lines.Insert(headerEnd, $"Module{newIdx}={entry}")
            If numIdx >= 0 Then
                lines(numIdx) = $"NumberOfModules={count + 1}"
            Else
                lines.Insert(0, $"NumberOfModules=1")
            End If

            File.WriteAllText(projectPath, String.Join(Environment.NewLine, lines))
            Return $"registered as Module{newIdx}={entry} (NumberOfModules={count + 1}); project .bak saved"
        End Function

        Private Shared Function CountOccurrences(source As String, search As String) As Integer
            Dim count = 0
            Dim idx = 0
            Do
                idx = source.IndexOf(search, idx, StringComparison.Ordinal)
                If idx < 0 Then Exit Do
                count += 1
                idx += search.Length
            Loop
            Return count
        End Function

    End Class
End Namespace
