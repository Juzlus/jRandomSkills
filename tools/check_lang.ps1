# Validates the translation files of jRandomSkills.
#   1. every language file must be valid JSON and contain exactly the keys of en.json
#   2. every translation key used with a string literal in the C# sources must exist in en.json
# Exit code 1 when something is wrong (used by .github/workflows/build.yml).
# Usage: pwsh tools/check_lang.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$langDir = Join-Path $root 'jRandomSkills - SRC Files/src/lang'
$srcDir = Join-Path $root 'jRandomSkills - SRC Files/src'
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Keys that intentionally fall back to the key text itself.
$allowedMissing = @('jRandomSkills')

function Read-LangKeys([string]$path) {
    $json = [System.IO.File]::ReadAllText($path, $utf8) | ConvertFrom-Json
    return @($json.PSObject.Properties.Name)
}

$failed = $false
$enKeys = Read-LangKeys (Join-Path $langDir 'en.json')

foreach ($file in Get-ChildItem (Join-Path $langDir '*.json') | Sort-Object Name) {
    try { $keys = Read-LangKeys $file.FullName }
    catch { Write-Host "ERROR $($file.Name): invalid JSON ($($_.Exception.Message))"; $failed = $true; continue }

    $missing = $enKeys | Where-Object { $keys -notcontains $_ }
    $extra = $keys | Where-Object { $enKeys -notcontains $_ }
    $dupes = $keys | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name

    if ($missing) { Write-Host "ERROR $($file.Name): $(@($missing).Count) key(s) missing: $($missing -join ', ')"; $failed = $true }
    if ($extra) { Write-Host "ERROR $($file.Name): $(@($extra).Count) key(s) not in en.json: $($extra -join ', ')"; $failed = $true }
    if ($dupes) { Write-Host "ERROR $($file.Name): duplicated key(s): $($dupes -join ', ')"; $failed = $true }
    if (-not $missing -and -not $extra -and -not $dupes) { Write-Host "OK    $($file.Name) ($($keys.Count) keys)" }
}

# Keys referenced from code through string literals.
$patterns = @(
    'GetTranslation(?:WithoutIlliterate)?\(\s*"([A-Za-z0-9_\-]+)"',
    'PrintTranslationToChatAll\([^;]*?\[\s*"([A-Za-z0-9_\-]+)"'
)
$used = @{}
foreach ($cs in Get-ChildItem $srcDir -Recurse -Filter *.cs) {
    $text = [System.IO.File]::ReadAllText($cs.FullName, $utf8)
    foreach ($pattern in $patterns) {
        foreach ($m in [regex]::Matches($text, $pattern)) {
            $key = $m.Groups[1].Value
            if (-not $used.ContainsKey($key)) { $used[$key] = $cs.Name }
        }
    }
}

$unknown = $used.Keys | Where-Object { ($enKeys -notcontains $_) -and ($allowedMissing -notcontains $_) } | Sort-Object
if ($unknown) {
    foreach ($key in $unknown) { Write-Host "ERROR code uses unknown translation key '$key' (first seen in $($used[$key]))" }
    $failed = $true
}
else { Write-Host "OK    all $($used.Count) translation keys used by literal in code exist in en.json" }

if ($failed) { exit 1 }
