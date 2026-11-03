// ESP8266 Wi-Fi: scan, connect with an event handler, and fetch a page with ESP8266HTTPClient.
// The radio is simulated (by default one open network, "ArduSim"); HTTP goes over this
// computer's real network. Without a network the request fails cleanly.

#include <ESP8266WiFi.h>
#include <ESP8266HTTPClient.h>

WiFiEventHandler gotIp;

void setup() {
  Serial.begin(115200);
  WiFi.mode(WIFI_STA);
  int n = WiFi.scanNetworks();
  Serial.printf("%d network(s) found\n", n);
  for (int i = 0; i < n; i++) {
    Serial.printf("  %s (%d dBm, %s)\n", WiFi.SSID(i).c_str(), WiFi.RSSI(i), WiFi.encryptionType(i) == ENC_TYPE_NONE ? "open" : "secured");
  }

  gotIp = WiFi.onStationModeGotIP([](const WiFiEventStationModeGotIP& event) {
    Serial.print("Got IP ");
    Serial.println(event.ip);
  });
  WiFi.begin("ArduSim", "");
  while (WiFi.status() != WL_CONNECTED) {
    delay(250);
  }

  WiFiClient client;
  HTTPClient http;
  if (http.begin(client, "http://example.com/")) {
    int code = http.GET();
    if (code > 0) {
      Serial.printf("HTTP %d, %u bytes\n", code, http.getString().length());
    } else {
      Serial.printf("HTTP failed: %s\n", http.errorToString(code).c_str());
    }
    http.end();
  }
}

void loop() {}