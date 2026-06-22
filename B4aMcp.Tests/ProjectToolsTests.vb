Imports System.IO
Imports B4aMcp.Tools
Imports Xunit

Public Class ProjectToolsTests

    <Fact>
    Public Sub AddAsset_Copies_And_Registers()
        Dim work = Path.Combine(Path.GetTempPath(), "proj_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(work)
        Try
            ' Fresh copy of the project fixture + a source asset to add.
            Dim proj = Path.Combine(work, "app.b4a")
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.b4a"), proj)
            Dim src = Path.Combine(work, "icon.png")
            File.WriteAllText(src, "not really a png")

            Dim res = ProjectTools.B4aAddAsset(proj, src)
            Assert.Contains("""ok"": true", res)

            ' File copied into Files\ and registered (fixture starts at NumberOfFiles=1).
            Assert.True(File.Exists(Path.Combine(work, "Files", "icon.png")))
            Dim text = File.ReadAllText(proj)
            Assert.Contains("NumberOfFiles=2", text)
            Assert.Contains("=icon.png", text)
        Finally
            Directory.Delete(work, True)
        End Try
    End Sub
End Class
