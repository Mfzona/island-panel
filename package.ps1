param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src\IslandPanel.csproj') -Raw)).Project.PropertyGroup.Version | Select-Object -First 1
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'app\IslandPanel.exe'))) { throw 'Сначала выполните build.ps1' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskStage = Join-Path ([IO.Path]::GetTempPath()) ('IslandPanel-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStage | Out-Null
try {
    $taskRuntime = Join-Path $taskStage 'app'
    New-Item -ItemType Directory -Path $taskRuntime | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $taskRoot 'app') -Force | Where-Object { $_.Extension -ne '.pdb' } | Copy-Item -Destination $taskRuntime -Recurse
    $taskTools = Join-Path $taskStage 'tools'
    New-Item -ItemType Directory -Path $taskTools | Out-Null
    foreach ($taskName in @('ffmpeg.exe', 'FFmpeg-COPYING-GPLv3.txt', 'NAudio-LICENSE.txt', 'QRCoder-LICENSE.txt')) {
        $taskFile = Join-Path $taskRoot ('tools\' + $taskName)
        if (Test-Path -LiteralPath $taskFile) { Copy-Item -LiteralPath $taskFile -Destination $taskTools }
    }
    foreach ($taskName in @('Island Panel.exe', 'README.md', 'README.txt', 'CHANGELOG.md', 'THIRD-PARTY.txt')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskStage
    }
    $taskGallery = Join-Path $taskRoot 'docs\images'
    if (Test-Path -LiteralPath $taskGallery) {
        New-Item -ItemType Directory -Path (Join-Path $taskStage 'docs') | Out-Null
        Copy-Item -LiteralPath $taskGallery -Destination (Join-Path $taskStage 'docs\images') -Recurse
    }
    $taskArchive = Join-Path $OutputDirectory ("IslandPanel-$taskVersion-win-x64.zip")
    Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskArchive -CompressionLevel Optimal -Force
    Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($taskArchive) } | Set-Content -LiteralPath ($taskArchive + '.sha256') -Encoding ascii
    Write-Output $taskArchive
}
finally {
    $taskResolvedStage = [IO.Path]::GetFullPath($taskStage)
    $taskExpectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($taskResolvedStage.StartsWith($taskExpectedParent, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($taskResolvedStage).StartsWith('IslandPanel-package-')) {
        Remove-Item -LiteralPath $taskResolvedStage -Recurse -Force
    }
}
