Imports System.Diagnostics
Imports System.IO

Namespace Utils
    ''' <summary>
    ''' Shared helpers for locating and invoking adb.exe.
    ''' Consolidates the FindAdb lookup and the ProcessStartInfo + async-read
    ''' boilerplate previously duplicated across AdbTools and DeviceTools.
    ''' </summary>
    Public Class AdbRunner

        ''' <summary>
        ''' Locates adb.exe. Resolution order:
        ''' 1. Configured AdbPath — AppConfig auto-detects this from the B4A IDE's
        '''    b4xV5.ini (ToolsFolder → ../platform-tools/adb.exe) when not set explicitly.
        ''' 2. PATH environment variable.
        ''' 3. Well-known Android SDK location under LocalAppData.
        ''' Returns Nothing if adb cannot be found.
        ''' </summary>
        Public Shared Function FindAdb() As String
            Dim cfg = AppConfig.Load()

            ' 1. Configured / b4xV5.ini-derived path
            If Not String.IsNullOrEmpty(cfg.AdbPath) Then
                If File.Exists(cfg.AdbPath) Then Return cfg.AdbPath
                Dim adbExe = Path.Combine(cfg.AdbPath, "adb.exe")
                If File.Exists(adbExe) Then Return adbExe
            End If

            ' 2. PATH environment variable
            Dim pathEnv = If(Environment.GetEnvironmentVariable("PATH"), "")
            For Each pathDir In pathEnv.Split(";"c)
                Dim candidate = Path.Combine(pathDir.Trim(), "adb.exe")
                If File.Exists(candidate) Then Return candidate
            Next

            ' 3. Well-known Android SDK location
            Dim localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            Dim sdkPath = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe")
            If File.Exists(sdkPath) Then Return sdkPath

            Return Nothing
        End Function

        ''' <summary>Builds the "-s SERIAL " argument fragment (with trailing space) or "" when no serial is given.</summary>
        Public Shared Function DeviceArg(deviceSerial As String) As String
            Return If(Not String.IsNullOrEmpty(deviceSerial), $"-s {deviceSerial} ", "")
        End Function

        ''' <summary>
        ''' Runs adb with the given arguments and returns combined stdout+stderr as trimmed text.
        ''' stderr lines are prefixed with <paramref name="errPrefix"/>.
        ''' Returns Nothing when adb cannot be located (callers surface their own message).
        ''' </summary>
        Public Shared Async Function RunText(arguments As String, timeoutMs As Integer,
                                             Optional errPrefix As String = "[err] ") As Task(Of String)
            Dim adbPath = FindAdb()
            If adbPath Is Nothing Then Return Nothing

            Dim output As New System.Text.StringBuilder()
            Using proc As New Process() With {.StartInfo = NewPsi(adbPath, arguments)}
                AddHandler proc.OutputDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                AddHandler proc.ErrorDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(errPrefix & e.Data)
                proc.Start()
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                Await Task.Run(Sub() proc.WaitForExit(timeoutMs))
            End Using
            Return output.ToString().Trim()
        End Function

        ''' <summary>
        ''' Runs adb and returns raw stdout bytes (for binary output such as 'exec-out screencap -p').
        ''' Returns Nothing when adb cannot be located.
        ''' </summary>
        Public Shared Async Function RunBinary(arguments As String, timeoutMs As Integer) As Task(Of Byte())
            Dim adbPath = FindAdb()
            If adbPath Is Nothing Then Return Nothing

            Using proc As New Process() With {.StartInfo = NewPsi(adbPath, arguments)}
                proc.Start()
                Dim ms As New MemoryStream()
                Dim buf(65535) As Byte
                Dim bytesRead As Integer
                Do
                    bytesRead = Await proc.StandardOutput.BaseStream.ReadAsync(buf, 0, buf.Length)
                    If bytesRead > 0 Then ms.Write(buf, 0, bytesRead)
                Loop While bytesRead > 0
                Await Task.Run(Sub() proc.WaitForExit(timeoutMs))
                Return ms.ToArray()
            End Using
        End Function

        ''' <summary>
        ''' Runs a (typically non-terminating) adb command such as 'logcat' for a fixed duration,
        ''' captures stdout, then kills the process. Returns the captured text, or Nothing if adb is missing.
        ''' </summary>
        Public Shared Async Function RunTextFor(arguments As String, durationMs As Integer) As Task(Of String)
            Dim adbPath = FindAdb()
            If adbPath Is Nothing Then Return Nothing

            Dim output As New System.Text.StringBuilder()
            Using proc As New Process() With {.StartInfo = NewPsi(adbPath, arguments)}
                AddHandler proc.OutputDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                AddHandler proc.ErrorDataReceived, Sub(s, e) If e.Data IsNot Nothing Then output.AppendLine(e.Data)
                proc.Start()
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                Await Task.Delay(durationMs)
                Try
                    If Not proc.HasExited Then proc.Kill(entireProcessTree:=True)
                Catch
                    ' process may have exited between the check and the kill — ignore
                End Try
            End Using
            Return output.ToString().Trim()
        End Function

        Private Shared Function NewPsi(adbPath As String, arguments As String) As ProcessStartInfo
            Return New ProcessStartInfo() With {
                .FileName = adbPath,
                .Arguments = arguments,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .UseShellExecute = False,
                .CreateNoWindow = True
            }
        End Function

    End Class
End Namespace
