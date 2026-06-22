Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports B4aMcp.Tools
Imports Xunit

Public Class SpriteToolsTests

    Private Shared Function MakeSheet(dir As String, w As Integer, h As Integer) As String
        Directory.CreateDirectory(dir)
        Dim p = Path.Combine(dir, "sheet.png")
        Using bmp = New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.Red)
            End Using
            bmp.Save(p, ImageFormat.Png)
        End Using
        Return p
    End Function

    <Fact>
    Public Sub Slice_Then_Pack_Roundtrips()
        Dim work = Path.Combine(Path.GetTempPath(), "spr_" & Guid.NewGuid().ToString("N"))
        Try
            ' 40x20 sheet, 2x1 grid -> two 20x20 frames
            Dim sheet = MakeSheet(work, 40, 20)
            Dim sliceJson = SpriteTools.B4aSpriteSlice(sheet, 2, 1, Path.Combine(work, "frames"), "f")
            Assert.Contains("""count"": 2", sliceJson)
            Assert.Contains("""frameWidth"": 20", sliceJson)
            Assert.Equal(2, Directory.GetFiles(Path.Combine(work, "frames"), "*.png").Length)

            ' Pack the frames back into an atlas + metadata sidecar
            Dim atlas = Path.Combine(work, "atlas.png")
            Dim packJson = SpriteTools.B4aSpritePack(Path.Combine(work, "frames", "*.png"), atlas, 0)
            Assert.Contains("""frameCount"": 2", packJson)
            Assert.True(File.Exists(atlas), "atlas not written")
            Assert.True(File.Exists(atlas & ".json"), "metadata sidecar not written")
        Finally
            Directory.Delete(work, True)
        End Try
    End Sub

    <Fact>
    Public Sub Slice_Rejects_Grid_Larger_Than_Sheet()
        Dim work = Path.Combine(Path.GetTempPath(), "spr_" & Guid.NewGuid().ToString("N"))
        Try
            Dim sheet = MakeSheet(work, 4, 4)
            Dim res = SpriteTools.B4aSpriteSlice(sheet, 8, 8, work, "f")
            Assert.StartsWith("Error", res)
        Finally
            Directory.Delete(work, True)
        End Try
    End Sub
End Class
