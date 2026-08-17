namespace Deck.Core.Model;

public enum ButtonSlotType
{
    Action,
    Folder
}

// Una posición (fila/columna) dentro de una Page. Es de tipo Action (dispara
// su lista ordenada de ActionStep) o Folder (navega a otra Page) — nunca las
// dos cosas a la vez.
public class ButtonSlot
{
    public Guid Id { get; set; }
    public Guid PageId { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public ButtonSlotType Type { get; set; }

    // Solo aplica si Type == Folder.
    public Guid? TargetPageId { get; set; }

    public string? Label { get; set; }
    public string? IconRef { get; set; }
}
