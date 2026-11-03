// ESP32 Tasks: two FreeRTOS tasks beside loop(). One blinks the LED on GPIO 4, the other counts and
// sends the count through a queue; loop() prints what arrives and blinks the on-board LED.
// Tasks are cooperative in the simulator: each one gives up the CPU in vTaskDelay or a queue wait.

const int ledPin = 4;
QueueHandle_t counts;

void blinkTask(void*) {
  pinMode(ledPin, OUTPUT);
  for (;;) {
    digitalWrite(ledPin, !digitalRead(ledPin));
    vTaskDelay(pdMS_TO_TICKS(250));
  }
}

void countTask(void*) {
  for (int n = 1;; n++) {
    xQueueSend(counts, &n, portMAX_DELAY);
    vTaskDelay(pdMS_TO_TICKS(1000));
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(LED_BUILTIN, OUTPUT);
  counts = xQueueCreate(4, sizeof(int));
  xTaskCreate(blinkTask, "blink", 2048, nullptr, 1, nullptr);
  xTaskCreate(countTask, "count", 2048, nullptr, 1, nullptr);
}

void loop() {
  int n;
  if (xQueueReceive(counts, &n, pdMS_TO_TICKS(100)) == pdTRUE) {
    Serial.printf("count %d from task %s\n", n, "count");
    digitalWrite(LED_BUILTIN, n % 2);
  }
}