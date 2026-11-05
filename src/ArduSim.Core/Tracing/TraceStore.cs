namespace ArduSim.Core.Tracing;

/// <summary>What one pixel column of a signal row shows. Low and High together mean it toggles within the column.</summary>
[Flags]
public enum ColumnLevel : byte
{
    None = 0,
    Low = 1,
    High = 2,
    Floating = 4,
    Pwm = 8,
    Tone = 16,
}

/// <summary>
/// The recorded signals on the viewing side of the logic analyzer (ADR-014): net samples per pin and
/// hardware bus events, in time order, bounded by dropping the oldest. A pin's picture merges both:
/// inside a bus event the wire levels come from the event; between the bytes of an I2C transaction SCL
/// stays low; a UART line nothing else drives idles high.
/// </summary>
public sealed class TraceStore
{
    private const int DefaultSampleLimit = 4_000_000;
    private const int DefaultEventLimit = 2_000_000;

    private readonly int _sampleLimit;
    private readonly int _eventLimit;
    private readonly Dictionary<int, List<PinSample>> _samples = [];
    private readonly Dictionary<int, List<BusEvent>> _events = [];
    private readonly List<BusDecoder> _decoders = [];
    private int _sampleCount;
    private int _eventCount;

    public TraceStore(int sampleLimit = DefaultSampleLimit, int eventLimit = DefaultEventLimit)
    {
        _sampleLimit = sampleLimit;
        _eventLimit = eventLimit;
    }

    /// <summary>The time of the last thing recorded.</summary>
    public ulong End { get; private set; }

    /// <summary>The oldest time still held.</summary>
    public ulong Start { get; private set; }

    /// <summary>Events were lost: too many for the channel, or dropped while the sketch ran flat out.</summary>
    public bool HasGaps { get; private set; }

    public bool IsEmpty => _sampleCount == 0 && _eventCount == 0;

    public void Clear()
    {
        _samples.Clear();
        _events.Clear();
        _sampleCount = 0;
        _eventCount = 0;
        Start = End;
        HasGaps = false;
        foreach (BusDecoder decoder in _decoders)
        {
            decoder.DropBefore(ulong.MaxValue);
        }
    }

    /// <summary>A bus row: it sees everything recorded so far and whatever comes later.</summary>
    public void AddDecoder(BusDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var samples = new List<PinSample>();
        foreach (int pin in decoder.Pins)
        {
            if (_samples.TryGetValue(pin, out List<PinSample>? list))
            {
                samples.AddRange(list);
            }
        }

        foreach (PinSample sample in samples.OrderBy(s => s.Time))
        {
            decoder.OnSample(sample);
        }

        foreach (BusEvent e in _events.Values.SelectMany(l => l).Distinct().OrderBy(e => e.Time))
        {
            decoder.OnEvent(e);
        }

        decoder.Advance(End);
        _decoders.Add(decoder);
    }

    public void RemoveDecoder(BusDecoder decoder) => _decoders.Remove(decoder);

    public void Append(ReadOnlySpan<PinSample> samples, ReadOnlySpan<BusEvent> events, bool overflowed, ulong now)
    {
        foreach (ref readonly PinSample sample in samples)
        {
            if (!_samples.TryGetValue(sample.Pin, out List<PinSample>? list))
            {
                _samples[sample.Pin] = list = [];
            }

            list.Add(sample);
            _sampleCount++;
            foreach (BusDecoder decoder in _decoders)
            {
                decoder.OnSample(sample);
            }
        }

        foreach (ref readonly BusEvent e in events)
        {
            foreach (byte pin in (ReadOnlySpan<byte>)[e.PinA, e.PinB, e.PinC])
            {
                if (pin != BusEvent.NoPin)
                {
                    if (!_events.TryGetValue(pin, out List<BusEvent>? list))
                    {
                        _events[pin] = list = [];
                    }

                    list.Add(e);
                }
            }

            _eventCount++;
            foreach (BusDecoder decoder in _decoders)
            {
                decoder.OnEvent(e);
            }
        }

        foreach (BusDecoder decoder in _decoders)
        {
            decoder.Advance(now);
        }

        End = Math.Max(End, now);
        HasGaps |= overflowed;
        Trim();
    }

    /// <summary>The bus events that touch a pin, for tests and tooltips.</summary>
    public IReadOnlyList<BusEvent> EventsOn(int pin) => _events.TryGetValue(pin, out List<BusEvent>? list) ? list : [];

    /// <summary>Paints <paramref name="columns"/> from <paramref name="start"/>, one column per <paramref name="nsPerColumn"/> nanoseconds.</summary>
    public void Render(int pin, ulong start, double nsPerColumn, Span<ColumnLevel> columns)
    {
        columns.Clear();
        PaintSamples(pin, start, nsPerColumn, columns);
        PaintEvents(pin, start, nsPerColumn, columns);
    }

    /// <summary>What a pin carries at a time (PWM duty, tone frequency): for the hover readout.</summary>
    public PinSample? SampleAt(int pin, ulong time)
    {
        if (!_samples.TryGetValue(pin, out List<PinSample>? list) || list.Count == 0)
        {
            return null;
        }

        int index = LastAtOrBefore(list, time);
        return index >= 0 ? list[index] : null;
    }

    /// <summary>The frames of a decoder that overlap the window, as an index range into its list.</summary>
    public static (int First, int Count) FramesIn(BusDecoder decoder, ulong start, ulong end)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        IReadOnlyList<DecodedFrame> frames = decoder.Frames;
        int low = 0;
        int high = frames.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (frames[middle].End < start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        int last = low;
        while (last < frames.Count && frames[last].Start <= end)
        {
            last++;
        }

        return (low, last - low);
    }

    private static ColumnLevel Flag(SignalLevel level) => level switch
    {
        SignalLevel.Low => ColumnLevel.Low,
        SignalLevel.High => ColumnLevel.High,
        SignalLevel.Pwm => ColumnLevel.Pwm,
        SignalLevel.Tone => ColumnLevel.Tone,
        _ => ColumnLevel.Floating,
    };

    private static int LastAtOrBefore(List<PinSample> list, ulong time)
    {
        int low = 0;
        int high = list.Count - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (list[middle].Time <= time)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    private static int FirstEndingAfter(List<BusEvent> list, ulong time)
    {
        int low = 0;
        int high = list.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (list[middle].End <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void PaintSamples(int pin, ulong start, double nsPerColumn, Span<ColumnLevel> columns)
    {
        if (!_samples.TryGetValue(pin, out List<PinSample>? list) || list.Count == 0)
        {
            return;
        }

        int index = LastAtOrBefore(list, start);
        ColumnLevel current = index >= 0 ? Flag(list[index].Level) : ColumnLevel.None;
        int next = index + 1;
        for (int column = 0; column < columns.Length; column++)
        {
            ulong from = start + (ulong)(column * nsPerColumn);
            ulong end = start + (ulong)((column + 1) * nsPerColumn);
            while (next < list.Count && list[next].Time <= from)
            {
                current = Flag(list[next].Level);
                next++;
            }

            ColumnLevel painted = current;
            while (next < list.Count && list[next].Time < end)
            {
                current = Flag(list[next].Level);
                painted |= current;
                next++;
            }

            columns[column] = painted;
        }
    }

    private void PaintEvents(int pin, ulong start, double nsPerColumn, Span<ColumnLevel> columns)
    {
        if (!_events.TryGetValue(pin, out List<BusEvent>? list) || list.Count == 0)
        {
            return;
        }

        ulong windowEnd = start + (ulong)(columns.Length * nsPerColumn);
        int index = FirstEndingAfter(list, start);

        // A UART line rests high, and an open I2C transaction holds SCL low, until the next byte.
        PaintRest(list, index, pin, start, nsPerColumn, windowEnd, columns);

        var transitions = new List<ulong>();
        while (index < list.Count && list[index].Time < windowEnd)
        {
            BusEvent e = list[index];
            int first = Math.Max(0, (int)((double)((long)e.Time - (long)start) / nsPerColumn));
            int last = Math.Min(columns.Length - 1, (int)((double)((long)e.End - 1 - (long)start) / nsPerColumn));
            if (e.Duration / nsPerColumn < 8 && first == last)
            {
                columns[first] = ColumnLevel.Low | ColumnLevel.High;

                // Faster than the screen can show: jump to the first event past this column.
                index = Math.Max(index + 1, FirstEndingAfter(list, start + (ulong)((first + 1) * nsPerColumn)));
                continue;
            }

            transitions.Clear();
            BusWaveform.Transitions(e, pin, transitions);
            for (int column = first; column <= last; column++)
            {
                ulong from = Math.Max(e.Time, start + (ulong)(column * nsPerColumn));
                ulong to = Math.Min(e.End - 1, start + (ulong)((column + 1) * nsPerColumn) - 1);
                ColumnLevel painted = BusWaveform.Level(e, pin, from) ? ColumnLevel.High : ColumnLevel.Low;
                painted |= BusWaveform.Level(e, pin, to) ? ColumnLevel.High : ColumnLevel.Low;
                foreach (ulong time in transitions)
                {
                    if (time >= from && time <= to)
                    {
                        painted |= BusWaveform.Level(e, pin, time) ? ColumnLevel.High : ColumnLevel.Low;
                    }
                }

                columns[column] = painted;
            }

            index++;
        }
    }

    private static void PaintRest(List<BusEvent> list, int index, int pin, ulong start, double nsPerColumn, ulong windowEnd, Span<ColumnLevel> columns)
    {
        for (int i = Math.Max(0, index - 1); i < list.Count && i <= index + 4096; i++)
        {
            BusEvent e = list[i];
            bool holds = e.IsUart || (e.IsI2c && e.Kind != BusEventKind.I2cStop);
            if (!holds || e.End >= windowEnd)
            {
                continue;
            }

            ulong restEnd = i + 1 < list.Count ? list[i + 1].Time : windowEnd;
            if (e.IsUart && i + 1 >= list.Count)
            {
                restEnd = windowEnd;
            }

            ColumnLevel level = BusWaveform.RestLevel(e, pin) ? ColumnLevel.High : ColumnLevel.Low;
            int first = Math.Max(0, (int)((double)((long)e.End - (long)start) / nsPerColumn));
            int last = Math.Min(columns.Length - 1, (int)((double)((long)Math.Min(restEnd, windowEnd) - 1 - (long)start) / nsPerColumn));
            for (int column = first; column <= last; column++)
            {
                if (!e.IsUart || columns[column] is ColumnLevel.Floating or ColumnLevel.None)
                {
                    columns[column] = level;
                }
            }
        }
    }

    private void Trim()
    {
        // The busiest list decides how far to cut: a quarter of it goes.
        if (_sampleCount > _sampleLimit)
        {
            List<PinSample> busiest = _samples.Values.MaxBy(l => l.Count)!;
            DropBefore(busiest[busiest.Count / 4].Time);
        }

        if (_eventCount > _eventLimit)
        {
            List<BusEvent> busiest = _events.Values.MaxBy(l => l.Count)!;
            DropBefore(busiest[busiest.Count / 4].Time);
        }
    }

    private void DropBefore(ulong cut)
    {
        foreach (List<PinSample> list in _samples.Values)
        {
            // One sample at or before the cut stays: it says what the pin was doing there.
            int keep = LastAtOrBefore(list, cut);
            if (keep > 0)
            {
                list.RemoveRange(0, keep);
                _sampleCount -= keep;
            }
        }

        var seen = new HashSet<BusEvent>();
        foreach (List<BusEvent> list in _events.Values)
        {
            int drop = FirstEndingAfter(list, cut);
            foreach (BusEvent e in list.Take(drop))
            {
                if (seen.Add(e))
                {
                    _eventCount--;
                }
            }

            list.RemoveRange(0, drop);
        }

        foreach (BusDecoder decoder in _decoders)
        {
            decoder.DropBefore(cut);
        }

        Start = cut;
    }
}
