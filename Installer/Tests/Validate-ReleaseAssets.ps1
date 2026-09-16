param(
    [Parameter(Mandatory=$true)][string]$Directory,
    [string]$Version='2.41.75',
    [string]$Repository='gustavomedina1976-a11y/amp-accesible'
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files=@(Get-ChildItem -LiteralPath $Directory -File)
$packages=@($files | Where-Object Extension -eq '.gdmupdate')
if($packages.Count -ne 1 -or $files.Count -ne 4){throw 'Release must contain exactly four assets and one incremental'}
$channel=Get-Content (Join-Path $Directory 'update-channel.json') -Raw | ConvertFrom-Json
if($channel.LatestVersion -ne $Version -or $channel.Packages.Count -ne 1){throw 'Invalid channel version/count'}
$link=$channel.Packages[0]
if($Version -eq '2.41.75' -and $link.FromVersion -ne '2.41.74'){throw 'Wrong 2.41.75 baseline'}
$expected="Amp_Accessible_$($link.FromVersion)_a_${Version}.gdmupdate"
if($packages[0].Name -ne $expected -or $link.ToVersion -ne $Version){throw 'Wrong package name/target'}
if($link.Url -ne "https://github.com/$Repository/releases/download/v$Version/$expected"){throw 'Invalid HTTPS asset URL'}
if((Get-FileHash $packages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $link.Sha256){throw 'Package SHA-256 mismatch'}
$archive=[IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $reader=[IO.StreamReader]::new($archive.GetEntry('update.json').Open())
    try{$manifest=$reader.ReadToEnd() | ConvertFrom-Json}finally{$reader.Dispose()}
    if($manifest.FromVersion -ne $link.FromVersion -or $manifest.ToVersion -ne $Version){throw 'Package manifest versions mismatch'}
    foreach($file in $manifest.Files){
        $entry=$archive.GetEntry('payload/'+$file.Path.Replace('\','/'))
        if($null -eq $entry){throw "Missing payload $($file.Path)"}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()}
        if($hash -ne $file.Sha256){throw "Payload hash mismatch $($file.Path)"}
    }
    foreach($rel in @($manifest.Files | ForEach-Object Path)+@($manifest.Delete)){
        if($rel -match '(^|[/\\])(NAM|IR|presets|scenes|settings)([/\\]|$)'){throw "Personal-data path in package: $rel"}
    }
} finally{$archive.Dispose()}
$zipPath=Join-Path $Directory "Amp_Accessible_${Version}_Publicacion.zip"
$zip=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($zipPath))
try { if(-not ($zip.Entries | Where-Object Name -eq 'AmpAccessible.exe') -or -not ($zip.Entries | Where-Object Name -eq 'AmpAccessible.dll')){throw 'Incomplete publication ZIP'} } finally{$zip.Dispose()}
$setup=Join-Path $Directory "Amp_Accessible_${Version}_Setup.exe"
$stream=[IO.File]::OpenRead($setup)
try{if($stream.ReadByte() -ne 77 -or $stream.ReadByte() -ne 90 -or $stream.Length -lt 1000000){throw 'Invalid Setup executable'}}finally{$stream.Dispose()}
Write-Host "PASS release assets: exactly four files; $expected; HTTPS URL, package and payload SHA-256, From/To, publication ZIP and Setup executable valid."
