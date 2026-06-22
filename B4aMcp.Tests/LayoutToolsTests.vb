Imports System.IO
Imports B4aMcp.Tools
Imports B4aMcp.Utils
Imports Newtonsoft.Json.Linq
Imports Xunit

Public Class LayoutToolsTests
    Private Shared ReadOnly FixtureBal As String = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.bal")

    Private Shared Function FreshCopy() As String
        Dim tmp = Path.Combine(Path.GetTempPath(), "lay_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp)
        Dim dest = Path.Combine(tmp, "work.bal")
        File.Copy(FixtureBal, dest)
        Return dest
    End Function

    <Theory>
    <InlineData("label", "Label")>
    <InlineData("button", "Button")>
    <InlineData("edittext", "EditText")>
    <InlineData("panel", "Panel")>
    Public Sub AddView_Adds_Valid_View_That_Roundtrips(viewType As String, designerType As String)
        Dim balPath = FreshCopy()
        Try
            Dim res = LayoutTools.B4aLayoutAddView(balPath, viewType, "vTest", "Activity", 5, 5, 80, 40, "Hi")
            Assert.StartsWith("OK", res)

            ' Read back: the new view and its header must be present, and it must roundtrip.
            Dim conv As New BalConverter(False)
            Dim jo = JObject.Parse(conv.ConvertBalToJson(Path.GetDirectoryName(balPath), "work.bal"))
            Dim kids = DirectCast(jo("Data")(":kids"), JObject)
            Assert.Contains(kids.Properties(), Function(p) DirectCast(p.Value, JObject)("name").ToString() = "vTest")
            Dim headers = DirectCast(jo("LayoutHeader")("ControlsHeaders"), JArray)
            Assert.Contains(headers, Function(h) h("Name").ToString() = "vTest" AndAlso h("DesignerType").ToString() = designerType)
        Finally
            Directory.Delete(Path.GetDirectoryName(balPath), True)
        End Try
    End Sub

    <Fact>
    Public Sub AddView_Rejects_Duplicate_Name()
        Dim balPath = FreshCopy()
        Try
            LayoutTools.B4aLayoutAddView(balPath, "label", "dupe")
            Dim res = LayoutTools.B4aLayoutAddView(balPath, "label", "dupe")
            Assert.Contains("already exists", res)
        Finally
            Directory.Delete(Path.GetDirectoryName(balPath), True)
        End Try
    End Sub

    <Fact>
    Public Sub DiffLayout_Detects_Added_View()
        Dim a = FreshCopy()
        Dim b = FreshCopy()
        Try
            LayoutTools.B4aLayoutAddView(b, "button", "btnNew")
            Dim diff = LayoutTools.B4aDiffLayout(a, b)
            Assert.Contains("btnNew", diff)
            Assert.Contains("addedViews", diff)
        Finally
            Directory.Delete(Path.GetDirectoryName(a), True)
            Directory.Delete(Path.GetDirectoryName(b), True)
        End Try
    End Sub
End Class
