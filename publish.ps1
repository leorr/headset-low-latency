# Publica este projeto num repositório novo no seu GitHub, de uma vez só.
# Uso: powershell -ExecutionPolicy Bypass -File .\publish.ps1 [-Name headset-low-latency] [-Private]
param(
    [string]$Name = 'headset-low-latency',
    [switch]$Private
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Ensure-Tool($cmd, $wingetId) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Host "Instalando $cmd..." -ForegroundColor Yellow
        winget install --id $wingetId -e --accept-source-agreements --accept-package-agreements
        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                    [Environment]::GetEnvironmentVariable('Path', 'User')
    }
}

try {
    Ensure-Tool git 'Git.Git'
    Ensure-Tool gh  'GitHub.cli'

    gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { gh auth login --web --git-protocol https }

    if (-not (Test-Path .git)) {
        git init -b main | Out-Null
        git add -A
        git commit -m "Serviço Windows para manter baixa latência do headset Nothing/CMF" | Out-Null
    }

    $visibility = if ($Private) { '--private' } else { '--public' }
    gh repo create $Name $visibility --source . --remote origin --push `
        --description "Serviço Windows que liga automaticamente a baixa latência de headsets Nothing/CMF"

    $user = gh api user --jq .login
    (Get-Content README.md -Raw).Replace('<seu-usuario>', $user) | Set-Content README.md -Encoding utf8
    git commit -am "README: link do clone" | Out-Null
    git push | Out-Null

    Write-Host "`nPronto: https://github.com/$user/$Name" -ForegroundColor Green
}
catch {
    Write-Host "ERRO: $_" -ForegroundColor Red
}
finally {
    Read-Host "`nPressione Enter para fechar"
}
