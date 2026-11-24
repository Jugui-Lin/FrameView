param([string]$Zig = 'zig')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deps = Join-Path $root '.deps'
$downloads = Join-Path $deps 'downloads'
$runtime = Join-Path $deps 'exr-runtime'
New-Item -ItemType Directory -Force $downloads, $runtime | Out-Null

function Get-Archive($name, $url, $sha256) {
    $path = Join-Path $downloads $name
    if (!(Test-Path -LiteralPath $path)) {
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile "$path.partial"
        if ((Get-FileHash -LiteralPath "$path.partial").Hash -ne $sha256) { throw "Checksum mismatch: $name" }
        Move-Item -LiteralPath "$path.partial" -Destination $path
    }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $sha256) { throw "Checksum mismatch: $name" }
    return $path
}

function Expand-Package($archive, $destination) {
    New-Item -ItemType Directory -Force $destination | Out-Null
    & tar.exe -xf $archive -C $destination
    if ($LASTEXITCODE) { throw "Unable to extract $archive" }
}

$nuget = 'https://api.nuget.org/v3-flatcontainer'
$magick = @{
    'magick.net.core' = '461232BFC8188EE3C244C7A73119A8C8D4ADA46F109D7FF97952F29729BDD438'
    'magick.net-q8-x64' = 'FB5414292085DB2BC28326FA6D73D04D5ED2EC49A0F32CFCD95467A6DE6B6A47'
}
foreach ($name in $magick.Keys) {
    $archive = Get-Archive "$name.zip" "$nuget/$name/15.1.0/$name.15.1.0.nupkg" $magick[$name]
    Expand-Package $archive (Join-Path $deps $name)
}
Copy-Item -LiteralPath @(
    (Join-Path $deps 'magick.net.core/lib/netstandard20/Magick.NET.Core.dll'),
    (Join-Path $deps 'magick.net-q8-x64/lib/netstandard20/Magick.NET-Q8-x64.dll'),
    (Join-Path $deps 'magick.net-q8-x64/runtimes/win-x64/native/Magick.Native-Q8-x64.dll')
) -Destination $deps -Force

$conda = 'https://conda.anaconda.org/conda-forge/win-64'
$packages = @(
    @('openexr-3.5.4-h4a61e80_0', 'A8912BEFE1477217B46150D376567ABE34E7579F65EEC3ECB5920B4C944C2E1B', 'OpenEXRCore.dll'),
    @('imath-3.2.3-h1608b31_1', '0F1ADAEAA4A9E0137FC61340F2D8389B87060A877D1C18186F1410FFB9F2FA92', ''),
    @('libdeflate-1.26-h1a1d4e4_0', 'F51F28E894579387A531C53D97E058642ECFB232C4CB156C43C9121A9044F3D8', 'deflate.dll'),
    @('openjph-0.33.0-hf13a347_0', '0DF9D040A87995D644A08761306CFC28298B9E4AE78EF6C42BCA982915FE5581', 'openjph.dll'),
    @('zstd-1.5.7-h534d264_7', 'CA7DAAE4F218A11FAB82CC2857F0EA518EC3F46ACEC60490485347A4C22C6B3E', 'zstd.dll')
)
foreach ($package in $packages) {
    $name, $hash, $dll = $package
    $folder = Join-Path $deps $name
    $archive = Get-Archive "$name.conda" "$conda/$name.conda" $hash
    Expand-Package $archive $folder
    $payload = Get-ChildItem -LiteralPath $folder -Filter 'pkg-*.tar.zst' | Select-Object -First 1
    if (!$payload) { throw "Missing package payload: $name" }
    Expand-Package $payload.FullName $folder
    if ($dll) { Copy-Item -LiteralPath (Join-Path $folder "Library/bin/$dll") -Destination $runtime -Force }
}

$openexr = Join-Path $deps "$($packages[0][0])/Library"
$imath = Join-Path $deps "$($packages[1][0])/Library"
& $Zig cc -target x86_64-windows-gnu -shared -O2 -Wall -I "$openexr/include/OpenEXR" -I "$imath/include" `
    (Join-Path $root 'src/ExrDecoder.c') "$openexr/lib/OpenEXRCore.lib" -o (Join-Path $runtime 'FrameViewExr.dll')
if ($LASTEXITCODE) { throw "OpenEXR decoder build failed ($LASTEXITCODE)." }
