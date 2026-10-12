# AIpet Windows 미리보기

**Windows에서 아직 실행 검증하지 않은 버전입니다.** `AIpet.exe`를 원하는 폴더에 두고
실행하세요. 자체 포함 Windows x64 빌드에는 별도 .NET 설치가 필요하지 않습니다.
서명되지 않은 실행 파일이므로 Windows SmartScreen 경고가 나올 수 있습니다.

- 펫 클릭: 작업 목록. 드래그: 이동. 오른쪽 클릭: 메뉴.
- 트레이 아이콘: 작업 목록, 보이기/숨기기, 일시 중지, 말풍선, 옵션과 미리보기.
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
