using HeadsetLowLatency;

// Modo utilitário: lista os dispositivos pareados e sai.
if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
{
    foreach (var d in BluetoothNative.GetPairedDevices())
        Console.WriteLine($"{BluetoothNative.FormatAddress(d.Address)}  {(d.Connected ? "[conectado]" : "           ")}  {d.Name}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "HeadsetLowLatency");
builder.Services.Configure<KeeperOptions>(builder.Configuration.GetSection("Headset"));
builder.Services.AddHostedService<Worker>();

builder.Build().Run();
