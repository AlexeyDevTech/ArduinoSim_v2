// Ds3231Clock: RTClib reads a DS3231 over I2C (A4/A5): the date, the time and the chip's
// temperature every second; its alarm 1 pulls SQW (wired to D2) low once a minute, at :30.
// The clock starts from the computer's time; set a fixed one in the part's properties.

#include <RTClib.h>

RTC_DS3231 rtc;
volatile bool alarm = false;

void onAlarm() { alarm = true; }

void setup() {
  Serial.begin(9600);
  if (!rtc.begin()) {
    Serial.println("No DS3231 found");
    while (true) delay(10);
  }
  if (rtc.lostPower()) {
    rtc.adjust(DateTime(F(__DATE__), F(__TIME__)));
  }
  rtc.disable32K();
  rtc.clearAlarm(1);
  rtc.writeSqwPinMode(DS3231_OFF);
  rtc.setAlarm1(DateTime(2000, 1, 1, 0, 0, 30), DS3231_A1_Second);
  pinMode(2, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(2), onAlarm, FALLING);
}

void loop() {
  DateTime now = rtc.now();
  char text[32];
  snprintf(text, sizeof text, "%04d-%02d-%02d %02d:%02d:%02d", now.year(), now.month(), now.day(), now.hour(), now.minute(), now.second());
  Serial.print(text);
  Serial.print("  ");
  Serial.print(rtc.getTemperature(), 2);
  Serial.println(" C");
  if (alarm) {
    alarm = false;
    rtc.clearAlarm(1);
    Serial.println("Alarm: half past the minute");
  }
  delay(1000);
}