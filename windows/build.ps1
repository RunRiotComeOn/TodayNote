# Rebuild on Windows with the .NET Framework 4.x compiler included with Windows.
$ErrorActionPreference = 'Stop'
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    $framework = Split-Path $compiler
    $output = Join-Path $PSScriptRoot 'build'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
        ('/out:' + (Join-Path $output 'TodayNote.exe')),
        ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')),
        ('/win32icon:' + (Join-Path $PSScriptRoot 'src\TodayNote.ico')),
        ('/resource:' + (Join-Path $PSScriptRoot 'src\Main.xaml') + ',Main.xaml'),
        ('/resource:' + (Join-Path $PSScriptRoot 'src\TodayNote.ico') + ',TodayNote.ico'))
    foreach ($name in @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Security.dll')) {
        $arguments += '/reference:' + (Join-Path $framework $name)
    }
    Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework
    foreach ($reference in @([System.Windows.Threading.Dispatcher].Assembly.Location, [System.Windows.Media.Brush].Assembly.Location, [System.Windows.Window].Assembly.Location)) {
        $arguments += '/reference:' + $reference
    }
    $arguments += @(Get-ChildItem (Join-Path $PSScriptRoot 'src\*.cs') | ForEach-Object { $_.FullName })
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    Copy-Item (Join-Path $PSScriptRoot 'src\TodayNote.exe.config') $output -Force
    Write-Host 'Built build\TodayNote.exe'
} catch { Write-Error $_; exit 1 }
