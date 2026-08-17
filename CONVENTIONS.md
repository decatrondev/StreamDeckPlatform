# Convenciones de código

Reglas mínimas para mantener el código consistente entre los dos. El resto lo
cubre `.editorconfig` (auto-aplicado por el IDE).

## Estructura

- **Ningún plugin referencia a otro plugin.** Todo lo que se comparte pasa por
  `Deck.Core` o `Deck.SDK` (regla dura, ver `README.md` sección de arquitectura).
- Un proyecto nuevo bajo `Plugins/` no se crea hasta que le toque su fase (ver
  roadmap) — no se scaffoldea plugins "por las dudas".
- El modelo de datos (`Core/Deck.Core/Model/`) es la única fuente de verdad de
  las entidades. Si un plugin necesita datos propios, van en su propio proyecto,
  nunca mezclados en `Deck.Core`.

## C#

- `namespace` con sintaxis de una línea (`namespace Deck.Core.Model;`), no bloque.
- Comentarios solo cuando explican el *por qué*, no el *qué* — si el nombre ya
  lo dice, no hace falta comentario.
- Nulabilidad explícita: si algo puede no tener valor, se marca `?` (ver
  `ButtonSlot.TargetPageId`), no se asume.
- `Guid` para IDs de entidades del dominio (no autoincrementales) — facilita
  generar IDs del lado del cliente antes de persistir.

## Commits

- Mensajes en español, describiendo el *por qué* del cambio, no el *qué* (el
  diff ya lo dice).
- Un commit por unidad de cambio coherente — no mezclar plugin + UI + docs en
  el mismo commit salvo que sean realmente inseparables.
