// BuzzerMelody: tone() on a piezo buzzer on D8 plays a scale, up and down. With sound on (the speaker
// button in the toolbar) you hear it; the buzzer shows the frequency either way. At other speeds the
// notes keep their pitch and get shorter or longer.

const int notes[] = {262, 294, 330, 349, 392, 440, 494, 523};
const int count = sizeof(notes) / sizeof(notes[0]);

void setup() {
}

void loop() {
  for (int i = 0; i < count; i++) {
    tone(8, notes[i], 180);
    delay(220);
  }
  for (int i = count - 2; i > 0; i--) {
    tone(8, notes[i], 180);
    delay(220);
  }
}