using ArduSim.Core.Diagnostics;

namespace ArduSim.Core.Circuits;

/// <summary>The DC solution (ADR-017): an island of coupled unknown nets, solved once per PWM phase.</summary>
public sealed partial class Circuit
{
    private const double LeakConductance = 1e-12;
    private const double OffConductance = 1e-9;
    private const double KneeTolerance = 1e-9;
    private const double SwingThreshold = 0.05;
    private const double LeastConduction = 1e-7;
    private const double VoltageSteps = 1e6;
    private const int MaxToggling = 4;
    private const int FlipAllRounds = 8;
    private const int DiodeRoundLimit = 512;

    private readonly NodalMatrix _matrix = new();
    private readonly List<Net> _island = [];
    private readonly Stack<Net> _walk = new();
    private readonly List<Driver> _islandDrivers = [];
    private readonly List<CircuitResistor> _islandResistors = [];
    private readonly List<CircuitDiode> _islandDiodes = [];
    private readonly List<Driver> _toggling = [];
    private readonly List<Net> _changed = [];
    private readonly double[] _weights = new double[1 << MaxToggling];
    private readonly double[][] _phases = new double[1 << MaxToggling][];
    private double[] _high = new double[4];
    private double[] _low = new double[4];
    private double[] _mean = new double[4];
    private int _visit;

    /// <summary>Ohms seen looking from a pin's net into what is connected to it: 0 on a rail, very large while it floats.</summary>
    public double SourceResistanceOf(PinRef pin)
    {
        Net net = NetOf(pin);
        if (TryKnownVoltage(net, out _))
        {
            return 0;
        }

        _visit++;
        CollectIsland(net);
        ChooseToggling();
        SetPhase((1 << _toggling.Count) - 1);
        SolveDiodes();
        return _matrix.ResistanceAt(net.Slot);
    }

    // Answers do not change below a microvolt, and rounding keeps noise from waking the watchers.
    private static double Snap(double volts) => Math.Round(volts * VoltageSteps) / VoltageSteps;

    private void ResolveAll()
    {
        _visit++;
        foreach (Net net in _nets)
        {
            if (TryKnownVoltage(net, out _))
            {
                ResolveKnown(net);
            }
        }

        foreach (Net net in _nets)
        {
            if (net.Visit != _visit && !TryKnownVoltage(net, out _))
            {
                SolveIsland(net);
            }
        }

        Revision++;
    }

    private void ResolveAround(Net net)
    {
        _changed.Clear();
        _visit++;
        if (TryKnownVoltage(net, out _))
        {
            ResolveKnown(net);
            foreach (CircuitElement element in net.Elements)
            {
                Net other = element.Other(net);
                if (other.Visit != _visit && !TryKnownVoltage(other, out _))
                {
                    SolveIsland(other);
                }
            }
        }
        else
        {
            SolveIsland(net);
        }

        Revision++;
        foreach (Net changed in _changed)
        {
            NotifyNet(changed);
        }
    }

    private void Assign(Net net, NetState state)
    {
        if (net.State != state)
        {
            net.State = state;
            _changed.Add(net);
        }
    }

    // A net holding an ideal source: its voltage is given, and a solve never crosses it.
    private static bool TryKnownVoltage(Net net, out double voltage)
    {
        bool any = false;
        voltage = 0;
        foreach (Driver driver in net.Drivers)
        {
            if (!driver.Drive.IsIdeal)
            {
                continue;
            }

            if (!any)
            {
                voltage = driver.Drive.Voltage;
                any = true;
            }
            else if (Math.Abs(driver.Drive.Voltage - voltage) > AgreementTolerance)
            {
                voltage = (voltage + driver.Drive.Voltage) / 2;
                return true;
            }
        }

        return any;
    }

    private void ResolveKnown(Net net)
    {
        TryKnownVoltage(net, out double voltage);
        net.Visit = _visit;
        net.Slot = -1;
        net.Known = voltage;
        bool conflict = CheckConflict(net);
        foreach (Driver driver in net.Drivers)
        {
            if (driver.Drive.Strength != DriveStrength.None && !driver.Drive.IsIdeal)
            {
                double amps = (driver.Drive.Voltage - voltage) * driver.Drive.Conductance;
                driver.Current = driver.PeakCurrent = amps;
                if (!conflict)
                {
                    CheckOverload(driver);
                }
            }
            else
            {
                driver.Current = driver.PeakCurrent = 0;
            }
        }

        foreach (CircuitElement element in net.Elements)
        {
            Net other = element.Other(net);
            if (TryKnownVoltage(other, out double otherVoltage))
            {
                double across = element.NetA == net ? voltage - otherVoltage : otherVoltage - voltage;
                element.Current = element.PeakCurrent = element.DirectCurrent(across);
            }
        }

        double snapped = Snap(voltage);
        Assign(net, new NetState(conflict ? NetCondition.Conflict : NetCondition.Driven, snapped, 1, 0, snapped));
    }

    // The usual case by far, and the hot one: a net with no resistor or LED on it and nothing that
    // toggles. The same equation in closed form, without the matrix.
    private bool TrySolveLone(Net net)
    {
        if (net.Elements.Count > 0)
        {
            return false;
        }

        double conductance = 0;
        double current = 0;
        bool strong = false;
        foreach (Driver driver in net.Drivers)
        {
            NetDrive drive = driver.Drive;
            if (drive.Strength == DriveStrength.None)
            {
                driver.Current = driver.PeakCurrent = 0;
            }
            else if (drive.Toggles)
            {
                return false;
            }
            else
            {
                double g = drive.Conductance;
                conductance += g;
                current += g * drive.Voltage * Math.Min(drive.Duty, 1);
                strong |= drive.Strength == DriveStrength.Strong;
            }
        }

        if (conductance == 0)
        {
            Assign(net, default);
            return true;
        }

        if (strong && CheckConflict(net))
        {
            return false;
        }

        double voltage = current / conductance;
        foreach (Driver driver in net.Drivers)
        {
            NetDrive drive = driver.Drive;
            if (drive.Strength != DriveStrength.None)
            {
                driver.Current = driver.PeakCurrent = ((drive.Voltage * Math.Min(drive.Duty, 1)) - voltage) * drive.Conductance;
                CheckOverload(driver);
            }
        }

        double snapped = Snap(voltage);
        Assign(net, new NetState(strong ? NetCondition.Driven : NetCondition.Pulled, snapped, 1, 0, snapped));
        return true;
    }

    private void SolveIsland(Net start)
    {
        start.Visit = _visit;
        if (TrySolveLone(start))
        {
            return;
        }

        CollectIsland(start);
        foreach (Driver driver in _islandDrivers)
        {
            driver.Current = driver.PeakCurrent = 0;
        }

        foreach (CircuitResistor resistor in _islandResistors)
        {
            resistor.Current = resistor.PeakCurrent = 0;
        }

        foreach (CircuitDiode diode in _islandDiodes)
        {
            diode.Current = diode.PeakCurrent = 0;
            diode.EverOn = false;
        }

        ChooseToggling();
        int size = _island.Count;
        if (_high.Length < size)
        {
            int capacity = Math.Max(size, _high.Length * 2);
            _high = new double[capacity];
            _low = new double[capacity];
            _mean = new double[capacity];
        }

        for (int i = 0; i < size; i++)
        {
            _high[i] = double.NegativeInfinity;
            _low[i] = double.PositiveInfinity;
            _mean[i] = 0;
        }

        int all = (1 << _toggling.Count) - 1;
        for (int mask = all; mask >= 0; mask--)
        {
            double weight = SetPhase(mask);
            SolveDiodes();
            Accumulate(weight, mask);
        }

        MarkReached();
        for (int i = 0; i < size; i++)
        {
            Net net = _island[i];
            Assign(net, StateOf(net, i, all));
            _dirtySet.Remove(net.Index);
        }

        foreach (Driver driver in _islandDrivers)
        {
            if (driver.Net.State.Condition != NetCondition.Conflict)
            {
                CheckOverload(driver);
            }
        }
    }

    private void CollectIsland(Net start)
    {
        _island.Clear();
        _islandDrivers.Clear();
        _islandResistors.Clear();
        _islandDiodes.Clear();
        start.Visit = _visit;
        start.Slot = 0;
        _island.Add(start);
        _walk.Push(start);
        while (_walk.TryPop(out Net? net))
        {
            foreach (Driver driver in net.Drivers)
            {
                if (driver.Drive.Strength != DriveStrength.None)
                {
                    _islandDrivers.Add(driver);
                }
                else
                {
                    driver.Current = driver.PeakCurrent = 0;
                }
            }

            foreach (CircuitElement element in net.Elements)
            {
                if (element.Visit != _visit)
                {
                    element.Visit = _visit;
                    if (element is CircuitDiode diode)
                    {
                        _islandDiodes.Add(diode);
                    }
                    else
                    {
                        _islandResistors.Add((CircuitResistor)element);
                    }
                }

                Net other = element.Other(net);
                if (TryKnownVoltage(other, out double voltage))
                {
                    other.Slot = -1;
                    other.Known = voltage;
                }
                else if (other.Visit != _visit)
                {
                    other.Visit = _visit;
                    other.Slot = _island.Count;
                    _island.Add(other);
                    _walk.Push(other);
                }
            }
        }
    }

    // Sources that toggle: each one doubles the phases to solve. A steady strong driver on the same
    // net holds it anyway, so the toggling one does not count (it agrees, or it is a short).
    private void ChooseToggling()
    {
        _toggling.Clear();
        foreach (Driver driver in _islandDrivers)
        {
            NetDrive drive = driver.Drive;
            driver.Source = drive.Voltage * Math.Min(drive.Duty, 1);
            if (!drive.Toggles)
            {
                continue;
            }

            bool held = false;
            foreach (Driver other in driver.Net.Drivers)
            {
                held |= other.Drive.Strength == DriveStrength.Strong && !other.Drive.Toggles;
            }

            if (held)
            {
                driver.Source = drive.Voltage;
            }
            else
            {
                _toggling.Add(driver);
            }
        }

        // In the board's order: a walk starts at whichever net changed, and neither the sources
        // that count as their average nor a tie between tones may depend on that.
        if (_toggling.Count > 1)
        {
            _toggling.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        }

        for (int i = MaxToggling; i < _toggling.Count; i++)
        {
            Driver driver = _toggling[i];
            _log.ReportOnce("circuit-toggling-" + driver.Pin, SimSeverity.Info, "circuit-toggling", $"More than {MaxToggling} PWM or tone sources are coupled together; {driver.Pin} counts as its average voltage.", driver.Owner);
        }

        if (_toggling.Count > MaxToggling)
        {
            _toggling.RemoveRange(MaxToggling, _toggling.Count - MaxToggling);
        }
    }

    private double SetPhase(int mask)
    {
        double weight = 1;
        for (int j = 0; j < _toggling.Count; j++)
        {
            Driver driver = _toggling[j];
            bool high = (mask & (1 << j)) != 0;
            driver.Source = high ? driver.Drive.Voltage : 0;
            weight *= high ? driver.Drive.Duty : 1 - driver.Drive.Duty;
        }

        _weights[mask] = weight;
        return weight;
    }

    // Solve, flip the diodes the result contradicts, solve again. The network is monotonic, so the
    // answer is unique; flipping only the first one after a few rounds cannot cycle.
    private void SolveDiodes()
    {
        for (int round = 0; ; round++)
        {
            Assemble();
            _matrix.Solve();
            bool flipped = false;
            foreach (CircuitDiode diode in _islandDiodes)
            {
                double across = VoltageOf(diode.NetA) - VoltageOf(diode.NetB);
                if (diode.On ? across < diode.Knee - KneeTolerance : across > diode.Knee + KneeTolerance)
                {
                    diode.On = !diode.On;
                    flipped = true;
                    if (round >= FlipAllRounds)
                    {
                        break;
                    }
                }
            }

            if (!flipped)
            {
                return;
            }

            if (round >= DiodeRoundLimit)
            {
                _log.ReportOnce("circuit-diodes", SimSeverity.Error, "circuit-unstable", $"The diodes in this circuit have no consistent state; the calculation was stopped.", variant: "diodes");
                return;
            }
        }
    }

    private void Assemble()
    {
        _matrix.Clear(_island.Count);
        for (int i = 0; i < _island.Count; i++)
        {
            _matrix.Leak(i, LeakConductance);
        }

        foreach (Driver driver in _islandDrivers)
        {
            _matrix.ToSource(driver.Net.Slot, driver.Drive.Conductance, driver.Source);
        }

        foreach (CircuitResistor resistor in _islandResistors)
        {
            Stamp(resistor.NetA, resistor.NetB, 1 / resistor.Ohms, 0);
        }

        foreach (CircuitDiode diode in _islandDiodes)
        {
            if (diode.On)
            {
                // Continuous at the knee: the off line up to it, the steep line from there.
                Stamp(diode.NetA, diode.NetB, diode.OnConductance, (diode.OnConductance - OffConductance) * diode.Knee);
            }
            else
            {
                Stamp(diode.NetA, diode.NetB, OffConductance, 0);
            }
        }
    }

    // Current from a to b = conductance × (Va − Vb) − offset.
    private void Stamp(Net a, Net b, double conductance, double offset)
    {
        if (a.Slot >= 0 && b.Slot >= 0)
        {
            _matrix.Between(a.Slot, b.Slot, conductance);
            _matrix.Inject(a.Slot, offset);
            _matrix.Inject(b.Slot, -offset);
        }
        else if (a.Slot >= 0)
        {
            _matrix.ToSource(a.Slot, conductance, b.Known);
            _matrix.Inject(a.Slot, offset);
        }
        else if (b.Slot >= 0)
        {
            _matrix.ToSource(b.Slot, conductance, a.Known);
            _matrix.Inject(b.Slot, -offset);
        }
    }

    private double VoltageOf(Net net) => net.Slot >= 0 ? _matrix.Solution[net.Slot] : net.Known;

    private void Accumulate(double weight, int mask)
    {
        double[]? phase = null;
        if (_toggling.Count > 0)
        {
            phase = _phases[mask] is { } known && known.Length >= _island.Count ? known : _phases[mask] = new double[Math.Max(_island.Count, 4)];
        }

        for (int i = 0; i < _island.Count; i++)
        {
            double voltage = _matrix.Solution[i];
            if (phase is not null)
            {
                phase[i] = voltage;
            }

            _mean[i] += weight * voltage;
            _high[i] = Math.Max(_high[i], voltage);
            _low[i] = Math.Min(_low[i], voltage);
        }

        foreach (Driver driver in _islandDrivers)
        {
            double amps = (driver.Source - VoltageOf(driver.Net)) * driver.Drive.Conductance;
            driver.Current += weight * amps;
            driver.PeakCurrent = Larger(driver.PeakCurrent, amps);
        }

        foreach (CircuitResistor resistor in _islandResistors)
        {
            double amps = (VoltageOf(resistor.NetA) - VoltageOf(resistor.NetB)) / resistor.Ohms;
            resistor.Current += weight * amps;
            resistor.PeakCurrent = Larger(resistor.PeakCurrent, amps);
        }

        foreach (CircuitDiode diode in _islandDiodes)
        {
            double amps = diode.On ? diode.OnCurrent(VoltageOf(diode.NetA) - VoltageOf(diode.NetB)) : 0;

            // A few nanoamps at the knee are the off state's leak seen from the other side. They
            // light nothing and connect nothing, whichever side of the knee the last solve left it on.
            if (amps < LeastConduction)
            {
                amps = 0;
            }

            diode.Current += weight * amps;
            diode.PeakCurrent = Larger(diode.PeakCurrent, amps);
            diode.EverOn |= amps > 0;
        }
    }

    internal static double Larger(double a, double b) => Math.Abs(b) > Math.Abs(a) ? b : a;

    // What a source reaches through resistors and diodes that conduct; the rest floats.
    private void MarkReached()
    {
        foreach (Net net in _island)
        {
            net.Reached = false;
        }

        foreach (Driver driver in _islandDrivers)
        {
            Reach(driver.Net);
        }

        foreach (CircuitResistor resistor in _islandResistors)
        {
            ReachFromKnown(resistor);
        }

        foreach (CircuitDiode diode in _islandDiodes)
        {
            if (diode.EverOn)
            {
                ReachFromKnown(diode);
            }
        }

        while (_walk.TryPop(out Net? net))
        {
            foreach (CircuitElement element in net.Elements)
            {
                if (element is not CircuitDiode { EverOn: false } && element.Other(net) is { Slot: >= 0 } other)
                {
                    Reach(other);
                }
            }
        }
    }

    private void ReachFromKnown(CircuitElement element)
    {
        if (element.NetA.Slot < 0 && element.NetB.Slot >= 0)
        {
            Reach(element.NetB);
        }
        else if (element.NetB.Slot < 0 && element.NetA.Slot >= 0)
        {
            Reach(element.NetA);
        }
    }

    private void Reach(Net net)
    {
        if (!net.Reached)
        {
            net.Reached = true;
            _walk.Push(net);
        }
    }

    private NetState StateOf(Net net, int slot, int all)
    {
        if (CheckConflict(net))
        {
            double middle = Snap(_mean[slot]);
            return new NetState(NetCondition.Conflict, middle, 1, 0, middle);
        }

        bool strong = false;
        bool held = false;
        foreach (Driver driver in net.Drivers)
        {
            if (driver.Drive.Strength == DriveStrength.Strong)
            {
                strong = true;
                held |= !driver.Drive.Toggles;
            }
        }

        if (!strong && !net.Reached)
        {
            return default;
        }

        NetCondition condition = strong ? NetCondition.Driven : NetCondition.Pulled;
        // Readers take a net's Duty as "high this long, about 0 V otherwise". So a net that a steady
        // output holds, or that only dips, is steady at its average: a HIGH pin whose LED goes on
        // and off with PWM at the other end droops a little, and it is still a HIGH pin.
        double average = Snap(_mean[slot]);
        if (held || _high[slot] - _low[slot] < SwingThreshold || _low[slot] >= _high[slot] / 2)
        {
            return new NetState(condition, average, 1, 0, average);
        }

        double middleLevel = (_high[slot] + _low[slot]) / 2;
        double duty = 0;
        for (int mask = 0; mask <= all; mask++)
        {
            if (_phases[mask][slot] >= middleLevel - (1 / VoltageSteps))
            {
                duty += _weights[mask];
            }
        }

        return new NetState(condition, Snap(_high[slot]), duty, ToneOf(slot, all), average);
    }

    // A net carries the tone of the source that moves it most.
    private int ToneOf(int slot, int all)
    {
        int tone = 0;
        double largest = SwingThreshold;
        for (int j = 0; j < _toggling.Count; j++)
        {
            if (_toggling[j].Drive.ToneHz > 0)
            {
                double swing = Math.Abs(_phases[all][slot] - _phases[all & ~(1 << j)][slot]);
                if (swing > largest)
                {
                    largest = swing;
                    tone = _toggling[j].Drive.ToneHz;
                }
            }
        }

        return tone;
    }

    private bool CheckConflict(Net net)
    {
        Driver? first = null;
        foreach (Driver driver in net.Drivers)
        {
            if (driver.Drive.Strength != DriveStrength.Strong)
            {
                continue;
            }

            if (first is null)
            {
                first = driver;
            }
            else if (Math.Abs(driver.Drive.Voltage - first.Drive.Voltage) > AgreementTolerance)
            {
                ReportShort(net, first, driver);
                return true;
            }
        }

        return false;
    }

    private void CheckOverload(Driver driver)
    {
        if (driver.CurrentLimit <= 0 || Math.Abs(driver.PeakCurrent) <= driver.CurrentLimit)
        {
            return;
        }

        string part = driver.Owner;
        foreach (PinRef pin in driver.Net.Pins)
        {
            if (pin.Component != driver.Owner)
            {
                part = pin.Component;
                break;
            }
        }

        _log.ReportOnce(
            "overload-" + driver.Pin,
            SimSeverity.Warning,
            "pin-overload",
            $"{driver.Pin.Pin} carries {Math.Abs(driver.PeakCurrent) * 1000:0} mA, above its {driver.CurrentLimit * 1000:0} mA limit; a real pin can be damaged. Add a series resistor.",
            part);
    }

    private void ReportShort(Net net, Driver a, Driver b)
    {
        string component = a.Owner != b.Owner ? b.Owner : a.Owner;
        _log.ReportOnce(
            "short-" + net.Pins[0],
            SimSeverity.Warning,
            "short-circuit",
            $"Short circuit on net {net.Name}: {a.Owner} drives {a.Drive.Voltage:0.#} V while {b.Owner} drives {b.Drive.Voltage:0.#} V.",
            component);
    }
}
