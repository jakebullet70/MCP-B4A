Imports B4aMcp.Utils
Imports Newtonsoft.Json.Linq
Imports Xunit

Public Class ToolResultTests

    <Fact>
    Public Sub Ok_Wraps_Data_And_Sets_Ok_True()
        Dim o = JObject.Parse(ToolResult.Ok(New With {.value = 42}))
        Assert.True(o("ok").Value(Of Boolean)())
        Assert.Equal(42, o("data")("value").Value(Of Integer)())
        Assert.Null(o("error"))
    End Sub

    <Fact>
    Public Sub Ok_Emits_Warnings_Only_When_Present()
        Assert.Null(JObject.Parse(ToolResult.Ok(New With {.x = 1}))("warnings"))
        Assert.Null(JObject.Parse(ToolResult.Ok(New With {.x = 1}, New String() {}))("warnings"))
        Dim withWarn = JObject.Parse(ToolResult.Ok(New With {.x = 1}, {"heads up"}))
        Assert.Equal("heads up", withWarn("warnings")(0).ToString())
    End Sub

    <Fact>
    Public Sub Ok_Passes_Through_Existing_JToken_Without_Reencoding()
        Dim layout = JObject.Parse("{""LayoutHeader"":{""Version"":5}}")
        Dim o = JObject.Parse(ToolResult.Ok(layout))
        ' data must be the object itself, not a JSON-encoded string
        Assert.Equal(JTokenType.Object, o("data").Type)
        Assert.Equal(5, o("data")("LayoutHeader")("Version").Value(Of Integer)())
    End Sub

    <Fact>
    Public Sub Fail_Sets_Ok_False_And_Error()
        Dim o = JObject.Parse(ToolResult.Fail("boom"))
        Assert.False(o("ok").Value(Of Boolean)())
        Assert.Equal("boom", o("error").ToString())
    End Sub

    <Fact>
    Public Sub Message_Is_Ok_With_Message_Field()
        Dim o = JObject.Parse(ToolResult.Message("done"))
        Assert.True(o("ok").Value(Of Boolean)())
        Assert.Equal("done", o("data")("message").ToString())
    End Sub
End Class
