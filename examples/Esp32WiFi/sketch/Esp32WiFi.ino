// ESP32 Wi-Fi: scan, connect, and fetch a page. The radio is simulated: the board sees the
// networks listed in the project (by default one open network, "ArduSim"); once connected,
// HTTP goes over this computer's real network. Without a network the request fails cleanly.

#include <WiFi.h>
#include <HTTPClient.h>

const char* ssid = "ArduSim";
const char* password = "";

void setup() {
  Serial.begin(115200);
  WiFi.mode(WIFI_STA);
  int n = WiFi.scanNetworks();
  Serial.printf("%d network(s) found\n", n);
  for (int i = 0; i < n; i++) {
    Serial.printf("  %s (%d dBm)\n", WiFi.SSID(i).c_str(), WiFi.RSSI(i));
  }

  WiFi.begin(ssid, password);
  Serial.print("Connecting");
  while (WiFi.status() != WL_CONNECTED) {
    delay(250);
    Serial.print(".");
  }
  Serial.println();
  Serial.print("Connected, IP ");
  Serial.println(WiFi.localIP());

  HTTPClient http;
  http.begin("http://example.com/");
  int code = http.GET();
  if (code > 0) {
    String body = http.getString();
    Serial.printf("HTTP %d, %u bytes\n", code, body.length());
  } else {
    Serial.printf("HTTP failed: %s\n", HTTPClient::errorToString(code).c_str());
  }
  http.end();
}

void loop() {}