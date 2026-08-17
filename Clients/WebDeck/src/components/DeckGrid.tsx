import type { Page } from "../lib/types"

interface Props {
  page: Page
  pressedKey: string | null
  failedKey: string | null
  onPress: (row: number, column: number) => void
}

export function DeckGrid({ page, pressedKey, failedKey, onPress }: Props) {
  const cells = Array.from({ length: page.rows }, (_, row) =>
    Array.from({ length: page.columns }, (_, column) => {
      const slot = page.buttons.find((b) => b.row === row && b.column === column)
      return { row, column, slot }
    }),
  )

  return (
    <div
      className="grid flex-1 place-content-center gap-3 p-6 3xl:gap-4 3xl:p-10"
      style={{ gridTemplateColumns: `repeat(${page.columns}, minmax(0, 1fr))` }}
    >
      {cells.flat().map(({ row, column, slot }) => {
        const key = `${row}-${column}`
        const isPressed = pressedKey === key
        const isFailed = failedKey === key

        // Un solo estado de borde/fondo por vez — nunca una clase base
        // (border-line) junto con una condicional (border-danger) para la
        // misma propiedad: Tailwind decide qué gana por el orden en que
        // genera las utilidades, no por el orden en el string de clases, así
        // que mezclarlas deja el resultado librado al azar (bug real, visto
        // a mano: border-danger perdía contra border-line).
        const stateClasses = !slot
          ? "border-dashed border-line/60 bg-transparent"
          : isFailed
            ? "border-danger bg-danger/10"
            : isPressed
              ? "border-accent bg-accent/10"
              : "border-line bg-surface-raised text-ink hover:border-accent/50"

        return (
          <button
            key={key}
            disabled={!slot}
            onClick={() => onPress(row, column)}
            className={`deck-key flex aspect-square w-20 flex-col items-center justify-center rounded-xl border text-center 3xl:w-28 ${stateClasses}`}
          >
            {slot?.type === "Folder" && (
              <svg viewBox="0 0 20 20" className="mb-1 size-5 fill-signal">
                <path d="M2 5.5A1.5 1.5 0 0 1 3.5 4h4.379a1.5 1.5 0 0 1 1.06.44l1.122 1.12A1.5 1.5 0 0 0 11.12 6H16.5A1.5 1.5 0 0 1 18 7.5v7A1.5 1.5 0 0 1 16.5 16h-13A1.5 1.5 0 0 1 2 14.5v-9Z" />
              </svg>
            )}
            {slot?.label && (
              <span className="line-clamp-2 px-1 text-[11px] font-medium leading-tight text-ink-muted 3xl:text-xs">
                {slot.label}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
