# OVIA 코드서명 배포 절차

정식 배포 설치파일은 **Inno Setup Compile 직후 반드시 코드서명**한다.

## 1. 1회 준비
- Windows용 신뢰 가능한 코드서명 인증서 준비
- 인증서의 개인키가 현재 배포 PC에서 사용 가능해야 함
- Windows SDK의 `signtool.exe` 설치
- 인증서 Thumbprint 확인:
  `Get-ChildItem Cert:\CurrentUser\My | Format-Table Subject,Thumbprint,NotAfter`

사용자 환경변수 예:
`setx OVIA_SIGN_CERT_THUMBPRINT "인증서Thumbprint"`

필요하면:
`setx OVIA_SIGNTOOL_PATH "C:\...\signtool.exe"`

## 2. 매 Release
1. OVIA 환경설정 > 버전정보의 최신 버전을 확인/저장
2. Release/x64 빌드
3. Inno Setup에서 `OVIA_Test_Setup.iss` Compile
4. PowerShell:
   `powershell -ExecutionPolicy Bypass -File .\Installer\Sign-OVIA-Setup.ps1`
5. 성공 메시지와 `.sha256.txt` 생성 확인
6. `Get-AuthenticodeSignature .\Installer\Output\OVIA_Setup_버전.exe`
   결과가 반드시 `Valid`
7. 서명 완료된 EXE만 ERP 서버에 업로드
8. 서버/다운로드본 SHA256이 `.sha256.txt`와 같은지 검증

## 정책
- 미서명(`NotSigned`) 설치파일은 정식 배포 금지.
- 서명 후 파일을 다시 수정하거나 재패킹하지 않는다. 변경하면 서명이 무효화된다.
- 타임스탬프를 반드시 사용한다.
- 인증서/PFX/개인키/암호는 소스 ZIP, Git, MD 압축파일에 넣지 않는다.
- 인증서가 없는 개발 환경에서도 Inno Setup Compile 자체는 가능하다.
