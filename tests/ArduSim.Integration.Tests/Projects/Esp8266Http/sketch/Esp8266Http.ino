// Fixture: an ESP8266 HTTP GET to a server the test runs on 127.0.0.1:TEST_PORT (Esp8266RunTests).
#include <ESP8266WiFi.h>
#include <ESP8266HTTPClient.h>

void setup() {
  Serial.begin(115200);
  WiFi.begin("ArduSim");
  while (WiFi.status() != WL_CONNECTED) delay(50);
  WiFiClient client;
  HTTPClient http;
  http.begin(client, "http://127.0.0.1:TEST_PORT/hello");
  http.addHeader("X-Board", "esp8266");
  int code = http.POST("ping");
  Serial.printf("code %d body %s\n", code, http.getString().c_str());
  http.end();
}

void loop() {}