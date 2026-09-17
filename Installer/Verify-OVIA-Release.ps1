param(
    [Parameter(Mandatory=$true)][string]$SetupPath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SetupPath)) {
    throw "설치파일을 찾을 수 없습니다: $SetupPath"
}

$path = (Resolve-Path -LiteralPath $SetupPath).Path
$sig = Get-AuthenticodeSignature -LiteralPath $path
$hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
$item = Get-Item -LiteralPath $path

Write-Host "File      : $($item.Name)"
Write-Host "Size      : $($item.Length)"
Write-Host "SHA256    : $($hash.Hash)"
Write-Host "SignStatus: $($sig.Status)"
if ($sig.SignerCertificate) {
    Write-Host "Signer    : $($sig.SignerCertificate.Subject)"
    Write-Host "Thumbprint: $($sig.SignerCertificate.Thumbprint)"
}
if ($sig.TimeStamperCertificate) {
    Write-Host "Timestamp : $($sig.TimeStamperCertificate.Subject)"
}

if ($sig.Status -ne 'Valid') {
    throw "정식 배포 금지: Authenticode 서명이 Valid가 아닙니다."
}

Write-Host "OVIA release verification SUCCESS" -ForegroundColor Green
