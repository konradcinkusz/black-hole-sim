<#
.SYNOPSIS
    Generates the RSA keypair the identity service signs tokens with.

.DESCRIPTION
    The same job as `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048`, for
    machines where `openssl` is not a command — which on Windows is most of them.

    Exists as a script rather than as a one-liner in the docs because the way to do this
    differs by platform and by PowerShell version, and a difference like that belongs in
    code somebody can run and fix, not in prose every reader has to adapt for themselves.

    Three routes are tried in order:
      1. .NET's own PEM export      (PowerShell 7+, which runs on .NET 7 or later)
      2. openssl on PATH            (Linux, macOS, or a PowerShell that can see one)
      3. openssl bundled with Git for Windows

.PARAMETER Path
    Where to write the key. Defaults to secrets/jwt-signing.pem, which is gitignored and
    is where docker-compose.yml and the Aspire AppHost expect to find it.

.PARAMETER Deployment
    Write nothing; print the key to stdout instead, for pasting into a platform secret
    (the JWT_SIGNING_KEY GitHub environment secret — see flyio/SECRETS.md).

    A deployed instance must not share a key with a laptop. This switch exists so the
    two are separate acts rather than one file copied to two places.

.EXAMPLE
    ./scripts/new-signing-key.ps1
    Writes secrets/jwt-signing.pem for local development.

.EXAMPLE
    ./scripts/new-signing-key.ps1 -Deployment
    Prints a fresh key to paste into JWT_SIGNING_KEY.
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path (Split-Path -Parent $PSScriptRoot) 'secrets/jwt-signing.pem'),
    [switch] $Deployment
)

$ErrorActionPreference = 'Stop'

function New-PemViaDotNet {
    # ExportPkcs8PrivateKeyPem arrived in .NET 7, so this is PowerShell 7+ only. Windows
    # PowerShell 5.1 runs on .NET Framework and will not have it.
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        if ($rsa | Get-Member -Name 'ExportPkcs8PrivateKeyPem' -MemberType Method) {
            return $rsa.ExportPkcs8PrivateKeyPem()
        }
        return $null
    }
    finally { $rsa.Dispose() }
}

function Find-OpenSsl {
    $onPath = Get-Command openssl -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # Git for Windows ships one. Most people who have this repository have Git.
    foreach ($candidate in @(
        "$env:ProgramFiles\Git\usr\bin\openssl.exe",
        "${env:ProgramFiles(x86)}\Git\usr\bin\openssl.exe",
        "$env:LOCALAPPDATA\Programs\Git\usr\bin\openssl.exe"
    )) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    return $null
}

$pem = New-PemViaDotNet

if (-not $pem) {
    $openssl = Find-OpenSsl

    if (-not $openssl) {
        throw @'
No way to generate a keypair was found. Either:
  - run this in PowerShell 7 (pwsh), which can do it with no external tool, or
  - install Git for Windows, which bundles openssl, or
  - generate one by hand:
      openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out secrets/jwt-signing.pem
'@
    }

    # -outform PEM and PKCS#8 are genpkey's defaults; named anyway, because the importer
    # rejects PKCS#1 with an error that names neither format.
    $pem = & $openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -outform PEM 2>$null | Out-String

    if ($LASTEXITCODE -ne 0 -or -not $pem.Contains('PRIVATE KEY')) {
        throw "openssl at '$openssl' did not produce a private key."
    }
}

if ($Deployment) {
    Write-Host 'Paste everything between the lines into the JWT_SIGNING_KEY secret,' -ForegroundColor Cyan
    Write-Host 'BEGIN and END lines included. Do not save it in this repository.'   -ForegroundColor Cyan
    Write-Host ('-' * 68)
    Write-Output $pem.Trim()
    Write-Host ('-' * 68)
    exit 0
}

$directory = Split-Path -Parent $Path
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (Test-Path $Path) {
    # Overwriting silently would invalidate every token and every session already issued
    # against the old key, with no way back.
    throw "$Path already exists. Delete it first if you really mean to replace the key."
}

# ASCII, and no BOM: a UTF-8 BOM in front of '-----BEGIN' makes the PEM unparseable, and
# it is invisible in every editor that would show you the file.
[System.IO.File]::WriteAllText($Path, $pem.Trim() + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $Path" -ForegroundColor Green
Write-Host 'This is a local development key. Generate a separate one for deployment:' -ForegroundColor Yellow
Write-Host '  ./scripts/new-signing-key.ps1 -Deployment'
