# Compila e instala o serviço "HeadsetLowLatency".
# Pode rodar com "Run with PowerShell": o script pede elevação (UAC) sozinho.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    exit
}

$ErrorActionPreference = 'Stop'
try {
    $name = 'HeadsetLowLatency'
    $src  = Join-Path $PSScriptRoot 'src'
    $dest = Join-Path $env:ProgramFiles 'HeadsetLowLatency'

    $existing = Get-Service $name -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne 'Stopped') { Stop-Service $name -Force }
        sc.exe delete $name | Out-Null
        Start-Sleep -Seconds 2
    }

    dotnet publish $src -c Release -o $dest
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

    New-Service -Name $name `
        -BinaryPathName "`"$dest\HeadsetLowLatency.exe`"" `
        -DisplayName 'Headset Low Latency Keeper' `
        -Description 'Liga automaticamente o modo de baixa latência do headset Nothing/CMF quando ele conecta.' `
        -StartupType Automatic | Out-Null

    # Reinicia sozinho se cair
    sc.exe failure $name reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

    Start-Service $name
    Write-Host "Serviço instalado em $dest e iniciado." -ForegroundColor Green
    Write-Host "Logs: Visualizador de Eventos > Logs do Windows > Aplicativo (fonte: HeadsetLowLatency)"
}
catch {
    Write-Host "ERRO: $_" -ForegroundColor Red
}
finally {
    Read-Host "`nPressione Enter para fechar"
}
