// Ds18b20: two DS18B20 thermometers on one 1-Wire bus (D2, 4.7 kOhm pull-up to 5V), read with
// the OneWire and DallasTemperature libraries. Each sensor's ROM address comes from its part id.

#include <OneWire.h>
#include <DallasTemperature.h>

OneWire oneWire(2);
DallasTemperature sensors(&oneWire);

void printAddress(const DeviceAddress address) {
  for (int i = 0; i < 8; i++) {
    if (address[i] < 16) {
      Serial.print('0');
    }
    Serial.print(address[i], HEX);
  }
}

void setup() {
  Serial.begin(9600);
  sensors.begin();
  Serial.print("Found ");
  Serial.print(sensors.getDeviceCount());
  Serial.println(" sensor(s)");
  for (int i = 0; i < sensors.getDeviceCount(); i++) {
    DeviceAddress address;
    if (sensors.getAddress(address, i)) {
      Serial.print("Sensor ");
      Serial.print(i);
      Serial.print(": ");
      printAddress(address);
      Serial.println();
    }
  }
}

void loop() {
  sensors.requestTemperatures();  // both convert at once; 750 ms at 12 bits
  for (int i = 0; i < sensors.getDeviceCount(); i++) {
    Serial.print("T");
    Serial.print(i);
    Serial.print(" = ");
    Serial.print(sensors.getTempCByIndex(i), 2);
    Serial.print(" C  ");
  }
  Serial.println();
  delay(1000);
}