using MobileDeck.Core;

namespace MobileDeck.Core.Tests;

public class DeckNavigationStackTests
{
    private static PageDto MakePage(string name) => new(Guid.NewGuid(), name, 3, 5, []);

    [Fact]
    public void Reset_SetsCurrentToRootPage_CannotGoBack()
    {
        var stack = new DeckNavigationStack();
        var root = MakePage("Principal");

        stack.Reset(root);

        Assert.Equal(root, stack.Current);
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void Push_MakesNewPageCurrent_AndAllowsGoingBack()
    {
        var stack = new DeckNavigationStack();
        var root = MakePage("Principal");
        var sub = MakePage("Escenas");

        stack.Reset(root);
        stack.Push(sub);

        Assert.Equal(sub, stack.Current);
        Assert.True(stack.CanGoBack);
    }

    [Fact]
    public void Pop_ReturnsToPreviousPage()
    {
        var stack = new DeckNavigationStack();
        var root = MakePage("Principal");
        var sub = MakePage("Escenas");

        stack.Reset(root);
        stack.Push(sub);
        stack.Pop();

        Assert.Equal(root, stack.Current);
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void Pop_AtRoot_DoesNothing()
    {
        var stack = new DeckNavigationStack();
        var root = MakePage("Principal");
        stack.Reset(root);

        stack.Pop();

        Assert.Equal(root, stack.Current);
    }
}
