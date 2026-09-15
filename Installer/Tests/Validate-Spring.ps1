param([string]$Baseline = 'faa76d5867de3ee4f96b8e960eac40cfc0cafc74')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo 'obj\SpringRegression'
New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $repo
$previousAppData = $env:APPDATA
try {
    dotnet build GDMAmpAccessible.csproj -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Application build failed.' }
    $sources = @(git ls-tree -r --name-only $Baseline -- Dsp Audio Models Persistence AppInfo.cs)
    $hashes = @()
    foreach ($file in $sources) {
        if (-not $file.EndsWith('.cs')) { continue }
        $original = (git show "${Baseline}:$file") -join "`n"
        if ($LASTEXITCODE) { throw "Cannot read baseline: $file" }
        $current = [IO.File]::ReadAllText((Join-Path $repo $file)).Replace("`r`n", "`n").TrimStart([char]0xfeff).TrimEnd("`n")
        $original = $original.TrimStart([char]0xfeff).TrimEnd("`n")
        if ($file -notin @('Dsp/ReverbEffect.cs', 'AppInfo.cs')) {
            if ($current -cne $original) { throw "Out-of-scope source changed: $file" }
            $sha = [Security.Cryptography.SHA256]::Create()
            $hashes += "$file $([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($current))).Replace('-', ''))"
            $sha.Dispose()
        }
        foreach ($version in @('current', 'baseline')) {
            $target = Join-Path $work "$version\$file"
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
            $content = if ($version -eq 'current') { $current } else { $original.Replace('GDMAmpAccessible', 'BaselineAmpAccessible') }
            [IO.File]::WriteAllText($target, $content, [Text.UTF8Encoding]::new($false))
        }
    }
    # UI, diagnostics, accessibility and navigation must remain byte-for-byte unchanged.
    $uiDiff = git diff $Baseline -- MainForm.cs Program.cs
    if ($uiDiff) { throw 'UI or entry point changed.' }
    $hashes | Set-Content (Join-Path $work 'unchanged-sha256.txt')
    Copy-Item (Join-Path $PSScriptRoot 'SpringRegression.cs.txt') (Join-Path $work 'Program.cs') -Force
    $refs = (Get-ChildItem 'bin\Release\net8.0-windows\NAudio*.dll' | ForEach-Object {
        '<Reference Include="{0}"><HintPath>{1}</HintPath></Reference>' -f $_.BaseName,$_.FullName
    }) -join "`n"
    @"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks><PlatformTarget>x64</PlatformTarget></PropertyGroup>
 <ItemGroup>$refs</ItemGroup>
</Project>
"@ | Set-Content (Join-Path $work 'Check.csproj')
    '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content (Join-Path $work 'NuGet.Config')
    $env:APPDATA = Join-Path $work 'AppData'
    dotnet restore (Join-Path $work 'Check.csproj') --configfile (Join-Path $work 'NuGet.Config')
    if ($LASTEXITCODE) { throw 'Test restore failed.' }
    dotnet run --project (Join-Path $work 'Check.csproj') -c Release --no-restore -- $work | Tee-Object (Join-Path $work 'results.txt')
    if ($LASTEXITCODE) { throw 'Spring regression failed.' }
    Write-Output "Verified $($hashes.Count) unchanged source hashes. Results: $work"
} finally {
    $env:APPDATA = $previousAppData
    Pop-Location
}
