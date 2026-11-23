# Builds RdpPanel: one single exe + zip (+ installer when Inno Setup is installed). Run it with build.bat.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Fail([string]$msg) { Write-Host ""; Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

# version from AppInfo.cs
$m = Select-String -Path AppInfo.cs -Pattern 'Version = "([^"]+)"'
if (-not $m) { Fail "Could not read the version from AppInfo.cs" }
$ver = $m.Matches[0].Groups[1].Value
Write-Host "== RDP Panel $ver ==" -ForegroundColor Cyan

# 1. .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download (the SDK, not only the runtime) and run build.bat again."
}

# 2. the two RDP interop DLLs (made once with aximp, see setup.bat)
if (-not ((Test-Path lib\MSTSCLib.dll) -and (Test-Path lib\AxMSTSCLib.dll))) {
    Write-Host "Creating the RDP interop DLLs (aximp)..."
    $ax = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows" -Recurse -Filter aximp.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ax) { Fail "aximp.exe not found. Install Visual Studio Build Tools with '.NET Framework 4.8 SDK' and 'Windows SDK', then run build.bat again (see README)." }
    New-Item -ItemType Directory -Force lib | Out-Null
    Push-Location lib
    & $ax.FullName "$env:windir\system32\mstscax.dll" | Out-Null
    Pop-Location
    if (-not (Test-Path lib\AxMSTSCLib.dll)) { Fail "aximp did not create AxMSTSCLib.dll" }
}

# 3. build (the first time this downloads the Costura package: internet needed)
Write-Host "Building..."
if (Test-Path bin\Release) { Remove-Item bin\Release -Recurse -Force }
dotnet build RdpPanel.csproj -c Release -p:Version=$ver
if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed (see above)." }

$out = "bin\Release\net48"
$exe = Join-Path $out "RdpPanel.exe"
if (-not (Test-Path $exe)) { Fail "RdpPanel.exe was not created." }
$size = (Get-Item $exe).Length
if ($size -lt 500000) { Fail "RdpPanel.exe is only $size bytes: the two DLLs were not built into it (Costura.Fody). Check that the package could be downloaded." }

# 4. dist
if (Test-Path dist) { Remove-Item dist -Recurse -Force }
New-Item -ItemType Directory -Force dist | Out-Null
Copy-Item $exe dist\RdpPanel.exe
Compress-Archive -Path dist\RdpPanel.exe -DestinationPath "dist\RdpPanel-$ver.zip"
Write-Host "Single exe: dist\RdpPanel.exe ($([math]::Round($size / 1MB, 1)) MB)" -ForegroundColor Green

# 5. installer (optional)
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    Write-Host "Building the installer..."
    & $iscc "/DAppVersion=$ver" installer\installer.iss | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "The installer could not be built (the exe and zip are fine)." -ForegroundColor Yellow }
    else { Write-Host "Installer: dist\RdpPanel-Setup-$ver.exe" -ForegroundColor Green }
} else {
    Write-Host "Inno Setup is not installed: no installer made (https://jrsoftware.org/isdl.php). The exe and zip are ready." -ForegroundColor Yellow
}

# 6. checksums
$sums = @(Get-ChildItem dist -File | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    "$h  $($_.Name)"
})
$sums | Out-File dist\SHA256SUMS.txt -Encoding ascii
Write-Host ""
Get-Content dist\SHA256SUMS.txt
