using System.Reflection;
using System.Runtime.CompilerServices;
using DXSuite.Director.Pages.Messages;

namespace DXSuite.Director.Tests;

public class MessagesPageHandlerTests
{
    // An async void handler hides its completion and exceptions from Blazor: a failed save or delete
    // is lost instead of surfacing, and the renderer cannot wait for the handler before re-rendering.
    // Compiled lambdas live in nested closure types, so those are scanned too.
    [Fact]
    public void Messages_pages_have_no_async_void_methods()
    {
        var messagesNamespace = typeof(MessagePage).Namespace;
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static |
                                      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var asyncVoid = typeof(MessagePage).Assembly.GetTypes()
            .Where(t => t.Namespace == messagesNamespace)
            .SelectMany(t => t.GetMethods(declared))
            .Where(m => m.ReturnType == typeof(void) && m.GetCustomAttribute<AsyncStateMachineAttribute>() is not null)
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .OrderBy(name => name)
            .ToList();

        Assert.True(asyncVoid.Count == 0, "async void methods: " + string.Join(", ", asyncVoid));
    }
}
