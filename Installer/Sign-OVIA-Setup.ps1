param(
    [string]$SetupPath = "",
    [string]$CertificateThumbprint = $env:OVIA_SIGN_CERT_THUMBPRINT,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$SignToolPath = $env:OVIA_SIGNTOOL_PATH
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host ""
    Write-Host "[OVIA SIGN] ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Find-SignTool {
    param([string]$ExplicitPath)

    if ($ExplicitPath -and (Test-Path -LiteralPath $ExplicitPath)) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $roots) {
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                Join-Path $_.FullName "x64\signtool.exe"
            } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    return $null
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SetupPath) {
    $buildInclude = Join-Path $scriptDir "ovia_version_build.iss"
    $version = $null
    if (Test-Path -LiteralPath $buildInclude) {
        $line = Get-Content -LiteralPath $buildInclude -Encoding UTF8 |
            Where-Object { $_ -match '^\s*#define\s+OviaVersion\s+"([^"]+)"' } |
            Select-Object -First 1
        if ($line -match '"([^"]+)"') { $version = $Matches[1] }
    }
    if (-not $version) {
        Fail "현재 OVIA 버전을 확인할 수 없습니다. -SetupPath를 직접 지정해 주세요."
    }
    $SetupPath = Join-Path $scriptDir ("Output\OVIA_Setup_{0}.exe" -f $version)
}

if (-not (Test-Path -LiteralPath $SetupPath)) {
    Fail "설치파일을 찾을 수 없습니다: $SetupPath"
}
$SetupPath = (Resolve-Path -LiteralPath $SetupPath).Path

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Fail "코드서명 인증서 Thumbprint가 없습니다. 사용자 환경변수 OVIA_SIGN_CERT_THUMBPRINT를 설정하거나 -CertificateThumbprint로 지정해 주세요."
}

$CertificateThumbprint = ($CertificateThumbprint -replace '\s','').ToUpperInvariant()

$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
    Where-Object { ($_.Thumbprint -replace '\s','').ToUpperInvariant() -eq $CertificateThumbprint } |
    Select-Object -First 1

if (-not $cert) {
    Fail "Thumbprint $CertificateThumbprint 인증서를 CurrentUser/My 또는 LocalMachine/My에서 찾을 수 없습니다."
}
if (-not $cert.HasPrivateKey) {
    Fail "선택한 인증서에 개인키가 없습니다. 코드서명용 개인키가 포함된 인증서가 필요합니다."
}
if ($cert.NotAfter -le (Get-Date)) {
    Fail "코드서명 인증서가 만료되었습니다: $($cert.NotAfter)"
}

$signtool = Find-SignTool $SignToolPath
if (-not $signtool) {
    Fail "signtool.exe를 찾을 수 없습니다. Windows SDK를 설치하거나 OVIA_SIGNTOOL_PATH를 지정해 주세요."
}

Write-Host "[OVIA SIGN] Setup     : $SetupPath"
Write-Host "[OVIA SIGN] Certificate: $($cert.Subject)"
Write-Host "[OVIA SIGN] Thumbprint : $CertificateThumbprint"
Write-Host "[OVIA SIGN] Timestamp  : $TimestampUrl"
Write-Host "[OVIA SIGN] SignTool   : $signtool"

& $signtool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /d "OVIA" $SetupPath
if ($LASTEXITCODE -ne 0) {
    Fail "signtool sign이 실패했습니다. ExitCode=$LASTEXITCODE"
}

& $signtool verify /pa /all /v $SetupPath
if ($LASTEXITCODE -ne 0) {
    Fail "서명 검증에 실패했습니다. ExitCode=$LASTEXITCODE"
}

$auth = Get-AuthenticodeSignature -LiteralPath $SetupPath
if ($auth.Status -ne 'Valid') {
    Fail "PowerShell Authenticode 검증 결과가 Valid가 아닙니다: $($auth.Status)"
}

$hash = (Get-FileHash -LiteralPath $SetupPath -Algorithm SHA256).Hash
$length = (Get-Item -LiteralPath $SetupPath).Length

$manifestPath = [IO.Path]::ChangeExtension($SetupPath, ".sha256.txt")
@(
    "OVIA signed release"
    "File=$([IO.Path]::GetFileName($SetupPath))"
    "Size=$length"
    "SHA256=$hash"
    "Signer=$($auth.SignerCertificate.Subject)"
    "Thumbprint=$($auth.SignerCertificate.Thumbprint)"
    "TimestampCertificate=$($auth.TimeStamperCertificate.Subject)"
    "SignedAt=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
) | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "[OVIA SIGN] SUCCESS" -ForegroundColor Green
Write-Host "SHA256 : $hash"
Write-Host "Manifest: $manifestPath"
