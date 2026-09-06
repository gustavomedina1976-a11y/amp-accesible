param(
    [Parameter(Mandatory=$true)][string]$Repositorio,
    [Parameter(Mandatory=$true)][string]$DirectorioPublicacion
)
$ErrorActionPreference = 'Stop'
if ($Repositorio -notmatch '^[^/\s]+/[^/\s]+$') {
    throw 'Repositorio debe tener formato propietario/repositorio, por ejemplo gustavo/amp-accessible.'
}
if (-not (Test-Path -LiteralPath $DirectorioPublicacion)) {
    throw "No existe el directorio de publicación: $DirectorioPublicacion"
}
$configPath = Join-Path $DirectorioPublicacion 'update-channel.json'
$config = [ordered]@{
    ManifestUrl = "https://github.com/$Repositorio/releases/latest/download/update-channel.json"
}
$config | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
Write-Host "Canal GitHub configurado en $configPath"
