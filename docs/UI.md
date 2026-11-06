# ArduSim UI

- Status: Approved by the owner on 2026-11-02, including the five proposals in §6. The usability pass of 2026-11-05 (ADR-016) was approved by the owner the same day and is written in below.
- Date: 2026-11-02
- Scope: the M3 schematic editor and the shell it lives in. Items marked *(M4)* / *(M5)* are placed now so the layout does not change later, and built in those milestones.
- Fixed by the brief and ADR-004: WPF, our own theme (dark default, light variant), flat surfaces, one accent color, 8 px rhythm, Segoe UI, subtle motion only on state changes, Russian strings from `.resx`, custom split layout (no docking library), Wokwi as the UX reference for canvas and wiring.

## 1. Principles

1. **The canvas is the product.** Panels frame it; nothing floats over it except transient banners, menus, tooltips and one line of hint at its bottom edge (§3.6).
2. **Live state is visible where it happens.** An LED glows on the part, a level shows on the pin, a warning sits on the part it concerns — not only in a log.
3. **Loud, specific, actionable.** Every problem says what happened, where, and offers the next action (jump to line, show part, rerun, stop, re-download).
4. **Keyboard-first for frequent actions, mouse-first for placement.** Every command has a shortcut; every shortcut is shown in its tooltip.
5. **Calm by default.** Neutral surfaces, one accent for "primary / selected / focused", semantic colors only for states (running, warning, error).

## 2. Layout

### 2.1 Main window (1360 × 860, minimum 960 × 560; it reopens where it was, and never larger than the screen)

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
│ ┌──────────────────────────────────────────────────────────────────┐                        │
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
| Left panel | 240 px (180–360) | Pages: Детали (the palette, §2.3), Проект (the explorer, §2.9) and, in a project with the Экраны tab, Экраны (§2.10). Collapsible (Ctrl+1), resizable, width persisted. Below 1200 px of window width both side panels draw at their minimums; the saved widths stay. |
| Center tabs | rest | "Схема" is always the first tab and cannot close; "Экраны" comes next in a project with a display (§2.10); one tab per sketch file. The strip scrolls by wheel; a middle click closes a code tab. **Рядом** (Ctrl+\, off by default, remembered) shows the code to the right of the schematic with a movable divider: code tabs pick the file, Схема moves the focus to the canvas. Without it F7 flips between Схема and the last code tab, and Run returns to Схема. |
| Properties (right) | 280 px (220–420) | Collapsible (Ctrl+2), resizable, persisted. |
| Bottom tabs | 240 px (120–60 % of height) | Collapsible (Ctrl+3 or the ▾ button); selected tab and height persisted. On a window under 760 px high it folds itself and opens on the first output, warning or build error; the user's own toggle wins. Ctrl+Shift+\ hides all three panels and brings them back as they were. |
| Status bar | 24 px | Project path, board, last build time, toolchain, actual speed. |

Panels collapse to a 24 px strip with a vertical label, so they can be reopened by mouse. Splitters are 1 px lines with a 6 px hit area.

### 2.2 Toolbar, left to right

| Group | Content | Notes |
| --- | --- | --- |
| Project | `◆` mark, project name with ▾ menu (Новый, Открыть…, Недавние ▸, Примеры ▸, Сохранить, Сохранить как…, Открыть папку в проводнике, Параметры, О точности симуляции), `●` when unsaved | The only menu; everything else is a button. Alt opens it, and its items have access keys. |
| Build & run | Собрать (Ctrl+B), **Запуск** (F5, primary), Пауза/Продолжить (F6), Стоп (Shift+F5) | Icon + label for these four. While a run exists Запуск reads **Перезапуск** (flat, a dot when the code changed after the run started). Стоп also cancels a build or the compiler download; the status counts units and seconds while building. |
| Step | loop() (F10), 1 / 10 / 100 мс (F11, Shift+F11, Ctrl+F11) | Enabled while paused or running (a step pauses first). |
| Speed | 0.1× 0.5× 1× 2× 10× макс, as a compact dropdown | Applies live. |
| Board | Nano ▾ | Board picker; changing it rebuilds and is undoable. |
| Clock | Virtual time `12.345 с`, tabular digits | Click toggles s / ms. |
| Status | Pill: Готово · Сборка… · Работает · Пауза · Прошивка упала · Не отвечает | Color dot per §3.1; click opens the relevant panel. |

Without a project the toolbar holds only the mark and the menu.

Every button says what it does and its key, and a disabled one says why it is disabled. A narrow window folds the toolbar by stages, the first that fits: (1) Собрать, Пауза and Стоп lose their labels; (2) the step buttons become one Шаг ▾ menu; (3) sound and speed move into a ⋯ menu. Запуск, the board, the clock and the status never leave, and nothing wraps.

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

- Categories in the order a beginner needs them: Свет, звук, движение; Резисторы; Управление; Датчики; Дисплеи (folded at first); Модули. Headers show their row counts; folding is remembered.
- The search understands everyday Russian words («датчик», «экран», «мотор»), both keyboard layouts and small typos, and says so when nothing matches. `A` on the canvas or Ctrl+F opens it; ↑↓ move the highlight, Enter places the highlighted part and returns to the canvas, Esc clears it, then leaves.
- A row's tooltip: name, one line about the part, its pins.
- Drag a row onto the canvas: a ghost of the part at the current zoom follows the cursor and snaps to the grid; Esc cancels.
- Double-click or Enter adds the part near the center of the view, on the nearest spot that covers no other part, selected.
- Presets appear as their own rows (e.g. "LCD 1602 I2C" = LCD with interface I2C, ADR-006) *(M4)*.

The toolbar has a speaker toggle next to the speed selector: buzzers sound or not, remembered between sessions *(M11)*.

### 2.4 Properties panel

```
┌────────────────────────────┐
│ ◖ led1              LED    │   id is editable (unique, [a-z0-9-]); type on the right
├────────────────────────────┤
│ Цвет      ● ● ● ● ● ●      │   choices: ≤ 4 → segmented control, colors → swatches, else dropdown
│ Сопротивл.[ 220     ] Ω    │   numbers: unit suffix, ↑/↓ and focused wheel step, Enter commits and returns to the canvas, Esc reverts; 10k, 4,7к, 1М are understood
│ Клавиша   [ Пробел  ]      │   key capture field: "press a key"
├────────────────────────────┤
│ Пины                       │
│  A   → board.D13           │   click a row → highlights that net
│        HIGH · 4,67 В · 13 мА │   while running, on a line of its own: level, voltage, current
│  C   → board.GND   0 В     │
├────────────────────────────┤
│ [⟳ Повернуть] [⇋ Отразить] [🗑] │
└────────────────────────────┘
```

- Nothing selected → project properties: library paths, and Очистить EEPROM, which asks on its first click and removes `eeprom.bin` and `preferences.json`. Board, speed and line ending are in the toolbar and the Monitor. In a bundled example a block above says what it does and what to try, with Показать код and Начать заново (a new copy; yours stays).
- Several parts selected → common properties only, with "—" for mixed values; an edit changes all of them, in one undo step.
- A click on a pin row lights that net on the schematic.
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

- AvalonEdit, Cascadia Mono 13, tab = 2 spaces (Arduino convention), auto-indent, bracket matching, current-line highlight. Brackets and quotes close themselves, and a closer typed where one stands steps over it.
- Ctrl+F finds in the file when the code has the focus (elsewhere: the part search on the schematic, the open file's find on a code tab); Ctrl+H replaces, Ctrl+G or Ctrl+L goes to a line, Ctrl+/ comments, Ctrl+T or Shift+Alt+F formats (clang-format), Alt+↑↓ moves lines, Ctrl+Shift+D duplicates. Right click shows them.
- clangd (ADR-016) answers while typing: completion after `.`, `->`, `::`, `#include <` and two letters (Ctrl+Space by hand), signatures after `(`, hover, F12 or Ctrl+click for the definition. Its answers never replace the build's diagnostics.
- Build errors: red squiggle and gutter ✖; warnings amber ⚠; F8 / Shift+F8 next / previous diagnostic; Run with errors opens the first one.
- The sketch folder is the source of truth: files reload silently when unchanged in the editor, keeping the caret, and Ctrl+Z brings the text back; the conflict bar appears only when both changed, with Оставить мою версию as its main button. While it is open, Save, Build and Run stop at that file and ask for a version first, so the other version is never written over.
- Run and Build save open files first (as the Arduino IDE does); Ctrl+S saves.

### 2.6 Bottom tabs

| Tab | Content |
| --- | --- |
| Монитор порта | Output (mono, wrapped, optional time stamps), input line, line ending segmented, Очистить. Автопрокрутка follows new text until the user scrolls away or selects text; scrolling back to the end, or ticking the box, resumes it. On boards with several UARTs, a port list picks what the Monitor and Plotter show and where input goes *(M11)*. |
| Плоттер *(M5)* | Live chart of numbers parsed from Serial lines (Arduino Serial Plotter format), auto-scaling, pause (lines that go by leave a break in the plot; a new run clears it). A click on a legend item hides that series and takes it out of the scale. |
| Сборка | Left: parsed diagnostics (icon, file:line, a plain Russian line when a rule knows the message, the message), click opens the editor at the location; Исправить where the compiler names a fix. Right: raw compiler output. Summary line with time and cache hits. The tab shows the error and warning counts. |
| Журнал | Simulation messages with a severity icon and time, severity filter (Все / Предупреждения / Ошибки), "Показать" jumps to and flashes the part. New rows are followed while the last row is in view; Копировать строку in the context menu or Ctrl+C. The tab shows a count badge for unseen warnings. |
| Диаграмма *(M11)* | Logic analyzer. Top: Запись (checkbox), Очистить, + Сигнал (board pins), + Шина (the board's I2C, SPI and UART; «Другие выводы…» opens a form for bit-banged buses on any pins), the scale and Δ. Left: names, ✕ removes a row. Right: time ruler, one waveform per pin, one row of labeled frames per bus. Wheel over the names scrolls rows; wheel over the plot zooms about the pointer, or about the latest time while following, which zooming does not stop; Вписать всё and К последним are beside the scale; with the keyboard, ←/→ pan, + and − zoom, Home fits all, End goes to the latest; a drag pans, a click sets the marker Δ measures from. A click on a wire on the running schematic adds its board pins. Records while the tab is open and Запись is on; time not recorded stays empty, shaded and captioned «не записано». A new run starts over at time 0. |

### 2.7 Start view (no project open)

```
┌──────────────────────────────────────────────────────────────────┐
│  Новый проект [Проект 3   ] Другая папка…       [Открыть папку…] │
│  [Nano] [Uno] [Mega] [Pro Mini] [ESP32] [NodeMCU] [Wemos]        │  one tile per board: a click creates the project
│                                                                  │
│  Недавние            Примеры [Поиск…]  Все 51|Arduino|ESP32|8266 │
│  Blink               С чего начать                               │
│  Button              [Мигание] [Кнопка] [Потенциометр] …         │  by topic, with Russian titles and a line each
└──────────────────────────────────────────────────────────────────┘
```

New project: a name and a click on a board's tile create `Документы\ArduSim\Projects\<name>` with `project.simproj.json` and `sketch/<name>.ino` with empty `setup()`/`loop()`, and open the scene with the board placed. Другая папка… picks the place. Ctrl+N inside a project comes back here, with the name ready to type. On a first launch the Недавние column suggests the first steps instead.

Открыть папку…, or a folder dropped on the window: a project opens; a folder with `platformio.ini` imports in place *(M11)*; a folder with `.ino` files gets a project file beside them and stays where it is; a folder with neither offers to create a project there.

### 2.8 Banners, dialogs, toasts

- **Banner** (top of the canvas, or of the start view, max 720 px, dismissible, also by Esc): run outcomes that need a decision — crash (exception, `file:line`, last API calls, Перезапустить / Открыть строку; the details stay in Журнал), firmware busy (Остановить; it hides itself when the sketch comes back), Runner exited (exit code, stderr in an expandable block), toolchain needed or failed (progress bar and Отмена while downloading; Скачать), a folder without a project (Создать проект здесь).
- **Toast** (bottom-right, 3 s, no action required): "Проект сохранён", "Файл перезагружен с диска".
- **Modal dialogs** only for: file and folder pickers, unsaved-changes on close, Settings, the library dialog (§2.9), "О точности симуляции" (a side sheet listing the fidelity limits of brief §3.1).
- **Part badges**: an amber ⚠ on a part with an active warning (floating input, short); hover shows the message, click → the log entry. Cleared on the next run.

### 2.9 Project explorer

The Проект page of the left panel (Ctrl+Shift+E). Groups, each foldable:

- **Скетч**: the sketch's files as a tree of its folders. In each folder its .ino files come first, then its subfolders, then the other files, by name. A file has a badge for what it is (`ino`, `c++`, `c`, `h`), here and on its code tab. As in Visual Studio a file holds what is in it: the header of its name, then its functions, types and variables from clangd, a class with its members, each with a badge and a click to its line. The file on screen is open in the tree; the chevron or ← → opens any other or closes this one, and a click on the row itself opens the file. A header with a source of its name sits under that source; alone it is a row of its folder. Alt+O and the file's menu go between a header and its source. A new `.h` starts with `#pragma once`, and a new source with the `#include` of the header of its name. A folder shows how many files it holds; a click or ← → folds it, and each project remembers what is folded. A file coming to the front (a tab, go to definition) unfolds its folders and scrolls into view. A file's menu has Открыть, Перейти к заголовку or к реализации, Новый файл здесь…, Переименовать, Удалить в корзину (no question: it can be brought back) and Показать в проводнике; a folder's has Новый файл здесь…, Свернуть все папки, Развернуть все папки and Показать в проводнике. The main file, named after the project, cannot be renamed or deleted here. Новый файл… is at the top and in the + of the tab strip, and its name may include a folder (src/app.cpp), but stays inside the sketch.
- **Схема**: the parts by id and kind; a click selects one and brings it into view. The row of the open file or the selected part is marked.
- **Библиотеки**: what is in the project's `libs` folder and what the last build used, each saying where it comes from (в проекте, встроенная, Документы, папка). A click opens its main header; the ✕ on a project's library moves it to the Recycle Bin. **Добавить…** on the group's header opens the library dialog.

The library dialog (ADR-018) searches the Arduino catalog by name, header or a word of the description. A row has the name, what it does, its size and author, a version list and Установить, which also brings the libraries it needs, as the row says. A bundled library says it is built in and cannot be installed; one in the project offers Обновить or Заменить, and removal; one for other boards says so and can still be installed. Из папки… and Из ZIP… stay at the bottom, for when there is no network or the library is not in the catalog. A library goes into the project's `libs` folder. A download shows its progress and can be cancelled; nothing changes in the project until it has arrived, been checked and unpacked. The compiler's "file not found" has a Найти библиотеку… button that opens this dialog with the header searched.

### 2.10 Экраны

The owner's decision of 2026-11-06 (ADR-019). A center tab beside Схема, there when the schematic has a 1602, 2004 or OLED display, the project has `screens.json`, or the sketch's code draws screens. It does not close. Opening it brings the left panel's Экраны page; leaving it brings back the page there was. With Рядом on it takes the schematic's place, with the code to its right. Запуск goes to Схема.

- **Карта**: each screen is a card with its name and a picture of it, the start screen marked ▶; each transition is an arrow labelled with what starts it, in the accent color for the selected screen. Drag a card to place it, the background to pan, the wheel to zoom (at the pointer); Показать всю карту fits it. A double-click on a card opens that screen, on empty space makes a new one there. A click on a label selects that transition. A card has a handle on its right edge: drag it to another card to make a transition. The right button has Открыть экран, Показать в коде, Сделать стартовым, Дублировать, Удалить on a card; Новый экран здесь, Новое меню здесь, Показать всю карту elsewhere. «С любого экрана» stands for what works everywhere.
- **Экран**, a construction surface: the selected screen at a whole multiple of its dots, with a faint line every eight pixels on an OLED. The toolbar's Текст, Значение (and Линия, Рамка, Прямоугольник on an OLED) add at the next free place on a click, or where they are dropped. A click selects an element; dragging moves it by whole character cells or pixels, drawn as it will be; the square handle resizes a shape or moves a line's end, and a value's field in a generated project. A double-click, F2 or Enter edits text in place. ← ↑ → ↓ move by one, Ctrl+D duplicates, Delete removes. Elements have a faint outline, the selected one the accent color. Text is white, values amber, what is drawn only under a condition is dimmer, and of alternatives only the first is drawn.
- **Left panel**: the screens by name, the start one marked, each with the number of transitions leaving it; a double-click opens the screen. + Экран is on top, and + Меню in a generated project.
- **Right panel**: the screen's or element's fields, then Показать в коде, По центру, Дублировать, Сделать стартовым and Удалить where they apply, then the menu's items, the transitions leaving this screen and those from any screen, each with its + where one can be added.
- **A project from code** is built the same way, and everything done on the tab goes straight into the source, in its tab, as new code in the sketch's own style or a change of one token, with undo (ADR-019). A screen's constant is its name here. What the sketch's shape does not allow (no enum of screens, no switch that draws, no key handler; what hangs on a condition) has no button or a line saying why. A transition's trigger there is a key. The tab reads the code again half a second after typing stops, and at the ↻ button.
- **A generated project**: the status line says where things live; Показать код opens `ui_hooks.cpp`, where values and actions are written. A menu's controls are chosen from the schematic's inputs and can be changed. A trigger is a button, a keypad's key, an encoder's turn or press, a time in ms, or an event a sketch sends with `uiSend`.
- **Without screens**: one sentence and Создать экраны, which creates the model and the code and connects the sketch to it. A sketch's own display object stays its own.
- Ctrl+Z and Ctrl+Y work on the tab's edits while it is in front. A notice above the status line says why an edit was not made, in the warning color.

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
| `TextHint` | `#8A92A3` | `#697080` | placeholders, line numbers, minor captions (4.5:1) |
| `BorderInput` | `#6B7384` | `#8A92A0` | edges of fields and sliders (3:1) |
| `Accent` | `#2BC4A9` | `#0B7A69` | primary button, selection, focus ring, active tab |
| `OnAccent` | `#0B1A17` | `#FFFFFF` | text on accent |
| `Running` | `#3AE06A` | `#1E9E4A` | status dot "Работает" |
| `Warning` | `#F0B44C` | `#946200` | warnings, "Пауза", "Не отвечает" |
| `Error` | `#F0605A` | `#C53030` | errors, crash |

Checked against WCAG AA (4.5:1 for 13 px text): `TextPrimary`, `TextSecondary`, `Accent`, `Warning` and `Error` on `Surface` and `Background`, and `OnAccent` on `Accent`, pass in both themes (lowest: dark `Error` on `SurfaceRaised` 4.64, light `Accent` on `Background` 4.81). `TextDisabled` is exempt, as WCAG allows for disabled controls.

**Wires** (stroke 2.2 canvas units ≈ 2 px at 100 %; a casing in the canvas color makes an upper wire show a gap where it crosses another): power `#E54848`, ground `#5A606E` (dark) / `#2D3138` (light), I2C fixed SDA `#3E8BFF` and SCL `#F2C94C` *(M4)*, other signals by their board pin, so they stay put across edits (green, blue, orange, purple, pink), selectable from 8 swatches (green, blue, yellow, orange, purple, pink, white, grey). Wire colors are never the only cue: hovering shows the net name and its pins.

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
- Focus: 2 px `Accent` outline, 2 px offset, on every focusable element; never removed. Each style names this ring, because WPF otherwise takes the system's dotted one, which cannot be seen on the dark theme. The canvas draws it inside its edge.

### 3.4 Icons

Fluent UI System Icons, Regular, 20 px in the toolbar's build-and-run group and 16 px elsewhere; Filled only for an active toggle. One icon per concept across the app (e.g. the same ▶ for Run in toolbar, menu and banner).

### 3.5 Motion

- Banner appearance: 160 ms ease-out. Nothing else animates yet; hover at 100 ms and panel collapse at 160 ms are approved and in `docs/UI-BACKLOG.md`.
- Simulation state is never animated or eased: an LED is as bright as the snapshot says, the moment it arrives.
- When Windows "Show animations" is off, all transitions are instant.

### 3.6 Canvas rendering

- Background `Canvas` with a dot every 0.1 inch (the pin pitch); dots fade out below 50 % zoom.
- Parts are flat, slightly stylized drawings of the real part (Wokwi-like, not schematic symbols), drawn from vector art in their definitions (ADR-007), crisp at every zoom.
- Board pins: gold rings with labels, which grow a little at far zoom; other parts' pins appear as small rings on hover and while wiring. Parts without leads (displays on a PCB, seven-segment, MAX7219, WS2812) always show their rings.
- Pin names are placed from the pins by `PinLabels`, on every part: centered on each pin in a row, beside it in a column, squeezed to the pitch rather than touching. Part art never has pin names typed into it.
- Text on a part is never mirrored or upside down, however the part is flipped or turned; a display's picture is not mirrored, and turns with its part.
- While running: a dot on every driven board pin (grey LOW, green HIGH, accent PWM, amber tone), wires of a HIGH net with a bright core, of a LOW net dimmed, of a PWM or tone net with dashes; a 2 px bar in the status color above the center.
- Currents on the schematic *(M12)*: while running, an LED shows its current above it and a resistor its voltage and current below it, upright however the part is turned, when any flows; Параметры → Токи на схеме hides them. Everything else with a current says so where the voltage is said: pin and wire tooltips and the Свойства pin rows.
- **One line of hint** at the bottom left, chosen by the state: only the board placed, wiring, a wire selected, a part selected, running. It can be switched off in Параметры.
- Live state: LED glow scaled by brightness, pushbutton cap pressed, slide-switch knob position, pot knob angle, buzzer frequency label, encoder knob angle, keypad and remote keys held, joystick cap leaning, sensor readings above the part *(M8)*, servo horn angle, relay LED and contact, seven-segment and TM1637 digits, MAX7219 matrices and WS2812 LEDs from their frames *(M9)*, displays from framebuffers *(M4/M5)*, color TFTs dimmed by their backlight, the Nokia 5110 glass darkening with its contrast, grayscale OLED levels *(M10)*: LCD with a visible 5×8 dot grid and backlight variants, OLED with pixel glow and color variants, nearest-neighbor scaling.
- Selection: 1 px `Accent` outline 3 px outside the part's bounds; multi-selection draws the union box too.
- Hover on a pin or wire: the whole net is highlighted (other wires dim to 40 %), tooltip with the pin's kind and bus role, while running its level, voltage and current (a board pin says whether it gives or takes it, PWM gives the average and the peak), and what it joins.
- Zoom indicator bottom-right of the canvas: percentage (click = 100 %) and "Вписать" (fit).

## 4. Interaction rules

### 4.1 Canvas navigation

| Gesture | Action |
| --- | --- |
| Mouse wheel | Zoom to cursor, 25 %–800 %, 1.2× per notch and in proportion for a touchpad's small steps (Wokwi convention) |
| Middle-drag, right-drag, or Space + left-drag | Pan (right button released in place: the context menu) |
| Arrows with nothing selected | Pan |
| A touchpad | Параметры → Прокрутка: the wheel pans and Ctrl+wheel or a pinch zooms |
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
| Right-click | Context menu: Повернуть, Отразить, Дублировать, Удалить, Свойства (part); Цвет ▸, Сбросить маршрут, Разложить провода, Удалить (wire); Добавить деталь, Вставить, Разложить провода, Вписать, Клавиши и жесты (canvas). While wiring it cancels the wire instead. |
| Tab / Shift+Tab | Next / previous part, brought into view |
| Enter | The keyboard goes to the selected part's properties |
| A | The part search |
| Esc during a drag | Back to where it started, with no undo entry |

These commands work whichever panel last had the focus, except in text fields and lists.

### 4.3 Wires

- **Layout** (ADR-020): the editor lays wires out: straight out of each pin through its part's nearest edge, at right angles, around part bodies, each net on its own track, the 0.1" grid first and the lines halfway between where it gets crowded. When a part moves, rotates, flips or changes size, what ends on it is laid out anew at once; a wire with both ends on what moves goes along; a wire a part comes to lie over moves aside. **Разложить** on the canvas (and in its menu) lays out all of them, corners set by hand too, in one undo step.
- **Start**: click a pin (pins show rings on hover; the nearest pin wins). A ghost wire follows the cursor as an orthogonal L (horizontal first; Tab toggles vertical-first), and over a target pin shows the route the wire will take.
- **Waypoints**: click on empty canvas to fix a corner; Backspace removes the last one.
- **Finish**: click another pin, or press on one pin and release on another (past 10 px, so a shaky click never joins neighbours). Valid targets glow while wiring; a click on the start pin, Esc or the right button cancels. Without a corner set by hand (or the bend Tab asks for), the wire is laid out. Wires always end on pins (ADR-008); several wires may share a pin.
- **Edit**: click selects a wire; drag a segment to move it perpendicular to itself (neighbors stay orthogonal); drag a corner to move it; double-click a segment to add a corner, or a corner to remove it: the route is then the user's, saved in the project, and stays until one end moves on its own. Drag an end (the round handles) to another pin; Сбросить маршрут in its menu gives the wire back to the editor; Delete removes the wire.
- **Coinciding pins**: a part's pin moved or placed exactly on another part's pin is connected, by a zero-length wire in the same undo step.
- **Color**: automatic by net type (§3.1) until the user picks a swatch; the choice is stored in the project.
- **One click "connect to board"** *(nice-to-have, M5)*: on modules with VCC/GND/SDA/SCL, a context-menu item wires them to the board's matching pins.

### 4.4 Running

- Run (F5) saves files, autosaves the scene, builds and starts, from the schematic; Stop (Shift+F5) ends the run; Pause/Resume (F6); F10 steps one `loop()`. While a run exists F5 restarts it with the latest code.
- **While running, the scene's structure is locked** (no adding, deleting, moving or rewiring; properties read-only). An attempt to change it shows a hint "Идёт симуляция, схема заблокирована. Остановить: Shift+F5" next to the cursor; a plain click only selects. A click selects sensors and parts that are not inputs, so their properties can be read and readings changed; a click on empty canvas clears the selection; buttons, switches, knobs and keys keep their click as input. Interactive parts stay live: press buttons (mouse or their key), flip switches, drag pot knobs vertically (wheel for fine steps), type in the Serial Monitor. *(M8, approved 2026-11-03)* Encoder: the wheel or a drag around the knob turns it one detent per notch / 18°, a click without moving presses the shaft (holding still holds it). Keypad and IR remote: press and hold keys with the mouse. Joystick: drag the cap (it springs back on release), a click without moving presses it. Sensor readings (temperature, humidity, distance) stay editable in Properties (text field and slider) and change by a vertical drag on the part (Shift: the second reading) or the wheel; while running they are inputs, not saved and not undoable.
- Part keys work whenever no text field has focus. Ctrl+click latches a pushbutton or a keypad or remote key until the next click on it. An input part selected with Tab takes the keyboard: arrows, PageUp/PageDown, Home, End on a potentiometer; arrows on an encoder, joystick or sensor; Space presses a shaft; 0-9, A-D, * and # on a keypad.
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
| Build error | Status "Ошибка сборки", Сборка tab selected, editor opens at the first error, squiggles in all affected files. The next good build puts the panels back, unless the user moved on. A good build alone says so in a toast. |
| Simulation warning | Log entry + ⚠ badge on the part + count on the Журнал tab; never a modal |
| Firmware crash | Banner with exception, `file:line` (click opens the editor there), last 20 API calls, Перезапустить |
| Firmware busy | Banner "Прошивка не отвечает" with Остановить |
| Runner exited | Banner with exit code and the Runner's output in an expandable block |
| Toolchain missing / corrupt | Banner with size and location; progress bar during download; Повторить on failure |
| External file change | Silent reload, or the editor's conflict bar when both sides changed |

### 4.8 Keyboard summary

| Keys | Command |
| --- | --- |
| F5, Ctrl+U / Shift+F5 / F6 / F10 | Run / Stop / Pause-Resume / Step loop() |
| F11 / Shift+F11 / Ctrl+F11 | Step 1 / 10 / 100 ms |
| Ctrl+B, Ctrl+R, Ctrl+Shift+B | Build |
| F1 | Keys and gestures: a sheet with a filter that does not block the window |
| F7 | Schematic and the last code tab |
| Ctrl+\ | Code beside the schematic |
| Ctrl+Shift+E / Ctrl+Shift+M | Project explorer / Monitor with its input line |
| Alt+O | Between a header and its source |
| Ctrl+F6, Ctrl+Shift+F6 | Next, previous pane |
| Ctrl+Shift+\ | Hide all panels, and bring them back |
| Alt | The project menu |
| Ctrl+Shift+S | Save the project as |
| Ctrl+N / Ctrl+O / Ctrl+S | New / Open / Save |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+F | Find in the code when it has the focus or its tab shows; otherwise search the palette; on the start view, the examples |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Toggle palette / properties / bottom panel |
| Ctrl+Tab, Ctrl+Shift+Tab / Ctrl+W | Next, previous tab / close code tab |
| F8 / Shift+F8 | Next / previous diagnostic |
| A, R, F, Delete, Ctrl+D, arrows | Find a part, rotate, flip, delete, duplicate, nudge |
| Ctrl+0 / Ctrl+9 | Fit / 100 % |

## 5. Accessibility and localization

- Keyboard reachability for every control (Tab order: toolbar → palette → canvas → properties → bottom panel); the canvas supports Tab to cycle parts and Enter to open their properties.
- Contrast per §3.1; focus always visible; color never the only carrier of meaning (wires and states also have text in tooltips and the status pill).
- Windows text size scales the interface's text, read at startup and on change (the code editor keeps its own size); per-monitor DPI respected (PerMonitorV2, layout in device-independent pixels).
- All strings in `Resources/Strings.resx` (Russian neutral). Simulation log messages get resource keys by their code in M3 (today they arrive in English from Core).

## 6. Decisions confirmed

The owner accepted every proposal on 2026-11-02; the alternatives are kept for the record.

1. **Wheel zooms** (Wokwi), Space/middle-drag pans. Alternative: wheel pans and Ctrl+wheel zooms (Figma, VS Code).
2. **Scene locked while running**, interactive parts stay live. Alternative: any edit stops the run automatically.
3. **Title bar and toolbar in one 48 px row** with custom chrome. Alternative: standard Windows title bar plus a toolbar row (simpler, 32 px less canvas).
4. **Parts drawn as stylized real parts** (Wokwi-like). Alternative: schematic symbols.
5. **Accent teal `#2BC4A9`** (as in M2). Alternative: say which color you prefer.
