Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports Newtonsoft.Json
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class EmulatorTools

        <McpServerTool, Description(
            "Lists the Android Virtual Devices (AVDs) available to the emulator (emulator -list-avds). " &
            "Use when no physical device is connected and you need to start one for b4a_install/run/screenshot.")>
        Public Shared Async Function B4aListEmulators() As Task(Of String)
            Dim emu = FindEmulator()
            If emu Is Nothing Then Return "Error: emulator.exe not found. Install the Android SDK 'emulator' package, or no AVDs are configured."
            Try
                Dim out = Await RunCaptured(emu, "-list-avds", 15_000)
                Dim avds = out.Split(New String() {Environment.NewLine, vbLf}, StringSplitOptions.RemoveEmptyEntries) _
                              .Where(Function(l) Not l.StartsWith("INFO") AndAlso Not l.Contains(":") AndAlso l.Trim().Length > 0) _
                              .Select(Function(l) l.Trim()).ToList()
                Return JsonConvert.SerializeObject(New With {.emulatorPath = emu, .count = avds.Count, .avds = avds}, Formatting.Indented)
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description(
            "Starts an Android emulator for the given AVD name (emulator -avd <name>), launched detached. " &
            "Returns immediately — the emulator takes time to boot; poll b4a_list_devices until it appears.")>
        Public Shared Function B4aStartEmulator(
            <Description("AVD name (from b4a_list_emulators)")> avdName As String,
            <Description("Extra emulator flags (optional), e.g. '-no-snapshot -gpu host'")> Optional extraArgs As String = ""
        ) As String
            If String.IsNullOrWhiteSpace(avdName) Then Return "Error: avdName is required"
            Dim emu = FindEmulator()
            If emu Is Nothing Then Return "Error: emulator.exe not found."
            Try
                Process.Start(New ProcessStartInfo() With {
                    .FileName = emu,
                    .Arguments = $"-avd {avdName} {extraArgs}".Trim(),
                    .UseShellExecute = True
                })
                Return $"OK: starting emulator '{avdName}'. Boot takes ~10–60s — poll b4a_list_devices until it shows 'device'."
            Catch ex As Exception
                Return $"Error launching emulator: {ex.Message}"
            End Try
        End Function

        ' ── Helpers ──────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Locates emulator.exe relative to the Android SDK root, derived from the adb path
        ''' (…/platform-tools/adb.exe → SDK root) or the B4A ini ToolsFolder.
        ''' </summary>
        Private Shared Function FindEmulator() As String
            Dim roots As New List(Of String)

            Dim adb = AdbRunner.FindAdb()
            If adb IsNot Nothing Then
                Dim platformTools = Path.GetDirectoryName(adb)
                Dim sdkRoot = Path.GetDirectoryName(platformTools)
                If Not String.IsNullOrEmpty(sdkRoot) Then roots.Add(sdkRoot)
            End If

            Try
                Dim ini = AppConfig.GetB4aIniValues()
                Dim toolsFolder As String = Nothing
                If ini.TryGetValue("ToolsFolder", toolsFolder) AndAlso Not String.IsNullOrEmpty(toolsFolder) Then
                    Dim sdkRoot = Path.GetDirectoryName(toolsFolder.TrimEnd("\"c, "/"c))
                    If Not String.IsNullOrEmpty(sdkRoot) Then roots.Add(sdkRoot)
                End If
            Catch
            End Try

            For Each root In roots
                For Each rel In {Path.Combine(root, "emulator", "emulator.exe"), Path.Combine(root, "tools", "emulator.exe")}
                    If File.Exists(rel) Then Return rel
                Next
            Next
            Return Nothing
        End Function

        Private Shared Async Function RunCaptured(exePath As String, arguments As String, timeoutMs As Integer) As Task(Of String)
            Dim psi As New ProcessStartInfo() With {
                .FileName = exePath,
                .Arguments = arguments,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .UseShellExecute = False,
                .CreateNoWindow = True
            }
            Dim output As New System.Text.StringBuilder()
            Using proc As New Process() With {.StartInfo = psi}
                AddHandler proc.OutputDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                AddHandler proc.ErrorDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                proc.Start()
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                Await Task.Run(Sub() proc.WaitForExit(timeoutMs))
            End Using
            Return output.ToString().Trim()
        End Function

    End Class
End Namespace
