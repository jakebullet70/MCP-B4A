Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.IO
Imports Newtonsoft.Json
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class AdbTools

        <McpServerTool, Description("Returns logcat output filtered by the 'B4A' tag. Read-only.")>
        Public Shared Async Function B4aGetLogcat(
            <Description("ADB device serial (optional, uses first device if not specified)")> Optional deviceSerial As String = "",
            <Description("Number of lines to return (default 100, max 500)")> Optional lines As Integer = 100,
            <Description("Additional logcat filter tag (in addition to B4A tag)")> Optional filter As String = ""
        ) As Task(Of String)
            lines = Math.Min(Math.Max(lines, 1), 500)
            Try
                Dim tagFilter = "B4A:*"
                If Not String.IsNullOrEmpty(filter) Then tagFilter &= $" {filter}:*"
                tagFilter &= " *:S"

                Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}logcat -d {tagFilter}", 15_000, "[stderr] ")
                If out Is Nothing Then
                    Return "Error: adb not found. Set adbPath in config or install Android SDK Platform Tools."
                End If

                Dim allLines = out.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                Dim lastLines = allLines.TakeLast(lines).ToArray()
                Dim prefix = If(allLines.Length > lines, $"[showing last {lines} of {allLines.Length} lines]", $"[{lastLines.Length} lines]")
                Return $"{prefix}{Environment.NewLine}{String.Join(Environment.NewLine, lastLines)}"
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description(
            "Follows logcat for a fixed number of seconds and returns the B4A-tagged lines captured during that window. " &
            "Unlike b4a_get_logcat (which dumps the existing buffer), this WATCHES for new output — call it right before " &
            "reproducing a crash or action to see it happen live.")>
        Public Shared Async Function B4aTailLog(
            <Description("Seconds to watch (default 5, max 30)")> Optional seconds As Integer = 5,
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = "",
            <Description("Additional logcat filter tag (in addition to B4A)")> Optional filter As String = ""
        ) As Task(Of String)
            seconds = Math.Min(Math.Max(seconds, 1), 30)
            Try
                Dim tagFilter = "B4A:*"
                If Not String.IsNullOrEmpty(filter) Then tagFilter &= $" {filter}:*"
                tagFilter &= " *:S"

                ' -T 1 starts the follow near "now" so we mostly see fresh lines.
                Dim out = Await AdbRunner.RunTextFor($"{AdbRunner.DeviceArg(deviceSerial)}logcat -T 1 {tagFilter}", seconds * 1000)
                If out Is Nothing Then
                    Return "Error: adb not found. Set adbPath in config or install Android SDK Platform Tools."
                End If
                Dim lines = out.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                Return $"[watched {seconds}s, {lines.Length} line(s)]{Environment.NewLine}{out}"
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description(
            "Extracts the most recent crash from logcat: the last AndroidRuntime FATAL EXCEPTION block and/or " &
            "the last B4A 'Error occurred on line: N (module)' entry. Returns the module + line when B4A reports it, " &
            "plus the stack trace. Use after an app crashes to jump straight to the failing location.")>
        Public Shared Async Function B4aGetLastCrash(
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            Try
                ' Pull both the B4A tag and AndroidRuntime (where uncaught exceptions land).
                Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}logcat -d AndroidRuntime:E B4A:* System.err:W *:S", 15_000)
                If out Is Nothing Then
                    Return "Error: adb not found. Set adbPath in config or install Android SDK Platform Tools."
                End If

                Dim allLines = out.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)

                ' B4A reports runtime errors as: "Error occurred on line: 123 (modulename)."
                Dim b4aErr As Object = Nothing
                Dim m = allLines.Select(Function(l) Text.RegularExpressions.Regex.Match(l, "Error occurred on line:\s*(\d+)\s*\(([^)]*)\)", Text.RegularExpressions.RegexOptions.IgnoreCase)) _
                                .LastOrDefault(Function(x) x.Success)
                If m IsNot Nothing AndAlso m.Success Then
                    b4aErr = New With {.line = Integer.Parse(m.Groups(1).Value), .module = m.Groups(2).Value.Trim()}
                End If

                ' Last FATAL EXCEPTION block (from the marker to the end of contiguous trace lines).
                Dim fatalIdx = -1
                For i = allLines.Length - 1 To 0 Step -1
                    If allLines(i).IndexOf("FATAL EXCEPTION", StringComparison.OrdinalIgnoreCase) >= 0 Then fatalIdx = i : Exit For
                Next
                Dim trace As String = Nothing
                If fatalIdx >= 0 Then
                    trace = String.Join(Environment.NewLine, allLines.Skip(fatalIdx).Take(40))
                End If

                If b4aErr Is Nothing AndAlso trace Is Nothing Then
                    Return "No crash found in the current logcat buffer. Reproduce the crash, then call this again (or use b4a_tail_log while reproducing)."
                End If

                Return JsonConvert.SerializeObject(New With {
                    .b4aError = b4aErr,
                    .fatalException = trace
                }, Formatting.Indented)
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description("Lists ADB-connected Android devices")>
        Public Shared Async Function B4aListDevices() As Task(Of String)
            Try
                Dim cached As String = Nothing
                If CacheManager.TryGetByTtl(Of String)("adb:devices", cached) Then Return cached

                Dim raw = Await AdbRunner.RunText("devices -l", 10_000)
                If raw Is Nothing Then
                    Return "Error: adb not found. Set adbPath in config or install Android SDK Platform Tools."
                End If

                Dim deviceLines = raw.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries) _
                    .Skip(1) _
                    .Where(Function(l) Not String.IsNullOrWhiteSpace(l)) _
                    .ToList()

                Dim devices = deviceLines.Select(Function(l)
                    Dim parts = l.Split(New Char() {" "c, Chr(9)}, StringSplitOptions.RemoveEmptyEntries)
                    Return New With {
                        .serial = If(parts.Length > 0, parts(0), ""),
                        .state = If(parts.Length > 1, parts(1), ""),
                        .info = If(parts.Length > 2, String.Join(" ", parts.Skip(2)), "")
                    }
                End Function).ToList()

                Dim result = JsonConvert.SerializeObject(New With {
                    .count = devices.Count,
                    .devices = devices
                }, Formatting.Indented)
                CacheManager.SetByTtl("adb:devices", result, 5)
                Return result
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description("Installs an APK on a connected Android device via ADB (-r flag allows reinstall).")>
        Public Shared Async Function B4aInstallApk(
            <Description("Full path to the APK file to install")> apkPath As String,
            <Description("ADB device serial (optional, uses first device if not specified)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            If Not File.Exists(apkPath) Then Return $"Error: APK not found: {apkPath}"
            Try
                Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}install -r ""{apkPath}""", 60_000)
                If out Is Nothing Then
                    Return "Error: adb not found. Set adbPath in config or install Android SDK Platform Tools."
                End If
                Return out
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description("Uninstalls an app from the device by package name (adb uninstall).")>
        Public Shared Async Function B4aUninstall(
            <Description("App package name (e.g. b4a.example)")> packageName As String,
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            If String.IsNullOrWhiteSpace(packageName) Then Return "Error: packageName is required"
            Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}uninstall {packageName}", 30_000)
            Return If(out Is Nothing, "Error: adb not found.", out)
        End Function

        <McpServerTool, Description("Clears an app's data and cache (adb shell pm clear) for a clean-slate test run.")>
        Public Shared Async Function B4aClearData(
            <Description("App package name")> packageName As String,
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            If String.IsNullOrWhiteSpace(packageName) Then Return "Error: packageName is required"
            Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}shell pm clear {packageName}", 15_000)
            Return If(out Is Nothing, "Error: adb not found.", out)
        End Function

        <McpServerTool, Description("Force-stops a running app (adb shell am force-stop).")>
        Public Shared Async Function B4aStopApp(
            <Description("App package name")> packageName As String,
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            If String.IsNullOrWhiteSpace(packageName) Then Return "Error: packageName is required"
            Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}shell am force-stop {packageName}", 10_000)
            If out Is Nothing Then Return "Error: adb not found."
            Return If(String.IsNullOrEmpty(out), $"Force-stopped {packageName}", out)
        End Function

        <McpServerTool, Description(
            "Grants a runtime permission to an app (adb shell pm grant). " &
            "permission is the full Android permission name, e.g. android.permission.CAMERA, " &
            "android.permission.ACCESS_FINE_LOCATION, android.permission.RECORD_AUDIO.")>
        Public Shared Async Function B4aGrantPermission(
            <Description("App package name")> packageName As String,
            <Description("Full Android permission name (e.g. android.permission.CAMERA)")> permission As String,
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            If String.IsNullOrWhiteSpace(packageName) OrElse String.IsNullOrWhiteSpace(permission) Then
                Return "Error: packageName and permission are required"
            End If
            Dim out = Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}shell pm grant {packageName} {permission}", 10_000)
            If out Is Nothing Then Return "Error: adb not found."
            Return If(String.IsNullOrEmpty(out), $"Granted {permission} to {packageName}", out)
        End Function

    End Class
End Namespace
