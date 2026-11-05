using System.Collections.ObjectModel;
using System.Globalization;
using ArduSim.App.Localization;
using ArduSim.Core.Boards;
using ArduSim.Core.Tracing;
using ArduSim.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>One row of the diagram: a pin's waveform, or a bus's decoded frames.</summary>
/// <param name="Pin">The board pin, or -1 for a bus row.</param>
public sealed record TimelineRow(string Title, int Pin, BusDecoder? Decoder);

/// <summary>An entry of the «+ Шина» menu.</summary>
public sealed record BusChoice(string Title, Func<BusDecoder> Create);

/// <summary>
/// The Диаграмма tab (ADR-014): the rows, the recorded signals and the time window. Recording runs
/// while the tab is open and its button is on; the window follows the latest time until the user pans.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    public const double MinNsPerPixel = 1;
    public const double MaxNsPerPixel = 200_000_000;

    private BoardProfile? _board;
    private double _viewWidth = 800;

    /// <summary>Очистить dropped every pin's level during a run: the Runner has to say them again.</summary>
    public event Action? Cleared;

    public TraceStore Store { get; } = new();

    public ObservableCollection<TimelineRow> Rows { get; } = [];

    /// <summary>Bumped on every change the view has to redraw for.</summary>
    [ObservableProperty]
    public partial long Version { get; private set; }

    /// <summary>The Запись button.</summary>
    [ObservableProperty]
    public partial bool Recording { get; set; } = true;

    /// <summary>The time at the left edge of the plot.</summary>
    [ObservableProperty]
    public partial ulong ViewStart { get; private set; }

    [ObservableProperty]
    public partial double NsPerPixel { get; private set; } = 1_000_000;

    /// <summary>The window keeps the latest time in view.</summary>
    [ObservableProperty]
    public partial bool Following { get; private set; } = true;

    [ObservableProperty]
    public partial ulong? Marker { get; private set; }

    [ObservableProperty]
    public partial ulong? Cursor { get; private set; }

    /// <summary>The readout: time per division, and the distance from the marker to the cursor.</summary>
    public string Readout
    {
        get
        {
            string scale = Strings.Format("Timeline_PerDivision", FormatTime(TickStep(NsPerPixel)));
            return Marker is { } marker && Cursor is { } cursor
                ? scale + "   Δ " + FormatTime((ulong)Math.Abs((long)cursor - (long)marker))
                : scale;
        }
    }

    public bool HasGaps => Store.HasGaps;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>What «+ Сигнал» offers: the board's pins not yet shown.</summary>
    public IReadOnlyList<BoardPin> PinChoices => _board is null ? [] : [.. _board.Pins.Where(p => Rows.All(r => r.Pin != p.Number))];

    /// <summary>What «+ Шина» offers: the board's buses on their default pins, each once.</summary>
    public IReadOnlyList<BusChoice> BusChoices
    {
        get
        {
            if (_board is not { } board)
            {
                return [];
            }

            var choices = new List<BusChoice>();
            foreach (I2cPins pins in board.I2c)
            {
                if (pins.Sda >= 0 && pins.Scl >= 0)
                {
                    choices.Add(new BusChoice(Strings.Format("Timeline_BusI2c", board.PinName(pins.Sda), board.PinName(pins.Scl)), () => new I2cDecoder(pins.Sda, pins.Scl)));
                }
            }

            foreach (SpiPins pins in board.Spi)
            {
                if (pins.Mosi >= 0 && pins.Sck >= 0)
                {
                    choices.Add(new BusChoice(Strings.Format("Timeline_BusSpi", board.PinName(pins.Mosi), board.PinName(pins.Sck)), () => new SpiDecoder(pins.Mosi, pins.Miso, pins.Sck)));
                }
            }

            int port = 0;
            foreach (UartPins pins in board.Uarts ?? [])
            {
                string name = port == 0 ? "Serial" : "Serial" + port.ToString(CultureInfo.InvariantCulture);
                if (pins.Tx >= 0)
                {
                    choices.Add(new BusChoice(Strings.Format("Timeline_BusUartTx", name, board.PinName(pins.Tx)), () => new UartDecoder(pins.Tx, 9600)));
                }

                if (pins.Rx >= 0)
                {
                    choices.Add(new BusChoice(Strings.Format("Timeline_BusUartRx", name, board.PinName(pins.Rx)), () => new UartDecoder(pins.Rx, 9600)));
                }

                port++;
            }

            return choices;
        }
    }

    /// <summary>The kinds the «Другие выводы» form can decode from real edges.</summary>
    public IReadOnlyList<string> CustomKinds { get; } = ["I2C", "SPI", "UART"];

    public IReadOnlyList<int> CustomBauds { get; } = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    /// <summary>Every pin of the board, for the form.</summary>
    public IReadOnlyList<BoardPin> AllPins => _board?.Pins ?? [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomFirstLabel), nameof(CustomSecondLabel), nameof(CustomHasSecond), nameof(CustomIsUart), nameof(CustomIsSpi))]
    public partial string CustomKind { get; set; } = "SPI";

    [ObservableProperty]
    public partial BoardPin? CustomFirst { get; set; }

    [ObservableProperty]
    public partial BoardPin? CustomSecond { get; set; }

    /// <summary>The optional chip select of an SPI row.</summary>
    [ObservableProperty]
    public partial BoardPin? CustomSelect { get; set; }

    [ObservableProperty]
    public partial int CustomBaud { get; set; } = 9600;

    public string CustomFirstLabel => CustomKind switch
    {
        "I2C" => "SDA",
        "SPI" => "MOSI",
        _ => Strings.Get("Timeline_Line"),
    };

    public string CustomSecondLabel => CustomKind == "I2C" ? "SCL" : "SCK";

    public bool CustomHasSecond => CustomKind != "UART";

    public bool CustomIsUart => CustomKind == "UART";

    public bool CustomIsSpi => CustomKind == "SPI";

    /// <summary>The form's Добавить: a row that decodes the real edges on the chosen pins.</summary>
    [RelayCommand]
    public void AddCustomBus()
    {
        if (CustomFirst is not { } first || (CustomHasSecond && CustomSecond is null))
        {
            return;
        }

        (string title, BusDecoder decoder) = CustomKind switch
        {
            "I2C" => (Strings.Format("Timeline_BusI2c", first.Name, CustomSecond!.Name), (BusDecoder)new I2cDecoder(first.Number, CustomSecond.Number)),
            "SPI" => (Strings.Format("Timeline_BusSpi", first.Name, CustomSecond!.Name), new SpiDecoder(first.Number, -1, CustomSecond.Number, CustomSelect?.Number ?? -1)),
            _ => (Strings.Format("Timeline_BusUart", first.Name, CustomBaud), new UartDecoder(first.Number, (uint)CustomBaud)),
        };
        AddBus(new BusChoice(title, () => decoder));
    }

    /// <summary>The LogicAnalyzerDemo example's rows: every bus, its pins, and the byte it shifts out by hand on D4 and D3.</summary>
    public void AddDemoRows()
    {
        foreach (BusChoice bus in BusChoices)
        {
            AddBus(bus);
        }

        foreach (string pin in (string[])["D3", "D4", "D5", "D10"])
        {
            if (_board?.FindPin(pin) is { } boardPin)
            {
                AddPin(boardPin);
            }
        }

        CustomKind = "SPI";
        CustomFirst = _board?.FindPin("D4");
        CustomSecond = _board?.FindPin("D3");
        AddCustomBus();
    }

    /// <summary>The project's board changed: the rows belong to the old board's pins.</summary>
    public void SetBoard(BoardProfile? board)
    {
        if (board?.Id == _board?.Id)
        {
            return;
        }

        _board = board;
        foreach (TimelineRow row in Rows)
        {
            if (row.Decoder is { } decoder)
            {
                Store.RemoveDecoder(decoder);
            }
        }

        Rows.Clear();
        CustomFirst = CustomSecond = CustomSelect = null;
        Reset();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AllPins));
    }

    /// <summary>A new run: the old recording goes, the rows stay.</summary>
    public void Reset()
    {
        Store.Clear();
        ViewStart = 0;
        Following = true;
        Marker = null;
        Cursor = null;
        Touch();
    }

    [RelayCommand]
    public void AddPin(BoardPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (Rows.All(r => r.Pin != pin.Number))
        {
            Rows.Add(new TimelineRow(pin.Name, pin.Number, null));
            OnPropertyChanged(nameof(IsEmpty));
            Touch();
        }
    }

    /// <summary>A wire was clicked on the running schematic: its board pins join the diagram.</summary>
    public void AddPins(IEnumerable<int> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        foreach (int number in pins)
        {
            if (_board?.FindPin(number) is { } pin)
            {
                AddPin(pin);
            }
        }
    }

    [RelayCommand]
    public void AddBus(BusChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        BusDecoder decoder = choice.Create();
        Store.AddDecoder(decoder);
        Rows.Add(new TimelineRow(choice.Title, -1, decoder));
        OnPropertyChanged(nameof(IsEmpty));
        Touch();
    }

    public void RemoveRow(TimelineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Decoder is { } decoder)
        {
            Store.RemoveDecoder(decoder);
        }

        Rows.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
        Touch();
    }

    [RelayCommand]
    private void Clear()
    {
        Reset();
        Cleared?.Invoke();
    }

    /// <summary>A block from the Runner, and the snapshot's virtual time.</summary>
    public void Append(TraceBlock? block, ulong now)
    {
        if (block is null)
        {
            if (now > Store.End)
            {
                Store.Append([], [], false, now);
                Follow();
                Touch();
            }

            return;
        }

        var samples = new PinSample[block.PinTimes.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = new PinSample(block.PinTimes[i], block.PinData[2 * i], (SignalLevel)block.PinData[(2 * i) + 1], block.PinValues[i]);
        }

        var events = new BusEvent[block.BusTimes.Length];
        for (int i = 0; i < events.Length; i++)
        {
            int at = 6 * i;
            events[i] = new BusEvent(block.BusTimes[i], block.BusDurations[i], (BusEventKind)block.BusData[at], block.BusData[at + 1], block.BusData[at + 2], block.BusData[at + 3], block.BusData[at + 4], block.BusData[at + 5]);
        }

        Store.Append(samples, events, block.Overflowed, now);
        Follow();
        Touch();
    }

    /// <summary>The plot's width in pixels, from the view.</summary>
    public void SetViewWidth(double width)
    {
        _viewWidth = Math.Max(1, width);
        Follow();
    }

    /// <summary>Wheel: zooms about the time under the pointer.</summary>
    /// <param name="x">Pixels from the plot's left edge.</param>
    /// <param name="factor">Above 1 zooms out.</param>
    public void ZoomAt(double x, double factor)
    {
        if (Following)
        {
            // Following stays on: the zoom is about the latest time, at the right edge.
            NsPerPixel = Math.Clamp(NsPerPixel * factor, MinNsPerPixel, MaxNsPerPixel);
            ViewStart = (ulong)Math.Max(0, Store.End - (_viewWidth * NsPerPixel));
            Touch();
            return;
        }

        double pinned = ViewStart + (x * NsPerPixel);
        NsPerPixel = Math.Clamp(NsPerPixel * factor, MinNsPerPixel, MaxNsPerPixel);
        ViewStart = (ulong)Math.Max(0, pinned - (x * NsPerPixel));
        Touch();
    }

    /// <summary>Вписать всё: the whole recording in the window; the view then stays put.</summary>
    [RelayCommand]
    private void FitAll()
    {
        double span = Math.Max(1, Store.End - Store.Start);
        NsPerPixel = Math.Clamp(span / Math.Max(1, _viewWidth), MinNsPerPixel, MaxNsPerPixel);
        ViewStart = Store.Start;
        Following = false;
        Touch();
    }

    /// <summary>К последним: back to following the latest time.</summary>
    [RelayCommand]
    private void GoLive()
    {
        ViewStart = (ulong)Math.Max(0, Store.End - (_viewWidth * NsPerPixel));
        Following = true;
        Touch();
    }

    /// <summary>Drag: moves the window by pixels; reaching the latest time follows it again.</summary>
    public void PanBy(double pixels)
    {
        double start = ViewStart + (pixels * NsPerPixel);
        ViewStart = (ulong)Math.Max(0, start);
        Following = ViewStart + (_viewWidth * NsPerPixel) >= Store.End;
        Touch();
    }

    /// <summary>Click on the plot: the marker Δ is measured from.</summary>
    public void SetMarker(double x)
    {
        Marker = ViewStart + (ulong)(Math.Max(0, x) * NsPerPixel);
        Touch();
    }

    public void SetCursor(double? x)
    {
        Cursor = x is { } position ? ViewStart + (ulong)(Math.Max(0, position) * NsPerPixel) : null;
        Touch();
    }

    /// <summary>The step between two ruler ticks for a scale: 1, 2 or 5 times a power of ten, about 100 pixels.</summary>
    public static ulong TickStep(double nsPerPixel)
    {
        double wanted = nsPerPixel * 100;
        double power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, wanted))));
        double unit = wanted / power;
        return (ulong)Math.Max(1, (unit < 2 ? 2 : unit < 5 ? 5 : 10) * power);
    }

    /// <summary>A time or a span in the unit that suits it.</summary>
    public static string FormatTime(ulong ns)
    {
        (ulong scale, string unit) = UnitFor(ns);
        return ((double)ns / scale).ToString("0.###", CultureInfo.CurrentCulture) + " " + unit;
    }

    /// <summary>A ruler tick: as many decimals as its step needs, so neighbouring ticks never read the same.</summary>
    public static string FormatTick(ulong tick, ulong step)
    {
        (ulong scale, string unit) = UnitFor(Math.Max(tick, step));
        int decimals = 0;
        for (ulong place = scale; step % place != 0; place /= 10)
        {
            decimals++;
        }

        return ((double)tick / scale).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture) + " " + unit;
    }

    private static (ulong Scale, string Unit) UnitFor(ulong ns) => ns switch
    {
        >= 1_000_000_000 => (1_000_000_000, Strings.Get("Unit_Seconds")),
        >= 1_000_000 => (1_000_000, Strings.Get("Unit_Milliseconds")),
        >= 1_000 => (1_000, Strings.Get("Unit_Microseconds")),
        _ => (1, Strings.Get("Unit_Nanoseconds")),
    };

    private void Follow()
    {
        if (Following)
        {
            ViewStart = (ulong)Math.Max(0, Store.End - (_viewWidth * NsPerPixel));
        }
    }

    private void Touch()
    {
        Version++;
        OnPropertyChanged(nameof(Readout));
        OnPropertyChanged(nameof(HasGaps));
    }
}
