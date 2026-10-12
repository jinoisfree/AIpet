# AIpet Windows 1.0.11 미리보기 1

GitHub에서 **AIpet-Setup-win-x64.exe**를 받아 실행하세요. 다음 버전도 같은 설치 파일을
실행하면 설치된 구버전 실행 파일을 새 파일로 교체합니다. 실행 중인 AIpet을 닫으라는
안내가 나오면 종료에 동의하세요. 설치 완료 후 새 AIpet을 실행할 수 있습니다.
이름·크기·위치 등 사용자 설정은 유지하며, 관리자 권한이나 별도 .NET 설치는 필요하지 않습니다.

설치 위치는 `%LOCALAPPDATA%\Programs\AIpet`이며 시작 메뉴와 바탕 화면에 바로가기를 만듭니다.
제거는 Windows 설정의 **설치된 앱 → AIpet → 제거**를 이용하세요. 제거해도 사용자 설정은 유지합니다.
서명되지 않은 실행 파일이므로 Windows SmartScreen 경고가 나올 수 있습니다.

이전 ZIP 버전을 사용했다면 첫 설치 전에 트레이 메뉴로 종료하고, 직접 압축을 풀었던 폴더의
구버전 `AIpet.exe`와 예전 바로가기를 삭제하세요. 다른 폴더의 휴대용 복사본은 설치 관리 대상이
아니므로 자동 탐색·삭제하지 않습니다. 이후 Setup으로 설치한 버전은 자동 교체됩니다.
ZIP은 설치 없이 사용하는 휴대용 버전으로 계속 제공합니다.

**창 소실 복구 보완은 아직 사용자 PC에서 확인하지 않았습니다.**

- 펫 클릭: 작업 목록. 드래그: 이동. 오른쪽 클릭: 메뉴.
- 트레이 아이콘 왼쪽 클릭: 숨겨지거나 사라진 펫 다시 표시. 더블 클릭: 작업 목록. 오른쪽 클릭: 메뉴.
- 종료: 오른쪽 클릭 또는 트레이 메뉴의 **AIpet 종료**.
- 설정: `%APPDATA%\AIpet\settings.json`
- 창 진단: `%LOCALAPPDATA%\AIpet\window.log` (최대 256 KiB). 펫이 사라지는 문제가 다시 나오면 이 파일을 전달해 주세요.
- 오류 기록: `%LOCALAPPDATA%\AIpet\error.log` (최대 1 MiB, 오래된 내용부터 새로 시작).

상태 진단 파일을 저장하려면 명령 프롬프트(cmd)에서 실행 파일 폴더로 이동한 뒤:

```bat
start "" /wait AIpet.exe --snapshot > snapshot.json
```

출력은 UTF-8 JSON입니다. 현재 작업 상태만 포함하며 대화 본문은 저장하지 않습니다.
기본 기록 위치는 `%USERPROFILE%\.codex\sessions`, `%USERPROFILE%\.claude\projects`입니다.
`CODEX_HOME` 또는 `CLAUDE_CONFIG_DIR`가 있으면 각각 그 경로의 `sessions`, `projects`를 읽습니다.
훅 자동 등록은 아직 제공하지 않습니다.

실행·DPI·드래그·메뉴·CPU 사용량과 실제 작업 상태를 확인하는 상세 절차는
[PORTING.md](PORTING.md)의 사용자 PC 체크리스트를 참고하세요. 오류가 발생하면
`error.log` 내용을 전달해 주세요.

코덱스 앱의 펫을 숨길 때 AIpet도 사라진다는 보고의 원인은 아직 확인되지 않았습니다.
외부 숨김·최소화·최상위 해제는 약 1초마다 점검해 복구하며, AIpet 메뉴로 직접 숨긴 경우에는
복구하지 않습니다. 가상 데스크톱 전환으로 숨겨진 창은 강제로 표시하지 않습니다.
프로세스가 강제 종료된 경우에는 자동 복구할 수 없습니다. window.log에는 창 이벤트의
플래그와 당시 전경 프로세스 이름만 기록합니다. 전경 프로세스가 요청의 발신자라는 뜻은 아닙니다.

펫 창이 닫히거나 파괴되면 앱이 현재 설정으로 재생성합니다. 최근 1분에 최대 5회 시도하며,
한도에 도달하면 시도 횟수가 줄어들 때까지 중단합니다. 메뉴로 숨긴 펫은 자동으로 재생성하지 않습니다.

## 사용량과 추적 계정

쉬는 상태의 말풍선은 남은 사용량 5초 → Codex의 가린 계정 3초 → Claude Code의 가린 계정 3초로
바뀝니다. 5시간·주간 중 덜 남은 값을 표시하고 주간 값에는 `주`를 붙입니다. 작업 목록의 **사용량**과
옵션의 **추적 계정**에는 전체 이메일이 표시됩니다. 말풍선에는 앞 1~2글자만 남기며 트레이 툴팁과
접근성 이름에는 이메일을 넣지 않습니다.

- Codex: 앱 서버에서 계정·사용량을 5분마다 조회합니다. 실행 파일은 설정의 `codexPath`, PATH의
  `codex.exe`·`codex.cmd`, `%APPDATA%\npm\codex.cmd`, `%LOCALAPPDATA%\Programs\Codex\codex.exe`,
  `%LOCALAPPDATA%\Programs\Codex\bin\codex.exe` 순으로 찾습니다. 설치 위치는 실제 Windows에서 미검증입니다.
  상대 경로인 PATH 항목과 `codexPath`는 무시합니다. 자동 탐색이 안 되면 종료 후 `settings.json`에 `"codexPath": "C:\\도구\\codex.cmd"`처럼 전체 경로를 넣으세요.
- Claude 앱 기록: `%APPDATA%\Claude\plan-usage-history.json`을 30초마다 읽으며, 없으면
  `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\plan-usage-history.json`을 찾습니다.
  두 위치는 미검증 가정입니다. 기록의 조직 ID와 Claude Code 계정의 조직 ID가 같을 때만 계정을 표시합니다.
- 계정 파일은 `%USERPROFILE%\.claude.json`, `CLAUDE_CONFIG_DIR`가 있으면 그 안의 `.claude.json`입니다.
- **Claude 사용량 직접 조회**는 기본 꺼짐입니다. 옵션에서 켜면 `%USERPROFILE%\.claude\.credentials.json`
  (`CLAUDE_CONFIG_DIR`가 있으면 그 안의 `.credentials.json`)에서 Claude Code 로그인 토큰을 읽어
  `api.anthropic.com`에만 조회합니다. 파일 형식은 미검증입니다. 로그인 갱신이나 파일 쓰기는 하지 않습니다.
  120초 간격이며 요청 제한(429)은 5·10·20분으로 늘립니다. 파일 접근 거부 시 껐다 켜거나 재시작해야 재개합니다.

Codex 앱 서버·직접 조회 값은 60분, 앱 기록은 30분 동안 유효하며 초기화 시간이 지난 값도 `—`가 됩니다.
일시 실패 시 유효 기간 안의 이전 값을 유지하고, 유효한 직접 조회 값이 없으면 앱 기록을 사용합니다.
잠시 쉬는 동안 새 조회는 하지 않습니다. 토큰·응답·전체 이메일은 파일이나 오류 로그에 저장하지 않습니다.

사용량 진단(cmd):

```bat
start "" /wait AIpet.exe --usage-snapshot > usage-snapshot.json
```

UTF-8 JSON에는 가린 계정만 들어가고 이 명령은 직접 조회를 실행하지 않습니다.
기존 `--snapshot`은 계정 정보를 포함하지 않습니다. **사용량 기능의 실제 Windows 실행과 직접 조회는 아직 미검증입니다.**


## 1.0.11 사용량 연결 보완 (개발 브랜치)

그 PC의 Codex 앱 또는 CLI에 로그인하고 Codex로 작업해야 합니다. 웹 ChatGPT 로그인은 해당하지 않습니다.
앱 서버를 실행하지 못해도 기존 작업 기록의 `token_count/rate_limits`에서 숫자·시각·요금제만 추출합니다.
앱 서버와 기록 중 관측 시각이 더 늦은 값을 사용합니다. 기록은 24시간 유효하며 초기화 시각이 지난 창은
제외합니다. 기록에는 이메일이 없어 `계정 확인 불가`로 표시하고 다른 출처의 이메일을 붙이지 않습니다.
기록 폴더는 `CODEX_HOME` 아래 `sessions`, 없으면 사용자 홈의 `.codex/sessions`입니다.

기존 실행 파일 후보 다음으로 Store 패키지 `OpenAI.Codex_2p2nqsd0c76g0`를 조회합니다.
그 패키지의 `app/resources`와 바로 아래 한 단계 폴더에서 `codex.exe`만 찾습니다.
실제 Store 설치 구조 및 패키지 밖에서의 실행 가능 여부는 미검증입니다.
`--usage-snapshot`의 `codexExecutable`은 configured/path/npm/store/none 종류만 출력하며 경로는 출력하지 않습니다.
`codexRecord`에는 별도로 읽은 기록 표본의 출처·시각·창별 값이 있어 app-server가 선택된 경우에도 확인할 수 있습니다.

Claude는 구독 계정으로 Claude Code에 로그인한 뒤 직접 조회를 켜거나 Claude 데스크톱 앱의 최근 기록이
있어야 합니다. API 키 로그인은 구독 한도 값이 없습니다. 앱 기록이 없거나 오래됐고 직접 조회가 꺼져 있으면
옵션을 켜라는 안내를 표시하고, 켜져 있으면 로그인 정보 없음·만료·인증 거부·요청 제한·연결 실패 등을 표시합니다.
실제 로그인 파일 및 직접 조회 동작은 사용자 PC에서 확인해야 합니다. 이번 버전은 아직 릴리스하지 않았습니다.
