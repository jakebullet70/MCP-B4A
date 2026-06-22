Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.IO
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class LayoutTools

        <McpServerTool, Description("Reads a B4A layout file (.bal or .bil) and returns its content as JSON. EditText properties of note: inputType (TEXT/NUMBERS/DECIMAL_NUMBERS/PHONE/NONE), password (bool), hint (string), singleLine (bool).")>
        Public Shared Function B4aReadLayout(
            <Description("Full path to the .bal or .bil layout file")> layoutPath As String
        ) As String
            If Not File.Exists(layoutPath) Then Return ToolResult.Fail($"File not found: {layoutPath}")
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            If ext <> ".bal" AndAlso ext <> ".bil" Then
                Return ToolResult.Fail($"File must have .bal or .bil extension")
            End If
            Try
                Dim cached As String = Nothing
                If CacheManager.TryGetByMtime(Of String)(layoutPath, cached) Then Return ToolResult.Ok(JObject.Parse(cached))

                Dim converter = New BalConverter(ext = ".bil")
                Dim dir = Path.GetDirectoryName(layoutPath)
                If String.IsNullOrEmpty(dir) Then dir = "."
                Dim json = converter.ConvertBalToJson(dir, Path.GetFileName(layoutPath))
                CacheManager.SetByMtime(layoutPath, json)
                Return ToolResult.Ok(JObject.Parse(json))
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description("Writes JSON data to a B4A layout file (.bal or .bil). Creates a .bak backup first. EditText controls MUST include 'password' (boolean) and 'inputType' (string: TEXT, NUMBERS, DECIMAL_NUMBERS, PHONE, or NONE). Missing required properties are auto-injected with safe defaults.")>
        Public Shared Function B4aWriteLayout(
            <Description("Full path to the .bal or .bil layout file to write")> layoutPath As String,
            <Description("JSON layout data (as returned by b4a_read_layout). EditText controls require: password (bool), inputType (string constant), hint (string), hintColor (color), singleLine (bool).")> jsonData As String
        ) As String
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            If ext <> ".bal" AndAlso ext <> ".bil" Then
                Return ToolResult.Fail($"File must have .bal or .bil extension")
            End If
            Try
                Dim json As JObject
                Try
                    json = JObject.Parse(jsonData)
                Catch ex As JsonException
                    Return ToolResult.Fail($"Invalid JSON — {ex.Message}")
                End Try

                ' Validate required structure
                If json("LayoutHeader") Is Nothing Then Return ToolResult.Fail($"Missing 'LayoutHeader' in JSON")
                If json("Variants") Is Nothing Then Return ToolResult.Fail($"Missing 'Variants' in JSON")
                If json("Data") Is Nothing Then Return ToolResult.Fail($"Missing 'Data' in JSON")

                ' Validate and fix EditText controls
                Dim warnings = ValidateAndFixEditTexts(json)

                ' Backup
                If File.Exists(layoutPath) Then
                    File.Copy(layoutPath, layoutPath & ".bak", overwrite:=True)
                End If

                Dim converter = New BalConverter(ext = ".bil")
                Using stream = File.Create(layoutPath)
                    converter.ConvertJsonToBalInMemory(json, stream)
                End Using
                CacheManager.Invalidate(layoutPath)
                Return ToolResult.Message($"backup saved as {layoutPath}.bak", warnings)
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description("Lists all .bal and .bil layout files in a B4A project directory")>
        Public Shared Function B4aListLayouts(
            <Description("Path to the B4A project directory (or .b4a file path)")> projectDir As String
        ) As String
            If projectDir.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                projectDir = If(Path.GetDirectoryName(projectDir), ".")
            End If
            If Not Directory.Exists(projectDir) Then Return ToolResult.Fail($"Directory not found: {projectDir}")
            Try
                Dim layouts = Directory.GetFiles(projectDir, "*.bal", SearchOption.AllDirectories) _
                    .Concat(Directory.GetFiles(projectDir, "*.bil", SearchOption.AllDirectories)) _
                    .OrderBy(Function(f) f) _
                    .Select(Function(f) New With {
                        .name = Path.GetFileName(f),
                        .path = f,
                        .sizeKb = Math.Round(New FileInfo(f).Length / 1024.0, 1)
                    }).ToList()
                Return ToolResult.Ok(New With {
                    .count = layouts.Count,
                    .layouts = layouts
                })
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Duplicates a layout file (.bal/.bil) to a new path — a quick way to start a new layout from an existing one. " &
            "The layout's name in B4A is its filename, so the copy becomes a new layout. Source and destination must share the extension.")>
        Public Shared Function B4aCloneLayout(
            <Description("Full path to the source .bal/.bil layout")> sourcePath As String,
            <Description("Full path for the new layout file (same extension as source)")> destPath As String,
            <Description("Overwrite the destination if it exists (default false)")> Optional overwrite As Boolean = False
        ) As String
            If Not File.Exists(sourcePath) Then Return ToolResult.Fail($"Source not found: {sourcePath}")
            Dim srcExt = Path.GetExtension(sourcePath).ToLowerInvariant()
            Dim dstExt = Path.GetExtension(destPath).ToLowerInvariant()
            If srcExt <> ".bal" AndAlso srcExt <> ".bil" Then Return ToolResult.Fail($"Source must be .bal or .bil")
            If dstExt <> srcExt Then Return ToolResult.Fail($"Destination extension ({dstExt}) must match source ({srcExt})")
            If File.Exists(destPath) AndAlso Not overwrite Then Return ToolResult.Fail($"Destination exists: {destPath} (set overwrite=true)")
            Try
                Dim dir = Path.GetDirectoryName(destPath)
                If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
                File.Copy(sourcePath, destPath, overwrite)
                Return ToolResult.Message($"cloned to {destPath}")
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Creates a new, minimal valid layout file (.bal/.bil) containing just an empty Activity at the given variant size. " &
            "Use this to scaffold a layout, then add views with b4a_write_layout. Default variant is 600x360 @ scale 1.")>
        Public Shared Function B4aCreateLayout(
            <Description("Full path for the new .bal/.bil layout file")> layoutPath As String,
            <Description("Design variant width in pixels (default 600)")> Optional width As Integer = 600,
            <Description("Design variant height in pixels (default 360)")> Optional height As Integer = 360,
            <Description("Design variant scale (default 1.0)")> Optional scale As Double = 1.0,
            <Description("Overwrite if the file already exists (default false)")> Optional overwrite As Boolean = False
        ) As String
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            If ext <> ".bal" AndAlso ext <> ".bil" Then Return ToolResult.Fail($"File must have .bal or .bil extension")
            If File.Exists(layoutPath) AndAlso Not overwrite Then Return ToolResult.Fail($"File exists: {layoutPath} (set overwrite=true)")
            If width <= 0 OrElse height <= 0 Then Return ToolResult.Fail($"width and height must be positive")

            Try
                Dim scaleStr = scale.ToString(Globalization.CultureInfo.InvariantCulture)
                Dim tpl = "{""LayoutHeader"":{""Version"":5,""GridSize"":10,""ControlsHeaders"":[{""Name"":""Activity"",""JavaType"":"".ActivityWrapper"",""DesignerType"":""Activity""}],""Files"":[],""DesignerScript"":[""'All variants script\nAutoScaleAll"",""'Variant specific script\n""]}," &
                          $"""Variants"":[{{""Scale"":{scaleStr},""Width"":{width},""Height"":{height}}}]," &
                          """Data"":{""csType"":""Dbasic.Designer.MetaActivity"",""type"":"".ActivityWrapper"",""animationDuration"":400,""drawable"":{""csType"":""Dbasic.Designer.Drawable.ColorDrawable"",""type"":"".drawable.ColorDrawable"",""color"":{""ValueType"":6,""Value"":""0xFFF0F8FF""}},""eventName"":""Activity"",""fullScreen"":false,""includeTitle"":true,""javaType"":"".ActivityWrapper"",""name"":""Activity"",""parent"":"""",""tag"":"""",""title"":""Activity"",""titleColor"":{""ValueType"":6,""Value"":""0xFFF0F8FF""},""visible"":true,""variant0"":{""left"":100,""top"":100,""width"":100,""height"":100,""hanchor"":0,""vanchor"":0},"":kids"":{}}," &
                          """FontAwesome"":false,""MaterialIcons"":false}"

                Dim json = JObject.Parse(tpl)
                If File.Exists(layoutPath) Then File.Copy(layoutPath, layoutPath & ".bak", overwrite:=True)
                Dim dir = Path.GetDirectoryName(layoutPath)
                If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)

                Dim converter = New BalConverter(ext = ".bil")
                Using stream = File.Create(layoutPath)
                    converter.ConvertJsonToBalInMemory(json, stream)
                End Using
                CacheManager.Invalidate(layoutPath)
                Return ToolResult.Message($"created {width}x{height} layout at {layoutPath}. Add views with b4a_write_layout.")
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        ' Minimal-but-faithful control templates (drawables/props match B4A Designer output).
        ' name, eventName, parent, coordinates and variant entries are injected at add time.
        Private Const TplLabel As String =
            "{""csType"":""Dbasic.Designer.MetaLabel"",""type"":"".LabelWrapper"",""javaType"":"".LabelWrapper""," &
            """drawable"":{""csType"":""Dbasic.Designer.Drawable.ColorWithCornersDrawable"",""type"":"".drawable.ColorDrawable"",""borderColor"":{""ValueType"":6,""Value"":""0xFF000000""},""borderWidth"":0,""color"":{""ValueType"":6,""Value"":""0x00FFFFFF""},""cornerRadius"":0}," &
            """ellipsize"":""NONE"",""enabled"":true,""fontAwesome"":"""",""fontsize"":{""ValueType"":7,""Value"":14.0},""hAlignment"":""LEFT"",""hanchor"":0,""height"":40,""left"":0,""materialIcons"":"""",""padding"":{""ValueType"":12},""singleLine"":false,""style"":""NORMAL"",""tag"":"""",""text"":""Label"",""textColor"":{""ValueType"":6,""Value"":""0xFF000000""},""top"":0,""typeface"":""DEFAULT"",""vAlignment"":""CENTER_VERTICAL"",""vanchor"":0,""visible"":true,""width"":120}"

        Private Const TplButton As String =
            "{""csType"":""Dbasic.Designer.MetaButton"",""type"":"".ButtonWrapper"",""javaType"":"".ButtonWrapper""," &
            """drawable"":{""csType"":""Dbasic.Designer.Drawable.StatelistDrawble"",""type"":"".drawable.StateListDrawable""," &
            """disabledDrawable"":{""csType"":""Dbasic.Designer.Drawable.ColorWithCornersDrawable"",""type"":"".drawable.ColorDrawable"",""borderColor"":{""ValueType"":6,""Value"":""0xFF000000""},""borderWidth"":0,""color"":{""ValueType"":6,""Value"":""0xFFDDDDDD""},""cornerRadius"":4}," &
            """enabledDrawable"":{""csType"":""Dbasic.Designer.Drawable.ColorWithCornersDrawable"",""type"":"".drawable.ColorDrawable"",""borderColor"":{""ValueType"":6,""Value"":""0xFF000000""},""borderWidth"":1,""color"":{""ValueType"":6,""Value"":""0xFFEEEEEE""},""cornerRadius"":4}," &
            """pressedDrawable"":{""csType"":""Dbasic.Designer.Drawable.ColorWithCornersDrawable"",""type"":"".drawable.ColorDrawable"",""borderColor"":{""ValueType"":6,""Value"":""0xFF000000""},""borderWidth"":1,""color"":{""ValueType"":6,""Value"":""0xFFCCCCCC""},""cornerRadius"":4}}," &
            """ellipsize"":""NONE"",""enabled"":true,""fontAwesome"":"""",""fontsize"":{""ValueType"":7,""Value"":14.0},""hAlignment"":""CENTER_HORIZONTAL"",""hanchor"":0,""height"":60,""left"":0,""materialIcons"":"""",""padding"":{""ValueType"":12},""singleLine"":true,""style"":""NORMAL"",""tag"":"""",""text"":""Button"",""textColor"":{""ValueType"":6,""Value"":""0xFF000000""},""top"":0,""typeface"":""DEFAULT"",""vAlignment"":""CENTER_VERTICAL"",""vanchor"":0,""visible"":true,""width"":120}"

        Private Const TplEditText As String =
            "{""csType"":""Dbasic.Designer.MetaEditText"",""type"":"".EditTextWrapper"",""javaType"":"".EditTextWrapper""," &
            """drawable"":{""csType"":""Dbasic.Designer.Drawable.ColorWithCornersDrawable"",""type"":"".drawable.ColorDrawable"",""borderColor"":{""ValueType"":6,""Value"":""0xFF000000""},""borderWidth"":1,""color"":{""ValueType"":6,""Value"":""0xFFFFFFFF""},""cornerRadius"":0}," &
            """enabled"":true,""fontAwesome"":"""",""fontsize"":{""ValueType"":7,""Value"":14.0},""forceDone"":false,""hAlignment"":""LEFT"",""hanchor"":0,""height"":60,""hint"":"""",""hintColor"":{""ValueType"":6,""Value"":""0xFFF0F0F0""},""inputType"":""TEXT"",""left"":0,""materialIcons"":"""",""padding"":{""ValueType"":12},""password"":false,""singleLine"":true,""style"":""NORMAL"",""tag"":"""",""text"":"""",""textColor"":{""ValueType"":6,""Value"":""0xFF000000""},""top"":0,""typeface"":""DEFAULT"",""vAlignment"":""CENTER_VERTICAL"",""vanchor"":0,""visible"":true,""width"":180,""wrap"":true}"

        Private Const TplPanel As String =
            "{""csType"":""Dbasic.Designer.MetaPanel"",""type"":"".PanelWrapper"",""javaType"":"".PanelWrapper""," &
            """drawable"":{""csType"":""Dbasic.Designer.Drawable.ColorWithCornersDrawable"",""type"":"".drawable.ColorDrawable"",""borderColor"":{""ValueType"":6,""Value"":""0xFF000000""},""borderWidth"":0,""color"":{""ValueType"":6,""Value"":""0x00FFFFFF""},""cornerRadius"":0}," &
            """elevation"":{""ValueType"":7,""Value"":0.0},""enabled"":true,""hanchor"":0,""height"":200,""left"":0,""padding"":{""ValueType"":12},""tag"":"""",""top"":0,""vanchor"":0,""visible"":true,""width"":200,"":kids"":{}}"

        <McpServerTool, Description(
            "Adds a view to an existing layout (.bal/.bil): a Label, Button, EditText, or Panel with B4A-correct defaults. " &
            "Sets the name/eventName, parent (default 'Activity'), position and size, and adds the matching entry for every " &
            "design variant. Creates a .bak backup. Then read it back / tweak with b4a_read_layout + b4a_write_layout.")>
        Public Shared Function B4aLayoutAddView(
            <Description("Full path to the .bal/.bil layout file")> layoutPath As String,
            <Description("View type: label | button | edittext | panel")> viewType As String,
            <Description("View name (also used as eventName), e.g. 'btnSave'")> name As String,
            <Description("Parent view name (default 'Activity')")> Optional parent As String = "Activity",
            <Description("Left in design pixels (default 10)")> Optional left As Integer = 10,
            <Description("Top in design pixels (default 10)")> Optional top As Integer = 10,
            <Description("Width in design pixels (default 120)")> Optional width As Integer = 120,
            <Description("Height in design pixels (default 60)")> Optional height As Integer = 60,
            <Description("Text/hint for label/button/edittext (optional)")> Optional text As String = ""
        ) As String
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            If ext <> ".bal" AndAlso ext <> ".bil" Then Return ToolResult.Fail($"File must have .bal or .bil extension")
            If Not File.Exists(layoutPath) Then Return ToolResult.Fail($"File not found: {layoutPath}")
            If String.IsNullOrWhiteSpace(name) Then Return ToolResult.Fail($"name is required")

            Dim tpl As String
            Dim designerType As String
            Select Case viewType.Trim().ToLowerInvariant()
                Case "label" : tpl = TplLabel : designerType = "Label"
                Case "button" : tpl = TplButton : designerType = "Button"
                Case "edittext", "edit" : tpl = TplEditText : designerType = "EditText"
                Case "panel" : tpl = TplPanel : designerType = "Panel"
                Case Else : Return ToolResult.Fail($"unknown viewType '{viewType}'. Use label | button | edittext | panel.")
            End Select

            Try
                Dim converter = New BalConverter(ext = ".bil")
                Dim dir = Path.GetDirectoryName(layoutPath)
                If String.IsNullOrEmpty(dir) Then dir = "."
                Dim json = JObject.Parse(converter.ConvertBalToJson(dir, Path.GetFileName(layoutPath)))

                Dim data = TryCast(json("Data"), JObject)
                If data Is Nothing Then Return ToolResult.Fail($"layout has no Data section")
                Dim kids = TryCast(data(":kids"), JObject)
                If kids Is Nothing Then
                    kids = New JObject()
                    data(":kids") = kids
                End If

                ' Reject duplicate names
                Dim headers = TryCast(json("LayoutHeader")?("ControlsHeaders"), JArray)
                If headers IsNot Nothing AndAlso headers.Any(Function(h) String.Equals(h("Name")?.ToString(), name, StringComparison.OrdinalIgnoreCase)) Then
                    Return ToolResult.Fail($"a view named '{name}' already exists in this layout")
                End If

                ' Build the control
                Dim ctrl = JObject.Parse(tpl)
                ctrl("name") = name
                ctrl("eventName") = name
                ctrl("parent") = parent
                ctrl("left") = left : ctrl("top") = top : ctrl("width") = width : ctrl("height") = height
                If Not String.IsNullOrEmpty(text) Then
                    If designerType = "EditText" Then ctrl("hint") = text Else ctrl("text") = text
                End If

                ' One variant entry per design variant
                Dim variantCount = If(TryCast(json("Variants"), JArray)?.Count, 1)
                For k = 0 To Math.Max(variantCount - 1, 0)
                    ctrl("variant" & k) = New JObject(
                        New JProperty("left", left), New JProperty("top", top),
                        New JProperty("width", width), New JProperty("height", height),
                        New JProperty("hanchor", 0), New JProperty("vanchor", 0))
                Next

                ' Append under the next integer key
                Dim nextKey = 0
                For Each p In kids.Properties()
                    Dim n As Integer
                    If Integer.TryParse(p.Name, n) Then nextKey = Math.Max(nextKey, n + 1)
                Next
                kids(nextKey.ToString()) = ctrl

                ' Register the header
                If headers Is Nothing Then
                    headers = New JArray()
                    DirectCast(json("LayoutHeader"), JObject)("ControlsHeaders") = headers
                End If
                headers.Add(New JObject(
                    New JProperty("Name", name),
                    New JProperty("JavaType", ctrl("javaType").ToString()),
                    New JProperty("DesignerType", designerType)))

                ' Run the existing EditText safety validation, then write
                Dim warnings = ValidateAndFixEditTexts(json)
                File.Copy(layoutPath, layoutPath & ".bak", overwrite:=True)
                Using stream = File.Create(layoutPath)
                    converter.ConvertJsonToBalInMemory(json, stream)
                End Using
                CacheManager.Invalidate(layoutPath)

                Return ToolResult.Message($"added {designerType} '{name}' to {Path.GetFileName(layoutPath)} (parent={parent}, {width}x{height} @ {left},{top}). Backup saved.", warnings)
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Compares two layouts (or the same layout before/after a change) and reports a human-readable diff: views added, " &
            "views removed, and per-view property changes. Compares by view name across the :kids tree.")>
        Public Shared Function B4aDiffLayout(
            <Description("Path to the first (baseline) .bal/.bil layout")> layoutA As String,
            <Description("Path to the second (compare) .bal/.bil layout")> layoutB As String
        ) As String
            If Not File.Exists(layoutA) Then Return ToolResult.Fail($"Not found: {layoutA}")
            If Not File.Exists(layoutB) Then Return ToolResult.Fail($"Not found: {layoutB}")
            Try
                Dim a = FlattenViews(LoadLayoutJson(layoutA))
                Dim b = FlattenViews(LoadLayoutJson(layoutB))

                Dim added = b.Keys.Where(Function(k) Not a.ContainsKey(k)).OrderBy(Function(k) k).ToList()
                Dim removed = a.Keys.Where(Function(k) Not b.ContainsKey(k)).OrderBy(Function(k) k).ToList()
                Dim changed As New List(Of Object)

                For Each k In a.Keys.Where(Function(x) b.ContainsKey(x))
                    Dim propChanges As New List(Of Object)
                    Dim av = a(k), bv = b(k)
                    Dim allProps = av.Properties().Select(Function(p) p.Name).Union(bv.Properties().Select(Function(p) p.Name)).Where(Function(n) n <> ":kids")
                    For Each pn In allProps
                        Dim avp = av(pn), bvp = bv(pn)
                        If Not JToken.DeepEquals(avp, bvp) Then
                            propChanges.Add(New With {.property = pn, .from = If(avp Is Nothing, "(absent)", avp.ToString(Formatting.None)), .to = If(bvp Is Nothing, "(absent)", bvp.ToString(Formatting.None))})
                        End If
                    Next
                    If propChanges.Count > 0 Then changed.Add(New With {.view = k, .changes = propChanges})
                Next

                Return ToolResult.Ok(New With {
                    .layoutA = Path.GetFileName(layoutA),
                    .layoutB = Path.GetFileName(layoutB),
                    .addedViews = added,
                    .removedViews = removed,
                    .changedViews = changed,
                    .identical = (added.Count = 0 AndAlso removed.Count = 0 AndAlso changed.Count = 0)
                })
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        <McpServerTool, Description(
            "Responsive audit: scans a layout's DesignerScript for hardcoded pixel positions/sizes that don't use %x/%y. " &
            "B4A layouts should be proportional to screen size — flags lines like 'Button1.SetLeftAndRight(0, 200)' where a " &
            "literal should likely be a percentage. Returns the flagged script lines with the offending numbers.")>
        Public Shared Function B4aLayoutCheckAnchors(
            <Description("Full path to the .bal/.bil layout file")> layoutPath As String
        ) As String
            If Not File.Exists(layoutPath) Then Return ToolResult.Fail($"Not found: {layoutPath}")
            Try
                Dim json = LoadLayoutJson(layoutPath)
                Dim script = TryCast(json("LayoutHeader")?("DesignerScript"), JArray)
                Dim findings As New List(Of Object)
                ' A positioning call argument that is a bare number (not followed by % and not a dip/percent expression).
                Dim callRx As New Text.RegularExpressions.Regex("\.(SetLeftAndRight|SetTopAndBottom|SetBounds|SetWidth|SetHeight|Left|Top|Width|Height)\b", Text.RegularExpressions.RegexOptions.IgnoreCase)
                Dim bareNumRx As New Text.RegularExpressions.Regex("(?<![\d%.])\b\d{2,}\b(?!\s*%)(?!\s*dip)", Text.RegularExpressions.RegexOptions.IgnoreCase)

                If script IsNot Nothing Then
                    For Each entry In script
                        Dim block = entry.ToString()
                        For Each rawLine In block.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
                            Dim line = rawLine.Trim()
                            If line.Length = 0 OrElse line.StartsWith("'") Then Continue For
                            If Not callRx.IsMatch(line) Then Continue For
                            Dim nums = bareNumRx.Matches(line)
                            If nums.Count > 0 Then
                                findings.Add(New With {.line = line, .hardcoded = nums.Cast(Of Text.RegularExpressions.Match)().Select(Function(m) m.Value).Distinct()})
                            End If
                        Next
                    Next
                End If

                Return ToolResult.Ok(New With {
                    .layout = Path.GetFileName(layoutPath),
                    .findingCount = findings.Count,
                    .note = If(findings.Count = 0, "No hardcoded pixel positions found in DesignerScript.", "Consider expressing these as %x/%y so the layout scales across devices."),
                    .findings = findings
                })
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        ' ── Layout helpers ────────────────────────────────────────────────────────

        Private Shared Function LoadLayoutJson(layoutPath As String) As JObject
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            Dim converter = New BalConverter(ext = ".bil")
            Dim dir = Path.GetDirectoryName(layoutPath)
            If String.IsNullOrEmpty(dir) Then dir = "."
            Return JObject.Parse(converter.ConvertBalToJson(dir, Path.GetFileName(layoutPath)))
        End Function

        ''' <summary>Flattens the :kids tree into a name -> control-JObject map.</summary>
        Private Shared Function FlattenViews(json As JObject) As Dictionary(Of String, JObject)
            Dim map As New Dictionary(Of String, JObject)(StringComparer.OrdinalIgnoreCase)
            Dim data = TryCast(json("Data"), JObject)
            If data Is Nothing Then Return map
            Dim rootName = If(data("name")?.ToString(), "Activity")
            map(rootName) = data
            CollectKids(data, map)
            Return map
        End Function

        Private Shared Sub CollectKids(node As JObject, map As Dictionary(Of String, JObject))
            Dim kids = TryCast(node(":kids"), JObject)
            If kids Is Nothing Then Return
            For Each p In kids.Properties()
                Dim ch = TryCast(p.Value, JObject)
                If ch Is Nothing Then Continue For
                Dim nm = ch("name")?.ToString()
                If Not String.IsNullOrEmpty(nm) AndAlso Not map.ContainsKey(nm) Then map(nm) = ch
                CollectKids(ch, map)
            Next
        End Sub

        ''' <summary>
        ''' Validates EditText controls in layout JSON and injects missing required properties.
        ''' B4A runtime (EditTextWrapper.build) crashes with NullPointerException if 'password' is missing.
        ''' Also validates inputType is a named constant (TEXT/NUMBERS/etc), not a numeric value.
        ''' </summary>
        Private Shared Function ValidateAndFixEditTexts(json As JObject) As List(Of String)
            Dim warnings As New List(Of String)
            Dim headers = TryCast(json("LayoutHeader")?("ControlsHeaders"), JArray)
            If headers Is Nothing Then Return warnings

            ' Identify EditText control names from headers
            Dim editTextNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each header As JObject In headers
                If header("DesignerType")?.ToString() = "EditText" Then
                    editTextNames.Add(header("Name").ToString())
                End If
            Next
            If editTextNames.Count = 0 Then Return warnings

            ' Find the :kids map in Data
            Dim data = TryCast(json("Data"), JObject)
            If data Is Nothing Then Return warnings
            Dim kids = TryCast(data(":kids"), JObject)
            If kids Is Nothing Then Return warnings

            Dim validInputTypes = {"TEXT", "NUMBERS", "DECIMAL_NUMBERS", "PHONE", "NONE"}

            For Each kvp In kids.Properties()
                Dim child = TryCast(kvp.Value, JObject)
                If child Is Nothing Then Continue For
                Dim name = child("name")?.ToString()
                If name Is Nothing OrElse Not editTextNames.Contains(name) Then Continue For

                ' password — REQUIRED: no null guard in EditTextWrapper.build (NPE at line 262)
                If child("password") Is Nothing Then
                    child.Add("password", New JValue(False))
                    warnings.Add($"Added missing 'password' to {name}")
                End If

                ' inputType — must be a named constant, not an integer
                Dim inputType = child("inputType")
                If inputType IsNot Nothing Then
                    Dim val = inputType.ToString()
                    If Not validInputTypes.Contains(val, StringComparer.OrdinalIgnoreCase) Then
                        ' Try mapping common integer values
                        Select Case val
                            Case "1" : child("inputType") = New JValue("TEXT") : warnings.Add($"Converted inputType 1 to 'TEXT' on {name}")
                            Case "2" : child("inputType") = New JValue("NUMBERS") : warnings.Add($"Converted inputType 2 to 'NUMBERS' on {name}")
                            Case "3" : child("inputType") = New JValue("DECIMAL_NUMBERS") : warnings.Add($"Converted inputType 3 to 'DECIMAL_NUMBERS' on {name}")
                            Case Else : warnings.Add($"WARNING: Invalid inputType '{val}' on {name} — must be TEXT/NUMBERS/DECIMAL_NUMBERS/PHONE/NONE")
                        End Select
                    End If
                End If

                ' hint — safe default
                If child("hint") Is Nothing Then
                    child.Add("hint", New JValue(""))
                End If

                ' singleLine — safe default
                If child("singleLine") Is Nothing Then
                    child.Add("singleLine", New JValue(True))
                    warnings.Add($"Added missing 'singleLine' to {name}")
                End If
            Next

            Return warnings
        End Function

    End Class
End Namespace
