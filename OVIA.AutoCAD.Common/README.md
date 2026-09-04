# OVIA.AutoCAD.Common

AutoCAD 2024~2027(향후 2028+)이 공통으로 컴파일하는 CAD 기능의 단일 기준 폴더입니다.

- `OviaCommands.cs`: 검증된 CAD 추출 핵심
- `OviaTitleTextCommands.cs`: CAD 제목 TEXT/MTEXT 선택 브리지
- `Data/Mapping/barlist_mapping.json`: 기존 AutoCAD.2027 아래에 있던 매핑 파일의 공통 보관 위치

버전별 `OVIA.AutoCAD.YYYY.csproj`는 이 폴더의 C# 소스를 `Compile Link`로 참조합니다.
버전별 프로젝트에는 TargetFramework / AutoCAD.NET API 버전 / AssemblyName / Installer stage 규칙만 둡니다.

주의: Desktop이 실제 BarList 표준화에 사용하는 기준 매핑은 `OVIA.Desktop/Data/Mapping/barlist_mapping.json`
및 `%APPDATA%/OVIA/Mapping/barlist_mapping.json`입니다. 두 매핑은 현재 버전도 다르므로 임의 병합하지 않습니다.
