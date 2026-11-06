using System.Globalization;
using System.Text;

namespace ArduSim.Core.Screens;

public static partial class ScreenCodeGenerator
{
    // The pieces of the generated runtime. Each goes in only if something the project does calls for it:
    // a function nothing calls would be a compiler's warning, and bytes a small board does not have.
    private static class Piece
    {
        public const string Clock = "clock";
        public const string ClockSeconds = "clockSeconds";
        public const string ClockDate = "clockDate";
        public const string Weekday = "weekday";
        public const string ClockSet = "clockSet";
        public const string DateSet = "dateSet";
        public const string DaysIn = "daysIn";
        public const string Timers = "timers";
        public const string Save = "save";
        public const string Wrap = "wrap";
        public const string Clamp = "clamp";
        public const string Copy = "copy";
        public const string Time = "time";
        public const string Date = "date";
        public const string Place = "place";
        public const string BigDigits = "bigDigits";
        public const string Bar = "bar";
        public const string Input = "input";
        public const string InputAdd = "inputAdd";
        public const string InputText = "inputText";
        public const string InputNumber = "inputNumber";
        public const string MenuCount = "menuCount";
    }

    private sealed partial class Generator
    {
        private sealed record SymbolEntry(string Name, string Code, IReadOnlyList<byte> Rows, char Nearest);

        // ---- symbols and letters --------------------------------------------------------------------

        private void CollectSymbols(string hooks)
        {
            foreach (ScreenNode screen in _d.Screens)
            {
                foreach (ScreenElement element in screen.Elements)
                {
                    switch (element.Kind)
                    {
                        case ElementKind.Text:
                            CollectText(element.Text);
                            break;
                        case ElementKind.Symbol:
                            AddSymbol(element.Text);
                            break;
                        case ElementKind.BigNumber when IsLcd:
                            AddSymbol(ScreenFont.BigTop);
                            AddSymbol(ScreenFont.BigBottom);
                            AddSymbol(ScreenFont.BigBoth);
                            break;
                        default:
                            break;
                    }

                    foreach (string word in element.Options)
                    {
                        CollectLetters(word);
                    }
                }

                if (screen.Menu is { } menu)
                {
                    CollectText(menu.Marker);
                    foreach (ScreenMenuItem item in menu.Items)
                    {
                        CollectText(item.Text);
                    }
                }
            }

            foreach (ScreenVariable variable in _d.Variables)
            {
                foreach (string word in variable.Options)
                {
                    CollectLetters(word);
                }
            }

            CollectLetters(hooks);
        }

        private void CollectText(string text)
        {
            foreach (ScreenFont.TextPart part in ScreenFont.Split(_d, text))
            {
                if (part.Letters is { } letters)
                {
                    CollectLetters(letters);
                }
                else
                {
                    AddSymbol(part.Symbol!.Name);
                }
            }
        }

        private void CollectLetters(string text)
        {
            foreach (char c in text)
            {
                if (c < 0x80 || _letters.ContainsKey(c))
                {
                    continue;
                }

                if (ScreenFont.LookAlike(c) is { } ascii)
                {
                    _letters[c] = CharLiteral(ascii.ToString());
                }
                else if (ScreenFont.Shape(c) is { } rows)
                {
                    string name = "U" + ((int)ScreenFont.Capital(c)).ToString("X4", CultureInfo.InvariantCulture);
                    if (_symbols.All(s => s.Name != name))
                    {
                        _symbols.Add(new SymbolEntry(name, "UI_S_" + name, rows, ScreenFont.NearestAscii(c)));
                    }

                    _letters[c] = "UI_S_" + name;
                }
            }
        }

        private void AddSymbol(string name)
        {
            if (_symbols.All(s => s.Name != name) && ScreenFont.Find(_d, name) is { } glyph)
            {
                _symbols.Add(new SymbolEntry(name, "UI_S_" + Identifier(name), glyph.Rows, '#'));
            }
        }

        private string? SymbolCode(string name) => _symbols.FirstOrDefault(s => s.Name == name)?.Code;

        private void Symbols(StringBuilder o)
        {
            if (_symbols.Count == 0)
            {
                return;
            }

            o.Append("// Symbols and letters no character shares a shape with, five dots by eight.\nstatic const uint8_t UI_FIRST_SYMBOL = 0x").Append(FirstSymbol.ToString("X2", CultureInfo.InvariantCulture)).Append(";\n");
            for (int i = 0; i < _symbols.Count; i++)
            {
                o.Append("static const uint8_t ").Append(_symbols[i].Code).Append(" = UI_FIRST_SYMBOL + ").Append(N(i)).Append(";\n");
            }

            o.Append("\nstatic const uint8_t uiSymbols[").Append(N(_symbols.Count)).Append("][8] PROGMEM = {\n");
            foreach (SymbolEntry symbol in _symbols)
            {
                o.Append("  {").Append(string.Join(", ", symbol.Rows.Select(r => N(r)))).Append("}, // ").Append(symbol.Name).Append('\n');
            }

            o.Append("};\n\n");
            if (IsLcd)
            {
                o.Append("// What shows if a screen wants more than the panel's eight programmable characters.\nstatic const char uiNearest[").Append(N(_symbols.Count)).Append("] PROGMEM = {");
                o.AppendJoin(", ", _symbols.Select(s => CharLiteral(s.Nearest.ToString())));
                o.Append("};\n\n");
            }
        }

        private void Letters(StringBuilder o)
        {
            if (_letters.Count == 0)
            {
                o.Append("size_t UiOut::write(uint8_t ch) {\n  if (ch >= ' ' && ch < 0x80) {\n    uiPut(ch);\n  }\n\n  return 1;\n}\n\n");
                return;
            }

            o.Append("// What each letter beyond ASCII shows.\nstatic uint8_t uiCellOf(uint16_t code) {\n  switch (code) {\n");
            foreach ((char letter, string cell) in _letters.OrderBy(l => l.Key))
            {
                o.Append("    case 0x").Append(((int)letter).ToString("X4", CultureInfo.InvariantCulture)).Append(": return ").Append(cell).Append(";\n");
            }

            o.Append("    default: return '?';\n  }\n}\n\n");
            o.Append("// Text comes as UTF-8, a byte at a time.\nsize_t UiOut::write(uint8_t ch) {\n  static uint16_t code;\n  static uint8_t more;\n  if (ch < 0x80) {\n    more = 0;\n    if (ch >= ' ') {\n      uiPut(ch);\n    }\n");
            o.Append("  } else if (ch >= 0xC0) {\n    more = ch >= 0xF0 ? 3 : ch >= 0xE0 ? 2 : 1;\n    code = ch & (ch >= 0xE0 ? 0x0F : 0x1F);\n  } else if (more > 0) {\n    code = (code << 6) | (ch & 0x3F);\n    more--;\n    if (more == 0) {\n      uiPut(uiCellOf(code));\n    }\n  }\n\n  return 1;\n}\n\n");
        }

        // ---- variables -------------------------------------------------------------------------------

        private void VariablesInHeader(StringBuilder o)
        {
            if (_d.Variables.Count == 0)
            {
                return;
            }

            o.Append("\n// The project's data.\n");
            foreach (ScreenVariable variable in _d.Variables.Where(v => v.Kind is not (VariableKind.Stopwatch or VariableKind.Countdown)))
            {
                o.Append("extern int32_t uiVar_").Append(variable.Name).Append(";\n");
            }

            if (_d.Variables.Any(v => v.Kind is VariableKind.Stopwatch or VariableKind.Countdown))
            {
                o.Append("\nstruct UiTimer {\n  uint32_t startedAt;\n  uint32_t amount;\n  uint32_t full;\n  bool running;\n  bool down;\n};\n\n");
                foreach (ScreenVariable variable in _d.Variables.Where(v => v.Kind is VariableKind.Stopwatch or VariableKind.Countdown))
                {
                    o.Append("extern UiTimer uiTimer_").Append(variable.Name).Append(";\n");
                }

                o.Append("uint32_t uiRead(const UiTimer& timer);\nvoid uiStart(UiTimer& timer);\nvoid uiStop(UiTimer& timer);\nvoid uiReset(UiTimer& timer);\nvoid uiTimerSet(UiTimer& timer, int32_t seconds);\n");
            }

            if (_d.Variables.Any(v => v.Saved))
            {
                o.Append("\n// Writes the variables kept in EEPROM: for when your own code changes one.\nvoid uiSave();\n");
            }
        }

        private void Variables(StringBuilder o)
        {
            if (_d.Variables.Count == 0)
            {
                return;
            }

            foreach (ScreenVariable variable in _d.Variables)
            {
                if (variable.Kind is VariableKind.Stopwatch or VariableKind.Countdown)
                {
                    string amount = variable.Kind == VariableKind.Countdown ? (Math.Max(variable.Initial, 0) * 1000L).ToString(CultureInfo.InvariantCulture) + "UL" : "0";
                    o.Append("UiTimer uiTimer_").Append(variable.Name).Append(" = {0, ").Append(amount).Append(", ").Append(amount).Append(", false, ").Append(variable.Kind == VariableKind.Countdown ? "true" : "false").Append("};\n");
                }
                else
                {
                    o.Append("int32_t uiVar_").Append(variable.Name).Append(" = ").Append(Long(variable.Initial)).Append(";\n");
                }
            }

            o.Append('\n');
            if (_d.Variables.Any(v => v.Kind is VariableKind.Stopwatch or VariableKind.Countdown))
            {
                o.Append("""
                    uint32_t uiRead(const UiTimer& timer) {
                      uint32_t gone = timer.running ? millis() - timer.startedAt : 0;
                      if (timer.down) {
                        return gone >= timer.amount ? 0 : timer.amount - gone;
                      }

                      return timer.amount + gone;
                    }

                    void uiStart(UiTimer& timer) {
                      if (timer.running) {
                        return;
                      }

                      if (timer.down && timer.amount == 0) {
                        timer.amount = timer.full;
                      }

                      if (!timer.down || timer.amount > 0) {
                        timer.startedAt = millis();
                        timer.running = true;
                      }
                    }

                    void uiStop(UiTimer& timer) {
                      timer.amount = uiRead(timer);
                      timer.running = false;
                    }

                    void uiReset(UiTimer& timer) {
                      timer.amount = timer.down ? timer.full : 0;
                      timer.running = false;
                    }

                    void uiTimerSet(UiTimer& timer, int32_t seconds) {
                      timer.amount = (uint32_t)seconds * 1000UL;
                      timer.full = timer.amount;
                      timer.running = false;
                    }


                    """.ReplaceLineEndings("\n"));
            }

            ScreenVariable[] saved = [.. _d.Variables.Where(v => v.Saved && v.Kind is not (VariableKind.Stopwatch or VariableKind.Countdown))];
            if (saved.Length > 0)
            {
                Need(Piece.Save);
                uint mark = 2166136261;
                foreach (char c in string.Join(",", saved.Select(v => v.Name + ":" + v.Kind)))
                {
                    mark = (mark ^ c) * 16777619;
                }

                o.Append("// Kept at the end of EEPROM. The mark says it is this project's, written with these variables.\n");
                o.Append("static const uint16_t UI_SAVED_MARK = 0x").Append((mark & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture)).Append(";\n");
                o.Append("static const uint16_t UI_SAVED_BYTES = sizeof(uint16_t) + ").Append(N(saved.Length)).Append(" * sizeof(int32_t);\n\n");
                o.Append("void uiSave() {\n  int at = EEPROM.length() - UI_SAVED_BYTES;\n  uint16_t mark = UI_SAVED_MARK;\n  EEPROM.put(at, mark);\n  at += sizeof mark;\n");
                foreach (ScreenVariable variable in saved)
                {
                    o.Append("  EEPROM.put(at, uiVar_").Append(variable.Name).Append(");\n  at += sizeof(int32_t);\n");
                }

                o.Append("#if defined(ARDUINO_ARCH_ESP32) || defined(ARDUINO_ARCH_ESP8266)\n  EEPROM.commit();\n#endif\n}\n\n");
                o.Append("static void uiLoadSaved() {\n#if defined(ARDUINO_ARCH_ESP32) || defined(ARDUINO_ARCH_ESP8266)\n  EEPROM.begin(512);\n#endif\n  int at = EEPROM.length() - UI_SAVED_BYTES;\n  uint16_t mark = 0;\n  EEPROM.get(at, mark);\n  if (mark != UI_SAVED_MARK) {\n    return;\n  }\n\n  at += sizeof mark;\n");
                foreach (ScreenVariable variable in saved)
                {
                    o.Append("  EEPROM.get(at, uiVar_").Append(variable.Name).Append(");\n  at += sizeof(int32_t);\n");
                }

                o.Append("}\n\n");
            }
        }

        // ---- the clock -------------------------------------------------------------------------------

        private void Clock(StringBuilder o)
        {
            if (!Uses(Piece.Clock))
            {
                return;
            }

            o.Append("// The date and the time");
            o.Append(_wiring.HasRtc ? ", from the DS3231.\n" : ", counted from when the board started.\n");
            o.Append("struct UiClock {\n  uint16_t year;\n  uint8_t month;\n  uint8_t day;\n  uint8_t hour;\n  uint8_t minute;\n  uint8_t second;\n};\n\n");
            o.Append("static UiClock uiClock = {2026, 1, 1, 0, 0, 0};\nstatic uint32_t uiClockAt;\n\n");
            if (_wiring.HasRtc)
            {
                o.Append("""
                    static RTC_DS3231 uiRtc;

                    static void uiClockRead() {
                      DateTime now = uiRtc.now();
                      uiClock = {now.year(), now.month(), now.day(), now.hour(), now.minute(), now.second()};
                    }

                    static void uiClockBegin() {
                      uiRtc.begin();
                      uiClockRead();
                    }

                    static void uiClockTick() {
                      if (millis() - uiClockAt >= 250) {
                        uiClockAt = millis();
                        uiClockRead();
                      }
                    }


                    """.ReplaceLineEndings("\n"));
            }
            else
            {
                o.Append("""
                    static void uiClockBegin() {
                      uiClockAt = millis();
                    }

                    static void uiClockTick() {
                      while (millis() - uiClockAt >= 1000) {
                        uiClockAt += 1000;
                        if (++uiClock.second < 60) {
                          continue;
                        }

                        uiClock.second = 0;
                        if (++uiClock.minute < 60) {
                          continue;
                        }

                        uiClock.minute = 0;
                        if (++uiClock.hour < 24) {
                          continue;
                        }

                        uiClock.hour = 0;
                        if (++uiClock.day > uiDaysIn(uiClock.year, uiClock.month)) {
                          uiClock.day = 1;
                          if (++uiClock.month > 12) {
                            uiClock.month = 1;
                            uiClock.year++;
                          }
                        }
                      }
                    }


                    """.ReplaceLineEndings("\n"));
            }

            if (Uses(Piece.ClockSeconds))
            {
                o.Append("static int32_t uiClockSeconds() {\n  return uiClock.hour * 3600L + uiClock.minute * 60 + uiClock.second;\n}\n\n");
            }

            if (Uses(Piece.ClockDate))
            {
                o.Append("static int32_t uiClockDate() {\n  return uiClock.year * 10000L + uiClock.month * 100 + uiClock.day;\n}\n\n");
            }

            if (Uses(Piece.Weekday))
            {
                o.Append("""
                    // From 0 for Monday.
                    static int32_t uiWeekday() {
                      static const uint8_t shift[12] = {0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4};
                      uint16_t year = uiClock.year - (uiClock.month < 3 ? 1 : 0);
                      uint8_t fromSunday = (year + year / 4 - year / 100 + year / 400 + shift[uiClock.month - 1] + uiClock.day) % 7;
                      return (fromSunday + 6) % 7;
                    }


                    """.ReplaceLineEndings("\n"));
            }

            string adjust = _wiring.HasRtc ? "  uiRtc.adjust(DateTime(uiClock.year, uiClock.month, uiClock.day, uiClock.hour, uiClock.minute, uiClock.second));\n" : "";
            string restart = _wiring.HasRtc ? "" : "  uiClockAt = millis();\n";
            if (Uses(Piece.ClockSet))
            {
                o.Append("static void uiClockSet(int32_t seconds) {\n  uiClock.hour = seconds / 3600 % 24;\n  uiClock.minute = seconds / 60 % 60;\n  uiClock.second = seconds % 60;\n").Append(restart).Append(adjust).Append("}\n\n");
            }

            if (Uses(Piece.DateSet))
            {
                o.Append("static void uiDateSet(int32_t date) {\n  uiClock.year = date / 10000;\n  uiClock.month = date / 100 % 100;\n  uiClock.day = date % 100;\n").Append(adjust).Append("}\n\n");
            }
        }

        // ---- the display -----------------------------------------------------------------------------

        private void DisplayRuntime(StringBuilder o)
        {
            if (IsLcd)
            {
                LcdRuntime(o);
            }
            else
            {
                OledRuntime(o);
            }
        }

        private void LcdRuntime(StringBuilder o)
        {
            o.Append("// A frame is drawn here first. uiFlush sends the panel only the cells that differ, a few on each pass,\n// so that a whole new screen neither flickers nor keeps the keys waiting.\n");
            o.Append("static const uint8_t UI_COLUMNS = ").Append(N(_display.Width)).Append(";\nstatic const uint8_t UI_ROWS = ").Append(N(_display.Height)).Append(";\n");
            o.Append("static const uint8_t UI_CELLS = UI_COLUMNS * UI_ROWS;\nstatic const uint8_t UI_CELLS_PER_PASS = 4;\n\n");
            o.Append("static uint8_t uiFrame[UI_CELLS];\nstatic uint8_t uiPanel[UI_CELLS];\nstatic uint8_t uiColumn;\nstatic uint8_t uiRow;\nstatic uint8_t uiScan;\nstatic uint8_t uiNextCell = UI_CELLS;\nstatic bool uiPending;\n\n");
            o.Append("""
                void uiAt(int x, int y, int size) {
                  (void)size;
                  uiColumn = x;
                  uiRow = y;
                }

                // Past the end of a row a text is cut off, not carried to another row.
                static void uiPut(uint8_t cell) {
                  if (uiColumn < UI_COLUMNS && uiRow < UI_ROWS) {
                    uiFrame[uiRow * UI_COLUMNS + uiColumn] = cell;
                    uiColumn++;
                  }
                }

                static void uiClear() {
                  memset(uiFrame, ' ', UI_CELLS);
                  uiColumn = 0;
                  uiRow = 0;
                }

                static void uiShow() {
                  uiPending = true;
                }


                """.ReplaceLineEndings("\n"));
            string send = $$"""
                static void uiSendCell(uint8_t at, uint8_t code) {
                  if (uiNextCell != at) {
                    {{D}}.setCursor(at % UI_COLUMNS, at / UI_COLUMNS);
                  }

                  {{D}}.write(code);
                  // Rows do not follow each other in the panel's memory.
                  uiNextCell = (at + 1) % UI_COLUMNS == 0 ? UI_CELLS : at + 1;
                }


                """;
            o.Append(send.ReplaceLineEndings("\n"));
            if (_symbols.Count == 0)
            {
                o.Append("""
                    static void uiFlush() {
                      if (!uiPending) {
                        return;
                      }

                      uint8_t sent = 0;
                      for (uint8_t seen = 0; seen < UI_CELLS && sent < UI_CELLS_PER_PASS; seen++) {
                        uint8_t at = uiScan;
                        uiScan = at + 1 == UI_CELLS ? 0 : at + 1;
                        if (uiFrame[at] != uiPanel[at]) {
                          uiSendCell(at, uiFrame[at]);
                          uiPanel[at] = uiFrame[at];
                          sent++;
                        }
                      }

                      if (sent < UI_CELLS_PER_PASS) {
                        uiPending = false;
                      }
                    }


                    """.ReplaceLineEndings("\n"));
                return;
            }

            string flush = $$"""
                // Which symbol each of the panel's eight programmable characters holds; 0 for none.
                static uint8_t uiLoaded[8];

                static bool uiIsSymbol(uint8_t cell) {
                  return cell >= UI_FIRST_SYMBOL && cell != 0xFF;
                }

                static int8_t uiSlotOf(uint8_t symbol) {
                  for (uint8_t slot = 0; slot < 8; slot++) {
                    if (uiLoaded[slot] == symbol) {
                      return slot;
                    }
                  }

                  return -1;
                }

                static void uiLoad(uint8_t slot, uint8_t symbol) {
                  uint8_t rows[8];
                  memcpy_P(rows, uiSymbols[symbol - UI_FIRST_SYMBOL], sizeof rows);
                  {{D}}.createChar(slot, rows);
                  uiLoaded[slot] = symbol;
                  // The panel's address is now in its character memory.
                  uiNextCell = UI_CELLS;
                }

                static void uiFlush() {
                  if (!uiPending) {
                    return;
                  }

                  // What the frame wants and what the panel still shows keep their programmable characters.
                  uint8_t wanted = 0;
                  uint8_t shown = 0;
                  uint8_t missing = 0;
                  for (uint8_t i = 0; i < UI_CELLS; i++) {
                    if (uiIsSymbol(uiFrame[i])) {
                      int8_t slot = uiSlotOf(uiFrame[i]);
                      if (slot >= 0) {
                        wanted |= 1 << slot;
                      } else if (missing == 0) {
                        missing = uiFrame[i];
                      }
                    }

                    if (uiIsSymbol(uiPanel[i])) {
                      int8_t slot = uiSlotOf(uiPanel[i]);
                      if (slot >= 0) {
                        shown |= 1 << slot;
                      }
                    }
                  }

                  // Loading one takes as long as nine cells: one to a pass, and nothing else on that pass.
                  if (missing != 0) {
                    uint8_t held = wanted | shown;
                    for (uint8_t slot = 0; slot < 8; slot++) {
                      if ((held & (1 << slot)) == 0) {
                        uiLoad(slot, missing);
                        return;
                      }
                    }
                  }

                  uint8_t sent = 0;
                  bool waiting = false;
                  for (uint8_t seen = 0; seen < UI_CELLS && sent < UI_CELLS_PER_PASS; seen++) {
                    uint8_t at = uiScan;
                    uiScan = at + 1 == UI_CELLS ? 0 : at + 1;
                    uint8_t want = uiFrame[at];
                    if (want == uiPanel[at]) {
                      continue;
                    }

                    uint8_t code = want;
                    uint8_t shows = want;
                    if (uiIsSymbol(want)) {
                      int8_t slot = uiSlotOf(want);
                      if (slot >= 0) {
                        code = slot;
                      } else if (wanted == 0xFF) {
                        // More than eight at once: the nearest character says what was meant.
                        code = pgm_read_byte(&uiNearest[want - UI_FIRST_SYMBOL]);
                      } else {
                        // Its character is not free yet: this cell lets go of the one it shows, if any, to help.
                        waiting = true;
                        if (!uiIsSymbol(uiPanel[at])) {
                          continue;
                        }

                        code = ' ';
                        shows = ' ';
                      }
                    }

                    uiSendCell(at, code);
                    uiPanel[at] = shows;
                    sent++;
                  }

                  if (sent < UI_CELLS_PER_PASS && !waiting) {
                    uiPending = false;
                  }
                }


                """;
            o.Append(flush.ReplaceLineEndings("\n"));
        }

        private void OledRuntime(StringBuilder o)
        {
            o.Append("static int uiX;\nstatic int uiY;\nstatic uint8_t uiSize = 1;\n");
            bool u8g2 = _display.Library == DisplayLibrary.U8g2;
            bool gyver = _display.Library == DisplayLibrary.GyverOled;
            if (u8g2)
            {
                o.Append("static uint8_t uiStep = 6;\n");
            }

            o.Append('\n');
            o.Append("void uiAt(int x, int y, int size) {\n  uiX = x;\n  uiY = y;\n");
            if (u8g2)
            {
                // Its fonts do not scale: a large text is another font, with its own width.
                o.Append("  uiSize = 1;\n  uiStep = size >= 2 ? 10 : 6;\n  ").Append(D).Append(".setFont(size >= 2 ? u8g2_font_10x20_tf : u8g2_font_6x10_tf);\n  ").Append(D).Append(".setFontPosTop();\n");
            }
            else if (gyver)
            {
                o.Append("  uiSize = size;\n  ").Append(D).Append(".setScale(size);\n");
            }
            else
            {
                o.Append("  uiSize = size;\n  ").Append(D).Append(".setTextColor(").Append(White).Append(");\n  ").Append(D).Append(".setTextSize(size);\n");
            }

            o.Append("}\n\n");
            if (_symbols.Count > 0 || Uses(Piece.Bar))
            {
                o.Append("static void uiBox(int x, int y, int width, int height) {\n  ").Append(D).Append(_display.Library switch
                {
                    DisplayLibrary.U8g2 => ".drawBox(x, y, width, height);",
                    DisplayLibrary.GyverOled => ".rect(x, y, x + width - 1, y + height - 1, OLED_FILL);",
                    _ => ".fillRect(x, y, width, height, " + White + ");",
                }).Append("\n}\n\n");
            }

            string step = u8g2 ? "uiStep" : "6 * uiSize";
            string cursor = gyver ? "setCursorXY" : "setCursor";
            o.Append("static void uiPut(uint8_t cell) {\n");
            if (_symbols.Count > 0)
            {
                o.Append("""
                      if (cell >= UI_FIRST_SYMBOL) {
                        uint8_t rows[8];
                        memcpy_P(rows, uiSymbols[cell - UI_FIRST_SYMBOL], sizeof rows);
                        for (uint8_t y = 0; y < 8; y++) {
                          for (uint8_t x = 0; x < 5; x++) {
                            if ((rows[y] & (0x10 >> x)) != 0) {
                              uiBox(uiX + x * uiSize, uiY + y * uiSize, uiSize, uiSize);
                            }
                          }
                        }
                      } else {

                    """.ReplaceLineEndings("\n"));
                o.Append("    ").Append(D).Append('.').Append(cursor).Append("(uiX, uiY);\n    ").Append(D).Append(".write(cell);\n  }\n\n");
            }
            else
            {
                o.Append("  ").Append(D).Append('.').Append(cursor).Append("(uiX, uiY);\n  ").Append(D).Append(".write(cell);\n");
            }

            o.Append("  uiX += ").Append(step).Append(";\n}\n\n");
            o.Append("static void uiClear() {\n  ").Append(D).Append(_display.Library switch
            {
                DisplayLibrary.U8g2 => ".clearBuffer();",
                DisplayLibrary.GyverOled => ".clear();",
                _ => ".clearDisplay();",
            }).Append("\n}\n\n");
            o.Append("static void uiShow() {\n  ").Append(D).Append(_display.Library switch
            {
                DisplayLibrary.U8g2 => ".sendBuffer();",
                DisplayLibrary.GyverOled => ".update();",
                _ => ".display();",
            }).Append("\n}\n\n");
        }

        // ---- text and what is made of it ---------------------------------------------------------------

        private void TextHelpers(StringBuilder o)
        {
            if (Uses(Piece.Copy))
            {
                o.Append("static void uiCopy(char* out, uint8_t size, const __FlashStringHelper* text) {\n  strncpy_P(out, reinterpret_cast<const char*>(text), size - 1);\n  out[size - 1] = 0;\n}\n\n");
            }

            if (Uses(Piece.Time))
            {
                o.Append("""
                    static void uiTextTime(char* out, uint8_t size, int32_t seconds, bool hours, bool lastPair) {
                      long tail = seconds % 60;
                      if (!hours) {
                        snprintf(out, size, "%02ld:%02ld", (long)(seconds / 60), tail);
                      } else if (lastPair) {
                        snprintf(out, size, "%02ld:%02ld:%02ld", (long)(seconds / 3600), (long)(seconds / 60 % 60), tail);
                      } else {
                        snprintf(out, size, "%02ld:%02ld", (long)(seconds / 3600), (long)(seconds / 60 % 60));
                      }
                    }


                    """.ReplaceLineEndings("\n"));
            }

            if (Uses(Piece.Date))
            {
                o.Append("""
                    // A date written as yyyymmdd. Order: 0 day first, 1 month first, 2 year first.
                    static void uiTextDate(char* out, uint8_t size, int32_t date, uint8_t order) {
                      long year = date / 10000;
                      long month = date / 100 % 100;
                      long day = date % 100;
                      if (order == 0) {
                        snprintf(out, size, "%02ld.%02ld.%04ld", day, month, year);
                      } else if (order == 1) {
                        snprintf(out, size, "%02ld.%02ld.%04ld", month, day, year);
                      } else {
                        snprintf(out, size, "%04ld-%02ld-%02ld", year, month, day);
                      }
                    }


                    """.ReplaceLineEndings("\n"));
            }

            if (Uses(Piece.Place))
            {
                string step = IsLcd ? "1" : "6 * size";
                o.Append("enum UiAlign : uint8_t {\n  UI_LEFT,\n  UI_CENTER,\n  UI_RIGHT\n};\n\n");
                o.Append("// A text in its field: where it lies there, and cut off at the field's end.\n");
                o.Append("static void uiPlace(int x, int y, int size, int width, UiAlign align, const char* text) {\n  int cells = 0;\n  for (const char* p = text; *p != 0; p++) {\n    if ((*p & 0xC0) != 0x80) {\n      cells++;\n    }\n  }\n\n");
                o.Append("  int room = width > cells ? width - cells : 0;\n  uiAt(x + (align == UI_CENTER ? room / 2 : align == UI_RIGHT ? room : 0) * (").Append(step).Append("), y, size);\n");
                o.Append("  int left = width;\n  for (const char* p = text; *p != 0; p++) {\n    if ((*p & 0xC0) != 0x80 && left-- == 0) {\n      break;\n    }\n\n    uiOut.write((uint8_t)*p);\n  }\n}\n\n");
            }

            if (Uses(Piece.BigDigits))
            {
                o.Append("// Digits three cells wide and two rows high, made of three symbols and the ROM's full block.\n");
                o.Append("static const uint8_t uiBigParts[10][6] PROGMEM = {\n");
                foreach (string digit in ScreenFont.BigDigits)
                {
                    o.Append("  {").AppendJoin(", ", digit.Select(part => part switch
                    {
                        'T' => SymbolCode(ScreenFont.BigTop),
                        'B' => SymbolCode(ScreenFont.BigBottom),
                        'X' => SymbolCode(ScreenFont.BigBoth),
                        'F' => "0xFF",
                        _ => "' '",
                    })).Append("},\n");
                }

                o.Append("""
                    };

                    static void uiBigText(uint8_t x, uint8_t y, const char* text) {
                      for (; *text != 0; text++) {
                        if (*text >= '0' && *text <= '9') {
                          for (uint8_t i = 0; i < 6; i++) {
                            if (i % 3 == 0) {
                              uiAt(x, y + i / 3);
                            }

                            uiPut(pgm_read_byte(&uiBigParts[*text - '0'][i]));
                          }

                          x += 4;
                        } else if (*text == ':') {
                          uiAt(x, y);
                          uiPut('.');
                          uiAt(x, y + 1);
                          uiPut('.');
                          x += 2;
                        } else {
                          uiAt(x, y + 1);
                          uiPut(*text);
                          x += 2;
                        }
                      }
                    }


                    """.ReplaceLineEndings("\n"));
            }

            if (Uses(Piece.Bar))
            {
                o.Append("static void uiBar(int x, int y, int width, int height, int32_t value, int32_t full) {\n  int32_t part = full > 0 && value > 0 ? (value > full ? full : value) : 0;\n");
                if (IsLcd)
                {
                    o.Append("  int filled = full > 0 ? part * width / full : 0;\n  (void)height;\n  uiAt(x, y);\n  for (int i = 0; i < filled; i++) {\n    uiPut(0xFF);\n  }\n}\n\n");
                }
                else
                {
                    o.Append("  int filled = full > 0 ? part * (width - 2) / full : 0;\n  uiBox(x, y, width, 1);\n  uiBox(x, y + height - 1, width, 1);\n  uiBox(x, y, 1, height);\n  uiBox(x + width - 1, y, 1, height);\n  if (filled > 0) {\n    uiBox(x + 1, y + 1, filled, height - 2);\n  }\n}\n\n");
                }
            }

            if (Uses(Piece.Input))
            {
                o.Append("// What is being typed on the screen on view.\nstatic uint8_t uiInputDigits[9];\nstatic uint8_t uiInputCount;\n\n");
            }

            if (Uses(Piece.InputAdd))
            {
                o.Append("static void uiInputAdd(char key, uint8_t most) {\n  if (uiInputCount < most) {\n    uiInputDigits[uiInputCount++] = key - '0';\n    uiDirty = true;\n  }\n}\n\n");
            }

            if (Uses(Piece.InputText))
            {
                o.Append("""
                    // The mask's underscores with the digits so far: from the left, or coming in from the right after zeros.
                    static void uiInputText(char* out, const char* mask, bool fromRight) {
                      uint8_t total = 0;
                      for (const char* p = mask; *p != 0; p++) {
                        if (*p == '_') {
                          total++;
                        }
                      }

                      uint8_t skip = fromRight && total > uiInputCount ? total - uiInputCount : 0;
                      uint8_t at = 0;
                      for (; *mask != 0; mask++) {
                        if (*mask != '_') {
                          *out++ = *mask;
                        } else {
                          *out++ = at < skip ? '0' : at - skip < uiInputCount ? '0' + uiInputDigits[at - skip] : '_';
                          at++;
                        }
                      }

                      *out = 0;
                    }


                    """.ReplaceLineEndings("\n"));
            }

            if (Uses(Piece.InputNumber))
            {
                o.Append("""
                    // Some of the digits as a number, counted as the mask counts them.
                    static int32_t uiInputNumber(uint8_t first, uint8_t count, uint8_t total) {
                      uint8_t skip = total > uiInputCount ? total - uiInputCount : 0;
                      int32_t number = 0;
                      for (uint8_t i = first; i < first + count; i++) {
                        number = number * 10 + (i < skip ? 0 : uiInputDigits[i - skip]);
                      }

                      return number;
                    }


                    """.ReplaceLineEndings("\n"));
            }

            if (Uses(Piece.MenuCount))
            {
                o.Append("static int32_t uiMenuCount() {\n  switch (uiScreen) {\n");
                foreach (ScreenNode screen in _d.Screens.Where(s => s.Menu is not null))
                {
                    o.Append("    case UI_").Append(Upper(screen.Id)).Append(": return ").Append(N(screen.Menu!.Items.Count)).Append(";\n");
                }

                o.Append("    default: return 0;\n  }\n}\n\n");
            }
        }

        private void Runtime(StringBuilder o)
        {
            if (Uses(Piece.Clock) && !_wiring.HasRtc)
            {
                Need(Piece.DaysIn);
            }

            if (Uses(Piece.Wrap))
            {
                o.Append("static int32_t uiWrap(int32_t value, int32_t low, int32_t high) {\n  int32_t span = high - low + 1;\n  int32_t past = (value - low) % span;\n  return low + (past < 0 ? past + span : past);\n}\n\n");
            }

            if (Uses(Piece.Clamp))
            {
                o.Append("static int32_t uiClamp(int32_t value, int32_t low, int32_t high) {\n  return value < low ? low : value > high ? high : value;\n}\n\n");
            }

            if (Uses(Piece.DaysIn))
            {
                o.Append("static uint8_t uiDaysIn(uint16_t year, uint8_t month) {\n  static const uint8_t days[12] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};\n  bool leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);\n  return month == 2 && leap ? 29 : days[month - 1];\n}\n\n");
            }

            Variables(o);
            Clock(o);
            Symbols(o);
            DisplayRuntime(o);
            o.Append("UiOut uiOut;\n\n");
            Letters(o);
            TextHelpers(o);
        }
    }
}
