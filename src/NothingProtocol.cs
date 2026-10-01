namespace HeadsetLowLatency;

/// <summary>
/// Protocolo RFCOMM dos produtos Nothing/CMF (o mesmo que o ear (web) usa).
/// Frame: 55 60 01 [cmd lo] [cmd hi] [len] 00 [opId] [payload...] [crc lo] [crc hi]
/// CRC-16/MODBUS sobre header + payload.
/// </summary>
internal static class NothingProtocol
{
    public static readonly Guid ServiceUuid = new("aeac4a03-dff5-498f-843a-34487cf133eb");

    public const ushort CmdLatencySet  = 0xF040; // payload: [01,00] = ligado, [02,00] = desligado
    public const ushort CmdLatencyRead = 0xC041;
    public const ushort ReplyLatency   = 0x4041; // byte 8: 1 = ligado, 2 = desligado

    public static byte[] BuildFrame(ushort command, ReadOnlySpan<byte> payload, byte operationId)
    {
        var frame = new byte[8 + payload.Length + 2];
        frame[0] = 0x55;
        frame[1] = 0x60;
        frame[2] = 0x01;
        frame[3] = (byte)(command & 0xFF);
        frame[4] = (byte)(command >> 8);
        frame[5] = (byte)payload.Length;
        frame[6] = 0x00;
        frame[7] = operationId;
        payload.CopyTo(frame.AsSpan(8));

        ushort crc = Crc16(frame.AsSpan(0, 8 + payload.Length));
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>Extrai frames completos do buffer (remove os consumidos).</summary>
    public static IEnumerable<(ushort Command, byte[] Frame)> ExtractFrames(List<byte> buffer)
    {
        var result = new List<(ushort, byte[])>();
        while (true)
        {
            int start = buffer.IndexOf(0x55);
            if (start < 0) { buffer.Clear(); break; }
            if (start > 0) buffer.RemoveRange(0, start);
            if (buffer.Count < 8) break;

            int total = 8 + buffer[5] + 2;
            if (buffer.Count < total) break;

            var frame = buffer.GetRange(0, total).ToArray();
            buffer.RemoveRange(0, total);
            result.Add(((ushort)(frame[3] | (frame[4] << 8)), frame));
        }
        return result;
    }
}
