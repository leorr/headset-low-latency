# Remove o serviço "HeadsetLowLatency". Pede elevação (UAC) sozinho.

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
    $dest = Join-Path $env:ProgramFiles 'HeadsetLowLatency'

    if (Get-Service $name -ErrorAction SilentlyContinue) {
        Stop-Service $name -Force -ErrorAction SilentlyContinue
        sc.exe delete $name | Out-Null
    }
    Start-Sleep -Seconds 2
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Write-Host "Serviço removido." -ForegroundColor Green
}
catch {
    Write-Host "ERRO: $_" -ForegroundColor Red
}
finally {
    Read-Host "`nPressione Enter para fechar"
}
