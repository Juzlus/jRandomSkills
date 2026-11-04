
$ErrorActionPreference = "Stop"

$addon = $env:CS2_CONTENT

if (-not (Test-Path -LiteralPath $addon)) {
    throw "Addon directory not found: $addon"
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$count = 0

$pngFiles = @(
    Get-ChildItem -LiteralPath $addon -Filter "*.png" -File -Recurse |
    Where-Object {
        $relativePath = $_.FullName.Substring($addon.Length).TrimStart('\')
        $parts = $relativePath -split '[\\/]'
        -not ($parts | Where-Object { $_.StartsWith('.') })
    }
)

Write-Host "Addon: $addon"
Write-Host "Found PNG files: $($pngFiles.Count)"

foreach ($png in $pngFiles) {
    $relative = $png.FullName.Substring($addon.Length + 1)
    $sourcePath = $relative.Replace("\", "\\")

    $vtex = [System.IO.Path]::ChangeExtension($png.FullName, ".vtex")

    if (Test-Path -LiteralPath $vtex) {
        Write-Host "[SKIP] VTEX already exists: $relative"
        continue
    }

$template = @"
<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {
            "m_name" "string" "SheetTexture"
            "m_fileName" "string" "$sourcePath"
            "m_colorSpace" "string" "srgb"
            "m_typeString" "string" "2D"
            "m_imageProcessorArray" "element_array"
            [
            ]
        }
    ]
    "m_outputTypeString" "string" "2D"
    "m_outputFormat" "string" "RGBA8888"
    "m_outputClearColor" "vector4" "0 0 0 0"
    "m_nOutputMinDimension" "int" "0"
    "m_nOutputMaxDimension" "int" "0"
    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {
            "m_inputTextureArray" "string_array"
            [
                "SheetTexture"
            ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_mipAlgorithm" "CDmeImageProcessor"
            {
                "m_algorithm" "string" ""
                "m_stringArg" "string" ""
                "m_vFloat4Arg" "vector4" "0 0 0 0"
            }
            "m_outputColorSpace" "string" "srgb"
        }
    ]
    "m_vClamp" "vector3" "0 0 0"
    "m_bNoLod" "bool" "1"
}
"@

    [System.IO.File]::WriteAllText($vtex, $template, $utf8)
    Write-Host "[CREATED] $relative"
    $count++
}

Write-Host "Created VTEX files: $count"
