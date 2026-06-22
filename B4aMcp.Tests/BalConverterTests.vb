Imports System.IO
Imports B4aMcp.Utils
Imports Newtonsoft.Json.Linq
Imports Xunit

Public Class BalConverterTests
    Private Shared ReadOnly FixturesDir As String = Path.Combine(AppContext.BaseDirectory, "Fixtures")

    <Fact>
    Public Sub Bal_Roundtrips_Losslessly()
        Dim conv As New BalConverter(False)
        Dim json1 = conv.ConvertBalToJson(FixturesDir, "sample.bal")

        Dim tempDir = Path.Combine(Path.GetTempPath(), "baltest_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
        Try
            Dim outPath = Path.Combine(tempDir, "out.bal")
            Using fs = File.Create(outPath)
                conv.ConvertJsonToBalInMemory(JObject.Parse(json1), fs)
            End Using
            Dim json2 = conv.ConvertBalToJson(tempDir, "out.bal")
            Assert.True(JToken.DeepEquals(JObject.Parse(json1), JObject.Parse(json2)),
                        "Layout JSON changed after a write→read roundtrip")
        Finally
            Directory.Delete(tempDir, True)
        End Try
    End Sub

    <Fact>
    Public Sub Bal_Has_Expected_Structure()
        Dim conv As New BalConverter(False)
        Dim jo = JObject.Parse(conv.ConvertBalToJson(FixturesDir, "sample.bal"))
        Assert.NotNull(jo("LayoutHeader"))
        Assert.NotNull(jo("Variants"))
        Assert.NotNull(jo("Data"))
        Assert.Equal("Activity", jo("Data")("name").ToString())
    End Sub
End Class
