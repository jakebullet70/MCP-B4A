Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.IO
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class CodeTools

        Private Shared ReadOnly ReservedNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Is", "ATan2", "Rnd"}

        <McpServerTool, Description(
            "Returns the structural outline of a B4A source file (.bas or .b4a): all Subs " &
            "(name, params, return type, visibility, line range), Type declarations, #Region blocks, " &
            "and module-level globals. Use this to navigate a module without reading the whole file.")>
        Public Shared Function B4aOutline(
            <Description("Full path to the .bas or .b4a file")> basPath As String
        ) As String
            If Not File.Exists(basPath) Then Return ToolResult.Fail($"File not found: {basPath}")
            Dim ext = Path.GetExtension(basPath).ToLowerInvariant()
            If ext <> ".bas" AndAlso ext <> ".b4a" Then
                Return ToolResult.Fail("File must have .bas or .b4a extension")
            End If
            Try
                Dim cached As String = Nothing
                If CacheManager.TryGetByMtime(Of String)("outline:" & basPath, cached) Then Return cached

                Dim outline = BasAnalyzer.Outline(File.ReadAllText(basPath))
                Dim result = ToolResult.Ok(New With {
                    .file = Path.GetFileName(basPath),
                    .lineCount = outline.LineCount,
                    .subCount = outline.Subs.Count,
                    .subs = outline.Subs.Select(Function(s) New With {
                        .name = s.Name,
                        .visibility = If(String.IsNullOrEmpty(s.Visibility), "public", s.Visibility),
                        .params = s.Params,
                        .returnType = s.ReturnType,
                        .startLine = s.StartLine,
                        .endLine = s.EndLine
                    }),
                    .types = outline.Types,
                    .regions = outline.Regions,
                    .globals = outline.Globals
                })
                CacheManager.SetByMtime("outline:" & basPath, result)
                Return result
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Searches every .bas module (and the .b4a file) in a project for a symbol — a Sub, Type, " &
            "or global. Returns where it is DEFINED and every line that REFERENCES it. Matching is " &
            "case-insensitive (B4A is case-insensitive), which makes this the safe way to scope a rename.")>
        Public Shared Function B4aFindSymbol(
            <Description("Path to the .b4a project file (or any directory inside the project to scan)")> projectPath As String,
            <Description("Symbol name to find (Sub, Type, or global variable). Case-insensitive.")> symbol As String,
            <Description("If true, include every reference line; if false (default), only definitions plus a reference count per file.")> Optional includeReferences As Boolean = False
        ) As String
            If String.IsNullOrWhiteSpace(symbol) Then Return ToolResult.Fail("symbol cannot be empty")

            Dim searchDir As String
            If File.Exists(projectPath) AndAlso projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                searchDir = Path.GetDirectoryName(projectPath)
            ElseIf Directory.Exists(projectPath) Then
                searchDir = projectPath
            Else
                Return ToolResult.Fail($"Not found: {projectPath} (expected a .b4a file or a directory)")
            End If
            If String.IsNullOrEmpty(searchDir) Then searchDir = "."

            Try
                ' Collect .bas modules plus the .b4a file (which embeds the main module's code).
                Dim files = Directory.GetFiles(searchDir, "*.bas", SearchOption.AllDirectories).ToList()
                files.AddRange(Directory.GetFiles(searchDir, "*.b4a", SearchOption.AllDirectories))

                Dim definitions As New List(Of Object)
                Dim references As New List(Of Object)
                Dim totalRefs As Integer = 0

                For Each f In files
                    Dim content As String
                    Try
                        content = File.ReadAllText(f)
                    Catch
                        Continue For
                    End Try
                    Dim rel = f.Substring(searchDir.Length).TrimStart(Path.DirectorySeparatorChar, "/"c)

                    ' Definitions: a Sub/Type/global whose name matches (case-insensitive)
                    Dim outline = BasAnalyzer.Outline(content)
                    For Each s In outline.Subs
                        If s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase) Then
                            definitions.Add(New With {.file = rel, .kind = "sub", .line = s.StartLine,
                                                      .signature = $"Sub {s.Name}({s.Params}){If(String.IsNullOrEmpty(s.ReturnType), "", " As " & s.ReturnType)}"})
                        End If
                    Next
                    For Each t In outline.Types
                        Dim tn = CStr(t.GetType().GetProperty("name").GetValue(t))
                        If tn.Equals(symbol, StringComparison.OrdinalIgnoreCase) Then
                            definitions.Add(New With {.file = rel, .kind = "type", .line = CInt(t.GetType().GetProperty("line").GetValue(t)), .signature = $"Type {tn}"})
                        End If
                    Next
                    For Each g In outline.Globals
                        Dim gn = CStr(g.GetType().GetProperty("name").GetValue(g))
                        If gn.Equals(symbol, StringComparison.OrdinalIgnoreCase) Then
                            definitions.Add(New With {.file = rel, .kind = "global", .line = CInt(g.GetType().GetProperty("line").GetValue(g)),
                                                      .signature = CStr(g.GetType().GetProperty("declaration").GetValue(g))})
                        End If
                    Next

                    ' References
                    Dim refLines = BasAnalyzer.FindReferenceLines(content, symbol)
                    If refLines.Count > 0 Then
                        totalRefs += refLines.Count
                        If includeReferences Then
                            Dim lineArr = content.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
                            references.Add(New With {
                                .file = rel,
                                .count = refLines.Count,
                                .lines = refLines.Select(Function(ln) New With {.line = ln, .text = lineArr(ln - 1).Trim()})
                            })
                        Else
                            references.Add(New With {.file = rel, .count = refLines.Count})
                        End If
                    End If
                Next

                Return ToolResult.Ok(New With {
                    .symbol = symbol,
                    .filesScanned = files.Count,
                    .definitionCount = definitions.Count,
                    .definitions = definitions,
                    .totalReferences = totalRefs,
                    .references = references
                })
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Statically lints B4A source for the well-known gotchas that cause hard-to-debug bugs: " &
            "reserved-word identifiers (Is/ATan2/Rnd), a local or parameter that shadows a module global " &
            "(B4A is case-insensitive, so this silently overwrites the global), MediaPlayer usage, " &
            "Colors.R/G/B/A() (which do not exist), and BitmapData (should be BitmapsData). " &
            "Accepts a single .bas file, a .b4a project, or a directory.")>
        Public Shared Function B4aLint(
            <Description("Path to a .bas file, a .b4a project, or a project directory to lint")> target As String
        ) As String
            Dim files As New List(Of String)
            If File.Exists(target) AndAlso target.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) Then
                files.Add(target)
            ElseIf File.Exists(target) AndAlso target.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                Dim dir = Path.GetDirectoryName(target)
                files.AddRange(Directory.GetFiles(If(String.IsNullOrEmpty(dir), ".", dir), "*.bas", SearchOption.AllDirectories))
            ElseIf Directory.Exists(target) Then
                files.AddRange(Directory.GetFiles(target, "*.bas", SearchOption.AllDirectories))
            Else
                Return ToolResult.Fail($"Not found (expected .bas, .b4a, or a directory): {target}")
            End If

            Try
                Dim fileReports As New List(Of Object)
                Dim total = 0
                For Each f In files
                    Dim content = File.ReadAllText(f)
                    Dim findings = LintModule(content)
                    If findings.Count > 0 Then
                        total += findings.Count
                        fileReports.Add(New With {.file = Path.GetFileName(f), .path = f, .findingCount = findings.Count, .findings = findings})
                    End If
                Next
                Return ToolResult.Ok(New With {
                    .filesLinted = files.Count,
                    .totalFindings = total,
                    .reports = fileReports
                })
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Renames a symbol across every .bas module (and the .b4a file) in a project, matching whole words " &
            "case-insensitively (B4A is case-insensitive). DEFAULTS TO A DRY RUN: returns every site that WOULD change " &
            "so you can review first. Set apply=true to write the changes (each modified file gets a .bak). " &
            "Caveat: matches inside string literals and comments are also renamed — review the dry run.")>
        Public Shared Function B4aRenameSymbol(
            <Description("Path to the .b4a project file or a project directory")> projectPath As String,
            <Description("Current symbol name")> oldName As String,
            <Description("New symbol name")> newName As String,
            <Description("Set true to actually write changes; false (default) returns a preview only")> Optional apply As Boolean = False
        ) As String
            If String.IsNullOrWhiteSpace(oldName) OrElse String.IsNullOrWhiteSpace(newName) Then Return ToolResult.Fail("oldName and newName are required")
            If Not Regex.IsMatch(newName, "^[A-Za-z_]\w*$") Then Return ToolResult.Fail($"'{newName}' is not a valid identifier")
            If oldName = newName Then Return ToolResult.Fail("oldName and newName are identical")

            Dim searchDir As String
            If File.Exists(projectPath) AndAlso projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                searchDir = Path.GetDirectoryName(projectPath)
            ElseIf Directory.Exists(projectPath) Then
                searchDir = projectPath
            Else
                Return ToolResult.Fail($"Not found: {projectPath}")
            End If
            If String.IsNullOrEmpty(searchDir) Then searchDir = "."

            Try
                Dim files = Directory.GetFiles(searchDir, "*.bas", SearchOption.AllDirectories).ToList()
                files.AddRange(Directory.GetFiles(searchDir, "*.b4a", SearchOption.AllDirectories))

                Dim rx As New Regex("\b" & Regex.Escape(oldName) & "\b", RegexOptions.IgnoreCase)
                Dim fileChanges As New List(Of Object)
                Dim totalReplacements = 0

                For Each f In files
                    Dim content As String
                    Try
                        content = File.ReadAllText(f)
                    Catch
                        Continue For
                    End Try
                    Dim matches = rx.Matches(content)
                    If matches.Count = 0 Then Continue For
                    totalReplacements += matches.Count

                    Dim rel = f.Substring(searchDir.Length).TrimStart(Path.DirectorySeparatorChar, "/"c)
                    Dim srcLines = content.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
                    Dim changedLineNos = BasAnalyzer.FindReferenceLines(content, oldName)
                    Dim preview = changedLineNos.Select(Function(ln) New With {
                        .line = ln,
                        .before = srcLines(ln - 1).Trim(),
                        .after = rx.Replace(srcLines(ln - 1), newName).Trim()
                    })
                    fileChanges.Add(New With {.file = rel, .path = f, .replacements = matches.Count, .changes = preview})

                    If apply Then
                        File.Copy(f, f & ".bak", overwrite:=True)
                        File.WriteAllText(f, rx.Replace(content, newName))
                    End If
                Next

                Return ToolResult.Ok(New With {
                    .applied = apply,
                    .oldName = oldName,
                    .newName = newName,
                    .filesAffected = fileChanges.Count,
                    .totalReplacements = totalReplacements,
                    .note = If(apply, "Changes written; .bak backups created per file.", "DRY RUN — set apply=true to write these changes."),
                    .files = fileChanges
                })
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        Private Shared Function LintModule(content As String) As List(Of Object)
            Dim findings As New List(Of Object)
            Dim lines = content.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim outline = BasAnalyzer.Outline(content)

            ' Module globals (name -> declaring line), for shadow detection.
            Dim globals As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            For Each g In outline.Globals
                Dim gn = CStr(g.GetType().GetProperty("name").GetValue(g))
                If Not globals.ContainsKey(gn) Then globals(gn) = CInt(g.GetType().GetProperty("line").GetValue(g))
            Next

            For Each s In outline.Subs
                Dim isGlobalsSub = s.Name.EndsWith("_Globals", StringComparison.OrdinalIgnoreCase) OrElse
                                   s.Name.Equals("Globals", StringComparison.OrdinalIgnoreCase)

                ' Reserved-word sub name
                If ReservedNames.Contains(s.Name) Then
                    findings.Add(Finding(s.StartLine, "CRITICAL", "reserved-name",
                        $"Sub '{s.Name}' uses a reserved word — rename it (B4A reserves Is/ATan2/Rnd)."))
                End If

                ' Parameter shadows a module global
                For Each pName In ParamNames(s.Params)
                    If globals.ContainsKey(pName) Then
                        findings.Add(Finding(s.StartLine, "MEDIUM", "param-shadows-global",
                            $"Parameter '{pName}' in '{s.Name}' shadows module global '{pName}' (declared line {globals(pName)})."))
                    End If
                Next

                ' Locals (in non-globals subs) shadowing a module global
                If Not isGlobalsSub Then
                    For ln = s.StartLine + 1 To Math.Min(s.EndLine - 1, lines.Length)
                        Dim decl = LocalDeclName(lines(ln - 1))
                        If decl IsNot Nothing AndAlso globals.ContainsKey(decl) Then
                            findings.Add(Finding(ln, "HIGH", "local-shadows-global",
                                $"Local '{decl}' in '{s.Name}' shadows module global '{decl}' (declared line {globals(decl)}). B4A is case-insensitive — this overwrites the global."))
                        End If
                    Next
                End If
            Next

            ' Line-based pattern checks (skip comment-only lines)
            For i = 0 To lines.Length - 1
                Dim line = lines(i)
                If line.TrimStart().StartsWith("'") Then Continue For
                Dim lineNo = i + 1

                If Regex.IsMatch(line, "\bMediaPlayer\b", RegexOptions.IgnoreCase) Then
                    findings.Add(Finding(lineNo, "HIGH", "mediaplayer",
                        "MediaPlayer can cause a NullPointerException at compile time — prefer SoundPool or stub the audio."))
                End If
                If Regex.IsMatch(line, "Colors\.[RGBA]\s*\(", RegexOptions.IgnoreCase) Then
                    findings.Add(Finding(lineNo, "MEDIUM", "colors-component",
                        "Colors.R/G/B/A() do not exist in B4A — use Bit.And/Bit.ShiftRight to extract components."))
                End If
                If Regex.IsMatch(line, "\bBitmapData\b") Then
                    findings.Add(Finding(lineNo, "LOW", "bitmapdata",
                        "GameView property is 'BitmapsData' (plural) — 'BitmapData' will fail at runtime."))
                End If
            Next

            Return findings
        End Function

        Private Shared Function Finding(line As Integer, severity As String, rule As String, message As String) As Object
            Return New With {.line = line, .severity = severity, .rule = rule, .message = message}
        End Function

        ''' <summary>Extracts parameter names from a sub's param string ("x As Int, y As Int" -> x, y).</summary>
        Private Shared Function ParamNames(params As String) As List(Of String)
            Dim result As New List(Of String)
            If String.IsNullOrWhiteSpace(params) Then Return result
            For Each part In params.Split(","c)
                Dim t = part.Trim()
                If t.Length = 0 Then Continue For
                Dim m = Regex.Match(t, "^([A-Za-z_]\w*)")
                If m.Success Then result.Add(m.Groups(1).Value)
            Next
            Return result
        End Function

        ''' <summary>Returns the declared name on a local Dim/Private/Public line, or Nothing.</summary>
        Private Shared Function LocalDeclName(line As String) As String
            Dim m = Regex.Match(line, "^\s*(Dim|Private|Public)\s+(Const\s+)?(?<name>[A-Za-z_]\w*)\b", RegexOptions.IgnoreCase)
            Return If(m.Success, m.Groups("name").Value, Nothing)
        End Function

    End Class
End Namespace
