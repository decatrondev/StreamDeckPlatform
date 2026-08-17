namespace Deck.Core.Model;

// Un perfil completo (ej. "Streaming", "Edición"). RootPageId apunta a la Page
// que se muestra al activar el perfil.
public class Profile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RootPageId { get; set; }
}
