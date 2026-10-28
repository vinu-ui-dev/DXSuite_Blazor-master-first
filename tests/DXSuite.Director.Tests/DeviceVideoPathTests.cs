using DXSuite.Director.Filters;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace DXSuite.Director.Tests;

public class DeviceVideoPathTests
{
    [Theory]
    [InlineData("Hardware", "HD1025", "hardware")]
    [InlineData("Page", "HD1025", "page")]
    [InlineData("Page", "HD225", "page")]
    [InlineData("Page", "XT1145", "page")]
    public void Each_video_path_maps_to_its_player_value(string videoPath, string model, string expected)
        => Assert.Equal(expected, DeviceVideoPath.ToPlayerValue(videoPath, model));

    // Series 4 ignores hwz="off" from BOS 9.1.151 while the page stays opaque, so page mode there
    // would hide the video; a stored Page must never reach one.
    [Theory]
    [InlineData("LS424")]
    [InlineData("HD1024")]
    [InlineData("XT1144")]
    [InlineData("XD234")]
    public void A_series_4_player_is_always_sent_the_hardware_plane(string model)
    {
        Assert.False(DeviceVideoPath.SupportsPage(model));
        Assert.Equal("hardware", DeviceVideoPath.ToPlayerValue(DeviceVideoPath.Page, model));
    }

    [Theory]
    [InlineData("HD1025")]
    [InlineData("LS445")]
    [InlineData("HD1026")]
    [InlineData(null)]
    [InlineData("")]
    public void Series_5_and_later_and_unknown_models_may_use_page_mode(string? model)
        => Assert.True(DeviceVideoPath.SupportsPage(model));

    [Theory]
    [InlineData("page")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_value_keeps_the_hardware_plane(string? videoPath)
        => Assert.Equal("hardware", DeviceVideoPath.ToPlayerValue(videoPath, "HD1025"));

    // The filter is what reaches the unassigned-device branch and the NotModified path, which
    // hand-built response literals would miss.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_filter_stamps_the_stashed_video_path(bool notModified)
    {
        var response = new PlayerContentResponse { NotModified = notModified };
        var context = ResultContext(response, stashed: "page");

        await new PlayerVideoPathFilter().OnResultExecutionAsync(context, Next(context));

        Assert.Equal("page", response.VideoPath);
    }

    [Fact]
    public async Task Without_a_device_row_nothing_is_stamped()
    {
        var response = new PlayerContentResponse { Status = "Device not registered" };
        var context = ResultContext(response, stashed: null);

        await new PlayerVideoPathFilter().OnResultExecutionAsync(context, Next(context));

        Assert.Null(response.VideoPath);
    }

    private static ResultExecutingContext ResultContext(PlayerContentResponse response, string? stashed)
    {
        var http = new DefaultHttpContext();
        if (stashed != null)
            http.Items[PlayerVideoPathFilter.ContextItemKey] = stashed;

        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(action, [], new OkObjectResult(response), controller: new object());
    }

    private static ResultExecutionDelegate Next(ResultExecutingContext context)
        => () => Task.FromResult(new ResultExecutedContext(context, context.Filters, context.Result, context.Controller));
}
