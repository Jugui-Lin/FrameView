param(
    [string]$OutputPath,
    [string]$DependencyDirectory,
    [string]$FfmpegDirectory
)
$ErrorActionPreference = 'Stop'
$appFolder = Split-Path -Parent $PSScriptRoot
if (!$OutputPath) { $OutputPath = Join-Path $appFolder 'build/FrameView.exe' }
if (!$DependencyDirectory) { $DependencyDirectory = Join-Path $appFolder '.deps' }
$DependencyDirectory = (Resolve-Path -LiteralPath $DependencyDirectory).Path
$noticesDirectory = Join-Path $appFolder 'third-party'
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$facade = Join-Path $framework 'netstandard.dll'
if (!(Test-Path -LiteralPath $facade)) { $facade = Join-Path $framework 'Facades\netstandard.dll' }
$sources = (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs').FullName
$bundleDir = (New-Item -ItemType Directory -Force (Join-Path (Split-Path -Parent $OutputPath) 'bundle-resources')).FullName
$payload = [ordered]@{
    'Magick.NET.Core.dll' = (Join-Path $DependencyDirectory 'Magick.NET.Core.dll')
    'Magick.NET-Q8-x64.dll' = (Join-Path $DependencyDirectory 'Magick.NET-Q8-x64.dll')
    'Magick.Native-Q8-x64.dll' = (Join-Path $DependencyDirectory 'Magick.Native-Q8-x64.dll')
}
$exrRuntime = Join-Path $DependencyDirectory 'exr-runtime'
foreach ($name in @('FrameViewExr.dll', 'OpenEXRCore.dll', 'deflate.dll', 'zstd.dll', 'openjph.dll')) {
    $payload[$name] = Join-Path $exrRuntime $name
}
$notices = Join-Path $bundleDir 'notices.txt'
$licenseFiles = @(
    (Join-Path $noticesDirectory 'APACHE-LICENSE-2.0.txt'),
    (Join-Path $noticesDirectory 'THIRD-PARTY-NOTICES.txt'),
    (Join-Path $noticesDirectory 'OpenEXR-runtime-notices.txt')
)
$contents = @('FrameView uses FFmpeg as a separate executable for video import and export.')
if ($FfmpegDirectory) {
    $FfmpegDirectory = (Resolve-Path -LiteralPath $FfmpegDirectory).Path
    $payload['ffmpeg.exe'] = Join-Path $FfmpegDirectory 'bin\ffmpeg.exe'
    $payload['ffprobe.exe'] = Join-Path $FfmpegDirectory 'bin\ffprobe.exe'
    $licenseFiles += (Join-Path $FfmpegDirectory 'readme.txt'), (Join-Path $FfmpegDirectory 'LICENSE')
}
foreach ($licenseFile in $licenseFiles) {
    $contents += ''
    $contents += (Get-Content -LiteralPath $licenseFile -Raw -Encoding UTF8)
}
[IO.File]::WriteAllLines($notices, $contents, [Text.UTF8Encoding]::new($false))
$manifest = Join-Path $bundleDir 'manifest.txt'
$hashes = foreach ($name in $payload.Keys) {
    (Get-FileHash -LiteralPath $payload[$name] -Algorithm SHA256).Hash + ' ' + $name
}
[IO.File]::WriteAllLines($manifest, [string[]]$hashes, [Text.UTF8Encoding]::new($false))
$resources = @("/resource:$manifest,FrameCue.Bundle.manifest,private")
$payload['notices'] = $notices
foreach ($name in $payload.Keys) {
    $gzipPath = Join-Path $bundleDir ($name + '.gz')
    $sourceStream = [IO.File]::OpenRead($payload[$name])
    try {
        $destination = [IO.File]::Create($gzipPath)
        try {
            $gzip = [IO.Compression.GZipStream]::new($destination, [IO.Compression.CompressionLevel]::Optimal)
            try { $sourceStream.CopyTo($gzip) } finally { $gzip.Dispose() }
        } finally { $destination.Dispose() }
    } finally { $sourceStream.Dispose() }
    $resources += "/resource:$gzipPath,FrameCue.Bundle.$name,private"
}
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ `
    "/out:$OutputPath" "/win32icon:$PSScriptRoot\FrameView.ico" "/win32manifest:$PSScriptRoot\FrameCue.manifest" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    "/r:$facade" "/r:$DependencyDirectory\Magick.NET.Core.dll" "/r:$DependencyDirectory\Magick.NET-Q8-x64.dll" $resources $sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
