param(
    [Parameter(Mandatory=$true)][object[]]$Releases,
    [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Version estable invalida.' }
# Compare stable semantic versions numerically; publication date and patch gaps do not matter.
$candidates = @($Releases | ForEach-Object {
    if (-not $_.draft -and -not $_.prerelease -and $_.tag_name -match '^v(?<v>[0-9]+\.[0-9]+\.[0-9]+)$') {
        $number = $Matches.v
        if ([version]$number -lt [version]$Version) {
            [pscustomobject]@{ Version=$number; Tag=$_.tag_name; SemanticVersion=[version]$number }
        }
    }
})
$previous = $candidates | Sort-Object SemanticVersion -Descending | Select-Object -First 1
if ($null -eq $previous) { throw "No existe una release estable anterior a $Version." }
$previous
