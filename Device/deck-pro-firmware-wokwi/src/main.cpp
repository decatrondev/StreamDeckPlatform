#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ILI9341.h>

#define NUM_ROWS 3
#define NUM_COLS 5
#define NUM_KEYS (NUM_ROWS * NUM_COLS)

// Bus SPI compartido por las 15 pantallas — cada una tiene su propio CS.
const uint8_t TFT_MOSI = 11;
const uint8_t TFT_SCK  = 12;
const uint8_t TFT_DC   = 13;
const uint8_t TFT_RST  = 14; // reset compartido entre las 15

const uint8_t TFT_CS[NUM_KEYS] = {
  1, 2, 4, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18, 19, 20
};

// Matriz de botones 3x5 (15 teclas, 8 pines en vez de 15)
const uint8_t ROW_PINS[NUM_ROWS] = {21, 35, 36};
const uint8_t COL_PINS[NUM_COLS] = {37, 39, 40, 41, 42};

Adafruit_ILI9341* displays[NUM_KEYS];
bool keyState[NUM_KEYS] = {false};

uint16_t colorFor(int idx) {
  static const uint16_t palette[] = {
    ILI9341_RED, ILI9341_GREEN, ILI9341_BLUE, ILI9341_YELLOW, ILI9341_CYAN,
    ILI9341_MAGENTA, ILI9341_ORANGE, ILI9341_WHITE, ILI9341_RED, ILI9341_GREEN,
    ILI9341_BLUE, ILI9341_YELLOW, ILI9341_CYAN, ILI9341_MAGENTA, ILI9341_ORANGE
  };
  return palette[idx % NUM_KEYS];
}

void drawKey(int idx, bool pressed) {
  Adafruit_ILI9341* d = displays[idx];
  uint16_t bg = pressed ? ILI9341_BLACK : colorFor(idx);
  uint16_t fg = pressed ? colorFor(idx) : ILI9341_BLACK;
  d->fillScreen(bg);
  d->setTextColor(fg);
  d->setTextSize(4);
  d->setCursor(90, 140);
  d->print(idx);
}

void setup() {
  Serial.begin(115200);
  SPI.begin(TFT_SCK, -1, TFT_MOSI, -1);

  for (int i = 0; i < NUM_KEYS; i++) {
    pinMode(TFT_CS[i], OUTPUT);
    digitalWrite(TFT_CS[i], HIGH);
    displays[i] = new Adafruit_ILI9341(&SPI, TFT_DC, TFT_CS[i], TFT_RST);
    displays[i]->begin();
    displays[i]->setRotation(1);
    drawKey(i, false);
  }

  for (int r = 0; r < NUM_ROWS; r++) {
    pinMode(ROW_PINS[r], OUTPUT);
    digitalWrite(ROW_PINS[r], HIGH);
  }
  for (int c = 0; c < NUM_COLS; c++) {
    pinMode(COL_PINS[c], INPUT_PULLUP);
  }

  Serial.println("READY");
}

// Protocolo placeholder por serial USB: "KEY:<indice 0-14>:DOWN" / "...:UP"
// Todavia sin cerrar con el resto del proyecto (ver 01-plan.md), pero deja
// algo concreto para probar la comunicacion end to end.
void loop() {
  for (int r = 0; r < NUM_ROWS; r++) {
    digitalWrite(ROW_PINS[r], LOW);
    delayMicroseconds(20);
    for (int c = 0; c < NUM_COLS; c++) {
      int idx = r * NUM_COLS + c;
      bool pressed = digitalRead(COL_PINS[c]) == LOW;
      if (pressed != keyState[idx]) {
        keyState[idx] = pressed;
        Serial.printf("KEY:%d:%s\n", idx, pressed ? "DOWN" : "UP");
        drawKey(idx, pressed);
      }
    }
    digitalWrite(ROW_PINS[r], HIGH);
  }
  delay(10);
}
