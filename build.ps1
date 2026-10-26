$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
dotnet publish (Join-Path $taskRoot 'src\IslandPanel.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $taskRoot 'app') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка приложения не удалась' }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /target:winexe /reference:System.Windows.Forms.dll "/out:$(Join-Path $taskRoot 'Island Panel.exe')" (Join-Path $taskRoot 'launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Сборка запускающего файла не удалась' }
Write-Host 'Готово: Island Panel.exe'
