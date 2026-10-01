using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace HeadsetLowLatency;

internal static class BluetoothNative
{
    public const AddressFamily AF_BTH = (AddressFamily)32;
    public const ProtocolType BTHPROTO_RFCOMM = (ProtocolType)3;

    public sealed record PairedDevice(ulong Address, string Name, bool Connected);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        public int fReturnAuthenticated;
        public int fReturnRemembered;
        public int fReturnUnknown;
        public int fReturnConnected;
        public int fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams, ref BLUETOOTH_DEVICE_INFO deviceInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO deviceInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    /// <summary>Lista dispositivos pareados (sem fazer inquiry, é barato).</summary>
    public static List<PairedDevice> GetPairedDevices()
    {
        var list = new List<PairedDevice>();
        var p = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1,
            fReturnRemembered = 1,
            fReturnConnected = 1,
            fReturnUnknown = 0,
            fIssueInquiry = 0,
            cTimeoutMultiplier = 0,
            hRadio = IntPtr.Zero
        };
        var info = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(), szName = "" };

        IntPtr h = BluetoothFindFirstDevice(ref p, ref info);
        if (h == IntPtr.Zero) return list;
        try
        {
            do
            {
                list.Add(new PairedDevice(info.Address, info.szName ?? "", info.fConnected != 0));
                info = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(), szName = "" };
            }
            while (BluetoothFindNextDevice(h, ref info));
        }
        finally
        {
            BluetoothFindDeviceClose(h);
        }
        return list;
    }

    public static ulong ParseAddress(string text) =>
        Convert.ToUInt64(text.Replace(":", "").Replace("-", "").Trim(), 16);

    public static string FormatAddress(ulong addr)
    {
        var hex = addr.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    /// <summary>
    /// Abre um socket RFCOMM pelo UUID do serviço (o Windows resolve o canal via SDP),
    /// igual o Chrome faz para o ear (web).
    /// </summary>
    public static async Task<Socket> ConnectRfcommAsync(ulong address, Guid service, TimeSpan timeout, CancellationToken ct)
    {
        var socket = new Socket(AF_BTH, SocketType.Stream, BTHPROTO_RFCOMM);
        try
        {
            var connect = Task.Run(() => socket.Connect(new BluetoothEndPoint(address, service)), ct);
            await connect.WaitAsync(timeout, ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>SOCKADDR_BTH (packed, 30 bytes): family(2) + btAddr(8) + serviceClassId(16) + port(4).</summary>
internal sealed class BluetoothEndPoint : EndPoint
{
    private const int SockAddrBthSize = 30;
    public ulong Address { get; }
    public Guid Service { get; }

    public BluetoothEndPoint(ulong address, Guid service)
    {
        Address = address;
        Service = service;
    }

    public override AddressFamily AddressFamily => BluetoothNative.AF_BTH;

    public override SocketAddress Serialize()
    {
        var sa = new SocketAddress(BluetoothNative.AF_BTH, SockAddrBthSize);
        var addr = BitConverter.GetBytes(Address);
        for (int i = 0; i < 8; i++) sa[2 + i] = addr[i];
        var guid = Service.ToByteArray();
        for (int i = 0; i < 16; i++) sa[10 + i] = guid[i];
        // port = 0 -> Windows faz a busca SDP pelo serviceClassId
        return sa;
    }

    public override EndPoint Create(SocketAddress socketAddress) => this;
}
