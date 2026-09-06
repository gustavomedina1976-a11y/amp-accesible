param(
    [Parameter(Mandatory=$true)][string]$Repositorio,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Tag,
    [Parameter(Mandatory=$true)][string]$DirectorioPaquetes,
    [Parameter(Mandatory=$true)][string]$Salida
)
$ErrorActionPreference = 'Stop'

$packages = @()
Get-ChildItem -LiteralPath $DirectorioPaquetes -Filter '*.gdmupdate' -File | Sort-Object Name | ForEach-Object {
    if ($_.BaseName -match '^Amp_Accessible_(?<from>[0-9]+\.[0-9]+\.[0-9]+)_a_(?<to>[0-9]+\.[0-9]+\.[0-9]+)$') {
        $from = $Matches.from
        $to = $Matches.to
        if ($to -eq $Version) {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $url = "https://github.com/$Repositorio/releases/download/$Tag/$($_.Name)"
            $packages += [ordered]@{
                FromVersion = $from
                ToVersion = $to
                Url = $url
                Sha256 = $hash
            }
        }
    }
}

$manifest = [ordered]@{
    LatestVersion = $Version
    Packages = $packages
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Salida -Encoding UTF8
Write-Host "Canal generado: $Salida"
Write-Host "Paquetes directos incluidos: $($packages.Count)"
