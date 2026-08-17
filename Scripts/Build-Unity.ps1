[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("WindowsClient", "LinuxServer")]
    [string]$Target,

    [string]$UnityPath = $env:QUIETER_UNITY_PATH,
    [string]$Output = (Join-Path $PSScriptRoot "..\Builds"),
    [string]$ServerHost = "",
    [ushort]$ServerPort = 7777,
    [string]$DtlsCaFile = "",
    [string]$DtlsServerName = "quieter-server",
    [uint32]$SteamAppId = 480,
    [string]$BuildVersion = "0.1.0-dev",
    [switch]$Production
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$requiredUnityVersion = "6000.5.8f1"

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$requiredUnityVersion\Editor\Unity.exe"
}

if ([string]::IsNullOrWhiteSpace($UnityPath) -or -not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity Editor не найден. Передайте -UnityPath или задайте QUIETER_UNITY_PATH."
}

$detectedUnityVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion.Split('_')[0]
if ($detectedUnityVersion -ne $requiredUnityVersion) {
    throw "Нужен Unity $requiredUnityVersion, найден $detectedUnityVersion."
}

if ($Production -and $SteamAppId -eq 480) {
    throw "Производственную сборку нельзя создавать со Steam App ID 480."
}

$env:QUIETER_BUILD_OUTPUT = [System.IO.Path]::GetFullPath($Output)
$env:QUIETER_STEAM_APP_ID = $SteamAppId.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:QUIETER_PRODUCTION_BUILD = if ($Production) { "1" } else { "0" }
$env:QUIETER_BUILD_VERSION = $BuildVersion

if (-not [string]::IsNullOrWhiteSpace($ServerHost)) {
    $env:QUIETER_SERVER_HOST = $ServerHost
    $env:QUIETER_SERVER_PORT = $ServerPort.ToString([Globalization.CultureInfo]::InvariantCulture)
}

if (-not [string]::IsNullOrWhiteSpace($DtlsCaFile)) {
    $env:QUIETER_DTLS_CA_FILE = (Resolve-Path -LiteralPath $DtlsCaFile).Path
    $env:QUIETER_DTLS_SERVER_NAME = $DtlsServerName
}

$method = if ($Target -eq "WindowsClient") {
    "Quieter.Editor.QuieterBuild.BuildWindowsFromCommandLine"
} else {
    "Quieter.Editor.QuieterBuild.BuildLinuxServerFromCommandLine"
}

& $UnityPath -batchmode -quit -projectPath $projectRoot -executeMethod $method
if ($LASTEXITCODE -ne 0) {
    throw "Unity завершил сборку с кодом $LASTEXITCODE. Проверьте Editor.log."
}

Write-Host "Сборка готова: $env:QUIETER_BUILD_OUTPUT"
