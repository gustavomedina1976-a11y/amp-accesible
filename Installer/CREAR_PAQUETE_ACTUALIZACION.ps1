param(
  [Parameter(Mandatory=$true)][string]$Anterior,
  [Parameter(Mandatory=$true)][string]$Nuevo,
  [Parameter(Mandatory=$true)][string]$Desde,
  [Parameter(Mandatory=$true)][string]$Hasta,
  [string]$Salida = ""
)
$ErrorActionPreference = 'Stop'
$Anterior = (Resolve-Path $Anterior).Path
$Nuevo = (Resolve-Path $Nuevo).Path
if ([string]::IsNullOrWhiteSpace($Salida)) {
  $Salida = Join-Path $PSScriptRoot ("Amp_Accessible_{0}_a_{1}.gdmupdate" -f $Desde,$Hasta)
}
$temp = Join-Path $env:TEMP ("GDM_UpdateBuild_" + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $temp 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
$files = @()
Get-ChildItem $Nuevo -File -Recurse | ForEach-Object {
  $rel = $_.FullName.Substring($Nuevo.Length).TrimStart('\')
  $old = Join-Path $Anterior $rel
  $newHash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $changed = (-not (Test-Path $old)) -or ((Get-FileHash $old -Algorithm SHA256).Hash.ToLowerInvariant() -ne $newHash)
  if ($changed) {
    $dest = Join-Path $payload $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item $_.FullName $dest -Force
    $files += [ordered]@{ Path=$rel.Replace('\','/'); Sha256=$newHash }
  }
}
$deleted = @()
Get-ChildItem $Anterior -File -Recurse | ForEach-Object {
  $rel = $_.FullName.Substring($Anterior.Length).TrimStart('\')
  if (-not (Test-Path (Join-Path $Nuevo $rel))) { $deleted += $rel.Replace('\','/') }
}
$manifest = [ordered]@{ FromVersion=$Desde; ToVersion=$Hasta; Files=$files; Delete=$deleted }
$manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $temp 'update.json') -Encoding UTF8
if (Test-Path $Salida) { Remove-Item $Salida -Force }
Compress-Archive -Path (Join-Path $temp '*') -DestinationPath ($Salida + '.zip') -CompressionLevel Optimal
Move-Item ($Salida + '.zip') $Salida -Force
Remove-Item $temp -Recurse -Force
Write-Host "Paquete incremental creado: $Salida"
Write-Host "Archivos nuevos o modificados: $($files.Count). Eliminados: $($deleted.Count)."
