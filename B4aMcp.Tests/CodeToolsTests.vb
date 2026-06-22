Imports System.IO
Imports B4aMcp.Tools
Imports Xunit

Public Class CodeToolsTests

    Private Shared Function WriteTempBas(content As String) As String
        Dim tmp = Path.Combine(Path.GetTempPath(), "lint_" & Guid.NewGuid().ToString("N") & ".bas")
        File.WriteAllText(tmp, content)
        Return tmp
    End Function

    <Fact>
    Public Sub Lint_Flags_Local_Shadowing_Global()
        Dim src = "Sub Process_Globals" & vbCrLf &
                  "    Public Counter As Int" & vbCrLf &
                  "End Sub" & vbCrLf &
                  "Sub Foo" & vbCrLf &
                  "    Dim counter As Int" & vbCrLf &   ' different case — still the same global in B4A
                  "End Sub" & vbCrLf
        Dim tmp = WriteTempBas(src)
        Try
            Dim json = CodeTools.B4aLint(tmp)
            Assert.Contains("local-shadows-global", json)
        Finally
            File.Delete(tmp)
        End Try
    End Sub

    <Fact>
    Public Sub Lint_Flags_Reserved_Sub_Name()
        Dim tmp = WriteTempBas("Sub Rnd" & vbCrLf & "End Sub" & vbCrLf)
        Try
            Dim json = CodeTools.B4aLint(tmp)
            Assert.Contains("reserved-name", json)
        Finally
            File.Delete(tmp)
        End Try
    End Sub

    <Fact>
    Public Sub Lint_Clean_Module_Has_No_Findings()
        Dim tmp = WriteTempBas("Sub Process_Globals" & vbCrLf & "End Sub" & vbCrLf &
                               "Public Sub Hello As String" & vbCrLf & "    Return ""hi""" & vbCrLf & "End Sub" & vbCrLf)
        Try
            Dim json = CodeTools.B4aLint(tmp)
            Assert.Contains("""totalFindings"": 0", json)
        Finally
            File.Delete(tmp)
        End Try
    End Sub

End Class
