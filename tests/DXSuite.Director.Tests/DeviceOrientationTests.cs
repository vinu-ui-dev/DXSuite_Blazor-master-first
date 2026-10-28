using DXSuite.Director.Filters;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace DXSuite.Director.Tests;

public class DeviceOrientationTests
{
    // Portrait is rot90 because the bench showed rot90 puts the page top on the output's left
    // edge, which is where it lands on a panel turned clockwise.
    [Theory]
    [InlineData("Landscape", "identity")]
    [InlineData("Portrait", "rot90")]
    [InlineData("PortraitFlipped", "rot270")]
    [InlineData("LandscapeFlipped", "rot180")]
    public void Each_orientation_maps_to_its_widget_transform(string orientation, string transform)
        => Assert.Equal(transform, DeviceOrientation.ToWidgetTransform(orientation));

    [Theory]
    [InlineData("portrait")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_value_leaves_the_panel_upright(string? orientation)
        => Assert.Equal("identity", DeviceOrientation.ToWidgetTransform(orientation));

    [Fact]
    public void Every_listed_orientation_has_its_own_transform()
        => Assert.Equal(
            DeviceOrientation.All.Count,
            DeviceOrientation.All.Select(DeviceOrientation.ToWidgetTransform).Distinct().Count());

    // The filter is what reaches the unassigned-device branch and the NotModified path, which
    // hand-built response literals would miss.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_filter_stamps_the_stashed_transform(bool notModified)
    {
        var response = new PlayerContentResponse { NotModified = notModified };
        var context = ResultContext(response, stashed: "rot90");

        await new PlayerOrientationFilter().OnResultExecutionAsync(context, Next(context));

        Assert.Equal("rot90", response.Orientation);
    }

    [Fact]
    public async Task Without_a_device_row_nothing_is_stamped()
    {
        var response = new PlayerContentResponse { Status = "Device not registered" };
        var context = ResultContext(response, stashed: null);

        await new PlayerOrientationFilter().OnResultExecutionAsync(context, Next(context));

        Assert.Null(response.Orientation);
    }

    private static ResultExecutingContext ResultContext(PlayerContentResponse response, string? stashed)
    {
        var http = new DefaultHttpContext();
        if (stashed != null)
            http.Items[PlayerOrientationFilter.ContextItemKey] = stashed;

        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(action, [], new OkObjectResult(response), controller: new object());
    }

    private static ResultExecutionDelegate Next(ResultExecutingContext context)
        => () => Task.FromResult(new ResultExecutedContext(context, context.Filters, context.Result, context.Controller));
}
