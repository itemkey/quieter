[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RemoteHost,

    [string]$RemoteUser = "quieter",
    [string]$RemoteRoot = "/opt/quieter",
    [string]$UnityPath = $env:QUIETER_UNITY_PATH,
    [string]$Version = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildRoot = Join-Path $projectRoot "Builds"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $revision = (& git -C $projectRoot rev-parse --short=8 HEAD 2>$null)
    if ([string]::IsNullOrWhiteSpace($revision)) { $revision = "working" }
    $Version = "{0}-{1}" -f (Get-Date -Format "yyyyMMddHHmmss"), $revision.Trim()
}

if ($Version -notmatch '^[0-9A-Za-z._-]+$') {
    throw "Version может содержать только буквы, цифры, точку, подчёркивание и дефис."
}

if ($RemoteRoot -notmatch '^/[0-9A-Za-z._/-]+$') {
    throw "RemoteRoot должен быть абсолютным Linux-путём без пробелов."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "Build-Unity.ps1") -Target LinuxServer -UnityPath $UnityPath -Output $buildRoot -BuildVersion $Version
}

$serverExecutable = Join-Path $buildRoot "LinuxServer\QuieterServer"
if (-not (Test-Path -LiteralPath $serverExecutable)) {
    throw "Linux Dedicated Server не найден: $serverExecutable"
}

$releaseRoot = Join-Path $projectRoot "Deploy\releases\$Version"
$archivePath = Join-Path $projectRoot "Deploy\releases\quieter-$Version.tar.gz"
if ((Test-Path -LiteralPath $releaseRoot) -or (Test-Path -LiteralPath $archivePath)) {
    throw "Локальный релиз $Version уже существует; задайте новую версию."
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "Backend") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "Deploy\secrets") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "Builds") | Out-Null

$backendSource = Join-Path $projectRoot "Backend\Quieter.ProfileService"
$backendDestination = Join-Path $releaseRoot "Backend\Quieter.ProfileService"
Get-ChildItem $backendSource -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $relative = $_.FullName.Substring($backendSource.Length + 1)
        $destinationFile = Join-Path $backendDestination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destinationFile) | Out-Null
        Copy-Item -Force $_.FullName $destinationFile
    }
Copy-Item -Recurse -Force (Join-Path $buildRoot "LinuxServer") (Join-Path $releaseRoot "Builds")
foreach ($file in @("docker-compose.yml", "GameServer.Dockerfile", "backup-postgres.sh", "README.md", ".env.example")) {
    Copy-Item -Force (Join-Path $projectRoot "Deploy\$file") (Join-Path $releaseRoot "Deploy\$file")
}
Copy-Item -Force (Join-Path $projectRoot "Deploy\secrets\README.md") (Join-Path $releaseRoot "Deploy\secrets\README.md")

& tar -czf $archivePath -C $releaseRoot .
if ($LASTEXITCODE -ne 0) { throw "Не удалось создать архив релиза." }

$destination = "$RemoteUser@$RemoteHost"
& ssh $destination "mkdir -p '$RemoteRoot/releases/$Version' '$RemoteRoot/shared/secrets'"
if ($LASTEXITCODE -ne 0) { throw "Не удалось подготовить каталог на сервере." }
& scp $archivePath "${destination}:$RemoteRoot/releases/$Version/release.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "Не удалось скопировать архив на сервер." }

$remoteScript = @'
set -eu
root='__ROOT__'
version='__VERSION__'
release="$root/releases/$version"
previous="$(readlink -f "$root/current" 2>/dev/null || true)"

test -f "$root/shared/.env"
test -f "$root/shared/secrets/postgres_password.txt"
test -f "$root/shared/secrets/profile_token.txt"
test -f "$root/shared/secrets/dtls_certificate.pem"
test -f "$root/shared/secrets/dtls_private_key.pem"

tar -xzf "$release/release.tar.gz" -C "$release"
rm -rf "$release/Deploy/secrets"
ln -s "$root/shared/secrets" "$release/Deploy/secrets"
ln -s "$root/shared/.env" "$release/Deploy/.env"

cd "$release/Deploy"
export QUIETER_RELEASE="$version"
docker compose build profile-service game-server
docker compose up -d --wait postgres
docker compose run --rm profile-service --migrate

ln -sfn "$release" "$root/current.next"
mv -Tf "$root/current.next" "$root/current"

if grep -q '^QUIETER_RELEASE=' "$root/shared/.env"; then
    sed -i "s/^QUIETER_RELEASE=.*/QUIETER_RELEASE=$version/" "$root/shared/.env"
else
    printf '\nQUIETER_RELEASE=%s\n' "$version" >>"$root/shared/.env"
fi

if ! docker compose up -d --remove-orphans --wait; then
    if [ -n "$previous" ] && [ -d "$previous/Deploy" ]; then
        previous_version="$(basename "$previous")"
        ln -sfn "$previous" "$root/current.next"
        mv -Tf "$root/current.next" "$root/current"
        sed -i "s/^QUIETER_RELEASE=.*/QUIETER_RELEASE=$previous_version/" "$root/shared/.env"
        cd "$previous/Deploy"
        export QUIETER_RELEASE="$previous_version"
        docker compose up -d --remove-orphans --wait
    fi
    exit 1
fi

docker compose ps
'@
$remoteScript = $remoteScript.Replace("__ROOT__", $RemoteRoot).Replace("__VERSION__", $Version)
$remoteScript | & ssh $destination "sh -s"
if ($LASTEXITCODE -ne 0) { throw "Выкладка не прошла проверку здоровья; выполнен доступный откат." }

Write-Host "Quieter $Version развёрнут на $RemoteHost и прошёл health checks."
