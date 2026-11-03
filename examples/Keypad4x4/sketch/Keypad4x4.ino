// Keypad4x4: the Keypad library on a 4x4 membrane keypad (rows D9-D6, columns D5-D2).
// Click the keys. Digits and letters build a code, # enters it, * clears it.

#include <Keypad.h>

const byte ROWS = 4;
const byte COLS = 4;
char keys[ROWS][COLS] = {
  {'1', '2', '3', 'A'},
  {'4', '5', '6', 'B'},
  {'7', '8', '9', 'C'},
  {'*', '0', '#', 'D'}
};
byte rowPins[ROWS] = {9, 8, 7, 6};
byte colPins[COLS] = {5, 4, 3, 2};

Keypad keypad = Keypad(makeKeymap(keys), rowPins, colPins, ROWS, COLS);
String code;

void setup() {
  Serial.begin(9600);
  Serial.println("Type a code, then #.");
}

void loop() {
  char key = keypad.getKey();
  if (!key) {
    return;
  }

  Serial.print("Key: ");
  Serial.println(key);
  if (key == '#') {
    Serial.print("Code: ");
    Serial.println(code == "1234" ? "1234 (correct)" : code);
    code = "";
  } else if (key == '*') {
    code = "";
  } else {
    code += key;
  }
}