# Deck Pro — prototipo Wokwi (circuito + firmware completo, sin carcasa)

ESP32-S3 DevKitC-1 + 15 pantallas SPI + matriz de botones 3x5 (15 teclas
con 8 pines, no 15). Todo el circuito vive en `diagram.json`, el firmware
en `src/main.cpp`.

## Nota sobre las pantallas

Wokwi todavía no tiene un componente nativo para GC9A01 (redonda) ni
ST7789 — el único display SPI a color que soporta oficialmente es el
`wokwi-ili9341` (240x320), así que el circuito usa 15 de esas como
representación visual. La lógica de pines/protocolo es la misma para
ST7789/GC9A01 en hardware real, solo cambia el driver de la librería.

## Simplificación respecto al plan original

El doc (`01-plan.md`) dejaba anotado un multiplexor (74HC4051/74HC595)
como necesario para manejar 15 líneas de Chip Select. Con el ESP32-S3
(no el ESP32 básico) sobran pines: 15 CS + SPI compartido (MOSI/SCK/DC/RST)
+ matriz de botones (8 pines) entran sin multiplexor externo — así que
ese componente sale de la lista de compras por ahora.

## Cómo abrirlo

**Opción A — wokwi.com (más simple, sin instalar nada):**
1. Ir a wokwi.com → New Project → ESP32-S3.
2. Reemplazar el contenido de `diagram.json` por el de este archivo.
3. Reemplazar el `sketch.ino` por el contenido de `src/main.cpp`.
4. En el editor, abrir "Library Manager" (ícono de libro) y agregar:
   `Adafruit GFX Library` y `Adafruit ILI9341`.
5. Correr la simulación (▶).

**Opción B — VS Code + extensión Wokwi + PlatformIO (local):**
1. Instalar la extensión "Wokwi for VS Code" y "PlatformIO".
2. Abrir esta carpeta en VS Code.
3. `pio run` para compilar (`platformio.ini` ya apunta a
   `esp32-s3-devkitc-1`).
4. F1 → "Wokwi: Start Simulator" (usa `wokwi.toml`).

## Qué hace el firmware

- Inicializa las 15 pantallas (bus SPI compartido, CS individual) y
  dibuja un color + número distinto en cada una.
- Escanea la matriz de botones 3x5.
- Al apretar/soltar una tecla: manda `KEY:<0-14>:DOWN` / `KEY:<0-14>:UP`
  por Serial USB (protocolo placeholder — sigue pendiente cerrarlo del
  todo con el resto del proyecto, ver "Todavía sin resolver" en
  `01-plan.md`) e invierte los colores de esa pantalla como feedback
  visual de que fue detectada.

## Pinout

| Función | Pin(es) ESP32-S3 |
|---|---|
| SPI MOSI | GPIO11 |
| SPI SCK | GPIO12 |
| Display DC (compartido) | GPIO13 |
| Display RST (compartido) | GPIO14 |
| Display CS (uno por tecla, 0-14) | GPIO1, 2, 4, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18, 19, 20 |
| Matriz botones — filas (3) | GPIO21, 35, 36 |
| Matriz botones — columnas (5) | GPIO37, 39, 40, 41, 42 |

Pines evitados a propósito: 0/3/45/46 (strapping, arrancan el boot mode),
43/44 (TX/RX del monitor serial), 38 (LED RGB WS2812 de a bordo).
