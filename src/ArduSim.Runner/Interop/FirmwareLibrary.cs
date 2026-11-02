using System.Globalization;
using System.Runtime.InteropServices;

namespace ArduSim.Runner.Interop;

/// <summary>A loaded firmware.dll and its five exports (ADR-001).</summary>
internal sealed unsafe class FirmwareLibrary
{
    private readonly delegate* unmanaged<uint> _abiVersion;
    private readonly delegate* unmanaged<SimHostApi*, int> _init;
    private readonly delegate* unmanaged<void> _setup;
    private readonly delegate* unmanaged<void> _loop;
    private readonly delegate* unmanaged<int, void> _dispatchInterrupt;

    private FirmwareLibrary(nint handle, string path)
    {
        Handle = handle;
        Path = path;
        _abiVersion = (delegate* unmanaged<uint>)Export("sim_fw_abi_version");
        _init = (delegate* unmanaged<SimHostApi*, int>)Export("sim_fw_init");
        _setup = (delegate* unmanaged<void>)Export("sim_fw_setup");
        _loop = (delegate* unmanaged<void>)Export("sim_fw_loop");
        _dispatchInterrupt = (delegate* unmanaged<int, void>)Export("sim_fw_dispatch_interrupt");
    }

    public nint Handle { get; }

    public string Path { get; }

    /// <summary>
    /// Publishes the host table for static constructors (ADR-001), loads the DLL, checks the ABI
    /// version and hands the table over through <c>sim_fw_init</c>.
    /// </summary>
    public static FirmwareLibrary Load(string path, SimHostApi* api)
    {
        Environment.SetEnvironmentVariable("ARDUSIM_HOST_API", ((nint)api).ToString("x", CultureInfo.InvariantCulture));
        nint handle;
        try
        {
            handle = NativeLibrary.Load(path);
        }
        catch (DllNotFoundException e)
        {
            throw new FirmwareException(
                $"Could not load {path}: {e.Message} If it says the DLL initialization failed, a global object's constructor in the sketch crashed.", e);
        }

        var firmware = new FirmwareLibrary(handle, path);
        uint version = firmware._abiVersion();
        if (version >> 16 != SimHostApi.AbiMajor || (version & 0xFFFF) > SimHostApi.AbiMinor)
        {
            throw new FirmwareException($"{path} was built for firmware ABI {version >> 16}.{version & 0xFFFF}; this runner speaks {SimHostApi.AbiMajor}.{SimHostApi.AbiMinor}. Rebuild the sketch.");
        }

        if (firmware._init(api) != 0)
        {
            throw new FirmwareException($"{path} rejected the simulator host table (ABI mismatch). Rebuild the sketch.");
        }

        return firmware;
    }

    public void Setup() => _setup();

    public void Loop() => _loop();

    public void DispatchInterrupt(int id) => _dispatchInterrupt(id);

    private nint Export(string name)
    {
        if (!NativeLibrary.TryGetExport(Handle, name, out nint address))
        {
            throw new FirmwareException($"{Path} is not an ArduSim firmware: export {name} is missing.");
        }

        return address;
    }
}

internal sealed class FirmwareException : Exception
{
    public FirmwareException()
    {
    }

    public FirmwareException(string message)
        : base(message)
    {
    }

    public FirmwareException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
