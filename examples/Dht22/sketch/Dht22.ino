// Dht22: the Adafruit DHT sensor library reading a DHT22 on D7 every two seconds.
// Change the temperature and humidity in the Properties panel while it runs, or drag the
// sensor up and down (Shift: humidity).

#include <DHT.h>

DHT dht(7, DHT22);

void setup() {
  Serial.begin(9600);
  dht.begin();
}

void loop() {
  delay(2000);
  float humidity = dht.readHumidity();
  float temperature = dht.readTemperature();
  if (isnan(humidity) || isnan(temperature)) {
    Serial.println("Failed to read from the DHT sensor!");
    return;
  }

  Serial.print("Humidity: ");
  Serial.print(humidity, 1);
  Serial.print(" %  Temperature: ");
  Serial.print(temperature, 1);
  Serial.print(" C  Heat index: ");
  Serial.print(dht.computeHeatIndex(temperature, humidity, false), 1);
  Serial.println(" C");
}