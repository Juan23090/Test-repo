param(
    [string]$ProjectDirectory = (Resolve-Path "$PSScriptRoot\.."),
    [string]$RequiredVersion = "3.9.2"
)

$ErrorActionPreference = "Stop"

$moduleDestination =
    Join-Path `
        $ProjectDirectory `
        "PowerShellModules"

if (Test-Path $moduleDestination)
{
    Write-Host "Removing existing PowerShell modules..."

    Remove-Item `
        -Path $moduleDestination `
        -Recurse `
        -Force
}

New-Item `
    -ItemType Directory `
    -Path $moduleDestination `
    -Force |
    Out-Null

Write-Host `
    "Downloading ExchangeOnlineManagement $RequiredVersion..."

Save-Module `
    -Name "ExchangeOnlineManagement" `
    -RequiredVersion $RequiredVersion `
    -Path $moduleDestination `
    -Repository "PSGallery" `
    -Force

if ($env:OS -eq "Windows_NT")
{
    Write-Host "Unblocking downloaded PowerShell module files..."

    Get-ChildItem `
        -Path $moduleDestination `
        -Recurse `
        -File |
        Unblock-File
}

$manifest =
    Get-ChildItem `
        -Path $moduleDestination `
        -Filter "ExchangeOnlineManagement.psd1" `
        -Recurse `
        -File |
    Where-Object {
        $_.FullName -match `
            [regex]::Escape($RequiredVersion)
    } |
    Select-Object `
        -First 1

if ($null -eq $manifest)
{
    throw `
        "ExchangeOnlineManagement $RequiredVersion was not found after Save-Module completed."
}

Write-Host `
    "ExchangeOnlineManagement $RequiredVersion saved to $moduleDestination"

Write-Host `
    "Module manifest found at $($manifest.FullName)"