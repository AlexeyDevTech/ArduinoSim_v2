// IrRemote: IRremote decodes the remote's NEC frames from an IR receiver on D11.
// Click the remote's keys; holding one sends repeats. The play key toggles the LED on D13.

#include <IRremote.hpp>

const int receivePin = 11;

const char* keyName(uint16_t command) {
  switch (command) {
    case 0x45: return "CH-";
    case 0x46: return "CH";
    case 0x47: return "CH+";
    case 0x44: return "PREV";
    case 0x40: return "NEXT";
    case 0x43: return "PLAY/PAUSE";
    case 0x07: return "VOL-";
    case 0x15: return "VOL+";
    case 0x09: return "EQ";
    case 0x16: return "0";
    case 0x19: return "100+";
    case 0x0D: return "200+";
    case 0x0C: return "1";
    case 0x18: return "2";
    case 0x5E: return "3";
    case 0x08: return "4";
    case 0x1C: return "5";
    case 0x5A: return "6";
    case 0x42: return "7";
    case 0x52: return "8";
    case 0x4A: return "9";
    default: return "?";
  }
}

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
  Serial.begin(9600);
  IrReceiver.begin(receivePin, DISABLE_LED_FEEDBACK);
  Serial.println("Press a key on the remote.");
}

void loop() {
  if (!IrReceiver.decode()) {
    return;
  }

  IRData& data = IrReceiver.decodedIRData;
  if (data.flags & IRDATA_FLAGS_IS_REPEAT) {
    Serial.println("(repeat)");
  } else if (data.protocol == NEC) {
    Serial.print("NEC address 0x");
    Serial.print(data.address, HEX);
    Serial.print(" command 0x");
    Serial.print(data.command, HEX);
    Serial.print(": ");
    Serial.println(keyName(data.command));
    if (data.command == 0x43) {
      digitalWrite(LED_BUILTIN, !digitalRead(LED_BUILTIN));
    }
  } else {
    IrReceiver.printIRResultShort(&Serial);
  }
  IrReceiver.resume();
}