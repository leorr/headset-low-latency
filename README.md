# ðŸŽ§ Headset Low Latency Keeper

ServiÃ§o do Windows que **liga automaticamente o modo de baixa latÃªncia** de headsets Nothing / CMF
toda vez que eles conectam ao PC.

Sem ele, toda vez que o headset Ã© desligado e ligado (ou reconecta), o modo de baixa latÃªncia volta
para o padrÃ£o e Ã© preciso abrir o [ear (web)](https://earweb.bttl.xyz/) para ligar de novo na mÃ£o.
Este serviÃ§o fala diretamente com o headset pelo mesmo protocolo que o ear (web) usa.

> Testado com o **CMF Headphone Pro** (codinome `forretress`). Outros modelos Nothing/CMF que
> suportam baixa latÃªncia no ear (web) usam o mesmo comando e devem funcionar.

---

## SumÃ¡rio

- [Como funciona](#como-funciona)
- [Arquitetura](#arquitetura)
- [Fluxo do serviÃ§o](#fluxo-do-serviÃ§o)
- [Protocolo](#protocolo)
- [Requisitos](#requisitos)
- [InstalaÃ§Ã£o](#instalaÃ§Ã£o)
- [ConfiguraÃ§Ã£o](#configuraÃ§Ã£o)
- [Logs](#logs)
- [DesinstalaÃ§Ã£o](#desinstalaÃ§Ã£o)
- [SoluÃ§Ã£o de problemas](#soluÃ§Ã£o-de-problemas)
- [Estrutura do projeto](#estrutura-do-projeto)
- [CrÃ©ditos](#crÃ©ditos)

---

## Como funciona

1. A cada poucos segundos o serviÃ§o consulta a API Bluetooth do Windows (`bthprops.cpl`) para saber se o
   headset pareado estÃ¡ **conectado**. Essa consulta nÃ£o faz busca por dispositivos e nÃ£o acorda o headset.
2. Quando o headset conecta, o serviÃ§o espera alguns segundos e abre um socket **RFCOMM** pelo UUID do
   serviÃ§o de controle da Nothing. O Windows resolve o canal via SDP.
3. Ele **lÃª** o estado atual da latÃªncia. Se estiver desligada, envia o comando para **ligar** e lÃª de
   novo para confirmar.
4. Enquanto o headset fica conectado, ele revalida periodicamente (padrÃ£o: a cada 5 min).
5. Quando o headset desconecta e reconecta, o ciclo recomeÃ§a.

O socket de controle Ã© fechado logo depois de cada operaÃ§Ã£o, entÃ£o nÃ£o fica preso ao headset.

## Arquitetura

```mermaid
flowchart LR
    subgraph Windows
        SCM[Service Control Manager] -->|inicia no boot| SVC[HeadsetLowLatency.exe<br/>Worker Service .NET 8]
        SVC -->|BluetoothFindFirstDevice<br/>bthprops.cpl| BTAPI[API Bluetooth Win32]
        SVC -->|socket AF_BTH / RFCOMM| WS[Winsock Bluetooth]
        SVC -->|ILogger| EV[Visualizador de Eventos]
        CFG[appsettings.json] --> SVC
    end
    WS <-->|Bluetooth Classic<br/>RFCOMM via SDP| HS[ðŸŽ§ Headset Nothing/CMF]
    BTAPI -.->|estado: conectado?| SVC
```

## Fluxo do serviÃ§o

### Loop principal

```mermaid
flowchart TD
    A([InÃ­cio do serviÃ§o]) --> B[Procurar headset pareado<br/>por nome ou MAC]
    B --> C{Conectado?}
    C -- NÃ£o --> D{Estava conectado<br/>antes?}
    D -- Sim --> E[Log: desconectado]
    D -- NÃ£o --> W
    E --> W
    C -- Sim --> F{Acabou de<br/>conectar?}
    F -- Sim --> G[Esperar ConnectDelaySeconds]
    G --> H[Garantir baixa latÃªncia]
    F -- NÃ£o --> I{Hora da<br/>revalidaÃ§Ã£o?}
    I -- Sim --> H
    I -- NÃ£o --> W
    H --> J{Sucesso?}
    J -- Sim --> K[PrÃ³xima verificaÃ§Ã£o em<br/>RecheckMinutes]
    J -- NÃ£o --> L[Tentar de novo em 15s]
    K --> W
    L --> W
    W[Esperar PollSeconds] --> B
```

### Garantir baixa latÃªncia

```mermaid
flowchart TD
    A([Abrir RFCOMM<br/>UUID aeac4a03-...]) --> B[Enviar leitura 0xC041]
    B --> C{Resposta 0x4041<br/>byte 8}
    C -- 1 = ligado --> D([JÃ¡ estÃ¡ ligado âœ”])
    C -- 2 = desligado / sem resposta --> E[Enviar 0xF040<br/>payload 01 00]
    E --> F[Aguardar 400 ms]
    F --> G[Enviar leitura 0xC041]
    G --> H{Resposta}
    H -- 1 --> I([Ligado com sucesso âœ”])
    H -- sem resposta --> J([Enviado, sem confirmaÃ§Ã£o])
    H -- 2 --> K([Erro â†’ nova tentativa em 15s])
```

### Conversa com o headset

```mermaid
sequenceDiagram
    participant S as ServiÃ§o
    participant W as Winsock (AF_BTH)
    participant H as Headset

    S->>W: connect(MAC, UUID do serviÃ§o)
    W->>H: Consulta SDP â†’ canal RFCOMM
    W->>H: Abre canal RFCOMM
    S->>H: 55 60 01 41 C0 00 00 01 25 10 (ler latÃªncia)
    H-->>S: 55 .. 41 40 .. [02] .. (desligada)
    S->>H: 55 60 01 40 F0 02 00 02 01 00 <crc> (ligar)
    S->>H: 55 60 01 41 C0 00 00 03 <crc> (ler latÃªncia)
    H-->>S: 55 .. 41 40 .. [01] .. (ligada)
    S->>W: close()
```

## Protocolo

Os headsets Nothing/CMF expÃµem um serviÃ§o **RFCOMM** proprietÃ¡rio:

| Item | Valor |
|---|---|
| UUID do serviÃ§o | `aeac4a03-dff5-498f-843a-34487cf133eb` |
| Transporte | Bluetooth Classic, RFCOMM (canal resolvido via SDP) |

Formato de frame:

```
 0    1    2    3        4        5     6    7      8 ... n     n+1     n+2
[55] [60] [01] [cmd lo] [cmd hi] [len] [00] [opId] [payload] [crc lo] [crc hi]
```

- `cmd`: comando de 16 bits, little-endian
- `len`: tamanho do payload
- `opId`: contador de operaÃ§Ã£o (1 byte, incrementa a cada envio)
- `crc`: CRC-16/MODBUS (poly `0xA001`, init `0xFFFF`) sobre header + payload

Comandos usados:

| Comando | CÃ³digo | Payload | DescriÃ§Ã£o |
|---|---|---|---|
| Ler latÃªncia | `0xC041` | (vazio) | Pede o estado atual |
| Resposta latÃªncia | `0x4041` | byte 8: `1` ligado, `2` desligado | Enviado pelo headset |
| Definir latÃªncia | `0xF040` | `01 00` ligar / `02 00` desligar | Altera o modo |

Exemplos de frames (opId = 1):

```
Ligar baixa latÃªncia: 55 60 01 40 F0 02 00 01 01 00 76 3C
Ler latÃªncia:         55 60 01 41 C0 00 00 01 25 10
```

## Requisitos

| DependÃªncia | Para quÃª | Como instalar |
|---|---|---|
| Windows 10 ou 11 | API Bluetooth Win32 + Winsock AF_BTH | â€” |
| Headset jÃ¡ **pareado** no Windows | O serviÃ§o nÃ£o faz pareamento | ConfiguraÃ§Ãµes â†’ Bluetooth |
| .NET 8 SDK | Compilar o projeto | `winget install Microsoft.DotNet.SDK.8` |
| Acesso ao nuget.org | Baixar os pacotes abaixo no build | jÃ¡ configurado via `src/nuget.config` |

Pacotes NuGet:

- `Microsoft.Extensions.Hosting` 8.0.1: host genÃ©rico, DI, configuraÃ§Ã£o, logging
- `Microsoft.Extensions.Hosting.WindowsServices` 8.0.1: integraÃ§Ã£o com o SCM e com o Event Log

> O SDK sÃ³ Ã© necessÃ¡rio para compilar. Depois de instalado, o serviÃ§o roda sÃ³ com o **.NET 8 Runtime**.

## InstalaÃ§Ã£o

### 1. Clonar

```powershell
git clone https://github.com/leorr/headset-low-latency.git
cd headset-low-latency
```

> Se baixou como `.zip`, desbloqueie os arquivos antes, porque o Windows bloqueia scripts vindos da internet:
> ```powershell
> Get-ChildItem -Recurse | Unblock-File
> ```

### 2. Testar no console (recomendado)

```powershell
cd src
dotnet run -- --list   # lista os dispositivos pareados; confira o nome/MAC do headset
dotnet run             # roda no console; desligue e ligue o headset e acompanhe o log
```

SaÃ­da esperada:

```
info: HeadsetLowLatency.Worker[0] Monitorando headset 'Headphone Pro'
info: HeadsetLowLatency.Worker[0] Headset conectado: CMF Headphone Pro (AA:BB:CC:DD:EE:FF)
info: HeadsetLowLatency.Worker[0] Baixa latÃªncia LIGADA com sucesso.
```

> Feche o ear (web) durante o teste, porque sÃ³ um app consegue usar o canal de controle por vez.

### 3. Instalar como serviÃ§o

Clique com o botÃ£o direito em `install.ps1` â†’ **Run with PowerShell** e aceite o UAC.

Ou, num terminal na pasta do projeto:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

O script:

1. Pede elevaÃ§Ã£o de administrador sozinho (UAC)
2. Para e remove uma versÃ£o anterior do serviÃ§o, se existir
3. Compila e publica em `C:\Program Files\HeadsetLowLatency`
4. Cria o serviÃ§o `HeadsetLowLatency` com inÃ­cio **automÃ¡tico**
5. Configura reinÃ­cio automÃ¡tico em caso de falha
6. Inicia o serviÃ§o

```mermaid
flowchart LR
    A[install.ps1] --> B{Ã‰ admin?}
    B -- NÃ£o --> C[Reabre elevado via UAC] --> A
    B -- Sim --> D[Remove serviÃ§o antigo]
    D --> E[dotnet publish]
    E --> F[New-Service<br/>StartupType Automatic]
    F --> G[sc failure â†’ restart]
    G --> H[Start-Service âœ”]
```

## ConfiguraÃ§Ã£o

Arquivo: `C:\Program Files\HeadsetLowLatency\appsettings.json`

```json
{
  "Headset": {
    "DeviceName": "Headphone Pro",
    "DeviceAddress": "",
    "PollSeconds": 3,
    "ConnectDelaySeconds": 3,
    "RecheckMinutes": 5
  }
}
```

| Chave | PadrÃ£o | DescriÃ§Ã£o |
|---|---|---|
| `DeviceName` | `Headphone Pro` | Trecho do nome do headset como aparece no Windows (sem diferenciar maiÃºsculas) |
| `DeviceAddress` | vazio | MAC no formato `AA:BB:CC:DD:EE:FF`. Se preenchido, tem prioridade sobre o nome |
| `PollSeconds` | `3` | Intervalo entre as verificaÃ§Ãµes de conexÃ£o |
| `ConnectDelaySeconds` | `3` | Espera apÃ³s conectar antes de mandar o comando |
| `RecheckMinutes` | `5` | RevalidaÃ§Ã£o periÃ³dica enquanto conectado (`0` desativa) |

Depois de editar, reinicie o serviÃ§o:

```powershell
Restart-Service HeadsetLowLatency
```

## Logs

**Visualizador de Eventos** â†’ Logs do Windows â†’ **Aplicativo** â†’ fonte `HeadsetLowLatency`

Ou pelo PowerShell:

```powershell
Get-EventLog -LogName Application -Source HeadsetLowLatency -Newest 20
```

## DesinstalaÃ§Ã£o

BotÃ£o direito em `uninstall.ps1` â†’ **Run with PowerShell**, ou:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

## SoluÃ§Ã£o de problemas

| Sintoma | Causa | SoluÃ§Ã£o |
|---|---|---|
| `install.ps1 is not digitally signed` | Arquivos marcados como baixados da internet | `Get-ChildItem -Recurse \| Unblock-File` |
| `No .NET SDKs were found` | SÃ³ o runtime estÃ¡ instalado | `winget install Microsoft.DotNet.SDK.8` e abrir um terminal **novo** |
| `NU1100: Unable to resolve 'Microsoft.Extensions.Hosting'` | NuGet sem fonte de pacotes | JÃ¡ resolvido pelo `src/nuget.config`; ou `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org` |
| Log nÃ£o mostra "Headset conectado" | Nome nÃ£o bate | Rode `dotnet run -- --list` e ajuste `DeviceName` ou `DeviceAddress` |
| `Falha ao aplicar baixa latÃªncia` repetido | Outro app (ear (web), Nothing X) usando o canal | Feche o outro app |
| LatÃªncia volta a desligar sem reconectar | Firmware mudou o modo sozinho | Diminua `RecheckMinutes` |

## Estrutura do projeto

```
.
â”œâ”€â”€ install.ps1              # compila, instala e inicia o serviÃ§o (auto-eleva)
â”œâ”€â”€ uninstall.ps1            # para e remove o serviÃ§o
â”œâ”€â”€ publish.ps1              # cria o repositÃ³rio no GitHub e faz o push
â”œâ”€â”€ README.md
â””â”€â”€ src/
    â”œâ”€â”€ HeadsetLowLatency.csproj
    â”œâ”€â”€ nuget.config         # fonte nuget.org explÃ­cita
    â”œâ”€â”€ appsettings.json     # configuraÃ§Ã£o do serviÃ§o
    â”œâ”€â”€ Program.cs           # host + modo --list
    â”œâ”€â”€ KeeperOptions.cs     # opÃ§Ãµes de configuraÃ§Ã£o
    â”œâ”€â”€ Worker.cs            # loop de monitoramento e lÃ³gica de baixa latÃªncia
    â”œâ”€â”€ BluetoothNative.cs   # P/Invoke bthprops.cpl + endpoint RFCOMM (SOCKADDR_BTH)
    â””â”€â”€ NothingProtocol.cs   # montagem de frames, CRC-16 e parser de respostas
```

## CrÃ©ditos

- Protocolo baseado no [ear (web)](https://github.com/radiance-project/ear-web) do radiance-project
- Vetores de teste conferidos com o [nothingctl](https://github.com/FormalSnake/nothingctl)

> Projeto nÃ£o oficial. NÃ£o Ã© afiliado Ã  Nothing Technology Limited. Use por sua conta e risco.

