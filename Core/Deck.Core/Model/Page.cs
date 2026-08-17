namespace Deck.Core.Model;

// Una grilla de botones. No pertenece directo a un Profile: se llega a ella
// siendo la raíz de un Profile, o siendo el destino de un ButtonSlot de tipo
// Folder en otra Page — misma estructura recursiva habilita carpetas anidadas
// sin un modelo aparte.
public class Page
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Columns { get; set; }
}
