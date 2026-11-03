// Esp32Sensors: IRremote (timer interrupt), the DHT library and DallasTemperature on the ESP32
// DevKit's FreeRTOS core: an IR receiver on GPIO 15, a DHT22 on GPIO 4, a DS18B20 on GPIO 5.

#include <IRremote.hpp>
#include <DHT.h>
#include <OneWire.h>
#include <DallasTemperature.h>

DHT dht(4, DHT22);
OneWire oneWire(5);
DallasTemperature sensors(&oneWire);

void setup() {
  Serial.begin(115200);
  IrReceiver.begin(15, DISABLE_LED_FEEDBACK);
  dht.begin();
  sensors.begin();
  Serial.printf("ds18b20: %d\n", sensors.getDeviceCount());
}

void loop() {
  static unsigned long last = 0;
  if (IrReceiver.decode()) {
    if (!(IrReceiver.decodedIRData.flags & IRDATA_FLAGS_IS_REPEAT)) {
      Serial.printf("ir: 0x%02X\n", IrReceiver.decodedIRData.command);
    }
    IrReceiver.resume();
  }
  if (millis() - last >= 2500) {
    last = millis();
    sensors.requestTemperatures();
    Serial.printf("dht: %.1f %.1f ds: %.2f\n", dht.readTemperature(), dht.readHumidity(), sensors.getTempCByIndex(0));
  }
  delay(1);
}