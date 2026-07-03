# qcd — quick cd

폴더명 패턴으로 빠르게 이동하는 cmd 명령. 하위 폴더 전체에서 이름에 특정 문자열이 포함된 폴더를 찾고, 여러 개면 번호로 선택해서 이동합니다.

```
C:\> qcd 커플러
C:\project\260703_커플러검토>

C:\> qcd proj/영동대로
  1) C:\project\...\영동대로교
  2) C:\project\...\영동대로_설계
선택 (엔터=취소): 1
C:\project\...\영동대로교>
```

## 특징

- **계층 힌트**: `qcd proj/영동대로` — 앞 세그먼트가 뒷 세그먼트의 검색 범위를 좁힘
- **한글 폴더명 완전 지원**
- **인덱스 없음** — 매번 라이브 스캔이라 방금 만든 폴더도 즉시 잡힘
- **단일 실행파일** — .NET 9 Native AOT로 빌드된 자립 exe (약 1.4MB, 런타임 불필요)
- **cmd 프로세스 격리 우회** — 배치 래퍼가 임시파일로 경로를 받아 `cd /d` 수행

## 설치

1. 이 저장소를 원하는 위치에 clone (예: `C:\dev\qcd`)
2. Windows 환경 변수 편집:
   - `Win + R` → `SystemPropertiesAdvanced` → 환경 변수(N)...
   - **사용자 변수의 Path** 편집 → "새로 만들기" → `C:\dev\qcd` 추가
3. 새 cmd 창을 열고 확인:
   ```
   where qcd
   ```
   → `C:\dev\qcd\qcd.bat` 표시되면 완료

> `setx`로 PATH를 조작하지 마세요 (1024자 제한). GUI로 편집하거나 PowerShell의 `[Environment]::SetEnvironmentVariable`를 사용하세요.

## 사용법

### 기본
```
qcd <query>
```
- 폴더명에 `query`가 포함된 폴더로 이동
- 여러 개 매칭 시 번호 선택 → 엔터로 취소
- 1개면 자동 이동

### 계층 힌트
```
qcd <seg1>/<seg2>[/...]
```
- `/` 또는 `\`로 구분 (혼용 가능)
- 각 세그먼트는 폴더명 부분일치 (대소문자 무시)
- 앞 세그먼트가 뒷 세그먼트의 검색 시작점을 좁혀줌

### 관리 명령
```
qcd --list-roots            검색 루트 목록
qcd --add-root <dir>        검색 루트 추가
qcd --remove-root <dir>     검색 루트 제거
qcd --help                  도움말
```

## 설정 — `roots.txt`

실행파일과 같은 폴더에 `roots.txt`가 있습니다 (첫 실행 시 자동 생성). 한 줄에 절대경로 1개, `#`으로 시작하는 줄은 주석.

```
# qcd search roots
C:\project
C:\dev
D:\work
```

**팁**: 루트를 좁게 잡을수록 빠릅니다. `C:\` 통째로 잡으면 매 호출마다 전체 스캔이라 느려집니다.

### 자동 제외 폴더
검색에서 자동으로 스킵되는 폴더: `node_modules`, `.git`, `.svn`, `.hg`, `.vs`, `.idea`, `AppData`, `$Recycle.Bin`, `System Volume Information`, `Windows`, `Program Files`, `Program Files (x86)`, `ProgramData` 등

## 파일 구조

```
qcd/
├─ qcd.bat          cmd 진입점 (PATH에 이 폴더 등록)
├─ qcd-core.exe     실행파일 (배치가 내부적으로 호출)
├─ roots.txt        검색 루트 (개인 설정, gitignored)
├─ Program.cs       C# 소스
└─ qcd.csproj       .NET 9 AOT 프로젝트
```

## 소스에서 빌드

**요구사항**: .NET 9 SDK, Visual C++ Build Tools (Native AOT 링크용)

```
dotnet publish -c Release -r win-x64
```

`bin/Release/net9.0/win-x64/publish/qcd-core.exe`를 프로젝트 루트로 복사.

## 동작 원리 — cmd 프로세스 격리 우회

cmd의 자식 프로세스(exe)는 부모 셸의 현재 디렉토리를 바꿀 수 없습니다. 따라서:

1. `qcd.bat`이 임시파일 경로를 만들어 `qcd-core.exe --out <tmpfile>`로 실행
2. exe가 대화형 UI를 콘솔에 그대로 노출 (stderr)한 채 매칭·선택 진행
3. 선택된 최종 경로만 `<tmpfile>`에 UTF-8로 기록
4. 배치가 `set /p`으로 경로를 읽어 `cd /d "<경로>"` 실행

한글 경로가 왕복 가능하도록 배치가 잠시 codepage를 65001(UTF-8)로 바꿨다가 원래대로 복원합니다.

> `qcd.exe` 대신 **`qcd-core.exe`** 라는 이름을 쓰는 이유: Windows PATHEXT 기본값이 `.EXE > .BAT`라, 같은 폴더에 `qcd.exe`와 `qcd.bat`이 있으면 exe가 먼저 실행되어 배치의 `cd`가 무시됩니다.

## 라이선스

MIT
