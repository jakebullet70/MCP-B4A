Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports Newtonsoft.Json
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class EnvTools

        <McpServerTool, Description(
            "Runs an environment health check for B4A development: verifies the B4A install (B4A.exe + B4ABuilder.exe), " &
            "the additional libraries folder, adb availability, the Java binary, the signing keystore, and connected devices. " &
            "Each check reports ok/warn/fail with a remediation hint. Call this first when builds or device tools misbehave.")>
        Public Shared Async Function B4aDoctor() As Task(Of String)
            Dim checks As New List(Of Object)

            Dim cfg = AppConfig.Load()

            ' B4A install
            If String.IsNullOrEmpty(cfg.B4aPath) Then
                checks.Add(Check("b4aPath", "fail", "not set", "Run b4a_set_config(key='b4aPath', value='C:\\Program Files\\Anywhere Software\\B4A')"))
            ElseIf Not Directory.Exists(cfg.B4aPath) Then
                checks.Add(Check("b4aPath", "fail", cfg.B4aPath, "Directory does not exist — fix b4aPath"))
            Else
                Dim builder = Path.Combine(cfg.B4aPath, "B4ABuilder.exe")
                Dim ide = Path.Combine(cfg.B4aPath, "B4A.exe")
                checks.Add(Check("B4ABuilder.exe", If(File.Exists(builder), "ok", "fail"), builder,
                                 If(File.Exists(builder), "", "Missing — builds will fail")))
                checks.Add(Check("B4A.exe (IDE)", If(File.Exists(ide), "ok", "warn"), ide,
                                 If(File.Exists(ide), "", "IDE not found — b4a_open_ide unavailable")))
            End If

            ' Additional libraries
            If String.IsNullOrEmpty(cfg.AdditionalLibrariesPath) Then
                checks.Add(Check("additionalLibrariesPath", "warn", "not set", "Optional; set if you use external libraries"))
            Else
                checks.Add(Check("additionalLibrariesPath", If(Directory.Exists(cfg.AdditionalLibrariesPath), "ok", "warn"),
                                 cfg.AdditionalLibrariesPath, If(Directory.Exists(cfg.AdditionalLibrariesPath), "", "Directory does not exist")))
            End If

            ' adb
            Dim adb = AdbRunner.FindAdb()
            checks.Add(Check("adb", If(adb IsNot Nothing, "ok", "warn"), If(adb, "not found"),
                             If(adb IsNot Nothing, "", "Install Android SDK Platform Tools or set adbPath — device tools unavailable")))

            ' Java
            If String.IsNullOrEmpty(cfg.JavaBin) Then
                checks.Add(Check("javaBin", "warn", "not set", "Usually auto-detected from b4xV5.ini; set if builds report a missing JDK"))
            Else
                checks.Add(Check("javaBin", If(Directory.Exists(cfg.JavaBin) OrElse File.Exists(cfg.JavaBin), "ok", "warn"),
                                 cfg.JavaBin, ""))
            End If

            ' Signing keystore
            Try
                Dim ini = AppConfig.GetB4aIniValues()
                Dim keyFile As String = Nothing
                ini.TryGetValue("SignKeyFile", keyFile)
                If String.IsNullOrEmpty(keyFile) Then
                    checks.Add(Check("keystore", "warn", "not configured", "Release builds need a keystore (configured in the B4A IDE)"))
                Else
                    checks.Add(Check("keystore", If(File.Exists(keyFile), "ok", "warn"), keyFile,
                                     If(File.Exists(keyFile), "", "Configured keystore file is missing")))
                End If
            Catch
                checks.Add(Check("keystore", "warn", "unreadable", "Could not read b4xV5.ini signing info"))
            End Try

            ' Connected devices
            If adb IsNot Nothing Then
                Dim devOut = Await AdbRunner.RunText("devices", 10_000)
                Dim devCount = 0
                If devOut IsNot Nothing Then
                    devCount = devOut.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries) _
                                     .Skip(1).Count(Function(l) l.Trim().EndsWith("device", StringComparison.OrdinalIgnoreCase))
                End If
                checks.Add(Check("devices", If(devCount > 0, "ok", "warn"), $"{devCount} connected",
                                 If(devCount > 0, "", "No device — connect one or start an emulator for device/install tools")))
            End If

            Dim failures = checks.Where(Function(c) CStr(c.GetType().GetProperty("status").GetValue(c)) = "fail").Count
            Dim warns = checks.Where(Function(c) CStr(c.GetType().GetProperty("status").GetValue(c)) = "warn").Count
            Return ToolResult.Ok(New With {
                .summary = If(failures > 0, "FAIL", If(warns > 0, "WARN", "OK")),
                .failures = failures,
                .warnings = warns,
                .configSources = AppConfig.GetSources(),
                .checks = checks
            })
        End Function

        <McpServerTool, Description(
            "Opens a B4A project (.b4a) in the B4A IDE (B4A.exe). Launches the IDE detached and returns immediately.")>
        Public Shared Function B4aOpenIde(
            <Description("Full path to the .b4a project file to open")> projectPath As String
        ) As String
            If Not File.Exists(projectPath) Then Return ToolResult.Fail($"File not found: {projectPath}")
            If Not projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                Return ToolResult.Fail($"File must have .b4a extension")
            End If

            Dim cfg = AppConfig.Load()
            If String.IsNullOrEmpty(cfg.B4aPath) Then Return ToolResult.Fail($"b4aPath is not configured.")
            Dim idePath = Path.Combine(cfg.B4aPath, "B4A.exe")
            If Not File.Exists(idePath) Then Return ToolResult.Fail($"B4A.exe not found at {idePath}")

            Try
                Process.Start(New ProcessStartInfo() With {
                    .FileName = idePath,
                    .Arguments = $"""{projectPath}""",
                    .UseShellExecute = True
                })
                Return ToolResult.Message($"opening {Path.GetFileName(projectPath)} in B4A IDE")
            Catch ex As Exception
                Return ToolResult.Fail(ex.Message)
            End Try
        End Function

        Private Shared Function Check(name As String, status As String, value As String, hint As String) As Object
            Return New With {.name = name, .status = status, .value = value, .hint = hint}
        End Function

    End Class
End Namespace
