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
  1, 2, 4, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18, 39, 40
};

// Matriz de botones 3x5 (15 teclas, 8 pines en vez de 15)
// Pines seguros de ESP32-S3 (evitando 35, 36, 37 del bus Octal Flash/PSRAM)
const uint8_t ROW_PINS[NUM_ROWS] = {21, 38, 45};
const uint8_t COL_PINS[NUM_COLS] = {46, 47, 48, 3, 0};

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
  Serial.begin(115200, SERIAL_8N1, 44, 43);
  delay(1000);
  Serial.println("\r\n--- ESP32-S3 FLOWDECK FIRMWARE STARTING ---");

  // Single hardware reset for all 15 displays sharing RST pin 14
  pinMode(TFT_RST, OUTPUT);
  digitalWrite(TFT_RST, HIGH);
  delay(10);
  digitalWrite(TFT_RST, LOW);
  delay(10);
  digitalWrite(TFT_RST, HIGH);
  delay(150);

  SPI.begin(TFT_SCK, -1, TFT_MOSI, -1);

  for (int i = 0; i < NUM_KEYS; i++) {
    pinMode(TFT_CS[i], OUTPUT);
    digitalWrite(TFT_CS[i], HIGH);
    displays[i] = new Adafruit_ILI9341(&SPI, TFT_DC, TFT_CS[i], -1);
    displays[i]->begin();
    displays[i]->setRotation(1);
    drawKey(i, false);
    Serial.printf("LCD %d READY\r\n", i);
  }

  for (int r = 0; r < NUM_ROWS; r++) {
    pinMode(ROW_PINS[r], INPUT);
  }
  for (int c = 0; c < NUM_COLS; c++) {
    pinMode(COL_PINS[c], INPUT_PULLUP);
  }

  Serial.println("=== SYSTEM READY - WAITING FOR BUTTON EVENTS ===");
  Serial.flush();
}

void loop() {
  for (int r = 0; r < NUM_ROWS; r++) {
    pinMode(ROW_PINS[r], OUTPUT);
    digitalWrite(ROW_PINS[r], LOW);
    delayMicroseconds(20);
    for (int c = 0; c < NUM_COLS; c++) {
      int idx = r * NUM_COLS + c;
      bool pressed = (digitalRead(COL_PINS[c]) == LOW);
      if (pressed != keyState[idx]) {
        keyState[idx] = pressed;
        Serial.printf("KEY:%d:%s\r\n", idx, pressed ? "DOWN" : "UP");
        Serial.flush();
        drawKey(idx, pressed);
      }
    }
    pinMode(ROW_PINS[r], INPUT);
  }
  delay(10);
}
