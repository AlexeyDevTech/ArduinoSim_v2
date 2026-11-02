<#
.SYNOPSIS
    Reproduces the ADR-002 toolchain measurements: download size, unpacked size,
    cold/edit compile time, link time, DLL imports and the end-to-end smoke test
    (load the DLL from .NET, call through the draft ABI, symbolize with dbghelp).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\toolchain-eval\Measure-Toolchains.ps1 -WorkDir D:\tmp\tc-eval
#>
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'ardusim-toolchain-eval'),
    [int]$EditRuns = 3
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$abiInclude = Join-Path $repo 'firmware\shim\include'
$smokeDir = Join-Path $PSScriptRoot 'smoke'
$commonFlags = @('-std=gnu++17', '-fno-exceptions', '-fno-rtti', '-Wall', '-Wextra', '-O1', '-g', "-I$abiInclude")

$toolchains = @(
    [pscustomobject]@{
        Name     = 'zig'
        Version  = '0.17.0'
        Url      = 'https://ziglang.org/download/0.17.0/zig-x86_64-windows-0.17.0.zip'
        Sha256   = 'b5663f69581dcf391293fbf16c06cb80d81d806545ce618b4d0bab7f0eb8c428'
        Compile  = { param($root, $src, $obj) & "$root\zig-x86_64-windows-0.17.0\zig.exe" c++ -target x86_64-windows-gnu @commonFlags -c $src -o $obj }
        Link     = { param($root, $obj, $dll) & "$root\zig-x86_64-windows-0.17.0\zig.exe" c++ -target x86_64-windows-gnu -shared -g $obj -o $dll }
    },
    [pscustomobject]@{
        Name     = 'llvm-mingw'
        Version  = '20261020-ucrt'
        Url      = 'https://github.com/mstorsjo/llvm-mingw/releases/download/20261020/llvm-mingw-20261020-ucrt-x86_64.zip'
        Sha256   = 'fd5462b7f5d93b2ff981fcd3f9c7e427e47f3203c64ea91c27bcc444eebd74cd'
        Compile  = { param($root, $src, $obj) & "$root\llvm-mingw-20261020-ucrt-x86_64\bin\x86_64-w64-mingw32-clang++.exe" @commonFlags -gcodeview -c $src -o $obj }
        Link     = { param($root, $obj, $dll) & "$root\llvm-mingw-20261020-ucrt-x86_64\bin\x86_64-w64-mingw32-clang++.exe" -shared -static -g -gcodeview "-Wl,--pdb=" $obj -o $dll }
    }
)

function Measure-Seconds([scriptblock]$Block) {
    $ErrorActionPreference = 'Continue' # compiler warnings arrive on stderr
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $output = & $Block 2>&1
    if ($LASTEXITCODE -ne 0) { throw "command failed ($LASTEXITCODE): $output" }
    if ($output) { Write-Host ($output | Out-String) }
    return [math]::Round($sw.Elapsed.TotalSeconds, 2)
}

$results = @()
$savedPath = $env:PATH
foreach ($tc in $toolchains) {
    Write-Host "=== $($tc.Name) $($tc.Version)"
    $dl = Join-Path $WorkDir "dl-$($tc.Name)"
    $root = Join-Path $WorkDir $tc.Name
    $build = Join-Path $WorkDir "build-$($tc.Name)"
    New-Item -ItemType Directory -Force $dl, $root | Out-Null
    if (Test-Path $build) { Remove-Item -Recurse -Force $build }
    New-Item -ItemType Directory -Force $build | Out-Null

    $archive = Join-Path $dl (Split-Path $tc.Url -Leaf)
    if (-not (Test-Path $archive)) { Invoke-WebRequest -Uri $tc.Url -OutFile $archive -UseBasicParsing }
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $tc.Sha256) { throw "checksum mismatch for $archive" }
    if (-not (Get-ChildItem $root)) { & "$env:SystemRoot\System32\tar.exe" -xf $archive -C $root }
    $unpacked = (Get-ChildItem -Recurse -File $root | Measure-Object Length -Sum).Sum

    # Approximate a clean machine: no MSYS2/MinGW/Visual Studio on PATH, private caches.
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $build 'zig-global-cache'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $build 'zig-local-cache'
    try {
        $src = Join-Path $build 'smoke_firmware.cpp'
        Copy-Item (Join-Path $smokeDir 'smoke_firmware.cpp') $src
        $obj = Join-Path $build 'smoke.o'
        $dll = Join-Path $build 'smoke.dll'

        $cold = Measure-Seconds { & $tc.Compile $root $src $obj }
        $linkCold = Measure-Seconds { & $tc.Link $root $obj $dll }
        $edits = @()
        for ($i = 1; $i -le $EditRuns; $i++) {
            Add-Content $src "// edit $i $(Get-Random)"
            $edits += Measure-Seconds { & $tc.Compile $root $src $obj }
        }
        $link = Measure-Seconds { & $tc.Link $root $obj $dll }
    }
    finally {
        $env:PATH = $savedPath
    }

    $readobj = Join-Path $WorkDir 'llvm-mingw\llvm-mingw-20261020-ucrt-x86_64\bin\llvm-readobj.exe'
    $imports = if (Test-Path $readobj) { (& $readobj --coff-imports $dll | Select-String '^\s*Name: (.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value }) -join ', ' } else { '?' }

    Write-Host '--- smoke loader'
    & dotnet run (Join-Path $smokeDir 'SmokeLoader.cs') -- $dll | Write-Host
    $smokeOk = $LASTEXITCODE -eq 0

    $results += [pscustomobject]@{
        Toolchain      = "$($tc.Name) $($tc.Version)"
        ArchiveMB      = [math]::Round((Get-Item $archive).Length / 1MB, 1)
        UnpackedMB     = [math]::Round($unpacked / 1MB, 0)
        ColdCompileS   = $cold
        EditCompileS   = [math]::Round(($edits | Measure-Object -Average).Average, 2)
        LinkColdS      = $linkCold
        LinkS          = $link
        DllKB          = [math]::Round((Get-Item $dll).Length / 1KB, 0)
        Pdb            = Test-Path ([IO.Path]::ChangeExtension($dll, '.pdb'))
        Imports        = $imports
        Smoke          = $smokeOk
    }
}

$results | Format-List
$results | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $WorkDir 'results.json')
