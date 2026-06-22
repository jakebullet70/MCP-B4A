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

    End Class
End Namespace
