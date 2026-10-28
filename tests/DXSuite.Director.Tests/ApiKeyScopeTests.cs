using DXSuite.Director.Authorization;
using DXSuite.Director.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// No UpdateController action checked a scope, so any active admin key of any scope could upload a
/// version, create a deployment, arm it and delete versions. Publishing introduces new code to the
/// fleet; deploying chooses who receives code that already exists, and they are different rights.
/// </summary>
public class ApiKeyScopeTests
{
    private sealed class TestController : ControllerBase;

    private static TestController ControllerWithPermissions(string? permissions)
    {
        var context = new DefaultHttpContext();
        context.Items["ApiKeyPermissions"] = permissions;

        return new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    [Fact]
    public void AKeyHoldingTheScopeIsAllowed()
    {
        var controller = ControllerWithPermissions(ApiKeyScopes.UpdatePublish);

        Assert.Null(controller.CheckScope(ApiKeyScopes.UpdatePublish));
    }

    [Fact]
    public void ADeployKeyCannotPublish()
    {
        // The split is the point: staging a rollout must not carry the right to introduce a binary.
        var controller = ControllerWithPermissions(ApiKeyScopes.UpdateDeploy);

        var denied = controller.CheckScope(ApiKeyScopes.UpdatePublish);

        Assert.NotNull(denied);
        Assert.Equal(403, denied.StatusCode);
    }

    [Fact]
    public void AWildcardKeyKeepsWorking()
    {
        // Existing keys must not break; only keys scoped to something narrower lose an access
        // they were never meant to have.
        var controller = ControllerWithPermissions(ApiKeyScopes.Wildcard);

        Assert.Null(controller.CheckScope(ApiKeyScopes.UpdatePublish));
        Assert.Null(controller.CheckScope(ApiKeyScopes.UpdateDeploy));
    }

    [Fact]
    public void AHeartbeatScopedKeyLosesTheUpdateApi()
    {
        var controller = ControllerWithPermissions(ApiKeyScopes.SignageHeartbeat);

        Assert.NotNull(controller.CheckScope(ApiKeyScopes.UpdatePublish));
        Assert.NotNull(controller.CheckScope(ApiKeyScopes.UpdateDeploy));
    }

    [Fact]
    public void EitherHalfOfTheSplitCanRead()
    {
        // A deploy-only key still has to list the versions it is choosing between.
        var publisher = ControllerWithPermissions(ApiKeyScopes.UpdatePublish);
        var deployer = ControllerWithPermissions(ApiKeyScopes.UpdateDeploy);

        Assert.Null(publisher.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy));
        Assert.Null(deployer.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy));
    }

    [Fact]
    public void AKeyHoldingNeitherCannotRead()
    {
        var controller = ControllerWithPermissions(ApiKeyScopes.SignageRead);

        var denied = controller.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy);

        Assert.NotNull(denied);
        Assert.Equal(403, denied.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AKeyWithNoPermissionsAtAllIsDenied(string? permissions)
    {
        var controller = ControllerWithPermissions(permissions);

        Assert.NotNull(controller.CheckScope(ApiKeyScopes.UpdatePublish));
        Assert.NotNull(controller.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy));
    }
}
