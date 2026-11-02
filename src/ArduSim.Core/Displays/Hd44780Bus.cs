namespace ArduSim.Core.Displays;

/// <summary>
/// The HD44780's bus interface: latches writes on the falling edge of E and, while E is high with
/// R/W high, says what the controller drives on its data lines. Fed from nets (parallel wiring) or
/// from a PCF8574 backpack alike.
/// </summary>
public sealed class Hd44780Bus(Hd44780 controller)
{
    private bool _enable;
    private bool _readRegister;

    public Hd44780 Controller { get; } = controller;

    /// <summary>DB7..DB0 as the controller drives them during a read; null while it does not drive.</summary>
    public int? DrivenLines { get; private set; }

    /// <summary>The interface lines now; <paramref name="dataLines"/> is DB7..DB0 as they read from outside.</summary>
    /// <returns>True when <see cref="DrivenLines"/> changed.</returns>
    public bool Update(bool registerSelect, bool readWrite, bool enable, int dataLines)
    {
        int? before = DrivenLines;
        if (enable && !_enable)
        {
            _readRegister = registerSelect;
            DrivenLines = readWrite ? Controller.ReadLines(registerSelect) : null;
        }
        else if (!enable && _enable)
        {
            if (DrivenLines is not null)
            {
                Controller.EndRead(_readRegister);
            }
            else if (!readWrite)
            {
                Controller.Write(registerSelect, dataLines);
            }

            DrivenLines = null;
        }
        else if (enable && readWrite != (DrivenLines is not null))
        {
            // R/W changed while E is high: start or stop driving.
            _readRegister = registerSelect;
            DrivenLines = readWrite ? Controller.ReadLines(registerSelect) : null;
        }

        _enable = enable;
        return before != DrivenLines;
    }

    public void Reset()
    {
        _enable = false;
        DrivenLines = null;
    }
}
