Imports B4aMcp.Utils
Imports Xunit

Public Class BasAnalyzerTests
    Private Const Src As String =
        "Sub Class_Globals" & vbCrLf &
        "    Private mName As String" & vbCrLf &
        "End Sub" & vbCrLf &
        "#Region Helpers" & vbCrLf &
        "Public Sub Add(a As Int, b As Int) As Int" & vbCrLf &
        "    Return a + b" & vbCrLf &
        "End Sub" & vbCrLf &
        "#End Region" & vbCrLf &
        "Type Point(x As Int, y As Int)" & vbCrLf

    <Fact>
    Public Sub Finds_Subs_With_Signature()
        Dim o = BasAnalyzer.Outline(Src)
        Assert.Equal(2, o.Subs.Count)
        Dim add = o.Subs.First(Function(s) s.Name = "Add")
        Assert.Equal("Int", add.ReturnType)
        Assert.Equal("public", add.Visibility)
        Assert.Equal("a As Int, b As Int", add.Params)
        Assert.Equal(5, add.StartLine)
        Assert.Equal(7, add.EndLine)
    End Sub

    <Fact>
    Public Sub Finds_Type_Region_And_Globals()
        Dim o = BasAnalyzer.Outline(Src)
        Assert.Single(o.Types)
        Assert.Single(o.Regions)
        Assert.Single(o.Globals)   ' mName from Class_Globals
    End Sub

    <Fact>
    Public Sub FindReferenceLines_Is_WholeWord_And_CaseInsensitive()
        Dim lines = BasAnalyzer.FindReferenceLines(Src, "ADD")
        Assert.Contains(5, lines)             ' "Public Sub Add" is line 5
        Assert.DoesNotContain(2, lines)       ' "mName" must not match "Add"
    End Sub
End Class
