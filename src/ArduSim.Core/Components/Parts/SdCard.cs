using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Storage;

namespace ArduSim.Core.Components.Parts;

/// <summary>Files a part keeps beyond the run, in a folder of the project; the host provides them (ADR-012).</summary>
public interface IPartFiles
{
    /// <summary>The files and folders under <paramref name="folder"/>, paths with '/'.</summary>
    IReadOnlyList<CardFile> Load(string folder);

    /// <summary>Makes <paramref name="folder"/> hold <paramref name="files"/>: changed files written, removed ones deleted.</summary>
    void Save(string folder, IReadOnlyList<CardFile> files);
}

/// <summary>
/// A microSD card module on hardware SPI (ADR-012): a 64 MB SDSC card (version 2, byte addressing)
/// in SPI mode with the commands SdFat sends: CMD0, CMD8, CMD55/ACMD41, CMD58, CMD9/CMD10 (CSD, CID),
/// CMD12, CMD13, CMD16, CMD17/18 and CMD24/25 with their data tokens, the erase commands and
/// ACMD13/ACMD23. Its FAT file system is built from a folder of the project when the run starts
/// and written back when the run ends. Bit-banged (software) SPI is not simulated.
/// </summary>
[SimComponent]
public sealed class SdCard : SimComponent, ISpiDevice
{
    private const byte Idle = 0x01;
    private const byte IllegalCommand = 0x04;

    private readonly Queue<byte> _out = new();
    private readonly byte[] _command = new byte[6];
    private readonly byte[] _block = new byte[FatImage.BlockSize + 2];
    private FatImage _image = new();
    private int _commandBytes;
    private bool _idle = true;
    private bool _application;
    private int _initPolls;
    private Receive _receive;
    private int _received;
    private uint _address;
    private bool _readingMany;
    private uint _eraseStart;
    private uint _eraseEnd;
    private bool? _clock;

    public SdCard(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    private enum Receive
    {
        None,

        /// <summary>CMD24: one block after its 0xFE token.</summary>
        Single,

        /// <summary>CMD25: blocks after 0xFC tokens until the 0xFD stop token.</summary>
        Many,
    }

    public static ComponentDefinition Spec { get; } = new(
        "sd-card",
        "Part_SdCard",
        "module",
        70,
        90,
        [
            new PinDefinition("GND", PinKind.Ground, 10, 90),
            new PinDefinition("VCC", PinKind.Power, 20, 90),
            new PinDefinition("MISO", PinKind.Digital, 30, 90),
            new PinDefinition("MOSI", PinKind.Digital, 40, 90),
            new PinDefinition("SCK", PinKind.Digital, 50, 90),
            new PinDefinition("CS", PinKind.Digital, 60, 90),
        ],
        [
            new PropertyDefinition("folder", PropertyKind.Text, "sd"),
            new PropertyDefinition("inserted", PropertyKind.Choice, "yes", ["yes", "no"]),
        ]);

    PinRef ISpiDevice.Sck => Pin("SCK");

    PinRef ISpiDevice.Mosi => Pin("MOSI");

    PinRef ISpiDevice.ChipSelect => Pin("CS");

    /// <summary>The project folder that holds the card's files.</summary>
    public string Folder => string.IsNullOrWhiteSpace(Properties.Text("folder")) ? "sd" : Properties.Text("folder").Trim();

    /// <summary>What is on the card now, read through its file system.</summary>
    public IReadOnlyList<CardFile> Files => _image.Files();

    private bool Inserted => Properties.Choice("inserted") == "yes";

    private bool Powered => PartSupply.Across(Net("VCC"), Net("GND")) >= 3;

    protected override void OnAttach()
    {
        Context.Spi?.Register(this);
        Circuit.Watch(Pin("SCK"), OnClock);
    }

    public override void OnCircuitReady()
    {
        IReadOnlyList<CardFile> files = Context.Files?.Load(Folder) ?? [];
        (FatImage image, IReadOnlyList<string> skipped) = FatImage.Create(files);
        _image = image;
        if (skipped.Count > 0)
        {
            Context.Log.Report(SimSeverity.Warning, "sd-skipped", $"{Id}: {skipped.Count} file(s) of the folder '{Folder}' did not go on the card (no room, or a name FAT cannot hold), e.g. {skipped[0]}.", Id);
        }

        SetState("files", files.Count(f => !f.IsFolder));
    }

    /// <summary>The run ends: the card's files go back to the project folder.</summary>
    public override void OnRunEnding()
    {
        if (Inserted && _image.IsDirty)
        {
            Context.Files?.Save(Folder, _image.Files());
        }
    }

    byte ISpiDevice.OnTransfer(byte mosi)
    {
        if (!Inserted || !Powered)
        {
            return 0xFF;
        }

        byte miso = _out.Count > 0 ? _out.Dequeue() : (byte)0xFF;
        if (_readingMany && _out.Count == 0)
        {
            QueueBlock(_address);
            _address += FatImage.BlockSize;
        }

        if (_receive != Receive.None && _commandBytes == 0)
        {
            ReceiveData(mosi);
        }
        else if (_commandBytes > 0 || (mosi & 0xC0) == 0x40)
        {
            _command[_commandBytes++] = mosi;
            if (_commandBytes == _command.Length)
            {
                _commandBytes = 0;
                Execute(_command[0] & 0x3F, (uint)((_command[1] << 24) | (_command[2] << 16) | (_command[3] << 8) | _command[4]));
            }
        }

        return miso;
    }

    // SdFat clocks the card only through the SPI peripheral; edges on SCK while it is selected mean software SPI.
    private void OnClock()
    {
        bool clock = Net("SCK") is { HasVoltage: true, Voltage: >= 1.5 };
        if (_clock is { } before && before != clock && Net("CS") is { HasVoltage: true, Voltage: < 1.5 })
        {
            Context.Log.ReportOnce($"sd-soft-spi-{Id}", SimSeverity.Warning, "sd-soft-spi", $"{Id}: the card is clocked by software (bit-banged) SPI, which is not simulated; use the board's hardware SPI pins.", Id);
        }

        _clock = clock;
    }

    private void Execute(int command, uint argument)
    {
        _out.Clear();
        _out.Enqueue(0xFF); // one byte of command response time (NCR)
        bool application = _application;
        _application = false;
        if (command == 0)
        {
            _idle = true;
            _readingMany = false;
            _receive = Receive.None;
            _initPolls = 0;
            R1(Idle);
            return;
        }

        if (application)
        {
            switch (command)
            {
                case 41:
                    // Ready after a couple of polls, as a card finishing its power-up.
                    _idle = ++_initPolls < 2;
                    R1(0);
                    return;
                case 13:
                    R1(0);
                    _out.Enqueue(0x00);
                    QueueData(new byte[64]);
                    return;
                case 23:
                    R1(0);
                    return;
            }
        }

        switch (command)
        {
            case 8:
                R1(0);
                foreach (byte b in new byte[] { 0x00, 0x00, (byte)(argument >> 8), (byte)argument })
                {
                    _out.Enqueue(b);
                }

                return;
            case 9:
                R1(0);
                QueueData(Csd());
                return;
            case 10:
                R1(0);
                QueueData(Cid());
                return;
            case 12:
                _readingMany = false;
                _out.Clear();
                _out.Enqueue(0xFF);
                _out.Enqueue(0xFF);
                R1(0);
                return;
            case 13:
                R1(0);
                _out.Enqueue(0x00);
                return;
            case 16 or 59:
                R1(0);
                return;
            case 17:
                R1(0);
                QueueBlock(argument);
                return;
            case 18:
                R1(0);
                QueueBlock(argument);
                _address = argument + FatImage.BlockSize;
                _readingMany = true;
                return;
            case 24 or 25:
                R1(0);
                _receive = command == 24 ? Receive.Single : Receive.Many;
                _address = argument;
                _received = -1;
                return;
            case 32:
                _eraseStart = argument;
                R1(0);
                return;
            case 33:
                _eraseEnd = argument;
                R1(0);
                return;
            case 38:
                for (uint address = _eraseStart; address <= _eraseEnd && address < FatImage.Capacity; address += FatImage.BlockSize)
                {
                    _image.Write(address / FatImage.BlockSize, new byte[FatImage.BlockSize]);
                }

                R1(0);
                _out.Enqueue(0x00);
                return;
            case 55:
                _application = true;
                R1(0);
                return;
            case 58:
                R1(0);
                foreach (byte b in new byte[] { _idle ? (byte)0x00 : (byte)0x80, 0xFF, 0x80, 0x00 })
                {
                    _out.Enqueue(b);
                }

                return;
            default:
                R1(IllegalCommand);
                return;
        }
    }

    private void R1(byte flags) => _out.Enqueue((byte)(flags | (_idle && flags == 0 ? Idle : 0)));

    // A data block from the card: a little access time (NAC), the 0xFE token, the bytes, CRC16.
    private void QueueData(ReadOnlySpan<byte> data)
    {
        _out.Enqueue(0xFF);
        _out.Enqueue(0xFE);
        foreach (byte b in data)
        {
            _out.Enqueue(b);
        }

        ushort crc = Crc16(data);
        _out.Enqueue((byte)(crc >> 8));
        _out.Enqueue((byte)crc);
    }

    private void QueueBlock(uint address)
    {
        Span<byte> block = stackalloc byte[FatImage.BlockSize];
        _image.Read(address / FatImage.BlockSize, block);
        QueueData(block);
    }

    // CMD24/25 data: wait for the start token, take 512 bytes and the CRC, answer "accepted", then busy.
    private void ReceiveData(byte mosi)
    {
        if (_received < 0)
        {
            if (mosi == 0xFE || (mosi == 0xFC && _receive == Receive.Many))
            {
                _received = 0;
            }
            else if (mosi == 0xFD && _receive == Receive.Many)
            {
                _receive = Receive.None;
                _out.Enqueue(0xFF);
                _out.Enqueue(0x00);
            }

            return;
        }

        _block[_received++] = mosi;
        if (_received < _block.Length)
        {
            return;
        }

        _image.Write(_address / FatImage.BlockSize, _block.AsSpan(0, FatImage.BlockSize));
        _address += FatImage.BlockSize;
        _out.Enqueue(0x05); // data accepted
        _out.Enqueue(0x00); // busy while it programs
        _out.Enqueue(0x00);
        _received = -1;
        if (_receive == Receive.Single)
        {
            _receive = Receive.None;
        }
    }

    // CSD version 1 for 64 MB: READ_BL_LEN 9, C_SIZE 255, C_SIZE_MULT 7 → 256 × 512 × 512 bytes.
    private static byte[] Csd()
    {
        byte[] csd = [0x00, 0x0E, 0x00, 0x32, 0x5B, 0x59, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        const int cSize = 255;
        const int multiplier = 7;
        csd[6] = (byte)(0x80 | ((cSize >> 10) & 0x03)); // READ_BLK_MISALIGN = 0, WRITE_BLK_MISALIGN = 0, DSR_IMP = 0
        csd[7] = (byte)(cSize >> 2);
        csd[8] = (byte)(((cSize & 0x03) << 6) | 0x3F);
        csd[9] = (byte)(0xFC | (multiplier >> 1));
        csd[10] = (byte)(((multiplier & 1) << 7) | 0x7F);
        csd[11] = 0x80;
        csd[12] = 0x0A;
        csd[13] = 0x40;
        csd[15] = (byte)((Crc7(csd.AsSpan(0, 15)) << 1) | 1);
        return csd;
    }

    private static byte[] Cid()
    {
        byte[] cid = [0x1D, (byte)'A', (byte)'S', (byte)'A', (byte)'R', (byte)'D', (byte)'S', (byte)'D', 0x10, 0x00, 0x00, 0x00, 0x01, 0x01, 0xA1, 0x01];
        cid[15] = (byte)((Crc7(cid.AsSpan(0, 15)) << 1) | 1);
        return cid;
    }

    private static byte Crc7(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte value in data)
        {
            int b = value;
            for (int i = 0; i < 8; i++)
            {
                crc <<= 1;
                if (((b & 0x80) ^ (crc & 0x80)) != 0)
                {
                    crc ^= 0x09;
                }

                b <<= 1;
            }
        }

        return (byte)(crc & 0x7F);
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
        }

        return crc;
    }
}
