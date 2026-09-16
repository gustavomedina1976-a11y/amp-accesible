#requires -Version 7.0
# Match the pwsh runtime used by GitHub Actions (ZIP entry separators differ in Windows PowerShell 5).
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work=Join-Path $repo ('obj\UpdateValidation\'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$selector=Join-Path $repo 'Installer\GitHub\SELECCIONAR_RELEASE_ANTERIOR.ps1'
$releases=@('2.41.9','2.41.73','2.41.58','2.41.74','2.41.75','2.42.0') | ForEach-Object { [pscustomobject]@{tag_name="v$_";draft=$false;prerelease=$false} }
$releases+=@([pscustomobject]@{tag_name='v2.41.74-rc.1';draft=$false;prerelease=$true},[pscustomobject]@{tag_name='v2.41.99';draft=$true;prerelease=$false})
if ((& $selector -Releases $releases -Version '2.41.75').Version -ne '2.41.74') { throw 'Previous release selection' }
if ((& $selector -Releases $releases -Version '2.41.73').Version -ne '2.41.58') { throw 'Patch gap selection' }
if ((& $selector -Releases $releases -Version '2.42.1').Version -ne '2.42.0') { throw 'Semantic minor selection' }
Write-Host 'PASS release selection: numeric versions, gaps, current/future, drafts and prereleases.'
foreach($dir in @('old','new','packages','installed','temp','project')) { New-Item -ItemType Directory -Force (Join-Path $work $dir) | Out-Null }
Set-Content (Join-Path $work 'old\program.dat') 'old'
Set-Content (Join-Path $work 'old\obsolete.dat') 'obsolete'
Set-Content (Join-Path $work 'new\program.dat') 'new'
Copy-Item (Join-Path $work 'old\*') (Join-Path $work 'installed')
$personal=@('NAM\personal.nam','IR\personal.wav','presets\mine.json','scenes\mine.json','settings\mine.json')
foreach($rel in $personal) {
    $path=Join-Path $work "installed\$rel";New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    Set-Content $path "personal-$rel"
}
$before=@{};foreach($rel in $personal){$before[$rel]=(Get-FileHash (Join-Path $work "installed\$rel")).Hash}
$package=Join-Path $work 'packages\Amp_Accessible_2.41.74_a_2.41.75.gdmupdate'
$oldTemp=$env:TEMP
try {
    $env:TEMP=Join-Path $work 'temp'
    & (Join-Path $repo 'Installer\CREAR_PAQUETE_ACTUALIZACION.ps1') -Anterior (Join-Path $work 'old') -Nuevo (Join-Path $work 'new') -Desde '2.41.74' -Hasta '2.41.75' -Salida $package
} finally { $env:TEMP=$oldTemp }
$channel=Join-Path $work 'channel.json'
$generator=Join-Path $repo 'Installer\GitHub\GENERAR_CANAL_ACTUALIZACION.ps1'
& $generator -Repositorio 'gustavomedina1976-a11y/amp-accesible' -Version '2.41.75' -Tag 'v2.41.75' -DirectorioPaquetes (Join-Path $work 'packages') -Salida $channel
$manifest=Get-Content $channel -Raw | ConvertFrom-Json
if($manifest.Packages.Count -ne 1 -or $manifest.Packages[0].Sha256 -ne (Get-FileHash $package).Hash.ToLowerInvariant()) { throw 'Channel hash/count' }
# The shipping UpdateService source is untouched. Only network and filesystem endpoints
# are redirected in this disposable test compilation, so no real install/account is used.
$source=[IO.File]::ReadAllText((Join-Path $repo 'Persistence\UpdateService.cs')).Replace('AppContext.BaseDirectory','Fixture.App').Replace('Path.GetTempPath()','Fixture.Temp').Replace('new HttpClient {','new HttpClient(new FixtureHandler()) {')
[IO.File]::WriteAllText((Join-Path $work 'project\UpdateService.cs'),$source)
Copy-Item (Join-Path $PSScriptRoot 'UpdateRegression.cs.txt') (Join-Path $work 'project\Program.cs')
$proj=Join-Path $work 'project\Test.csproj'
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content (Join-Path $work 'project\NuGet.Config')
$previousAppData=$env:APPDATA
try {
$env:APPDATA=Join-Path $work 'ToolAppData'
foreach($version in @('2.41.74','2.41.73')) {
    "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><Version>$version</Version></PropertyGroup></Project>" | Set-Content $proj
    dotnet restore $proj --configfile (Join-Path $work 'project\NuGet.Config') -v quiet
    if($LASTEXITCODE){throw 'Updater test restore failed'}
    dotnet run --project $proj -c Release --no-restore -- $work $channel $package
    if($LASTEXITCODE){throw 'Updater regression failed'}
}
} finally { $env:APPDATA=$previousAppData }
$apply=(Get-Content (Join-Path $work 'apply-script.txt') -Raw).Trim()
$resolved=[IO.Path]::GetFullPath($apply)
if(-not $resolved.StartsWith($work+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Apply script outside test workspace'}
# Execute the real generated copy/delete instructions. Mock only process waiting/restart.
& {
    function Get-Process { param($Id,$ErrorAction) return $null }
    function Start-Sleep { param($Milliseconds,$Seconds) }
    function Start-Process {
        param($FilePath,$WorkingDirectory,[switch]$PassThru)
        $fake=[pscustomobject]@{HasExited=$false}
        $fake | Add-Member ScriptMethod Refresh {}
        return $fake
    }
    & $apply -PidToWait 0
}
if((Get-Content (Join-Path $work 'installed\program.dat') -Raw).Trim() -ne 'new' -or (Test-Path (Join-Path $work 'installed\obsolete.dat'))) {throw 'Apply failed'}
foreach($rel in $personal){if((Get-FileHash (Join-Path $work "installed\$rel")).Hash -ne $before[$rel]){throw "Personal data changed: $rel"}}
Write-Host 'PASS incremental application: updates/deletions applied; NAM, IR, presets, scenes and settings byte-identical.'
# A second package must stop manifest generation, not silently advertise several bases.
Copy-Item $package (Join-Path $work 'packages\Amp_Accessible_2.41.73_a_2.41.75.gdmupdate')
$rejected=$false
try { & $generator -Repositorio 'fixture/test' -Version '2.41.75' -Tag 'v2.41.75' -DirectorioPaquetes (Join-Path $work 'packages') -Salida $channel } catch { $rejected=$true }
if(-not $rejected){throw 'Multiple incrementals accepted'}
Write-Host 'PASS: update regressions. Setup compile is validated separately by Inno Setup in the release workflow.'
