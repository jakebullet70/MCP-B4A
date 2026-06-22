Imports ModelContextProtocol.Server
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports Newtonsoft.Json
Imports B4aMcp.Utils

Namespace Tools
    <McpServerToolType>
    Public Class BuildTools
        Private Const LastBuildLogKey As String = "lastBuildLog"

        <McpServerTool, Description("Compiles a B4A project using B4ABuilder.exe. Returns the full build log and the output APK path on success.")>
        Public Shared Async Function B4aBuild(
            <Description("Full path to the .b4a project file")> projectPath As String,
            <Description("Build mode: 'release' (default, signed APK), 'debug' (unsigned APK), 'bundle' (signed AAB)")> Optional mode As String = "release"
        ) As Task(Of String)
            If Not File.Exists(projectPath) Then Return $"Error: File not found: {projectPath}"
            If Not projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase) Then
                Return "Error: File must have .b4a extension"
            End If

            Dim cfg = AppConfig.Load()
            If String.IsNullOrEmpty(cfg.B4aPath) Then
                Return "Error: b4aPath is not configured. Use b4a_set_config(key='b4aPath', value='C:\\B4A')"
            End If

            Dim builderPath = Path.Combine(cfg.B4aPath, "B4ABuilder.exe")
            If Not File.Exists(builderPath) Then
                Return $"Error: B4ABuilder.exe not found at {builderPath}"
            End If

            Dim baseFolder = Path.GetDirectoryName(projectPath)
            Dim projectFile = Path.GetFileName(projectPath)
            Dim normalizedMode = If(mode, "release").ToLowerInvariant().Trim()

            ' Choose build task
            Dim buildTask As String = If(normalizedMode = "bundle", "BuildBundle", "Build")

            ' NoSign only for debug mode
            Dim noSignArg As String = If(normalizedMode = "debug", " -NoSign=True", "")

            ' Always pass the INI file so B4ABuilder picks up the correct keystore
            Dim iniArg As String = ""
            Dim iniPath = AppConfig.GetB4aIniPath()
            If File.Exists(iniPath) Then iniArg = $" -INI=""{iniPath}"""

            Dim args = $"-Task={buildTask} -BaseFolder=""{baseFolder}"" -Project=""{projectFile}"" -Obfuscate=False -ShowWarnings=True{noSignArg}{iniArg}"

            Try
                Dim psi As New ProcessStartInfo() With {
                    .FileName = builderPath,
                    .Arguments = args,
                    .WorkingDirectory = baseFolder,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .UseShellExecute = False,
                    .CreateNoWindow = True
                }

                Dim output As New System.Text.StringBuilder()
                Dim exitCode As Integer = -1
                Using proc As New Process() With {.StartInfo = psi}
                    AddHandler proc.OutputDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                    AddHandler proc.ErrorDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                    proc.Start()
                    proc.BeginOutputReadLine()
                    proc.BeginErrorReadLine()
                    Await Task.Run(Sub() proc.WaitForExit(300_000))
                    exitCode = proc.ExitCode
                End Using

                Dim log = output.ToString()
                CacheManager.Store(LastBuildLogKey, log)

                Dim result As New System.Text.StringBuilder()
                result.AppendLine($"Build completed (exit code {exitCode}):")
                result.AppendLine(log)

                ' Append output APK/AAB path if build succeeded
                If exitCode = 0 Then
                    Dim projectName = Path.GetFileNameWithoutExtension(projectPath)
                    Dim ext = If(normalizedMode = "bundle", ".aab", ".apk")
                    Dim outputPath = Path.Combine(baseFolder, "Objects", projectName & ext)
                    If File.Exists(outputPath) Then
                        result.AppendLine($"Output: {outputPath}")
                    End If
                End If

                Return result.ToString().TrimEnd()
            Catch ex As Exception
                Return $"Error starting build: {ex.Message}"
            End Try
        End Function

        <McpServerTool, Description(
            "Compiles a B4A project AND installs the resulting APK on the device in one step. " &
            "Equivalent to calling b4a_build followed by b4a_install_apk. " &
            "Returns the combined build log and install result.")>
        Public Shared Async Function B4aBuildAndInstall(
            <Description("Full path to the .b4a project file")> projectPath As String,
            <Description("ADB device serial (optional, uses first device if not specified)")> Optional deviceSerial As String = "",
            <Description("Build mode: 'release' (default) or 'debug'")> Optional mode As String = "release"
        ) As Task(Of String)
            ' Build
            Dim buildResult = Await B4aBuild(projectPath, mode)

            ' Check for failure (exit code != 0 means "Completed successfully" won't appear)
            If Not buildResult.Contains("Completed successfully") Then
                Return $"Build FAILED — install skipped.{Environment.NewLine}{buildResult}"
            End If

            ' Extract APK path from build output
            Dim apkPath As String = Nothing
            For Each line In buildResult.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                If line.StartsWith("Output:") Then
                    apkPath = line.Substring("Output:".Length).Trim()
                    Exit For
                End If
            Next

            If String.IsNullOrEmpty(apkPath) OrElse Not File.Exists(apkPath) Then
                Return $"Build succeeded but APK path not found in output.{Environment.NewLine}{buildResult}"
            End If

            ' Install
            Dim installResult = Await AdbTools.B4aInstallApk(apkPath, deviceSerial)
            Return $"{buildResult}{Environment.NewLine}--- Install ---{Environment.NewLine}{installResult}"
        End Function

        <McpServerTool, Description(
            "One-shot run loop: builds the project, installs the APK, clears logcat, launches the app, then watches " &
            "logcat for a few seconds and reports any crash. Returns build status, install result, and the captured log " &
            "(with the crash highlighted if one occurred). The fastest way to confirm a change actually runs on device.")>
        Public Shared Async Function B4aRun(
            <Description("Full path to the .b4a project file")> projectPath As String,
            <Description("Build mode: 'release' (default) or 'debug'")> Optional mode As String = "release",
            <Description("Main activity (default '.main')")> Optional activity As String = ".main",
            <Description("Seconds to watch logcat after launch (default 6, max 30)")> Optional watchSeconds As Integer = 6,
            <Description("ADB device serial (optional)")> Optional deviceSerial As String = ""
        ) As Task(Of String)
            If Not File.Exists(projectPath) Then Return $"Error: File not found: {projectPath}"
            watchSeconds = Math.Min(Math.Max(watchSeconds, 1), 30)

            ' 1. Build + install
            Dim buildInstall = Await B4aBuildAndInstall(projectPath, deviceSerial, mode)
            If buildInstall.StartsWith("Build FAILED") OrElse buildInstall.Contains("install skipped") Then
                Return buildInstall
            End If

            ' 2. Resolve package name from the project
            Dim packageName As String
            Try
                packageName = B4aParser.Parse(projectPath).PackageName
            Catch
                packageName = Nothing
            End Try
            If String.IsNullOrEmpty(packageName) Then
                Return $"{buildInstall}{Environment.NewLine}--- Run ---{Environment.NewLine}Built and installed, but could not read the package name from the project to launch it."
            End If

            ' 3. Clear logcat, launch, then watch
            Await AdbRunner.RunText($"{AdbRunner.DeviceArg(deviceSerial)}logcat -c", 10_000)
            Dim launch = Await DeviceTools.B4aLaunchApp(packageName, activity, deviceSerial)
            Dim watched = Await AdbRunner.RunTextFor($"{AdbRunner.DeviceArg(deviceSerial)}logcat AndroidRuntime:E B4A:* System.err:W *:S", watchSeconds * 1000)

            ' 4. Detect a crash in the captured window
            Dim crash As String = "none detected"
            If watched IsNot Nothing Then
                Dim wLines = watched.Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                Dim m = wLines.Select(Function(l) Text.RegularExpressions.Regex.Match(l, "Error occurred on line:\s*(\d+)\s*\(([^)]*)\)", Text.RegularExpressions.RegexOptions.IgnoreCase)) _
                              .LastOrDefault(Function(x) x.Success)
                If m IsNot Nothing AndAlso m.Success Then
                    crash = $"B4A error on line {m.Groups(1).Value} ({m.Groups(2).Value.Trim()})"
                ElseIf wLines.Any(Function(l) l.IndexOf("FATAL EXCEPTION", StringComparison.OrdinalIgnoreCase) >= 0) Then
                    crash = "FATAL EXCEPTION — see captured log below"
                End If
            End If

            Dim sb As New System.Text.StringBuilder()
            sb.AppendLine(buildInstall)
            sb.AppendLine($"--- Run ({packageName}{activity}) ---")
            sb.AppendLine($"Launch: {launch}")
            sb.AppendLine($"Crash: {crash}")
            sb.AppendLine($"--- Watched {watchSeconds}s ---")
            sb.AppendLine(If(watched, "(no adb)"))
            Return sb.ToString().TrimEnd()
        End Function

        <McpServerTool, Description("Returns the log from the last b4a_build call")>
        Public Shared Function B4aGetBuildLog() As String
            Dim log As String = Nothing
            If CacheManager.TryGet(Of String)(LastBuildLogKey, log) Then Return log
            Return "No build log available. Run b4a_build first."
        End Function

        <McpServerTool, Description(
            "Parses the last build log into structured errors: each with the message, and the module + line " &
            "when B4A reports 'Occurred on line: N (Module)'. Returns { success, errorCount, errors:[{module,line,message,source}] }. " &
            "Pass logText to parse a specific log instead of the cached last build.")>
        Public Shared Function B4aParseBuildErrors(
            <Description("Optional: raw build-log text to parse. If omitted, parses the last b4a_build log.")> Optional logText As String = ""
        ) As String
            Dim log As String = logText
            If String.IsNullOrEmpty(log) Then
                Dim cached As String = Nothing
                If Not CacheManager.TryGet(Of String)(LastBuildLogKey, cached) Then
                    Return "No build log available. Run b4a_build first (or pass logText)."
                End If
                log = cached
            End If

            Dim lines = log.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim errors As New List(Of Object)
            Dim descRx As New Text.RegularExpressions.Regex("^\s*Error(?: description)?:\s*(?<msg>.+)$", Text.RegularExpressions.RegexOptions.IgnoreCase)
            Dim lineRx As New Text.RegularExpressions.Regex("Occurred on line:\s*(?<line>\d+)\s*\((?<mod>[^)]*)\)", Text.RegularExpressions.RegexOptions.IgnoreCase)

            For i = 0 To lines.Length - 1
                Dim dm = descRx.Match(lines(i))
                If Not dm.Success Then Continue For

                Dim msg = dm.Groups("msg").Value.Trim()
                Dim moduleName As String = Nothing
                Dim lineNo As Integer? = Nothing
                Dim source As String = Nothing

                ' Look at the next couple of lines for the location + source snippet.
                For j = i + 1 To Math.Min(i + 3, lines.Length - 1)
                    Dim lm = lineRx.Match(lines(j))
                    If lm.Success Then
                        moduleName = lm.Groups("mod").Value.Trim()
                        lineNo = Integer.Parse(lm.Groups("line").Value)
                        If j + 1 < lines.Length Then source = lines(j + 1).Trim()
                        Exit For
                    End If
                Next

                errors.Add(New With {
                    .moduleName = moduleName,
                    .line = lineNo,
                    .message = msg,
                    .source = source
                })
            Next

            Dim succeeded = log.IndexOf("Completed successfully", StringComparison.OrdinalIgnoreCase) >= 0
            Return JsonConvert.SerializeObject(New With {
                .success = succeeded,
                .errorCount = errors.Count,
                .errors = errors
            }, Formatting.Indented)
        End Function

        <McpServerTool, Description("Returns the signing configuration from B4A IDE settings: keystore path, alias, and whether signing is fully configured. Does NOT expose the password.")>
        Public Shared Function B4aGetSigningInfo() As String
            Try
                Dim ini = AppConfig.GetB4aIniValues()
                Dim keyFile As String = Nothing
                Dim keyAlias As String = Nothing
                Dim keyPassword As String = Nothing
                ini.TryGetValue("SignKeyFile", keyFile)
                ini.TryGetValue("SignKeyAlias", keyAlias)
                ini.TryGetValue("SignKeyPassword", keyPassword)

                Dim keyFileExists = Not String.IsNullOrEmpty(keyFile) AndAlso File.Exists(keyFile)

                Return JsonConvert.SerializeObject(New With {
                    .keyFile = If(keyFile, ""),
                    .keyFileExists = keyFileExists,
                    .keyAlias = If(keyAlias, ""),
                    .hasPassword = Not String.IsNullOrEmpty(keyPassword),
                    .signingConfigured = keyFileExists AndAlso Not String.IsNullOrEmpty(keyAlias) AndAlso Not String.IsNullOrEmpty(keyPassword)
                }, Formatting.Indented)
            Catch ex As Exception
                Return $"Error: {ex.Message}"
            End Try
        End Function

    End Class
End Namespace
