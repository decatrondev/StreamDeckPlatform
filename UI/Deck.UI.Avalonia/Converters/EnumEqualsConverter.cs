using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Deck.UI.Avalonia.Converters;

// ObjectConverters.Equal (el de Avalonia) sirve para IsVisible (solo ida),
// pero NO implementa ConvertBack — lo confirmado corriendo
// ObjectConverters.Equal.ConvertBack a mano, tira NotImplementedException.
// Usado en un RadioButton.IsChecked de ida y vuelta (como el selector
// Acción/Carpeta), eso significa que tocar el radio nunca actualiza la
// propiedad real: se ve marcado pero el valor de atrás no cambia. Este
// converter sí soporta las dos direcciones — al pasar a true, devuelve el
// parameter; al pasar a false, DoNothing (no pisa el valor con "nada
// seleccionado", que es lo que corresponde para un grupo de radio buttons).
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
