namespace HeadsetLowLatency;

public sealed class KeeperOptions
{
    /// <summary>Parte do nome do headset como aparece no Windows (case-insensitive).</summary>
    public string DeviceName { get; set; } = "Headphone Pro";

    /// <summary>Endereço MAC opcional (AA:BB:CC:DD:EE:FF). Se preenchido, tem prioridade sobre o nome.</summary>
    public string? DeviceAddress { get; set; }

    /// <summary>De quantos em quantos segundos verificar se o headset conectou.</summary>
    public int PollSeconds { get; set; } = 3;

    /// <summary>Espera após detectar a conexão, para o headset terminar de subir os perfis.</summary>
    public int ConnectDelaySeconds { get; set; } = 3;

    /// <summary>Reverificação periódica enquanto conectado (0 = desativado).</summary>
    public int RecheckMinutes { get; set; } = 5;
}
