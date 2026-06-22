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
            If Not File.Exists(layoutPath) Then Return $"Error: File not found: {layoutPath}"
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            If ext <> ".bal" AndAlso ext <> ".bil" Then
                Return "Error: File must have .bal or .bil extension"
            End If
            Try
                Dim cached As String = Nothing
                If CacheManager.TryGetByMtime(Of String)(layoutPath, cached) Then Return cached

                Dim converter = New BalConverter(ext = ".bil")
                Dim dir = Path.GetDirectoryName(layoutPath)
                If String.IsNullOrEmpty(dir) Then dir = "."
                Dim json = converter.ConvertBalToJson(dir, Path.GetFileName(layoutPath))
                CacheManager.SetByMtime(layoutPath, json)
                Return json
            Catch ex As Exception
                Return $"Error reading layout: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description("Writes JSON data to a B4A layout file (.bal or .bil). Creates a .bak backup first. EditText controls MUST include 'password' (boolean) and 'inputType' (string: TEXT, NUMBERS, DECIMAL_NUMBERS, PHONE, or NONE). Missing required properties are auto-injected with safe defaults.")>
        Public Shared Function B4aWriteLayout(
            <Description("Full path to the .bal or .bil layout file to write")> layoutPath As String,
            <Description("JSON layout data (as returned by b4a_read_layout). EditText controls require: password (bool), inputType (string constant), hint (string), hintColor (color), singleLine (bool).")> jsonData As String
        ) As String
            Dim ext = Path.GetExtension(layoutPath).ToLowerInvariant()
            If ext <> ".bal" AndAlso ext <> ".bil" Then
                Return "Error: File must have .bal or .bil extension"
            End If
            Try
                Dim json As JObject
                Try
                    json = JObject.Parse(jsonData)
                Catch ex As JsonException
                    Return $"Error: Invalid JSON — {ex.Message}"
                End Try

                ' Validate required structure
                If json("LayoutHeader") Is Nothing Then Return "Error: Missing 'LayoutHeader' in JSON"
                If json("Variants") Is Nothing Then Return "Error: Missing 'Variants' in JSON"
                If json("Data") Is Nothing Then Return "Error: Missing 'Data' in JSON"

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
                Dim result = $"OK: backup saved as {layoutPath}.bak"
                If warnings.Count > 0 Then
                    result &= Environment.NewLine & "[AUTO-FIX] " & String.Join("; ", warnings)
                End If
                Return result
            Catch ex As Exception
                Return $"Error writing layout: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description("Lists all .bal and .bil layout files in a B4A project directory")>
        Public Shared Function B4aListLayouts(
            <Description("Path to the B4A project directory (or .b4a file path)")> projectDir As String
        ) As String
            If projectDir.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                projectDir = If(Path.GetDirectoryName(projectDir), ".")
            End If
            If Not Directory.Exists(projectDir) Then Return $"Error: Directory not found: {projectDir}"
            Try
                Dim layouts = Directory.GetFiles(projectDir, "*.bal", SearchOption.AllDirectories) _
                    .Concat(Directory.GetFiles(projectDir, "*.bil", SearchOption.AllDirectories)) _
                    .OrderBy(Function(f) f) _
                    .Select(Function(f) New With {
                        .name = Path.GetFileName(f),
                        .path = f,
                        .sizeKb = Math.Round(New FileInfo(f).Length / 1024.0, 1)
                    }).ToList()
                Return JsonConvert.SerializeObject(New With {
                    .count = layouts.Count,
                    .layouts = layouts
                }, Formatting.Indented)
            Catch ex As Exception
                Return $"Error: {ex.Message}"
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
            If Not File.Exists(sourcePath) Then Return $"Error: Source not found: {sourcePath}"
            Dim srcExt = Path.GetExtension(sourcePath).ToLowerInvariant()
            Dim dstExt = Path.GetExtension(destPath).ToLowerInvariant()
            If srcExt <> ".bal" AndAlso srcExt <> ".bil" Then Return "Error: Source must be .bal or .bil"
            If dstExt <> srcExt Then Return $"Error: Destination extension ({dstExt}) must match source ({srcExt})"
            If File.Exists(destPath) AndAlso Not overwrite Then Return $"Error: Destination exists: {destPath} (set overwrite=true)"
            Try
                Dim dir = Path.GetDirectoryName(destPath)
                If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
                File.Copy(sourcePath, destPath, overwrite)
                Return $"OK: cloned to {destPath}"
            Catch ex As Exception
                Return $"Error: {ex.Message}"
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
            If ext <> ".bal" AndAlso ext <> ".bil" Then Return "Error: File must have .bal or .bil extension"
            If File.Exists(layoutPath) AndAlso Not overwrite Then Return $"Error: File exists: {layoutPath} (set overwrite=true)"
            If width <= 0 OrElse height <= 0 Then Return "Error: width and height must be positive"

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
                Return $"OK: created {width}x{height} layout at {layoutPath}. Add views with b4a_write_layout."
            Catch ex As Exception
                Return $"Error creating layout: {ex.Message}"
            End Try
        End Function

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
