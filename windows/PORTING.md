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
- 파일 교체는 생성 시각 변경 또는 현재 offset보다 작은 파일 크기로 판정한다.
  Swift의 inode 판정과 달라서 같은 생성 시각·같거나 더 큰 크기를 유지하는
  교체는 감지할 수 없다. 이는 이번 계획에서 지정한 방식이다.
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
`/Users/mac-mini-m4-homeserver/.dotnet/dotnet`이다. sudo/brew/전역 설치는 사용하지 않았다.

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

SDK는 `/Users/mac-mini-m4-homeserver/.dotnet/dotnet` **8.0.425**를 사용했다.
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
- 최신 실행 파일: `/Users/mac-mini-m4-homeserver/paseo-work/AIpet/windows/src/AIpet.App/bin/Release/net8.0-windows/win-x64/publish/AIpet.exe`
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
기존 preview.1 릴리스는 갱신하지 않는다.


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
