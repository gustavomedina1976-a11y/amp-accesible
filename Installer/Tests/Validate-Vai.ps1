param([string]$Baseline = 'd5dda55d404b92606f4ae614cfaa9ae000c91491'$UiOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo 'obj\VaiValidation'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$previousAppData = $env:APPDATA
New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $repo
try {
    $env:APPDATA = Join-Path $work 'ToolAppData'
    & $dotnet build GDMAmpAccessible.csproj -c Release --no-restore -v quiet
    if ($LASTEXITCODE) { throw 'Application build failed' }
    $protected = git diff $Baseline -- Dsp Audio Models Program.cs MainForm.cs .github Persistence/UpdateService.cs Persistence/SceneStore.cs Persistence/PresetStore.cs Persistence/AudioSettingsStore.cs Persistence/DualGuitarBankStore.cs Persistence/DualGuitarSceneStore.cs
    if ($protected) { throw 'Protected audio, DSP, models or persistence changed' }
    $accompaniment = git diff faa76d5 -- Dsp/BassAccompanimentGenerator.cs Dsp/DrumMachineGenerator.cs Dsp/PianoAccompanimentGenerator.cs Audio/PracticeRecorder.cs
    if ($accompaniment) { throw 'Approved accompaniment/recorder changed' }
    $files = @(git ls-files --cached --others --exclude-standard -- '*.cs') | Where-Object { $_ -notlike 'Installer/*' -and $_ -ne 'Program.cs' }
    foreach ($file in $files) {
        $target = Join-Path $work "current\$file"
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        # Only redirect test filesystem paths. Production sources are never rewritten.
        $content = [IO.File]::ReadAllText((Join-Path $repo $file)).Replace('Environment.GetFolderPath(', 'TestPaths.GetFolderPath(')
        [IO.File]::WriteAllText($target, $content, [Text.UTF8Encoding]::new($false))
    }
    $baseFiles = @(git ls-tree -r --name-only $Baseline -- Dsp Audio Models Persistence AppInfo.cs)
    foreach ($file in $baseFiles) {
        if (-not $file.EndsWith('.cs')) { continue }
        $content = (git show "${Baseline}:$file") -join "`n"
        if ($LASTEXITCODE) { throw "Cannot read baseline $file" }
        $content = $content.Replace('GDMAmpAccessible', 'BaselineAmpAccessible').Replace('Environment.GetFolderPath(', 'TestPaths.GetFolderPath(')
        $target = Join-Path $work "baseline\$file"
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::WriteAllText($target, $content, [Text.UTF8Encoding]::new($false))
    }
    Copy-Item (Join-Path $PSScriptRoot 'VaiRegression.cs.txt') (Join-Path $work 'Program.cs') -Force
    $refs = (Get-ChildItem 'bin\Release\net8.0-windows\NAudio*.dll' | ForEach-Object {
        '<Reference Include="{0}"><HintPath>{1}</HintPath></Reference>' -f $_.BaseName,$_.FullName
    }) -join "`n"
    @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks><PlatformTarget>x64</PlatformTarget></PropertyGroup><ItemGroup>$refs</ItemGroup></Project>
"@ | Set-Content (Join-Path $work 'Check.csproj')
    '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content (Join-Path $work 'NuGet.Config')
    & $dotnet restore (Join-Path $work 'Check.csproj') --configfile (Join-Path $work 'NuGet.Config') -v quiet
    if ($LASTEXITCODE) { throw 'Test restore failed' }
    $testArgs = @($work)
    & $dotnet run --project (Join-Path $work 'Check.csproj') -c Release --no-restore -- @testArgs |
        Tee-Object (Join-Path $work 'results.txt')
    if ($LASTEXITCODE) { throw 'Vai validation failed' }
} finally {
    $env:APPDATA = $previousAppData
    Pop-Location
}
