param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "tests/Wocel.Capture.Tests/Wocel.Capture.Tests.csproj"
$appProject = Join-Path $repoRoot "src/Wocel.Capture.Windows/Wocel.Capture.Windows.csproj"
$outputRoot = Join-Path $repoRoot "dist/wocel-capture"
$publishDir = Join-Path $outputRoot "publish"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet test $testProject --configuration $Configuration --nologo --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Wocel Capture tests failed." }

dotnet publish $appProject --configuration $Configuration --runtime $Runtime --self-contained true --output $publishDir `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --nologo --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Wocel Capture publish failed." }

$executable = Join-Path $publishDir "Wocel Capture.exe"
if (-not (Test-Path $executable)) { throw "Published executable was not created: $executable" }

$clientId = $env:WOCEL_GOOGLE_CLIENT_ID
if (-not [string]::IsNullOrWhiteSpace($clientId)) {
    Set-Content -Path (Join-Path $publishDir "oauth-client-id.txt") -Value $clientId.Trim() -Encoding UTF8 -NoNewline
}

$secretPatterns = @("AIza[0-9A-Za-z_-]{30,}", "ya29\.[0-9A-Za-z_-]+")
$sourceFiles = Get-ChildItem (Join-Path $repoRoot "src/Wocel.Capture*") -Recurse -File -Include *.cs,*.xaml,*.json
foreach ($pattern in $secretPatterns) {
    $match = $sourceFiles | Select-String -Pattern $pattern
    if ($match) { throw "Possible Google secret found in source: $($match.Path)" }
}

$isccCandidates = @(@(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique)

if ($isccCandidates.Count -gt 0) {
    & $isccCandidates[0] (Join-Path $repoRoot "installer/WocelCapture.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }
    Write-Host "Installer: $(Join-Path $outputRoot 'WocelCaptureSetup.exe')"
} else {
    Write-Warning "Inno Setup 6 was not found. Portable publish is ready at $publishDir"
}
