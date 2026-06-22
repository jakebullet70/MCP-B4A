Imports System.IO
Imports B4aMcp.Utils
Imports Xunit

Public Class B4aParserTests
    Private Shared ReadOnly Fixture As String = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.b4a")

    <Fact>
    Public Sub Parses_Project_Attributes()
        Dim p = B4aParser.Parse(Fixture)
        Assert.Equal("Test App", p.AppLabel)
        Assert.Equal("b4a.test", p.PackageName)
        Assert.Equal("3", p.VersionCode)
        Assert.Equal("1.2.0", p.VersionName)
    End Sub

    <Fact>
    Public Sub Parses_Libraries()
        Dim p = B4aParser.Parse(Fixture)
        Assert.Equal(2, p.Libraries.Count)
        Assert.Contains("core", p.Libraries)
        Assert.Contains("json", p.Libraries)
    End Sub

    <Fact>
    Public Sub Uses_NumberOfModules_Not_NumberOfFiles()
        ' Regression: fixture has NumberOfModules=2 but NumberOfFiles=1.
        ' The parser must use NumberOfModules and read both modules.
        Dim p = B4aParser.Parse(Fixture)
        Assert.Equal(2, p.Modules.Count)
    End Sub

    <Fact>
    Public Sub Extracts_Manifest_Block()
        Dim p = B4aParser.Parse(Fixture)
        Assert.Contains("uses-permission", p.ManifestBlock)
    End Sub
End Class
