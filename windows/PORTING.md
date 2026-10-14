# Windows 2단계: WPF 펫 앱

Core와 CLI에 WPF 앱을 연결하고 Windows x64용 AIpet.exe를 생성했다.
macOS에서 교차 빌드·Core 테스트·publish를 확인했으며, **Windows 실행은 아직 미검증**이다.
Core에는 외부 패키지가 없다.

```sh
python -m pip install -r requirements-build.txt
python windows/scripts/prepare_assets.py
dotnet build windows/AIpet.sln -c Release
dotnet test windows/AIpet.sln -c Release
dotnet run --project windows/src/AIpet.Cli -- --snapshot
```

훅 CLI는 `--hook codex|claude [--event-root 경로]`를 받는다. stdin에서 최대
1 MiB만 읽고, 출력 없이 항상 종료 코드 0으로 돌아온다. 훅 파일은 원본 version 1
형식과 작업 ID SHA-256 파일 이름을 유지한다. 대화 본문·도구 인자·출력은 저장하지
않는다. 화면 도구의 출력에서는 제한된 창 헤더만 추출한다.

## 원본 대응과 플랫폼 차이

- Models, TranscriptDecoder, TranscriptTail, ComputerUseActivity, ScreenFocus,
  StatusMonitor, HookBridge, PetIdentity, PetAnimation, PetLayout,
  PetPointerInteraction, BubbleReveal을 `src/AIpet.Core/`에 이식했다.
- `Geometry.cs`의 Point/Size/Rect는 CoreGraphics의 수학 연산을 대신한다.
  좌표는 원본대로 y가 위로 증가한다. WPF의 ScreenCoordinates가 좌표 변환을 담당한다.
- PetSpritePresentation은 변환 계산과 RGBA 바이트 기반 SpriteContentMetrics.Measure를 제공한다.
  WPF SpriteAtlas가 이미지를 디코딩한다. 원본 외곽 보강 및 실제 창 탐색은 제외했다.
- 기록 기본 위치는 `CODEX_HOME/sessions`, `CLAUDE_CONFIG_DIR/projects`이며,
  환경 변수가 없으면 UserProfile 아래 `.codex/sessions`, `.claude/projects`다.
  생성자에서 루트를 덮어쓸 수 있다.
- 이벤트 기본 위치는 `LocalApplicationData/AIpet/events`다. Windows에서는
  `%LOCALAPPDATA%\AIpet\events`이며 생성자 및 CLI 인자로 변경할 수 있다.
- cwd 마지막 요소는 `/`와 `\`를 모두 지원한다.
- Windows 파일 교체는 열린 핸들의 GetFileInformationByHandle 볼륨 일련번호와 파일 인덱스로 판정한다.
  모든 OS에서 offset 직전 최대 64바이트의 SHA-256을 비교하며, 생성 시각 변경·크기 감소도 확인한다.
  보조 검사를 위한 원문은 보관하지 않는다. 크기·생성 시각·수정 시각이 모두 같으면 파일을 열지 않는다.
  따라서 모든 메타데이터까지 동일하게 보존한 교체는 다음 변화 전까지 감지하지 못할 수 있다.
  Windows 식별 API 실패 시에는 생성 시각·크기·경계 해시 보조 판단을 사용한다.
- Unix에서는 이벤트 디렉터리 0700, 이벤트 파일 0600을 적용한다. Windows에서는
  POSIX 권한 설정을 호출하지 않는다.
- PetIdentity는 NFC와 StringInfo 문자 요소를 사용한다. UserDefaults 대신
  기존 주입 사전 API 외에 SettingsStore로 영구 JSON 설정을 저장한다.
- App.swift에서는 훅과 snapshot 분기만 CLI로 옮겼다. snapshot은 ISO 날짜,
  키 정렬, 들여쓰기를 사용한다. 한국어 상태·이름·연결 문구는 원문을 유지했다.
- ScreenTarget은 CRLF 헤더도 읽는다. OS의 실제 창을 찾는 코드는 포함하지 않는다.

## 테스트 대응표

원본의 순서·조건·반복 범위를 유지해 파일마다 xUnit Fact 하나로 묶었다.
아래 수는 반복 실행 횟수가 아니라 소스의 `check`/`precondition` 호출 지점 수다.

| Swift 파일 | 원본 검사 지점 | C# 검사 지점 | xUnit Fact |
|---|---:|---:|---:|
| CoreTests.swift | 63 | 63 | 1 |
| PetLayoutTests.swift | 43 | 43 | 1 |
| PetAnimationTests.swift | 37 | 37 | 1 |
| InteractionTests.swift | 31 | 31 | 1 |
| BubbleRevealTests.swift | 5 | 5 | 1 |
| 합계 | 179 | 179 | 5 |

명시된 Swift 검사에서 누락한 항목은 없다. 배율·다중 화면·프레임 반복도 유지했다.
Interaction의 31개 호출 지점은 반복을 포함하면 37회 실행된다.

`Tests/test_hooks.py`의 CLI 검사 2개는 `CliTests.cs`에 옮겼다.
정상 입력의 무출력·개인정보 제외·파일 수·상태·Unix 0600과 잘못된 입력의 무출력을
검사한다. Windows에서는 POSIX 모드 검사만 적용하지 않는다.

추가 15개 실행 사례는 Windows cwd/UNC, NFC·문자 요소, ISO 날짜·CRLF,
파일 크기 제한·잘린 파일·같은 크기 교체, 세션 발견 상한·훅 우선순위,
이미지 없이 계산하는 스프라이트 변환, 실제 CLI 파일 교체·실패·snapshot을 검증한다.
1단계 xUnit 실행 사례는 22개이며, 2단계 7개를 더해 29개였으며, 아래 PC 확인 전 보완 후 현재 37개다.

이식하지 않은 Python 검사 목록:

- `test_preview_and_create_only_and_idempotency`
- `test_settings_conflict_causes_no_partial_write`

두 검사는 CLI 동작이 아니라 `scripts/configure_hooks.py`의 설정 파일 편집을
검사한다. 해당 Windows 설정 도구는 계획의 3단계 범위이므로 이번에 작성하지 않았다.
xUnit에서 Skip 처리해 통과로 숨기지 않고 이 목록으로 남긴다.

## 1단계 검증 이력 (2026-10-11, macOS arm64)

공식 `https://dot.net/v1/dotnet-install.sh`의 `--channel 8.0 --install-dir "$HOME/.dotnet" --no-path`
설치가 성공했다. 사용한 SDK는 **8.0.425**, 실행 파일은
`~/.dotnet/dotnet`이다. sudo/brew/전역 설치는 사용하지 않았다.

- 위 SDK로 `dotnet build windows/AIpet.sln -c Release`: 경고 **0**, 오류 **0**.
- `dotnet test windows/AIpet.sln -c Release`: 통과 **22**, 실패 **0**, 건너뜀 **0**.
- `dotnet run --project windows/src/AIpet.Cli -- --hook claude --event-root <임시폴더>`:
  stdin에 session_id=s1, PermissionRequest, cwd=`C:\Users\a\proj` 전달.
  종료 코드 **0**, stdout **빈 문자열**, stderr **빈 문자열**, JSON **1개**.
  `state=waiting`, `project=proj`, `provider=claude`, `sessionID=s1`, `version=1` 확인.
  파일명: `e25e1af58555c8d7d9e818b5b0c95ba86f9beb6c277c5f82815b230959a56822.json`.
- Swift/C# 검사 호출 지점 수와 원본의 명시적 진단 문구 대응을 대조했다. 누락 없음.
- `bash scripts/test.sh`: Core **63개**, Layout, Animation, Interaction **37개** 통과 후,
  `scripts/prepare_assets.py`에서 `ModuleNotFoundError: No module named 'PIL'`로 종료 코드 **1**.
  지시대로 Pillow를 설치하거나 환경을 변경하지 않았다.
- 위 스크립트의 이후 단계인 SpritePresentation, 렌더링 비교, BubbleReveal의
  **macOS 실행은 미실행**이다. BubbleReveal의 C# 대응 검사는 통과했다.
- `git status --short`: `?? windows/`만 표시. 기존 추적 파일 변경 없음.
  작업 브랜치 `feature/windows`. 커밋·푸시 없음.

이 1단계 기록 이후의 WPF 빌드 결과는 아래 2단계 검증을 참고한다. Windows 실행과 GitHub Actions 실행은 아직 하지 않았다.
Windows의 실제 Codex·Claude 기록 경로와 출력은 사용자 PC에서 추가 확인해야 한다.
원래 제품 완료 조건인 Windows 펫 표시와 두 AI 상태 표시 확인은 후속 단계에 남아 있다.

## 2단계 구현

- `TranscriptTail`은 변경 없는 파일을 열지 않으며 실제 남은 크기만큼 읽기 버퍼를 할당한다.
- `PetStatusPresenter`는 Swift updatePet의 일시 중지, 긴급 작업, 고정 선택, 화면 작업 우선순위,
  여러 작업 요약, 최근 응답, 대기/미확인/졸기 문구와 경보 키를 계산한다.
- `SettingsStore`는 `%APPDATA%\AIpet\settings.json`에 `petName`, `petScale`, `position`,
  `bubbleAlwaysShown`을 원자적으로 저장한다. 손상 파일은 기본값으로 복구한다.
  position의 x/y는 Windows 가상 데스크톱의 물리 픽셀 좌표로 저장한다.
- `CommandEntry`는 앱과 CLI가 함께 사용한다. 훅은 UI·Mutex보다 먼저 실행하며,
  snapshot은 UTF-8 출력이다. WinExe의 snapshot은 리디렉션을 보존하고 필요하면 부모 콘솔에 연결한다.
- WPF 투명 최상위 창은 작업 표시줄/Alt+Tab 표시와 포커스 활성화를 막는다.
  픽셀 알파를 이용해 펫 및 표시 중인 말풍선에만 입력을 처리한다.
- 행별 프레임 수, 별도 wave/doze 그림, 행별 변환과 방향별 변환은 SpriteAtlas.swift를 따른다.
  Core 타임라인, 16방향 시선, 단발 점프, 드래그 방향, 3분 뒤 졸기와 z 표시를 연결했다.
- 말풍선은 고정 크기이며 방울 셋, 위/아래 배치, 호버 유지, 경보 2초 표시,
  항상 보이기, 0.18초 페이드, 작업 중 녹색 1.6초 점멸을 제공한다.
- 트레이 메뉴, 작업 목록의 지표 3개·서비스 필터·고정 선택·연결 상태·새로고침·일시 중지,
  이름 및 크기 옵션, 위치 초기화, 동작/마우스 시선 미리보기를 구현했다.
- 상태 폴링은 2초마다 백그라운드에서 실행하며 중복을 막고 UI 스레드에 반영한다.
- 자산은 Python 스크립트로 만들며 Generated/는 Git에서 제외한다. wave/doze PNG는 원본을 링크한다.
- GitHub Actions는 windows-latest에서 자산 준비, 테스트, win-x64 자체 포함 publish 후
  AIpet-win-x64 아티팩트를 업로드하도록 설정했다. 아직 원격에서 실행하지 않았다.

### 원본과 다른 점 및 한계

- SwiftUI/AppKit의 표면은 WPF 기본 컨트롤과 불투명에 가까운 둥근 말풍선으로 구현했다.
  픽셀 단위 외형 일치는 목표가 아니다. macOS 전용 문구는 “이 PC의 최근 기록”으로 변경했다.
- 원본은 전체 화면 상단까지 갈 수 있지만 Windows는 **모든 방향을 작업 영역 안 4 DIP**로
  제한한다. 위쪽에 작업 표시줄이 있는 경우에도 말풍선과 펫을 가리지 않도록 한 차이다.
- ScreenCoordinates 한 곳에서 모니터 DPI, 물리 픽셀↔Core 좌표, Core 로컬↔WPF 좌표를 변환한다.
  단일 화면 100%/150%를 대상으로 DIP를 유지하도록 구현했으나 실제 Windows 검증은 필요하다.
- 서로 다른 DPI의 화면 경계에서는 펫 중심으로 화면을 선택하고 드래그 기준을 물리 픽셀로
  유지한다. 창과 말풍선이 두 화면에 걸치면 하나의 HWND DPI만 적용되므로 경계를 넘을 때
  크기/말풍선 위치가 순간적으로 달라질 수 있다. 저장 위치 복원도 물리 좌표 기준이므로
  화면 배율·배치 변경 뒤에는 작업 영역 안으로 재배치된다.
- 동작 줄이기는 SystemParameters.ClientAreaAnimation을 따른다. 일반 애니메이션/졸기,
  말풍선 이동/페이드/점멸을 줄이며, 방향 미리보기의 프레임 순회는 Swift 원본을 따른다.
- WPF의 DPI 설정은 매니페스트 PerMonitorV2로 유지한다. WinForms는 트레이/메뉴에만 사용하므로
  최초 빌드에서 발생한 WFAC010만 프로젝트에서 제외했다. 다른 경고는 숨기지 않았다.
  근거: [Microsoft WPF DPI 예제](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md),
  [WFAC010 설명](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/wfdev-diagnostics/wfac010).
- 요청 범위대로 실제 AI 대상 창 바라보기, strengthened/외곽 보강, 유리/블러,
  승인 알림 연결 안내, ui-smoke, 설치/훅 설정 스크립트, 루트 README는 구현하지 않았다.
- Windows에서 훅 자동 등록은 아직 하지 않는다. 명령 호출과 이벤트 수신은 제공하지만
  Codex/Claude 실제 기록 위치와 훅 연결은 3단계에서 확인해야 한다.
- Windows 저장 실패 때만 “설정을 저장할 수 없습니다.”라는 오류 안내를 추가했다.

## 2단계 실제 검증 (2026-10-11, macOS arm64)

SDK는 `~/.dotnet/dotnet` **8.0.425**를 사용했다.
Pillow 11.3.0은 Git에서 무시되는 루트 `.venv`에 requirements-build.txt로 설치했다.
원본 Resources, scripts 및 루트 .gitignore를 수정하지 않았다.

| 명령/검사 | 실제 결과 |
|---|---|
| `.venv/bin/python windows/scripts/prepare_assets.py` | 체크섬·크기·PNG 픽셀 일치·ICO 7개 크기 통과 |
| `~/.dotnet/dotnet build windows/AIpet.sln -c Release` | 성공, 경고 0·오류 0 |
| `~/.dotnet/dotnet test windows/AIpet.sln -c Release` | 통과 29·실패 0·건너뜀 0 |
| `~/.dotnet/dotnet publish windows/src/AIpet.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` | 성공, AIpet.exe 생성 |
| PE 헤더 확인 | x64 machine=0x8664, Windows GUI subsystem=2 |
| 빌드 DLL 리소스 이름 확인 | spritesheet, AIpet.ico, wave, doze 포함 |
| `git status --short --untracked-files=all` | windows/ 및 .github/workflows/windows.yml만 표시 |

추가 테스트 7개: 변경 없는 파일의 무오류/열기 생략/할당 상한, 일시 중지,
긴급·오류 60초 경계, 고정·여러 작업, 대기·졸기·최근 응답 20초 경계,
RGBA 임계값/경계, 설정 왕복·손상·크기 제한·원자 교체.

생성 파일:

- `src/AIpet.App/Assets/Generated/spritesheet.png`: 2,553,955 bytes,
  SHA256 `c73be92f5c02c44b17ad04e7438ff262c8151adf5d497c846951b89dc3ca8e4c`
- `src/AIpet.App/Assets/Generated/AIpet.ico`: 93,645 bytes,
  SHA256 `f324c270db00ca1835faaa9f9420d096391cc739646cbdc59a9755c50735d33c`
- `src/AIpet.App/bin/Release/net8.0-windows/win-x64/publish/AIpet.exe`:
  **164,827,173 bytes (157.19 MiB)**. 런타임 및 native 라이브러리를 포함하며,
  첫 실행에서 native 라이브러리는 .NET의 임시 추출 위치를 사용한다.
  publish 폴더의 PDB 두 개는 디버깅용이고 실행에 필요하지 않다.

WPF 실행·Win32 입력/트레이·DPI·Windows의 훅 실행과 실시간 상태 변화는 macOS에서
확인하지 못했다. GitHub Actions는 설정 파일만 작성했고 푸시·실행하지 않았다.
2단계에서는 기존 macOS 전체 테스트를 재실행하지 않았다(1단계 결과는 위 이력 참고).

## 사용자 Windows PC 확인 체크리스트

아래는 **미검증** 항목이다. 100%와 150% 배율 각각에서 확인한다.

- [ ] 코덱스 앱의 펫을 숨겼다 다시 켜도 AIpet 펫이 남아 있다.
- [ ] 트레이 아이콘 왼쪽 클릭으로 숨긴 펫이 다시 나온다.
- [ ] AIpet 메뉴로 숨긴 펫은 다시 나타나지 않는다(최소 5초 확인).
- [ ] AIpet 종료가 정상 동작하며 펫과 트레이가 함께 사라진다.
- [ ] 가상 데스크톱 전환 시 다른 데스크톱의 펫을 강제로 불러오지 않는다.
- [ ] 사라짐이 재발하면 `%LOCALAPPDATA%\AIpet\window.log`와 트레이 잔존 여부를 전달한다.
- [ ] AIpet.exe 실행 시 펫과 트레이가 나타나고 별도 콘솔·작업 표시줄 항목은 생기지 않는다.
- [ ] 두 번째 실행이 펫을 추가로 띄우지 않는다. Alt+Tab에 펫 창은 나타나지 않는다.
- [ ] 다른 앱에서 입력하는 동안 펫/말풍선 갱신이 키보드 포커스를 뺏지 않는다.
- [ ] 메모장·브라우저를 새로 열거나 Alt+Tab으로 전환해 펫 위치를 덮어도 펫이 앞에 남는다.
- [ ] 다른 앱의 항상 위 창을 새로 열거나 전환해 펫에 겹치면 1초 주기를 기다리지 않고 앞으로 복구되며, 그 앱에서 입력이 계속된다.
      `window.log`의 `GUARD_RECOVERY`에 `covered=True`가 기록되는지 확인한다.
- [ ] `window.log`의 `ZORDER_HOOKS`에서 `foreground`, `shown`, `reorder`가 모두 `True`인지 확인한다.
- [ ] 다른 앱의 창이 펫과 겹치지 않을 때 가림 복구가 반복되지 않는다. AIpet 메뉴·옵션은 펫 위에서도 조작 가능하다.
- [ ] 메뉴로 숨긴 상태에서 다른 창을 열어도 펫이 다시 나타나지 않는다. 트레이로 다시 표시하면 앞에 나온다.
- [ ] 드래그 방향, 화면 가장자리 4 DIP, 말풍선 상하 전환과 작업 표시줄 회피가 맞는다.
- [ ] 25%/100%/200% 크기와 말풍선 고정 크기를 확인한다. 다른 DPI의 화면으로 드래그한다.
- [ ] 펫 진입 시 점프 한 번, 이후 10초 시선 추적, 조용한 3분 뒤 졸기를 확인한다.
- [ ] 말풍선 기본 숨김·호버·항상 보이기와 작업 중 녹색 점멸을 확인한다.
- [ ] 오른쪽 클릭 메뉴가 바깥 클릭으로 닫힌다.
- [ ] 가만히 있을 때 작업 관리자에서 CPU 사용이 거의 0%다.
- [ ] 훅 명령 `echo {...} | AIpet.exe --hook claude`의 소요 시간이 2초보다 충분히 짧다.
- [ ] `%LOCALAPPDATA%\AIpet\error.log`가 생겼다면 내용을 전달한다.
- [ ] 트레이의 모든 메뉴, 동작/시선 미리보기, 숨기기/보이기, 종료를 확인한다.
- [ ] 작업 목록의 서비스 필터·고정 선택·새로고침·일시 중지/재개를 확인한다.
- [ ] 이름 검증·저장, 크기·위치·말풍선 설정의 재실행 복원을 확인한다.
- [ ] Windows의 애니메이션 효과를 끈 뒤 reducedMotion 동작을 확인한다.
- [ ] Claude Code와 Codex의 실제 작업 시작·응답·확인 필요 상태가 반영된다.
- [ ] 아래 훅은 UI를 새로 띄우지 않고 stdout/stderr가 비며 JSON의 state가 waiting이다.

PowerShell 예시(명령 이름은 `AIpet.exe --hook claude`):

```powershell
$eventRoot = Join-Path $env:LOCALAPPDATA 'AIpet\events'
'{"session_id":"windows-test","hook_event_name":"PermissionRequest","cwd":"C:\\Users\\a\\proj"}' |
    & .\AIpet.exe --hook claude --event-root $eventRoot
Get-ChildItem $eventRoot -Filter *.json | Get-Content
```

- [ ] 같은 이벤트에서 경보 말풍선이 2초만 자동 표시되고 반복 폴링이 재경보하지 않는다.
- [ ] `--snapshot` 출력이 UTF-8이며 한국어가 깨지지 않는다. WinExe이므로 리디렉션/프로세스 종료 대기 방식으로 확인한다.

## 2단계 변경 파일

기존 파일 수정:

- `windows/.gitignore`, `windows/AIpet.sln`: 생성 자산 제외 및 앱 프로젝트 추가.
- `windows/src/AIpet.Core/TranscriptTail.cs`: 변경 없는 파일 및 작은 읽기의 할당 개선.
- `windows/src/AIpet.Core/PetSpritePresentation.cs`: RGBA 바이트 측정.
- `windows/src/AIpet.Cli/Program.cs`: 공용 명령 처리 사용.
- `windows/PORTING.md`: 결과·차이·확인 절차.

새 파일:

- `windows/src/AIpet.Core/PetStatusPresenter.cs`, `SettingsStore.cs`, `CommandEntry.cs`
- `windows/tests/AIpet.Core.Tests/StageTwoTests.cs`
- `windows/scripts/prepare_assets.py`
- `windows/src/AIpet.App/AIpet.App.csproj`, `app.manifest`, `GlobalUsings.cs`
- `windows/src/AIpet.App/Program.cs`, `PetApplication.cs`
- `windows/src/AIpet.App/PetWindow.cs`, `PetSurface.cs`, `ScreenCoordinates.cs`, `SpriteAtlas.cs`
- `windows/src/AIpet.App/DashboardWindow.cs`, `OptionsWindow.cs`
- `.github/workflows/windows.yml`

브랜치는 feature/windows이며 커밋·푸시하지 않았다. Windows PC에서 펫과 두 AI의
상태 표시를 확인해야 원래 제품 완료 조건을 충족한다.

## Windows PC 확인 전 보완 (2026-10-12)

- `PetRenderState` 값 비교로 프레임·배치·투명도·문구·상태·일시 중지·동작 줄이기가
  같으면 명시적 InvalidateVisual 호출을 생략한다. 말풍선이 보이고 작업 중일 때만
  20Hz 단위의 점멸 값을 갱신한다. 입력/애니메이션 판단용 60Hz 타이머는 유지하며
  실제 유휴 CPU 사용량은 Windows에서 확인해야 한다.
- 펫 오른쪽 클릭만 `SetForegroundWindow(menu.Handle)` 뒤 메뉴를 표시한다.
  트레이 메뉴 경로는 그대로다. 바깥 클릭 닫힘은 아직 실행 검증하지 않았다.
- `%LOCALAPPDATA%\AIpet\error.log`에 시각·예외 종류·메시지·스택을 기록한다.
  1 MiB를 초과하게 되는 쓰기 전에 파일을 비우며, 매우 긴 메시지/스택도 제한한다.
  Exception.Data, 대화/도구 payload, 기록 본문, 임의 객체는 직렬화하지 않는다.
  기록 자체의 실패는 무시한다.
- Dispatcher 예외는 기록하고 계속 실행한다. 종류·메시지·스택이 같은 오류가
  연속 10회 보고되면 종료한다. AppDomain 미처리 예외는 기록하지만 런타임의
  프로세스 종료를 막지는 않는다. 미관찰 Task 예외는 개별 원인 예외를 기록하고 관찰 처리한다.
- `--hook`은 전역 오류 처리기와 GUI 초기화에 앞서 별도 try/catch에서 처리하여
  예상하지 못한 실패도 무출력·종료 코드 0을 유지한다.
- 생성자에서 SourceInitialized 처리기를 등록한 다음, OnStartup에서
  `EnsureHandle → Restore → UpdatePet → Show`를 실행한다. 핸들이 생성될 때
  확장 스타일/메시지 훅이 등록되는 코드 순서를 확인했다. 시작 깜빡임 제거는 Windows 확인 대상이다.
- 짧은 사용 안내와 진단 명령을 `windows/README.md`에 추가했다.

이번 변경 파일 9개:

- 수정: `src/AIpet.App/PetWindow.cs`, `PetSurface.cs`, `PetApplication.cs`, `Program.cs`, `PORTING.md`
- 추가: `src/AIpet.Core/PetRenderState.cs`, `ErrorLog.cs`, `tests/AIpet.Core.Tests/PreflightTests.cs`, `README.md`

최종 검증:

- `~/.dotnet/dotnet build windows/AIpet.sln -c Release`: 경고 0·오류 0.
- `~/.dotnet/dotnet test windows/AIpet.sln -c Release`: **통과 37·실패 0·건너뜀 0**.
  기존 29개와 화면 비교/점멸·로그·반복 예외 판단 테스트 8개가 통과했다.
- `~/.dotnet/dotnet publish windows/src/AIpet.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`: 성공.
- 최신 실행 파일: `windows/src/AIpet.App/bin/Release/net8.0-windows/win-x64/publish/AIpet.exe`
  **164,832,293 bytes (157.20 MiB)**. 위 2단계 최초 생성본을 대체했다.
- `git status --short --untracked-files=all`: windows/와 기존 .github/workflows/windows.yml만 표시.
  `git diff --exit-code`: 0. 이번에는 workflow 파일을 수정하지 않았다.
- 기존 생성 자산을 사용했다. 자산 재생성·Windows 실행·GitHub Actions 실행·커밋·푸시는 하지 않았다.


## 외부 숨김 방어와 창 진단

사용자 보고인 “코덱스 앱의 펫을 숨기면 AIpet도 사라짐”의 원인은 **미확인**이다.
코덱스 펫 상태와의 직접 연동은 없으며, 이번 수정은 원인 확정이 아닌 방어·진단이다.

- PetWindow.IntendedVisible로 시작·메뉴·미리보기의 표시 의도를 추적한다. 메뉴 토글은
  현재 OS 상태 대신 이 의도를 기준으로 한다. 사용자의 숨김 의도는 자동 복구하지 않는다.
- Closing은 앱의 quitting이 아닌 경우 취소한다. Quit/예외 종료/OnExit의 종료 상태에서는
  방어와 감시가 개입하지 않는다.
- 숨김 반환 전에 약 1초마다 Win32 visible/minimized/topmost 및 DWM cloaked를 읽는다.
  숨김/최소화는 SW_SHOWNOACTIVATE로, 최상위 해제는 HWND_TOPMOST + NOACTIVATE로 복구한다.
  WPF Visibility/WindowState도 맞추고 렌더 캐시를 비운다. cloaked는 기록만 한다.
- WM_CLOSE, WM_SHOWWINDOW, 닫기/최소화 WM_SYSCOMMAND, 창 순서/숨김 변경, WM_DESTROY 및
  복구 전후 상태를 window.log에 기록한다. 시작 시 초기 생성 이벤트와 앱의 표시·숨김·이동·복구
  호출 중 발생한 메시지 및 종료 중 메시지는 제외한다.
- 로그는 시각·이벤트 이름·플래그·전경 프로세스 이름만 기록하며 제목·명령줄·대화/기록 본문을
  읽거나 저장하지 않는다. 전경 프로세스는 관측 당시 정보이며 요청 발신자의 증거가 아니다.
- 로그 한도는 256 KiB다. ErrorLog와 공용 BoundedLogFile을 사용하고, 이벤트 종류별로
  초당 1건 이하로 제한한다(서로 다른 메시지가 번갈아 반복될 때도 각 종류의 빈도를 제한).
  쓰기 실패는 무시하며 error.log의 기존 1 MiB 한도는 유지한다.
- 이 최초 방어에서는 프로세스 강제 종료나 HWND 파괴까지 복원하지 못했다(창 재생성 보완은 아래 참고). 모든 실제 Win32 메시지 수신,
  다른 앱과의 상호작용, 종료·가상 데스크톱 동작은 사용자 PC에서 확인해야 한다.

변경 파일: Core/WindowGuard.cs, BoundedLogFile.cs, WindowLog.cs, ErrorLog.cs;
App/WindowDiagnostics.cs, PetWindow.cs, PetApplication.cs;
Tests/WindowGuardTests.cs; windows/README.md, windows/PORTING.md.

로컬 검증: Release 빌드 경고 0·오류 0, 테스트 통과 47·실패 0·건너뜀 0(기존 37+추가 10).
추가 검사는 표시 의도·외부 숨김·최소화·최상위 해제·cloaked 분기 및 로그 필드/빈도/크기/UTF-8/실패를 검증한다.
실제 Windows 재현 해결 여부는 아직 확인하지 않았다. 이 변경은 브랜치와 CI 아티팩트로만 제공하며,
당시 변경은 기존 릴리스를 수정하지 않고 1.0.9 preview.2로 제공했다.


## 창 재생성과 트레이 복구 보완

원인은 여전히 추정 단계다. 트레이가 남지만 펫이 복귀하지 않는다는 보고에 따라,
PetApplication의 독립적인 1초 타이머가 Closed/핸들 0/IsWindow 실패를 확인한다.
표시 의도는 앱이 보관하며, 의도 보임이고 종료 중이 아닐 때만 같은 SpriteAtlas와
현재 위치·크기·이름·말풍선 설정·일시 중지·상태 문구로 새 창을 만든다.
이전 창의 타이머·앱 이벤트·메시지 훅은 정리한다. 메뉴·미리보기·옵션은 유효한 창을
확인한 뒤 호출하며, 숨김 의도 상태에서 창이 없으면 설정만 저장한다.
트레이 왼쪽 클릭은 숨김/소실 상태를 표시로 전환하며, 이미 보이면 아무것도 하지 않는다.

Core WindowRecreationGuard는 성공/실패 모두 포함해 최근 60초간 최대 5회 재생성을 허용한다.
한도 내 시도가 만료되면 다시 시도하며, 최초 정상 시작은 재생성 횟수에 포함하지 않는다.
window.log에는 PET_CLOSED/PET_LOST/RECREATE_SUCCESS/RECREATE_FAILED/RECREATE_LIMIT를 남긴다.
GUARD_CLOAKED는 상태 전환 시에만 기록한다. 프로세스 강제 종료는 여전히 복구할 수 없다.
실제 외부 창 파괴·Codex 상호작용·트레이 클릭·종료·가상 데스크톱은 Windows에서 확인해야 한다.

로컬 검증: Release 빌드 경고 0·오류 0, 테스트 통과 53·실패 0·건너뜀 0(기존 47+추가 6),
win-x64 자체 포함 단일 실행 파일 publish 성공. 재생성 판단의 표시 의도·종료·창 유무·
실패 포함 횟수 제한·60초 경계 만료를 테스트했다. WPF 런타임 동작은 Mac에서 실행하지 않았다.


## Windows 설치와 재설치

Inno Setup 설치 파일 `AIpet-Setup-win-x64.exe`를 Actions 아티팩트에 추가했다.
고정 AppId와 사용자별 설치 위치로 재설치 시 구버전 AIpet.exe를 교체하며,
동일한 미리보기 버전 번호도 교체하도록 ignoreversion을 사용한다.
Restart Manager로 설치 대상 실행 파일을 사용하는 프로세스를 종료한 뒤 교체한다.
사용자 설정/기록은 삭제하지 않으며 시작 메뉴·바탕 화면 바로가기와 제거 항목을 관리한다.
다른 위치의 기존 ZIP 실행 파일은 자동 삭제하지 않는다. 첫 전환 시 종료·삭제 절차는 README 참고.
Mac 설치 스크립트는 이미 기존 앱 교체를 지원하므로 변경하지 않았다.

CI에서 새 설치, 이전 파일 교체, 실행 중 업그레이드, 단일 제거 항목, 설정 유지, 제거를 확인한다.
실제 사용자 PC에서는 설치 UI, SmartScreen, 기존 ZIP에서 전환, 업데이트 후 펫 실행을 확인해야 한다.
기존 GitHub 릴리스 파일은 자동 변경하지 않으며 새 설치 파일은 현재 브랜치 Actions에서 제공한다.

## 기록 파일 교체 감지 보완

Windows CI의 `rotation reload` 실패에 대응해 생성 시각 외에 파일 식별자와 읽기 경계 해시를
사용한다. 파일 시스템 터널링이나 시각 해상도는 가능한 원인이며 당시 실패 원인은 확정하지 않았다.
구조체와 식별자 비교 기준은 [Microsoft GetFileInformationByHandle 문서](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle)를 따른다.
API 실패 시 보조 판단으로 돌아가며 외부 패키지는 추가하지 않았다.

생성 시각을 동일하게 맞춘 원자 교체/같은 파일 덮어쓰기 각각의 크기 감소·동일·증가 6건과,
메타데이터만 변경·미완성 줄 덧붙이기·중복 방지 1건을 추가했다. 기존 검사는 변경하거나 생략하지 않았다.
로컬 Release 빌드는 경고 0·오류 0, 테스트는 통과 60·실패 0·건너뜀 0이다.

## Mac 1.0.10 사용량 기능 이식

Core `Usage.cs`에 Mac 모델·파서·유효 기간·이메일 가림·말풍선 순환·재시도·진단 규칙을 이식했다.
`UsageSources.cs`는 경로와 Codex 실행기·HTTP 요청을 주입할 수 있다. Codex는 10초/표준 출력 1MiB 제한,
종료 시 프로세스 트리 정리, stderr 폐기를 적용했다. `.cmd`는 숨김 cmd.exe로 실행하며 명령 확장 문자
(`%`, `!`, 따옴표, 줄바꿈)가 있는 경로는 연결 실패로 처리한다. Claude 직접 조회는 쿠키·리디렉션 없이
5초/응답 1MiB 제한이며 토큰 갱신·저장·응답 로깅은 하지 않는다. 키체인 접근 거부 문구만 Windows의
`로그인 파일 접근 거부`로 바꿨다. 실행 파일 후보와 기록·로그인 파일 위치는 README에 명시했다.

`UsageMonitor`는 백그라운드에서 겹치지 않게 조회하고 옵션 변경·일시 중지 뒤 도착한 이전 결과를
버린다. 작업 목록과 옵션은 화면 작업 영역으로 크기를 제한하고 세로 스크롤한다. 전체 계정 텍스트는
접근성 자동화에서 제외했다. 말풍선은 화면의 실제 글꼴 너비로 구분자 축약 여부를 정하고,
`PetRenderState.Subtitle` 비교가 문구 순환 시 다시 그리기를 유발한다. 기존 작업 중·긴급·일시 중지
문구는 유지한다. `settings.json`에 선택 항목 `codexPath`, 기본 false인 `claudeUsageDirectEnabled`를 추가했다.

검사 대응: `Tests/UsageTests.swift`의 83개 검사를 `All83SwiftUsageChecks` 하나에 순서대로 전부 옮겼고
실행한 검사 수도 83으로 검증한다. 추가 xUnit 검사는 합성 경로·조직 일치·크기 제한·실행기·즉시 종료·
JSON-RPC 파이프·가짜 HTTP·설정 저장·조회 주기·429·접근 거부·중복 방지·일시 중지·결과 폐기를 다룬다.
실제 로그인 토큰을 열거나 직접 조회를 실행하지 않았다. Mac에서 WPF 실행은 불가능하므로 화면 동작은
컴파일 검증만 했다. Codex 설치 위치, Claude 앱 기록 위치, `.credentials.json` 형식,
`CLAUDE_CONFIG_DIR`의 실제 배치는 Windows에서 확인해야 한다.

추가 사용자 PC 체크리스트:

- [ ] 쉬는 말풍선에 사용량과 가린 계정이 5초/3초/3초로 나온다.
- [ ] 작업 목록과 옵션에 추적 계정이 나오고 작은 화면에서도 스크롤할 수 있다.
- [ ] `AIpet.exe --usage-snapshot` 결과에 가린 계정과 조회 출처가 표시된다.
- [ ] 직접 조회를 켜면 Claude 값이 나오고 끄면 앱 기록으로 돌아간다.
- [ ] 잠시 쉬는 동안 새 사용량 조회가 일어나지 않는다.
- [ ] 설치 환경에 맞는 `codexPath`와 Claude 기록·계정 파일 위치를 확인한다.

사용량 기능은 Windows 1.0.10 미리보기 1로 제공한다. ZIP 이름은 `AIpet-1.0.10-preview.1-win-x64.zip`, 설치 프로그램 버전은 1.0.10이다. 기존 릴리스는 유지한다.
Codex 탐색은 설정의 `codexPath`와 PATH 항목이 절대 경로인 경우에만 사용한다. 상대 경로 및 Windows 드라이브 상대 경로를 제외하는 테스트를 추가했다.

이번 로컬 검증 결과: `dotnet build windows/AIpet.sln -c Release` 경고 0·오류 0,
`dotnet test windows/AIpet.sln -c Release` 통과 73·실패 0·건너뜀 0(기존 60+추가 13),
win-x64 자체 포함 단일 파일 publish 성공(`AIpet.exe` 164,903,973바이트).
Mac에서 CLI `--usage-snapshot`은 Codex `—`/`Codex 를 찾을 수 없음`, Claude `—`/오래된 기록,
계정은 가림, `directQuery`는 `실행 안 함`이었다. 일반 `--snapshot` 출력의 `@`는 0개였다.
GitHub Actions와 실제 Windows 실행은 푸시 금지 범위에 따라 이번에는 실행하지 않았다.


## 1.0.11 미리보기 1 — Codex 기록과 Store 탐색

TranscriptDecoder가 기존 2MiB tail 읽기 범위에서 사용량 숫자·시각·요금제만 보관하고 StatusMonitor가
파일별 최신 표본 중 가장 최근 것을 제공한다. 작업 상태에는 영향을 주지 않으며 제외된 세션의 메타데이터도
파싱할 수 있다. 기존 subagents 디렉터리 제외·200파일 발견 제한은 유지한다.
app-server와 기록 중 최신 값을 선택하며 기록 TTL은 24시간, 계정은 항상 확인 불가다.
일반 --snapshot 형식은 그대로이고 --usage-snapshot에 선택 출처·이유·실행 파일 종류·별도 기록 표본을 추가했다.

Store 조회는 [GetPackagesByPackageFamily](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagesbypackagefamily)와
[GetPackagePathByFullName](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname)을 사용한다.
실제 Windows API 실행과 패키지 안 codex.exe의 외부 실행 가능 여부는 미검증이며 합성 폴더만 테스트했다.
제품 프로세스 실행 실패는 개인정보 없는 Codex 앱 실행 불가 상태로 반환한다.
워크플로 ZIP은 AIpet-1.0.11-preview.1-win-x64.zip, 설치 프로그램 기본 버전은 1.0.11이다.
루트 README의 기존 배포 다운로드 링크는 유지하며 새 릴리스·병합은 별도 승인 후 진행한다.

사용자 PC 추가 확인: Codex 작업 후 기록 출처 사용량, Store 설치 Codex 후보 종류와 실행 여부,
작업 목록의 이유 문구, Claude 직접 조회, 기존 펫 소실 현상의 재현 여부.


1.0.11 로컬 검증: Release 빌드 경고 0·오류 0, xUnit 80개 통과·실패 0·건너뜀 0(기존 74+추가 6).
Mac에서 검증용 CODEX_HOME을 지정한 CLI는 실행 파일 종류 none이며 Codex 작업 기록을 선택했다.
주간 사용 50%, 남음 주 50%, 관측 2026-10-12T12:19:34.97Z, 초기화 2026-10-18T13:37:57Z,
계정 확인 불가를 확인했다. Claude는 오래된 기록이라 —와 직접 조회를 켜라는 안내였다.
일반 --snapshot은 @ 0개였다. 실제 Windows Store 실행·사용량 화면·직접 조회는 확인하지 않았다.


## 1.0.11 미리보기 2 — 표시 복구와 진단 보강

펫 소실 원인은 아직 확인하지 못했다. 사용자 확인으로 Windows 사용량 트래킹은 정상이며,
제공된 Console.OutputEncoding 예외는 진단 명령의 별도 결함이다. 원인 확인 전의 보강으로 다음을 추가했다.

- 진단 명령은 UTF-8 스트림에 직접 쓰며, 표준 출력이 없으면 LocalApplicationData/AIpet에 파일 저장한다.
  --output으로 파일을 지정할 수 있고 최종 실패는 종료 코드 1, 훅은 무출력·0을 유지한다.
- 문서화된 IVirtualDesktopManager를 WPF STA에서 생성·호출·해제한다. HRESULT를 검사하며
  COM 오류는 숫자 코드만 한 번 기록한다. 다른 가상 데스크톱이면 전경 창의 데스크톱 ID를 얻어
  자기 프로세스의 펫 HWND만 옮긴다. 전경 창 없음·빈 ID는 다음 점검에서 재시도한다.
  cloaked 처리보다 먼저 이동을 시도하고, 여전히 cloaked이면 기존 표시 복구는 하지 않는다.
- GetWindowRect와 모니터 작업 영역을 픽셀 좌표로 비교한다. 보이는 스프라이트 위치는 실제 창 좌표와
  Core 배치의 상대 위치를 ScreenCoordinates에서 변환한다. 화면 밖이면 Constrain으로 배치하며
  배치 캐시를 무효화해 외부에서 옮긴 창도 실제 SetWindowPos로 복구한다. 드래그 중에는 생략한다.
- 재생성 최근 1분 5회 제한 이후에도 10초 간격으로 시도한다. 직접 보이기·트레이 클릭은 즉시 시도한다.
- 진단 정보 저장 메뉴는 diagnostics.json에 숫자·상태와 허용된 로그만 저장한다. 로그의 임의 텍스트는
  버리고 알려진 이벤트·숫자/불리언 플래그·프로세스 이름만 내보낸다.
- 워크플로 ZIP 이름은 AIpet-1.0.11-preview.2-win-x64.zip, 앱 표기는 1.0.11-preview.2,
  설치 프로그램 AppVersion은 1.0.11을 유지한다. 공개 릴리스·README 다운로드 표는 변경하지 않는다.

사용자 PC 확인:
- [ ] 가상 데스크톱 전환 후 펫이 따라오며 의도적으로 숨긴 펫은 나오지 않는다.
- [ ] 모니터 분리 및 해상도 변경 후 펫이 보인다. 혼합 DPI도 확인한다.
- [ ] 드래그 중에는 복구가 끼어들지 않고 화면 밖 창은 드래그 종료 후 복구된다.
- [ ] 펫 소실 시 트레이 왼쪽 클릭으로 복구하고 진단 정보 저장으로 diagnostics.json·window.log를 전달한다.
- [ ] 콘솔 없는 --snapshot 실행이 파일을 만들며 --output과 리디렉션도 동작한다.

COM/Win32/WPF 실행은 Mac에서 확인할 수 없다. 실제 Windows에서 위 항목과 펫 소실의 재현 여부를 확인해야 한다.

로컬 검증: Release 빌드 경고 0·오류 0, xUnit 통과 94·실패 0·건너뜀 0(기존 82+추가 12),
win-x64 자체 포함 단일 파일 publish 성공. CLI --snapshot --output으로 파일 생성·유효 JSON·BOM 없음·@ 없음·stdout 무출력을 확인했다.
추가 테스트는 출력 실패 대체/지정 파일/UTF-8/최종 실패, 데스크톱 판단, 음수 좌표·다중 화면,
10초 제한과 수동 우회, 진단의 허용 목록·최근 50항목·개인정보 제외를 검증한다.

화면 밖 복구 반복 방지 보완: 포함 판단에 2픽셀 여유를 두고, 스프라이트 중심에서 가장 가까운
작업 영역보다 스프라이트가 크면 해당 작업 영역과의 겹침만 요구한다. 복구 사이에는 최소 30초를
두며, GetWindowRect로 실제 X/Y 변경을 확인한 경우에만 Moved를 호출해 설정을 저장한다.
Release 빌드 경고 0·오류 0, 테스트 99개 통과(기존 94+추가 5). 실제 Windows 동작은 미검증이다.


## 설치 시 예전 사본 정리

Product=AIpet, Company=jinoisfree, AssemblyTitle=AIpet을 명시한다. 신원 판정은 ProductName이
AIpet이고 OriginalFilename 또는 InternalName이 AIpet.dll/AIpet.exe/AIpet인 경우만 허용한다.
서명 인증은 아니며, 신원이 확인되지 않은 동명 실행 파일은 건드리지 않는다.

--retire-old-copies는 GUI·오류 로거 전에 처리한다. 현재 사용자 SID·세션·프로세스 이름과
버전 정보를 확인하고 종료 직전 다시 확인한다. 최대 총 10초 종료 예산을 두며 실패해도 계속한다.
종료된 사본과 locations.json에 등록된 사본 중 현재 실행 파일과 다른 확인된 AIpet.exe만 삭제한다.
Windows 최종 파일 경로 API로 junction 등 별칭을 해소하고 대소문자·구분자·점 경로도 비교한다.
폴더·설정·기록은 삭제하지 않는다. install.log는 256KiB 제한이며 결과 식별자만 기록한다.
locations.json은 정상 시작 시 원자 교체로 저장하며 최대 20개, 중복 제거, 실패 무시 정책이다.
이 기록에만 전체 경로를 보관하며 진단 출력에는 포함하지 않는다.

ZIP 실행은 다른 경로/버전의 확인된 인스턴스만 종료하고 Mutex를 재획득한다. 파일은 삭제하지 않는다.
같은 사본의 중복 실행은 조용히 종료한다. 설치 프로그램은 파일 복사 후 조용한 설치에서도
정리 명령 완료를 기다린 다음 기존 실행 옵션을 처리한다.

로컬 Release 빌드 경고 0·오류 0, xUnit 105개 통과(기존 99+추가 6).
CI 검사는 새 빌드와 공개 preview.1의 버전 정보, 실행 중/꺼진 사본 정리, 무관한 동명 프로세스 보존,
설정 보존·로그 경로 비노출, ZIP 교체·동일 사본 유지와 기존 설치 검사를 확인하도록 확장했다.
실제 사용자 Windows PC 동작과 처음부터 등록되지 않은 꺼진 구버전 탐색은 검증/지원하지 않는다.
새 릴리스와 루트 README 다운로드 링크는 이번 범위에 포함하지 않는다.

## 창 전환 시 표시 순서 복구

기존 1초 주기 검사에 창 이벤트 감시를 추가했다.
[SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)으로
다른 프로세스의 전경 창 전환·창 표시·표시 순서 변경을 감지한다.
UI 스레드에서 등록한 out-of-context 콜백은 다음 검사만 요청하며, 실제 복구는 기존 타이머에서 처리한다.
이벤트가 있으면 1초를 기다리지 않되 검사 사이에 최소 100ms를 두어 연속 이벤트를 합친다.
이벤트 누락이나 훅 등록 실패에 대비한 1초 주기 검사는 유지한다.

표시 의도·cloaked·실제 겹침을 확인한 뒤 기존 HWND_TOPMOST + SWP_NOACTIVATE 호출로 복구하므로
AIpet 메뉴로 숨긴 창을 다시 보이게 하거나 입력 포커스를 옮기지 않는다.
자기 프로세스 이벤트와 자식 컨트롤 이벤트는 제외하고, 외부에서 펫 창의 표시 순서를 바꾼 경우도
WM_WINDOWPOSCHANGED에서 검사를 요청한다. 창 종료·파괴·재생성 시 훅을 해제한다.
ZORDER_HOOKS 로그에는 세 종류의 훅 등록 성공 여부만 기록하며 진단 내보내기에도 포함한다.

로컬 검증: Release 빌드 경고 0·오류 0, 테스트 122개 통과(기존 118개 + 검사 예약 회귀 검사 4개).
win-x64 자체 포함 단일 실행 파일 publish도 성공했다.
추가 검사는 새 창 이벤트의 조기 검사, 연속 이벤트 합치기, 복구 중 새 요청 보존, 주기 검사 유지를 다룬다.
WinEvent 수신·실제 표시 순서·입력 포커스는 macOS에서 실행 검증하지 못했다.
위 Windows PC 체크리스트로 확인해야 하며 기존 공개 설치 파일은 이 변경으로 갱신되지 않는다.
