namespace MobileDeck.Core;

// Pila de páginas visitadas — misma idea que el pageStack de Web Deck
// (App.tsx), separada acá en una clase propia porque en Blazor conviene
// tener el estado de navegación testeable sin depender de un componente vivo.
public sealed class DeckNavigationStack
{
    private readonly List<PageDto> _stack = [];

    public PageDto? Current => _stack.Count > 0 ? _stack[^1] : null;

    public bool CanGoBack => _stack.Count > 1;

    public void Reset(PageDto rootPage)
    {
        _stack.Clear();
        _stack.Add(rootPage);
    }

    public void Push(PageDto page) => _stack.Add(page);

    public void Pop()
    {
        if (CanGoBack) _stack.RemoveAt(_stack.Count - 1);
    }
}
