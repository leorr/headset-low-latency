using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace HeadsetLowLatency;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly KeeperOptions _opt;
    private byte _opId;

    public Worker(ILogger<Worker> log, IOptions<KeeperOptions> opt)
    {
        _log = log;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Monitorando headset '{Name}' {Addr}", _opt.DeviceName,
            string.IsNullOrWhiteSpace(_opt.DeviceAddress) ? "" : $"({_opt.DeviceAddress})");

        bool wasConnected = false;
        DateTime nextCheck = DateTime.MaxValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var dev = FindDevice();
                bool connected = dev is { Connected: true };

                if (connected && !wasConnected)
                {
                    _log.LogInformation("Headset conectado: {Name} ({Addr})", dev!.Name, BluetoothNative.FormatAddress(dev.Address));
                    await Task.Delay(TimeSpan.FromSeconds(_opt.ConnectDelaySeconds), ct);
                    nextCheck = await TryEnsure(dev, ct);
                }
                else if (connected && DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = await TryEnsure(dev!, ct);
                }
                else if (!connected && wasConnected)
                {
                    _log.LogInformation("Headset desconectado.");
                    nextCheck = DateTime.MaxValue;
                }

                wasConnected = connected;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro no loop de monitoramento");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_opt.PollSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private BluetoothNative.PairedDevice? FindDevice()
    {
        var devices = BluetoothNative.GetPairedDevices();

        if (!string.IsNullOrWhiteSpace(_opt.DeviceAddress))
        {
            ulong addr = BluetoothNative.ParseAddress(_opt.DeviceAddress);
            return devices.FirstOrDefault(d => d.Address == addr);
        }

        return devices.FirstOrDefault(d => d.Name.Contains(_opt.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Retorna quando deve ser a próxima verificação.</summary>
    private async Task<DateTime> TryEnsure(BluetoothNative.PairedDevice dev, CancellationToken ct)
    {
        try
        {
            await EnsureLowLatency(dev.Address, ct);
            return _opt.RecheckMinutes > 0 ? DateTime.UtcNow.AddMinutes(_opt.RecheckMinutes) : DateTime.MaxValue;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Falha ao aplicar baixa latência ({Msg}). Tentando de novo em 15s.", ex.Message);
            return DateTime.UtcNow.AddSeconds(15);
        }
    }

    private async Task EnsureLowLatency(ulong address, CancellationToken ct)
    {
        using var socket = await BluetoothNative.ConnectRfcommAsync(
            address, NothingProtocol.ServiceUuid, TimeSpan.FromSeconds(15), ct);
        socket.ReceiveTimeout = 2000;

        bool? state = ReadLatency(socket);
        if (state == true)
        {
            _log.LogInformation("Baixa latência já está ligada.");
            return;
        }

        Send(socket, NothingProtocol.CmdLatencySet, [0x01, 0x00]);
        await Task.Delay(400, ct);

        state = ReadLatency(socket);
        if (state == false)
            throw new InvalidOperationException("o headset respondeu que a baixa latência continua desligada");

        _log.LogInformation(state == true
            ? "Baixa latência LIGADA com sucesso."
            : "Comando de baixa latência enviado (sem confirmação de leitura).");
    }

    private void Send(Socket s, ushort cmd, byte[] payload)
    {
        _opId++;
        s.Send(NothingProtocol.BuildFrame(cmd, payload, _opId));
    }

    private bool? ReadLatency(Socket s)
    {
        Send(s, NothingProtocol.CmdLatencyRead, []);

        var buffer = new List<byte>();
        var chunk = new byte[512];
        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            int n;
            try { n = s.Receive(chunk); }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut) { break; }
            if (n <= 0) break;

            buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            foreach (var (cmd, frame) in NothingProtocol.ExtractFrames(buffer))
            {
                if (cmd == NothingProtocol.ReplyLatency && frame.Length > 8)
                    return frame[8] switch { 1 => true, 2 => false, _ => null };
            }
        }
        return null;
    }
}
