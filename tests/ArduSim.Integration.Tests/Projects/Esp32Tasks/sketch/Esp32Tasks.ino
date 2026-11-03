void blinker(void*) {
  for (;;) {
    Serial.printf("task at %lu ms\n", millis());
    vTaskDelay(pdMS_TO_TICKS(700));
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(LED_BUILTIN, OUTPUT);
  ledcAttach(4, 5000, 10);
  xTaskCreate(blinker, "blinker", 2048, nullptr, 1, nullptr);
  Serial.printf("ESP32 %s, %u MHz\n", ESP.getChipModel(), getCpuFrequencyMhz());
}

void loop() {
  digitalWrite(LED_BUILTIN, HIGH);
  ledcWrite(4, 256);
  delay(500);
  digitalWrite(LED_BUILTIN, LOW);
  ledcWrite(4, 1023);
  delay(500);
}