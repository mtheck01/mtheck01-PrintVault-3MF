param(
    [Parameter(Mandatory=$true)]
    [string]$RegistrationToken,
    [string]$RepositoryUrl = "https://github.com/mtheck01/mtheck01-PrintVault-3MF",
    [string]$RunnerName = "PrintVault-Validation",
    [string]$RunnerDirectory = "C:\actions-runner",
    [switch]$InstallService
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

New-Item -ItemType Directory -Force -Path $RunnerDirectory | Out-Null
Set-Location $RunnerDirectory

$release = Invoke-RestMethod -Uri "https://api.github.com/repos/actions/runner/releases/latest" -Headers @{ "User-Agent" = "PrintVault-Runner-Setup" }
$asset = $release.assets | Where-Object { $_.name -match "actions-runner-win-x64-.*\.zip$" } | Select-Object -First 1
if (-not $asset) { throw "Could not locate the current Windows x64 GitHub Actions runner package." }

$zip = Join-Path $RunnerDirectory $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip
Expand-Archive -LiteralPath $zip -DestinationPath $RunnerDirectory -Force
Remove-Item $zip -Force

$arguments = @(
    "--unattended",
    "--url", $RepositoryUrl,
    "--token", $RegistrationToken,
    "--name", $RunnerName,
    "--labels", "printvault-validation",
    "--work", "_work"
)
if ($InstallService) {
    Write-Host "The runner will be installed as a Windows service after registration."
    Write-Host "IMPORTANT: configure the service to run as the Windows account that owns the PrintVault library."
    $arguments += "--runasservice"
}
& (Join-Path $RunnerDirectory "config.cmd") @arguments

if ($LASTEXITCODE -ne 0) {
    throw "GitHub Actions runner registration failed with exit code $LASTEXITCODE."
}

Write-Host "PrintVault validation runner installed."
Write-Host "Runner name: $RunnerName"
Write-Host "Label: printvault-validation"
Write-Host "Directory: $RunnerDirectory"
Write-Host "InstallService: $InstallService"
if ($InstallService) { Write-Host "Verify the Windows service Log On account before the first autonomous run." }
