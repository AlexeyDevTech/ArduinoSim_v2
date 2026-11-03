# ArduSim UI

- Status: Approved by the owner on 2026-11-02, including the five proposals in §6.
- Date: 2026-11-02
- Scope: the M3 schematic editor and the shell it lives in. Items marked *(M4)* / *(M5)* are placed now so the layout does not change later, and built in those milestones.
- Fixed by the brief and ADR-004: WPF, our own theme (dark default, light variant), flat surfaces, one accent color, 8 px rhythm, Segoe UI, subtle motion only on state changes, Russian strings from `.resx`, custom split layout (no docking library), Wokwi as the UX reference for canvas and wiring.

## 1. Principles

1. **The canvas is the product.** Panels frame it; nothing floats over it except transient banners, menus and tooltips.
2. **Live state is visible where it happens.** An LED glows on the part, a level shows on the pin, a warning sits on the part it concerns — not only in a log.
3. **Loud, specific, actionable.** Every problem says what happened, where, and offers the next action (jump to line, show part, rerun, stop, re-download).
4. **Keyboard-first for frequent actions, mouse-first for placement.** Every command has a shortcut; every shortcut is shown in its tooltip.
5. **Calm by default.** Neutral surfaces, one accent for "primary / selected / focused", semantic colors only for states (running, warning, error).

## 2. Layout

### 2.1 Main window (1360 × 860, minimum 1000 × 640)

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ ◆ ArduSim  Blink ▾ ●   │ ⚒ Собрать  ▶ Запуск  ❚❚  ■ │ ⤼ ⏱1 ⏱10 ⏱100 │ 1× ▾ │ Nano ▾ │ 0.000 с  ● Готово │ – □ × │  title bar + toolbar, 48 px
├──────────────┬───────────────────────────────────────────────────────────────┬───────────────┤
│ 🔍 Поиск     │ [Схема] [Blink.ino ●] [config.h]                          +   │ Свойства      │
│──────────────│───────────────────────────────────────────────────────────────│───────────────│
│ ▾ Платы      │                                                               │ led1 · LED    │
│   Nano       │        · · · · · · · · · · · · · · · · · · · · ·              │               │
│ ▾ Вывод      │        ·   ┌──────────────────────┐  ◖◗  · · ·                │ Цвет  ● ● ● ● │
│   Светодиод  │        ·   │ ▣ NANO   ▪L ▪ON      │──R──◖ ▸ · ·                │ Пины          │
│   RGB-свет.  │        ·   └──────────────────────┘      · · ·                │  A → board.D13│
│   Пищалка    │        · · · · · · · · · · · · · · · · · · · · ·              │  C → board.GND│
│ ▾ Ввод       │                                                               │               │
│   Кнопка     │                                                   ┌────────┐  │ [⟳] [⇋] [🗑]  │
│   Переключ.  │                                                   │ 100% ⤢ │  │               │
│   Потенц.    │                                                   └────────┘  │               │
│ ▾ Пассивные  │                                                               │               │
│   Резистор   │                                                               │               │
│ ▾ Дисплеи(M4)│                                                               │               │
├──────────────┴───────────────────────────────────────────────────────────────┴───────────────┤
│ [Монитор порта] [Плоттер (M5)] [Сборка] [Журнал ⚠1] [Диаграмма (позже)]                  ▾  │
│ ┌──────────────────────────────────────────────────────────────────┐  9600 бод              │
│ │ Press the button to toggle the LED.                              │  ☑ Автопрокрутка       │
│ │ LED on                                                           │  ☐ Время               │
│ └──────────────────────────────────────────────────────────────────┘  [Очистить]           │
│ [ ввод…                                         ] нет|LF|CR|CR+LF  [Отправить]             │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ D:\Projects\Blink  ·  Arduino Nano  ·  сборка 0.41 с  ·  компилятор llvm-mingw 20261020    1.0× │  status bar, 24 px
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Region | Size | Behavior |
| --- | --- | --- |
| Title bar + toolbar | 48 px, one row | Custom window chrome: app mark, project menu, run controls, board, clock, status, window buttons (maximize supports Windows 11 snap layouts). |
| Palette (left) | 240 px (180–360) | Collapsible (Ctrl+1), resizable, width persisted. |
| Center tabs | rest | "Схема" is always the first tab and cannot close; one tab per sketch file. |
| Properties (right) | 280 px (220–420) | Collapsible (Ctrl+2), resizable, persisted. |
| Bottom tabs | 240 px (120–60 % of height) | Collapsible (Ctrl+3 or the ▾ button); selected tab and height persisted. |
| Status bar | 24 px | Project path, board, last build time, toolchain, actual speed. |

Panels collapse to a 24 px strip with a vertical label, so they can be reopened by mouse. Splitters are 1 px lines with a 6 px hit area.

### 2.2 Toolbar, left to right

| Group | Content | Notes |
| --- | --- | --- |
| Project | `◆` mark, project name with ▾ menu (Новый, Открыть…, Недавние ▸, Примеры ▸, Сохранить, Открыть папку в проводнике, Параметры, О точности симуляции), `●` when unsaved | The only menu; everything else is a button. |
| Build & run | Собрать (Ctrl+B), **Запуск** (F5, primary), Пауза/Продолжить (F6), Стоп (Shift+F5) | Icon + label for these four. |
| Step | loop() (F10), 1 / 10 / 100 мс | Icon buttons with tooltips; enabled while paused or running (a step pauses first). |
| Speed | 0.1× 0.5× 1× 2× 10× макс, as a compact dropdown | Applies live. |
| Board | Nano ▾ | Board picker; changing it rebuilds and is undoable. |
| Clock | Virtual time `12.345 с`, tabular digits | Click toggles s / ms. |
| Status | Pill: Готово · Сборка… · Работает · Пауза · Прошивка упала · Не отвечает | Color dot per §3.1; click opens the relevant panel. |

### 2.3 Palette

```
┌──────────────────────┐
│ 🔍 Поиск деталей     │   Ctrl+F focuses; filters names and keywords live
├──────────────────────┤
│ ▾ Вывод              │   categories: Платы, Вывод, Ввод, Датчики (M8), Дисплеи, Модули (M9), Пассивные
│  ◖ Светодиод         │   row 32 px: 20 px icon + name
│  ◖ RGB-светодиод     │   hover: surface-hover; tooltip: one-line description + pin list
│  ◉ Пищалка           │
└──────────────────────┘
```

- Drag a row onto the canvas: a ghost of the part at the current zoom follows the cursor and snaps to the grid; Esc cancels.
- Double-click or Enter adds the part at the center of the view, selected.
- Presets appear as their own rows (e.g. "LCD 1602 I2C" = LCD with interface I2C, ADR-006) *(M4)*.

### 2.4 Properties panel

```
┌────────────────────────────┐
│ ◖ led1              LED    │   id is editable (unique, [a-z0-9-]); type on the right
├────────────────────────────┤
│ Цвет      ● ● ● ● ● ●      │   choices: ≤ 4 → segmented control, colors → swatches, else dropdown
│ Сопротивл.[ 220     ] Ω    │   numbers: unit suffix, ↑/↓ and wheel step, Enter commits, Esc reverts
│ Клавиша   [ Пробел  ]      │   key capture field: "press a key"
├────────────────────────────┤
│ Пины                       │
│  A   → board.D13   HIGH    │   live level while running; click a row → highlights that net
│  C   → board.GND   0 В     │
├────────────────────────────┤
│ [⟳ Повернуть] [⇋ Отразить] [🗑] │
└────────────────────────────┘
```

- Nothing selected → project properties: board, sketch folder, baud and line ending defaults, speed, library paths, EEPROM (size, "Очистить EEPROM").
- Several parts selected → common properties only, with "—" for mixed values.
- A wire selected → color swatches, its two ends, Delete.
- An SD card module → its folder (a path in the project, default `sd`) and a button «Открыть папку» that opens it in Explorer; the folder is the card's contents *(M9)*.
- Every edit is one undo step; invalid input keeps the old value and shows the reason under the field.

### 2.5 Code tabs

```
 [Схема] [Blink.ino ●] [config.h]
┌─────────────────────────────────────────────────────────────────┐
│  5 │ void loop() {                                              │
│  6 │✖  digitalWrit(LED_BUILTIN, HIGH);                          │  gutter icon + squiggle; hover = message
│  7 │   delay(1000);                                             │
└─────────────────────────────────────────────────────────────────┘
 ⚠ Файл изменён вне ArduSim.  [Загрузить с диска] [Оставить мою версию]   ← inline bar, only on conflict
```

- AvalonEdit, Cascadia Mono 13, tab = 2 spaces (Arduino convention), auto-indent, bracket matching, current-line highlight.
- Build errors: red squiggle and gutter ✖; warnings amber ⚠; F8 / Shift+F8 next / previous diagnostic; Run with errors opens the first one.
- The sketch folder is the source of truth: files reload silently when unchanged in the editor; the conflict bar appears only when both changed.
- Run and Build save open files first (as the Arduino IDE does); Ctrl+S saves.

### 2.6 Bottom tabs

| Tab | Content |
| --- | --- |
| Монитор порта | Output (mono, autoscroll, optional time stamps), input line, line ending segmented, baud label, Очистить. |
| Плоттер *(M5)* | Live chart of numbers parsed from Serial lines (Arduino Serial Plotter format), auto-scaling, pause. |
| Сборка | Left: parsed diagnostics (icon, file:line, message), click opens the editor at the location. Right: raw compiler output. Summary line with time and cache hits. |
| Журнал | Simulation messages with time, severity filter (Все / Предупреждения / Ошибки), "Показать" jumps to and flashes the part. The tab shows a count badge for unseen warnings. |
| Диаграмма *(later)* | Pin timeline / logic analyzer (§4 later list). |

### 2.7 Start view (no project open)

```
┌──────────────────────────────────────────────────────┐
│  ArduSim                                             │
│  [+ Новый проект]   [Открыть папку…]                 │
│                                                      │
│  Недавние                      Примеры               │
│  Blink      D:\Projects\Blink  ┌──────┐ ┌──────┐     │
│  Button     …\Button           │Blink │ │Button│ …   │  examples gallery cards (M5: thumbnails)
└──────────────────────────────────────────────────────┘
```

New project: pick a folder and a board; the app creates `project.simproj.json`, `sketch/<name>.ino` with empty `setup()`/`loop()`, and opens the scene with the board placed.

### 2.8 Banners, dialogs, toasts

- **Banner** (top of the canvas, max 720 px, dismissible): run outcomes that need a decision — crash (exception, `file:line`, last API calls, Перезапустить / Открыть строку), firmware busy (Остановить), Runner exited (exit code, stderr in an expandable block), toolchain needed or failed (progress bar while downloading; Скачать / Повторить).
- **Toast** (bottom-right, 3 s, no action required): "Проект сохранён", "Файл перезагружен с диска".
- **Modal dialogs** only for: file and folder pickers, unsaved-changes on close, Settings, "О точности симуляции" (a side sheet listing the fidelity limits of brief §3.1).
- **Part badges**: an amber ⚠ on a part with an active warning (floating input, short); click → the log entry. Cleared on the next run.

## 3. Visual language

### 3.1 Color tokens

One accent (teal). Semantic colors mark states only.

| Token | Dark | Light | Use |
| --- | --- | --- | --- |
| `Background` | `#14161B` | `#F4F5F7` | window |
| `Surface` | `#1B1E25` | `#FFFFFF` | panels, toolbar |
| `SurfaceRaised` | `#232731` | `#FFFFFF` | buttons, banners, menus |
| `SurfaceHover` | `#2C313C` | `#ECEEF2` | hover |
| `Border` | `#2C313C` | `#DDE1E7` | dividers, control borders |
| `BorderStrong` | `#3A404C` | `#C5CBD4` | focus-adjacent, inputs |
| `Canvas` | `#101216` | `#FAFBFC` | schematic background |
| `CanvasGrid` | `#262A33` | `#D9DDE3` | grid dots |
| `TextPrimary` | `#E7E9EE` | `#1B1E25` | text |
| `TextSecondary` | `#9BA3B4` | `#5C6372` | labels, hints |
| `TextDisabled` | `#5C6372` | `#A3AAB6` | disabled |
| `Accent` | `#2BC4A9` | `#0B7A69` | primary button, selection, focus ring, active tab |
| `OnAccent` | `#0B1A17` | `#FFFFFF` | text on accent |
| `Running` | `#3AE06A` | `#1E9E4A` | status dot "Работает" |
| `Warning` | `#F0B44C` | `#946200` | warnings, "Пауза", "Не отвечает" |
| `Error` | `#F0605A` | `#C53030` | errors, crash |

Checked against WCAG AA (4.5:1 for 13 px text): `TextPrimary`, `TextSecondary`, `Accent`, `Warning` and `Error` on `Surface` and `Background`, and `OnAccent` on `Accent`, pass in both themes (lowest: dark `Error` on `SurfaceRaised` 4.64, light `Accent` on `Background` 4.81). `TextDisabled` is exempt, as WCAG allows for disabled controls.

**Wires** (stroke 2.2 canvas units ≈ 2 px at 100 %): power `#E54848`, ground `#5A606E` (dark) / `#2D3138` (light), I2C fixed SDA `#3E8BFF` and SCL `#F2C94C` *(M4)*, other signals default `#3BB273`, selectable from 8 swatches (green, blue, yellow, orange, purple, pink, white, grey). Wire colors are never the only cue: hovering shows the net name and its pins.

### 3.2 Typography

Segoe UI Variable (Text optical size), fallback Segoe UI; Cascadia Mono, fallback Consolas, for code, Serial and numbers that change (tabular figures).

| Style | Size / line | Weight | Use |
| --- | --- | --- | --- |
| Title | 15 / 20 | SemiBold | banner titles, panel headers |
| Body | 13 / 20 | Regular | default |
| Small | 12 / 16 | Regular | secondary text, list metadata |
| Caption | 11 / 14 | Regular | badges, canvas labels at 100 % |
| Mono | 12 / 18 (editor 13 / 20) | Regular | code, Serial, build output, clock |

Sentence case everywhere ("Открыть папку", not "Открыть Папку"). Arduino identifiers stay as written (`setup()`, `loop()`, `Serial`, `INPUT_PULLUP`).

### 3.3 Spacing, sizes, shape

- 8 px rhythm with 4 px half-steps: 4, 8, 12, 16, 24, 32.
- Control height 28 px (toolbar, fields), list rows 28 px (palette 32 px), panel header 36 px.
- Radii: 6 px controls, 8 px banners/menus/cards, 4 px chips and badges.
- Elevation: none on docked surfaces (1 px borders separate them). Floating elements (menus, tooltips, banners, drag ghosts) get a 1 px border plus shadow `0 4 16 rgba(0,0,0,.35)` (dark) / `rgba(16,24,40,.12)` (light).
- Focus: 2 px `Accent` outline, 2 px offset, on every focusable element; never removed.

### 3.4 Icons

Fluent UI System Icons, Regular, 20 px in the toolbar and 16 px in lists; Filled only for an active toggle. One icon per concept across the app (e.g. the same ▶ for Run in toolbar, menu and banner).

### 3.5 Motion

- Color and opacity transitions on hover/press: 100 ms ease-out. Panel collapse and banner appearance: 160 ms ease-out. Nothing else animates.
- Simulation state is never animated or eased: an LED is as bright as the snapshot says, the moment it arrives.
- When Windows "Show animations" is off, all transitions are instant.

### 3.6 Canvas rendering

- Background `Canvas` with a dot every 0.1 inch (the pin pitch); dots fade out below 50 % zoom.
- Parts are flat, slightly stylized drawings of the real part (Wokwi-like, not schematic symbols), drawn from vector art in their definitions (ADR-007), crisp at every zoom.
- Board pins: gold rings with labels; other parts' pins appear as small rings on hover and while wiring.
- Live state: LED glow scaled by brightness, pushbutton cap pressed, slide-switch knob position, pot knob angle, buzzer frequency label, encoder knob angle, keypad and remote keys held, joystick cap leaning, sensor readings above the part *(M8)*, servo horn angle, relay LED and contact, seven-segment and TM1637 digits, MAX7219 matrices and WS2812 LEDs from their frames *(M9)*, displays from framebuffers *(M4/M5)*: LCD with a visible 5×8 dot grid and backlight variants, OLED with pixel glow and color variants, nearest-neighbor scaling.
- Selection: 1 px `Accent` outline 3 px outside the part's bounds; multi-selection draws the union box too.
- Hover on a pin or wire: the whole net is highlighted (other wires dim to 40 %), tooltip "D13 · выход · HIGH" (running) or "D13 · цифровой, ШИМ нет" (stopped).
- Zoom indicator bottom-right of the canvas: percentage (click = 100 %) and "Вписать" (fit).

## 4. Interaction rules

### 4.1 Canvas navigation

| Gesture | Action |
| --- | --- |
| Mouse wheel | Zoom to cursor, 25 %–800 %, in 1.2× steps (Wokwi convention) |
| Middle-drag, or Space + left-drag | Pan |
| Shift + wheel | Pan horizontally |
| Ctrl+0 / Ctrl+9 | Fit scene / 100 % |
| Ctrl+= / Ctrl+− | Zoom in / out around the view center |

### 4.2 Selecting and editing parts

| Gesture | Action |
| --- | --- |
| Click | Select (deselects others); click on empty canvas deselects |
| Shift/Ctrl + click | Add / toggle in selection |
| Left-drag on empty canvas | Marquee selection (parts fully inside) |
| Ctrl+A / Esc | Select all / clear selection |
| Drag a selected part | Move the selection, snapped to the 0.1 inch grid; hold Alt to move freely |
| Arrow keys | Nudge one grid step (Shift: 10 steps) |
| R / Shift+R | Rotate 90° clockwise / counter-clockwise about the selection center |
| F | Flip horizontally |
| Delete / Backspace | Delete selection (wires attached to deleted parts go too) |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste (pasted at the cursor, ids renumbered, wires inside the selection kept) |
| Ctrl+D | Duplicate with a 0.2 inch offset |
| Right-click | Context menu: Повернуть, Отразить, Дублировать, Удалить, Свойства (part); Цвет ▸, Удалить (wire); Вставить, Вписать (canvas) |

### 4.3 Wires

- **Start**: click a pin (pins show rings on hover). A ghost wire follows the cursor as an orthogonal L (horizontal first; Tab toggles vertical-first).
- **Waypoints**: click on empty canvas to fix a corner; Backspace removes the last one.
- **Finish**: click another pin. Valid targets glow while wiring; a click on the start pin or Esc cancels. Wires always end on pins (ADR-008); several wires may share a pin.
- **Edit**: click selects a wire; drag a segment to move it perpendicular to itself (neighbors stay orthogonal); drag a corner to move it; double-click a segment to add a corner; Delete removes the wire.
- **Color**: automatic by net type (§3.1) until the user picks a swatch; the choice is stored in the project.
- **One click "connect to board"** *(nice-to-have, M5)*: on modules with VCC/GND/SDA/SCL, a context-menu item wires them to the board's matching pins.

### 4.4 Running

- Run (F5) saves files, autosaves the scene, builds and starts; Stop (Shift+F5) ends the run; Pause/Resume (F6); F10 steps one `loop()`.
- **While running, the scene's structure is locked** (no adding, deleting, moving or rewiring; properties read-only). The first such attempt shows a hint "Остановите симуляцию, чтобы изменить схему" next to the cursor. Interactive parts stay live: press buttons (mouse or their key), flip switches, drag pot knobs vertically (wheel for fine steps), type in the Serial Monitor. *(M8, approved 2026-11-03)* Encoder: the wheel or a drag around the knob turns it one detent per notch / 18°, a click without moving presses the shaft (holding still holds it). Keypad and IR remote: press and hold keys with the mouse. Joystick: drag the cap (it springs back on release), a click without moving presses it. Sensor readings (temperature, humidity, distance) stay editable in Properties (text field and slider) and change by a vertical drag on the part (Shift: the second reading) or the wheel; while running they are inputs, not saved and not undoable.
- Part keys work whenever no text field has focus.
- Changing the speed applies immediately; stepping while running pauses first.
- After a run ends, warning badges and the log stay until the next run.

### 4.5 Undo

Everything that changes the project is undoable (Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z): placement, moves (a drag is one step), rotation, deletion, wiring and wire edits, property edits, board change. Selection, zoom, panel layout and simulation inputs are not. History is per project and survives Save; it is cleared on close.

### 4.6 Files and projects

- Ctrl+N new, Ctrl+O open, Ctrl+S save scene and files; the title shows ● while anything is unsaved.
- Closing with unsaved changes asks once: Сохранить / Не сохранять / Отмена.
- Bundled examples open as copies under `Documents\ArduSim\Projects` (ADR-008); the recent list holds 10 projects.

### 4.7 Errors and feedback

| Situation | Feedback |
| --- | --- |
| Build error | Status "Ошибка сборки", Сборка tab selected, editor opens at the first error, squiggles in all affected files |
| Simulation warning | Log entry + ⚠ badge on the part + count on the Журнал tab; never a modal |
| Firmware crash | Banner with exception, `file:line` (click opens the editor there), last 20 API calls, Перезапустить |
| Firmware busy | Banner "Прошивка не отвечает" with Остановить |
| Runner exited | Banner with exit code and the Runner's output in an expandable block |
| Toolchain missing / corrupt | Banner with size and location; progress bar during download; Повторить on failure |
| External file change | Silent reload, or the editor's conflict bar when both sides changed |

### 4.8 Keyboard summary

| Keys | Command |
| --- | --- |
| F5 / Shift+F5 / F6 / F10 | Run / Stop / Pause-Resume / Step loop() |
| Ctrl+B | Build |
| Ctrl+N / Ctrl+O / Ctrl+S | New / Open / Save |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+F | Search the palette |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Toggle palette / properties / bottom panel |
| Ctrl+Tab / Ctrl+W | Next tab / close code tab |
| F8 / Shift+F8 | Next / previous diagnostic |
| R, F, Delete, Ctrl+D, arrows | Rotate, flip, delete, duplicate, nudge |
| Ctrl+0 / Ctrl+9 | Fit / 100 % |

## 5. Accessibility and localization

- Keyboard reachability for every control (Tab order: toolbar → palette → canvas → properties → bottom panel); the canvas supports Tab to cycle parts and Enter to open their properties.
- Contrast per §3.1; focus always visible; color never the only carrier of meaning (wires and states also have text in tooltips and the status pill).
- Windows text scaling and per-monitor DPI respected (PerMonitorV2, layout in device-independent pixels).
- All strings in `Resources/Strings.resx` (Russian neutral). Simulation log messages get resource keys by their code in M3 (today they arrive in English from Core).

## 6. Decisions confirmed

The owner accepted every proposal on 2026-11-02; the alternatives are kept for the record.

1. **Wheel zooms** (Wokwi), Space/middle-drag pans. Alternative: wheel pans and Ctrl+wheel zooms (Figma, VS Code).
2. **Scene locked while running**, interactive parts stay live. Alternative: any edit stops the run automatically.
3. **Title bar and toolbar in one 48 px row** with custom chrome. Alternative: standard Windows title bar plus a toolbar row (simpler, 32 px less canvas).
4. **Parts drawn as stylized real parts** (Wokwi-like). Alternative: schematic symbols.
5. **Accent teal `#2BC4A9`** (as in M2). Alternative: say which color you prefer.
