# SPDX-License-Identifier: MPL-2.0

$ErrorActionPreference = 'Stop'
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $installer -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw '未找到 Visual Studio Roslyn C# 编译器。' }
Push-Location -LiteralPath $PSScriptRoot
try {
    $source = (Get-ChildItem -LiteralPath src -Filter '*.cs').FullName
    $branding = @(
        if (Test-Path -LiteralPath 'src\solar.ico' -PathType Leaf) {
            '/win32icon:src\solar.ico'
            '/resource:src\solar.ico,solar.ico'
        }
        if (Test-Path -LiteralPath 'src\solar.png' -PathType Leaf) { '/resource:src\solar.png,solar.png' }
    )
    $presets = @(foreach ($game in 'boktai2', 'boktai3', 'boktaids') {
        $preset = "src\assets\$game.sav"
        if (Test-Path -LiteralPath $preset -PathType Leaf) { "/resource:$preset,battle.$game.sav" }
        if ($game -eq 'boktaids') {
            foreach ($role in 'host', 'guest') {
                $checkpoint = "src\assets\$game.$role.start.ml1"
                if (Test-Path -LiteralPath $checkpoint -PathType Leaf) { "/resource:$checkpoint,battle.$game.start.$role" }
            }
        }
        else {
            $checkpoint = "src\assets\$game.start.sgm"
            if (Test-Path -LiteralPath $checkpoint -PathType Leaf) { "/resource:$checkpoint,battle.$game.start" }
        }
    })
    New-Item -Path 'stage\engines' -ItemType Directory -Force | Out-Null
    foreach ($entry in @(@('native-build', 'gds-gba.dll'), @('native-ds-build', 'gds-ds.dll'))) {
        Copy-Item -LiteralPath (Join-Path $entry[0] ('Release\' + $entry[1])) -Destination 'stage\engines'
    }
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.VisualBasic.dll '/out:stage\boksol.exe' @branding @presets @source
    if ($LASTEXITCODE -ne 0) { throw 'boksol 编译失败。' }
}
finally {
    Pop-Location
}
