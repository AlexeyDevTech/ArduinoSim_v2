// Standalone shim test harness: links the shim with a fake host and checks the
// Arduino API behaviour that needs no simulator (String, Print, Stream, port
// registers, random, pgmspace, EEPROM, Wire). Built and run by
// tests/ArduSim.Integration.Tests/ShimHarnessTests.cs; exit code = failed checks.
#include <Arduino.h>
#include <EEPROM.h>
#include <Wire.h>
#include <stdio.h>

#include "sim_abi.h"

namespace {

int g_checks = 0;
int g_failures = 0;

#define CHECK(cond)                                                       \
    do {                                                                  \
        g_checks++;                                                       \
        if (!(cond)) {                                                    \
            g_failures++;                                                 \
            printf("%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #cond); \
        }                                                                 \
    } while (0)

#define CHECK_STR(actual, expected)                                                                   \
    do {                                                                                              \
        g_checks++;                                                                                   \
        char a_[256];                                                                                 \
        snprintf(a_, sizeof a_, "%s", (actual));                                                      \
        const char* e_ = (expected);                                                                  \
        if (strcmp(a_, e_) != 0) {                                                                    \
            g_failures++;                                                                             \
            printf("%s:%d: expected \"%s\" but got \"%s\" (%s)\n", __FILE__, __LINE__, e_, a_, #actual); \
        }                                                                                             \
    } while (0)

// ---- fake host -------------------------------------------------------------

struct Call {
    const char* name;
    int32_t a;
    int32_t b;
};

Call g_calls[512];
int g_call_count = 0;
uint64_t g_now_us = 0;
int32_t g_levels[32];
uint8_t g_eeprom[1024];
int g_warnings = 0;
char g_last_log[256];
const char* g_serial_input = "";
size_t g_serial_pos = 0;
char g_serial_output[1024];
size_t g_serial_output_len = 0;
SimCrashInfo g_crash;

void record(const char* name, int32_t a = 0, int32_t b = 0)
{
    if (g_call_count < 512) {
        g_calls[g_call_count++] = {name, a, b};
    }
}

int count_calls(const char* name)
{
    int n = 0;
    for (int i = 0; i < g_call_count; i++) {
        n += strcmp(g_calls[i].name, name) == 0 ? 1 : 0;
    }
    return n;
}

bool has_call(const char* name, int32_t a, int32_t b)
{
    for (int i = 0; i < g_call_count; i++) {
        if (strcmp(g_calls[i].name, name) == 0 && g_calls[i].a == a && g_calls[i].b == b) {
            return true;
        }
    }
    return false;
}

void h_pin_mode(int32_t pin, int32_t mode) { record("pin_mode", pin, mode); }
void h_digital_write(int32_t pin, int32_t level)
{
    record("digital_write", pin, level);
    g_levels[pin] = level;
}
int32_t h_digital_read(int32_t pin)
{
    record("digital_read", pin);
    return g_levels[pin];
}
int32_t h_analog_read(int32_t pin) { return pin * 10; }
void h_analog_write(int32_t pin, int32_t value) { record("analog_write", pin, value); }
void h_pwm_config(int32_t pin, uint32_t, int32_t bits) { record("pwm_config", pin, bits); }
void h_dac_write(int32_t pin, int32_t value) { record("dac_write", pin, value); }
void h_analog_reference(int32_t) {}
void h_tone(int32_t pin, uint32_t frequency, uint32_t) { record("tone", pin, static_cast<int32_t>(frequency)); }
void h_no_tone(int32_t pin) { record("no_tone", pin); }
uint64_t h_now_us() { return g_now_us += 1; }
void h_delay_us(uint32_t us)
{
    g_now_us += us;
    record("delay_us", static_cast<int32_t>(us));
}
void h_yield() {}
void h_serial_begin(int32_t, uint32_t baud, uint32_t config, int32_t, int32_t) { record("serial_begin", static_cast<int32_t>(baud), static_cast<int32_t>(config)); }
void h_serial_end(int32_t) {}
int32_t h_serial_write(int32_t, const uint8_t* data, int32_t length)
{
    for (int32_t i = 0; i < length && g_serial_output_len + 1 < sizeof g_serial_output; i++) {
        g_serial_output[g_serial_output_len++] = static_cast<char>(data[i]);
    }
    g_serial_output[g_serial_output_len] = 0;
    return length;
}
int32_t h_serial_read(int32_t) { return g_serial_input[g_serial_pos] != 0 ? static_cast<uint8_t>(g_serial_input[g_serial_pos++]) : -1; }
int32_t h_serial_peek(int32_t) { return g_serial_input[g_serial_pos] != 0 ? static_cast<uint8_t>(g_serial_input[g_serial_pos]) : -1; }
int32_t h_serial_available(int32_t) { return static_cast<int32_t>(strlen(g_serial_input + g_serial_pos)); }
int32_t h_serial_available_for_write(int32_t) { return 64; }
void h_serial_flush(int32_t) {}
// One fake I2C device at 0x27: it ACKs, NACKs a data byte of 0xEE, and answers reads with 0x10, 0x11, ...
uint8_t g_i2c_next_read = 0x10;
void h_i2c_begin(int32_t, int32_t, int32_t) { record("i2c_begin"); }
void h_i2c_end(int32_t) {}
void h_i2c_set_clock(int32_t, uint32_t hz) { record("i2c_set_clock", static_cast<int32_t>(hz)); }
int32_t h_i2c_start(int32_t, uint8_t address, int32_t read)
{
    record("i2c_start", address, read);
    return address == 0x27 ? 1 : 0;
}
int32_t h_i2c_write(int32_t, uint8_t value)
{
    record("i2c_write", value);
    return value == 0xEE ? 0 : 1;
}
uint8_t h_i2c_read(int32_t, int32_t ack)
{
    record("i2c_read", ack);
    return g_i2c_next_read++;
}
void h_i2c_stop(int32_t) { record("i2c_stop"); }
void h_spi_begin(int32_t, int32_t, int32_t, int32_t, int32_t) {}
void h_spi_end(int32_t) {}
void h_spi_settings(int32_t, uint32_t, int32_t, int32_t) {}
uint8_t h_spi_transfer(int32_t, uint8_t) { return 0xFF; }
uint8_t h_eeprom_read(int32_t address) { return g_eeprom[address]; }
void h_eeprom_write(int32_t address, uint8_t value)
{
    g_eeprom[address] = value;
    record("eeprom_write", address, value);
}
void h_attach_interrupt(int32_t number, int32_t mode) { record("attach_interrupt", number, mode); }
void h_detach_interrupt(int32_t number) { record("detach_interrupt", number); }
void h_set_interrupts_enabled(int32_t enabled) { record("set_interrupts_enabled", enabled); }
void h_log(int32_t level, const char* message)
{
    if (level == SIM_LOG_WARNING) {
        g_warnings++;
    }
    snprintf(g_last_log, sizeof g_last_log, "%s", message);
}
void h_fatal(const char* message)
{
    printf("fatal: %s\n", message);
    exit(70);
}

SimHostApi make_api()
{
    SimHostApi api = {};
    api.struct_size = sizeof(SimHostApi);
    api.abi_version = SIM_ABI_VERSION;
    api.pin_mode = h_pin_mode;
    api.digital_write = h_digital_write;
    api.digital_read = h_digital_read;
    api.analog_read = h_analog_read;
    api.analog_write = h_analog_write;
    api.pwm_config = h_pwm_config;
    api.dac_write = h_dac_write;
    api.analog_reference = h_analog_reference;
    api.tone = h_tone;
    api.no_tone = h_no_tone;
    api.now_us = h_now_us;
    api.delay_us = h_delay_us;
    api.yield = h_yield;
    api.serial_begin = h_serial_begin;
    api.serial_end = h_serial_end;
    api.serial_write = h_serial_write;
    api.serial_read = h_serial_read;
    api.serial_peek = h_serial_peek;
    api.serial_available = h_serial_available;
    api.serial_available_for_write = h_serial_available_for_write;
    api.serial_flush = h_serial_flush;
    api.i2c_begin = h_i2c_begin;
    api.i2c_end = h_i2c_end;
    api.i2c_set_clock = h_i2c_set_clock;
    api.i2c_start = h_i2c_start;
    api.i2c_write = h_i2c_write;
    api.i2c_read = h_i2c_read;
    api.i2c_stop = h_i2c_stop;
    api.spi_begin = h_spi_begin;
    api.spi_end = h_spi_end;
    api.spi_settings = h_spi_settings;
    api.spi_transfer = h_spi_transfer;
    api.eeprom_read = h_eeprom_read;
    api.eeprom_write = h_eeprom_write;
    api.attach_interrupt = h_attach_interrupt;
    api.detach_interrupt = h_detach_interrupt;
    api.set_interrupts_enabled = h_set_interrupts_enabled;
    api.log = h_log;
    api.fatal = h_fatal;
    api.crash_info = &g_crash;
    return api;
}

// ---- helpers ---------------------------------------------------------------

class Capture : public Print {
public:
    size_t write(uint8_t c) override
    {
        if (len_ + 1 < sizeof buf_) {
            buf_[len_++] = static_cast<char>(c);
            buf_[len_] = 0;
        }
        return 1;
    }
    const char* text() const { return buf_; }
    void clear()
    {
        len_ = 0;
        buf_[0] = 0;
    }

private:
    char buf_[256] = {};
    size_t len_ = 0;
};

class Feed : public Stream {
public:
    explicit Feed(const char* text) : text_(text) {}
    int available() override { return static_cast<int>(strlen(text_ + pos_)); }
    int read() override { return text_[pos_] != 0 ? static_cast<uint8_t>(text_[pos_++]) : -1; }
    int peek() override { return text_[pos_] != 0 ? static_cast<uint8_t>(text_[pos_]) : -1; }
    size_t write(uint8_t) override { return 0; }

private:
    const char* text_;
    size_t pos_ = 0;
};

int g_loop_calls = 0;

// ---- tests -----------------------------------------------------------------

void test_string()
{
    String s("hello");
    s += ' ';
    s += String("world");
    CHECK_STR(s.c_str(), "hello world");
    CHECK(s.length() == 11);
    CHECK(s.indexOf('o') == 4);
    CHECK(s.lastIndexOf('o') == 7);
    CHECK(s.indexOf("world") == 6);
    CHECK_STR(s.substring(6).c_str(), "world");
    CHECK_STR(s.substring(8, 6).c_str(), "wo");
    CHECK(s.startsWith("hell") && s.endsWith("rld") && !s.startsWith("world"));
    CHECK(s.equalsIgnoreCase(String("HELLO WORLD")));

    String sum = "a" + String(1) + 2 + 'c' + 3.5;
    CHECK_STR(sum.c_str(), "a12c3.50");
    String chained = String("x=") + 42u + ", y=" + -7L;
    CHECK_STR(chained.c_str(), "x=42, y=-7");

    CHECK_STR(String(255, HEX).c_str(), "ff");
    CHECK_STR(String(5, BIN).c_str(), "101");
    CHECK_STR(String(3.14159, 3).c_str(), "3.142");
    CHECK_STR(String(-0.5f).c_str(), "-0.50");
    CHECK(String("  42  ").toInt() == 42);
    CHECK(String("3.25").toFloat() == 3.25f);

    String t("  padded\t\n");
    t.trim();
    CHECK_STR(t.c_str(), "padded");
    String r("one two one");
    r.replace("one", "three");
    CHECK_STR(r.c_str(), "three two three");
    r.replace('e', 'E');
    CHECK_STR(r.c_str(), "thrEE two thrEE");
    r.remove(5, 4);
    CHECK_STR(r.c_str(), "thrEE thrEE");
    r.toUpperCase();
    CHECK_STR(r.c_str(), "THREE THREE");

    String self("ab");
    self += self;
    CHECK_STR(self.c_str(), "abab");
    String moved(static_cast<String&&>(self));
    CHECK_STR(moved.c_str(), "abab");
    CHECK(self.length() == 0);

    CHECK(String("abc") == "abc" && "abc" == String("abc") && String("a") < String("b"));
    char buf[4];
    String("abcdef").toCharArray(buf, sizeof buf);
    CHECK_STR(buf, "abc");
    CHECK_STR(String(F("flash")).c_str(), "flash");
}

void test_print()
{
    Capture out;
    out.print(-42);
    CHECK_STR(out.text(), "-42");
    out.clear();
    out.print(255, HEX);
    CHECK_STR(out.text(), "FF");
    out.clear();
    out.print(-1, HEX);
    CHECK_STR(out.text(), "FFFFFFFF");
    out.clear();
    out.print(3.14159);
    CHECK_STR(out.text(), "3.14");
    out.clear();
    out.print(2.5, 0);
    CHECK_STR(out.text(), "3");
    out.clear();
    out.print(1e10);
    CHECK_STR(out.text(), "ovf");
    out.clear();
    out.print(NAN);
    CHECK_STR(out.text(), "nan");
    out.clear();
    out.println(F("flash"));
    CHECK_STR(out.text(), "flash\r\n");
    out.clear();
    out.print('A');
    out.print(static_cast<unsigned char>(65));
    out.print(65, 0);
    CHECK_STR(out.text(), "A65A");
    out.clear();
    out.print(4294967295UL);
    CHECK_STR(out.text(), "4294967295");
}

void test_stream()
{
    Feed numbers("abc 123,45 x-3.25y 7");
    CHECK(numbers.parseInt() == 123);
    CHECK(numbers.parseInt() == 45);
    CHECK(numbers.parseFloat() == -3.25f);
    CHECK(numbers.parseInt(SKIP_WHITESPACE) == 0);  // 'y' is not whitespace

    Feed words("alpha,beta,gamma");
    CHECK_STR(words.readStringUntil(',').c_str(), "alpha");
    char buf[8] = {};
    CHECK(words.readBytesUntil(',', buf, sizeof buf - 1) == 4);
    CHECK_STR(buf, "beta");
    CHECK_STR(words.readString().c_str(), "gamma");

    Feed overlap("xxneeneedle!");
    CHECK(overlap.find("needle"));
    CHECK(overlap.read() == '!');

    Feed until("key=1;value");
    CHECK(!until.findUntil("value", ";"));
    CHECK(until.find("value"));

    Feed empty("");
    empty.setTimeout(5);
    uint64_t before = g_now_us;
    CHECK(empty.read() == -1);
    CHECK(empty.parseInt() == 0);
    CHECK(g_now_us - before >= 4000);  // timed out on the virtual clock (millisecond granularity)
}

void test_core_pins_and_time()
{
    g_call_count = 0;
    pinMode(13, OUTPUT);
    digitalWrite(13, HIGH);
    CHECK(has_call("pin_mode", 13, OUTPUT) && has_call("digital_write", 13, 1));
    CHECK(digitalRead(13) == HIGH);
    CHECK(analogRead(0) == 140);  // channel 0 is A0 (pin 14)
    CHECK(analogRead(A3) == 170);

    uint64_t before = g_now_us;
    delay(5);
    CHECK(g_now_us - before >= 5000);
    unsigned long m = millis();
    CHECK(m == g_now_us / 1000);

    g_call_count = 0;
    shiftOut(4, 5, MSBFIRST, 0xA0);
    CHECK(count_calls("digital_write") == 24);
    CHECK(g_calls[0].a == 4 && g_calls[0].b == 1);  // first bit of 0xA0, MSB first

    tone(8, 440);
    noTone(8);
    CHECK(has_call("tone", 8, 440) && count_calls("no_tone") == 1);
}

void test_port_registers()
{
    DDRB = 0;
    PORTB = 0;
    g_call_count = 0;
    DDRB = 0x20;  // D13 output
    CHECK(has_call("pin_mode", 13, SIM_PIN_OUTPUT));
    PORTB |= _BV(PB5);
    CHECK(has_call("digital_write", 13, 1));
    CHECK((PORTB & _BV(5)) != 0);
    PINB = _BV(PB5);  // toggles PORTB5
    CHECK(has_call("digital_write", 13, 0));
    DDRD &= static_cast<uint8_t>(~_BV(PD2));
    PORTD |= _BV(PD2);  // input with pull-up
    CHECK(has_call("digital_write", 2, 1));
    g_levels[3] = 1;
    CHECK((PIND & _BV(PD3)) != 0);

    pinMode(9, OUTPUT);
    CHECK((DDRB & _BV(PB1)) != 0);  // pinMode keeps the DDR shadow in sync
    digitalWrite(9, HIGH);
    CHECK((PORTB & _BV(PB1)) != 0);

    int warnings = g_warnings;
    TCCR1A = 0;
    TCCR1B |= _BV(CS10);
    TCCR1A = 1;
    CHECK(g_warnings == warnings + 2);
    CHECK(strstr(g_last_log, "TCCR1B") != nullptr);
    CHECK(OCR1A == 0);

    g_call_count = 0;
    uint8_t saved = SREG;
    cli();
    CHECK(SREG == 0);
    SREG = saved;
    CHECK(SREG == 0x80);
    CHECK(has_call("set_interrupts_enabled", 0, 0) && has_call("set_interrupts_enabled", 1, 0));
}

void test_random_and_math()
{
    srandom(1);
    CHECK(random() == 16807);
    CHECK(random() == 282475249);
    CHECK(random() == 1622650073);
    randomSeed(42);
    long a = random(100);
    randomSeed(42);
    CHECK(random(100) == a && a >= 0 && a < 100);
    CHECK(random(10, 10) == 10);
    CHECK(map(512, 0, 1023, 0, 255) == 127);
    CHECK(map(5, 0, 10, 100, 0) == 50);
    CHECK(constrain(300, 0, 255) == 255);
    CHECK(min(3, 7) == 3 && max(-1, 2) == 2 && min(2.5, 1) == 1);
    CHECK(bitRead(0x10, 4) == 1 && lowByte(0x1234) == 0x34 && highByte(0x1234) == 0x12 && word(0x12, 0x34) == 0x1234);
    CHECK(B10101010 == 0xAA);
    char buf[16];
    CHECK_STR(dtostrf(3.14159, 6, 2, buf), "  3.14");
    CHECK_STR(utoa(255u, buf, 16), "ff");
}

const char g_first[] PROGMEM = "first";
const char g_second[] PROGMEM = "second";
const char* const g_table[] PROGMEM = {g_first, g_second};
const uint16_t g_words[] PROGMEM = {0x1234, 0xBEEF};

void test_pgmspace()
{
    char buf[16];
    strcpy_P(buf, (char*)pgm_read_word(&(g_table[1])));
    CHECK_STR(buf, "second");
    CHECK(pgm_read_word(&g_words[1]) == 0xBEEF);
    CHECK(pgm_read_byte(&g_first[1]) == 'i');
    CHECK(strlen_P(g_second) == 6);
}

struct Settings {
    uint8_t version;
    float threshold;
};

void test_eeprom()
{
    g_call_count = 0;
    EEPROM.write(10, 0x42);
    CHECK(EEPROM.read(10) == 0x42 && EEPROM[10] == 0x42);
    EEPROM.update(10, 0x42);
    CHECK(count_calls("eeprom_write") == 1);  // update skips unchanged bytes
    Settings in = {3, 1.5f};
    EEPROM.put(100, in);
    Settings out = {};
    EEPROM.get(100, out);
    CHECK(out.version == 3 && out.threshold == 1.5f);
    CHECK(EEPROM.length() == 1024);
    EEPROM[11] += 1;
    CHECK(EEPROM[11] == 1);
    CHECK(eeprom_read_byte(reinterpret_cast<const uint8_t*>(10)) == 0x42);
}

void test_wire()
{
    g_call_count = 0;
    Wire.begin();
    Wire.setClock(400000);
    CHECK(has_call("i2c_begin", 0, 0) && has_call("i2c_set_clock", 400000, 0));

    // A write transaction: START + address, the bytes, STOP; 0 = success.
    g_call_count = 0;
    Wire.beginTransmission(0x27);
    CHECK(Wire.write(0x12) == 1);
    uint8_t pair[] = {0x34, 0x56};
    CHECK(Wire.write(pair, 2) == 2);
    CHECK(Wire.endTransmission() == 0);
    CHECK(g_call_count == 5 && has_call("i2c_start", 0x27, 0) && has_call("i2c_write", 0x56, 0) && count_calls("i2c_stop") == 1);

    // Nobody at 0x50: NACK on the address returns 2 and still ends with STOP.
    g_call_count = 0;
    Wire.beginTransmission(0x50);
    Wire.write(1);
    CHECK(Wire.endTransmission() == 2);
    CHECK(count_calls("i2c_write") == 0 && count_calls("i2c_stop") == 1);

    // NACK on a data byte returns 3.
    Wire.beginTransmission(0x27);
    Wire.write(0xEE);
    Wire.write(0x01);
    CHECK(Wire.endTransmission() == 3);

    // The 33rd byte does not fit: write() returns 0, one warning, 32 bytes go out.
    g_call_count = 0;
    int warnings = g_warnings;
    Wire.beginTransmission(0x27);
    size_t accepted = 0;
    for (int i = 0; i < 33; i++) {
        accepted += Wire.write(static_cast<uint8_t>(i));
    }
    CHECK(accepted == 32 && g_warnings == warnings + 1);
    CHECK(Wire.endTransmission() == 0 && count_calls("i2c_write") == 32);

    // endTransmission(false) keeps the bus: no STOP, so the next START is a repeated start.
    g_call_count = 0;
    Wire.beginTransmission(0x27);
    Wire.write(0x00);
    CHECK(Wire.endTransmission(false) == 0 && count_calls("i2c_stop") == 0);

    // requestFrom: ACK after every byte but the last, then STOP; the bytes are read back in order.
    g_i2c_next_read = 0x10;
    CHECK(Wire.requestFrom(0x27, 3) == 3);
    CHECK(has_call("i2c_start", 0x27, 1) && has_call("i2c_read", 1, 0) && has_call("i2c_read", 0, 0) && count_calls("i2c_stop") == 1);
    CHECK(Wire.available() == 3 && Wire.peek() == 0x10 && Wire.read() == 0x10 && Wire.read() == 0x11 && Wire.read() == 0x12 && Wire.read() == -1);

    // A read from a missing device returns 0 bytes.
    CHECK(Wire.requestFrom(0x50, 2) == 0 && Wire.available() == 0);

    // Register address first, then a repeated-start read (requestFrom with iaddress).
    g_call_count = 0;
    CHECK(Wire.requestFrom(static_cast<uint8_t>(0x27), static_cast<uint8_t>(1), static_cast<uint32_t>(0x0102), static_cast<uint8_t>(2), static_cast<uint8_t>(true)) == 1);
    CHECK(has_call("i2c_write", 0x01, 0) && has_call("i2c_write", 0x02, 0) && count_calls("i2c_start") == 2 && count_calls("i2c_stop") == 1);
}

void test_entry_points()
{
    g_serial_input = "x";
    g_serial_pos = 0;
    sim_fw_loop();  // serialEvent is not defined: the weak reference must be null
    CHECK(g_loop_calls == 1);
    sim_fw_setup();

    Serial.begin(9600);
    CHECK(has_call("serial_begin", 9600, SERIAL_8N1));
    g_serial_output_len = 0;
    Serial.print("v=");
    Serial.println(12);
    CHECK_STR(g_serial_output, "v=12\r\n");
    CHECK(Serial.read() == 'x' && Serial.read() == -1);
}

}  // namespace

void setup() {}
void loop() { g_loop_calls++; }

int main()
{
    static SimHostApi api = make_api();
    if (sim_fw_init(&api) != 0) {
        printf("sim_fw_init rejected the fake host\n");
        return 100;
    }

    test_string();
    test_print();
    test_stream();
    test_core_pins_and_time();
    test_port_registers();
    test_random_and_math();
    test_pgmspace();
    test_eeprom();
    test_wire();
    test_entry_points();

    printf("shim tests: %d checks, %d failed\n", g_checks, g_failures);
    return g_failures;
}
