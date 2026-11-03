#include <WiFi.h>
#include <HTTPClient.h>

WiFiServer server(47821);

void serve(void*) {
  for (;;) {
    WiFiClient c = server.accept();
    if (c) {
      String line = c.readStringUntil('\n');
      while (c.connected()) {
        String h = c.readStringUntil('\n');
        if (h == "\r" || h.length() == 0) break;
      }
      c.print("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello");
      c.stop();
      line.trim();
      Serial.println("served " + line);
    }
    vTaskDelay(5);
  }
}

void setup() {
  Serial.begin(115200);
  int n = WiFi.scanNetworks();
  Serial.printf("%d networks, first %s\n", n, WiFi.SSID(0).c_str());
  WiFi.begin("ArduSim");
  while (WiFi.status() != WL_CONNECTED) delay(100);
  Serial.print("IP ");
  Serial.println(WiFi.localIP());
  server.begin();
  xTaskCreate(serve, "serve", 4096, nullptr, 1, nullptr);
  HTTPClient http;
  http.begin("http://127.0.0.1:47821/hi");
  int code = http.GET();
  Serial.printf("code %d body %s at %lu ms\n", code, http.getString().c_str(), millis());
  http.end();
}

void loop() { delay(1000); }