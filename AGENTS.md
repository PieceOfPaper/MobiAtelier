# AGENTS.md: 에이전트 작업 지침

이 저장소는 Codex와 Claude Code를 번갈아 쓰며 만든다. 두 에이전트 모두 이 파일을 기준으로 삼는다(Claude Code는 `CLAUDE.md`가 이 파일을 불러온다).
한쪽 에이전트의 메모리와 대화 내용은 다른 쪽이 보지 못한다. 오래 지켜야 할 규칙은 이 파일에, 진행 상황과 인계 사항은 `Docs\PROGRESS.md`에 남긴다.

---

## 1. 프로젝트 요약

- **MobiAtelier(모비 아틀리에)**: 마비노기 모바일 캐릭터의 외형·의상·염색을 미리 보는 Unity 기반 Windows용 꾸미기 도구.
- 사용자 PC에 설치된 마비노기 모바일 PC 클라이언트의 패치 파일을 런타임에 직접 읽어 캐릭터를 조립한다.
- **게임 리소스는 프로그램과 저장소에 포함하지 않는다.**

## 2. 작업을 시작할 때

1. **`Docs\PROGRESS.md`를 먼저 읽는다.** 현재 단계, 최근 작업, 다음 할 일, 인계 메모가 있다.
2. `README.md`의 기획서 목록에서 작업 주제에 맞는 `Docs\*.md`를 읽는다. 상태 전환은 `Docs\ProgramFlow.md`, 항목별 세부는 각 기획서가 기준이다.
3. 게임 데이터 구조나 런타임 조립이 필요하면 3장의 참고 자료를 본다.
4. `git status`로 다른 에이전트나 사용자가 남긴 미커밋 변경이 있는지 확인한다. 남의 변경은 건드리지 않는다.

## 3. 게임 설치 폴더와 로컬 참고 자료

- 게임 설치 폴더(`<install>`, 예: `C:\Nexon\MabinogiMobile`)는 **절대 쓰지 않는다.** 읽기만 한다.
- 문서와 코드에서 설치 폴더는 `<install>`로 쓴다. 실제 경로는 앱 설정에서 읽는다(`Docs\Startup.md`).
- **루트에 `AGENTS.local.md`가 있으면 반드시 읽는다.** 이 PC에만 있는 참고 자료와 그 취급 규칙이 있다(git 제외 파일).
  - 그 파일의 내용(폴더 경로, 이름, 출처)은 저장소의 어떤 파일(코드, 주석, 문서, 커밋 메시지)에도 옮겨 적지 않는다.

## 4. 환경

- Windows 11, Unity **6000.3.27f2**, URP 17, Input System 1.12. 에디터 경로: `C:\Program Files\Unity\Hub\Editor\6000.3.27f2\Editor\Unity.exe`.
- C# 언어 버전은 Unity가 정한 9.0이다. 그보다 새 문법을 쓰지 않는다.
- 셸은 PowerShell 5.1 또는 Git Bash. .NET SDK 10, Python 3.13이 있다.
- 파일 인코딩은 UTF-8이다. 한글이 많으므로 파이썬은 `-X utf8`로 실행한다. 콘솔 한글이 깨져 보여도 파일은 정상일 수 있다.

## 5. Unity 작업 규칙

- **에셋과 `.meta`는 짝으로 다룬다.** 옮기거나 이름을 바꾸거나 지울 때 `.meta`도 같이 한다. 기존 `.meta`의 GUID를 바꾸지 않는다(참조가 끊어진다).
- 새 `.cs` 파일의 `.meta`는 직접 만들지 않고 Unity가 만들게 둔다. Unity가 만든 `.meta`는 에셋과 같이 커밋한다.
- 씬(`.unity`), 프리팹, `.asset` YAML은 손으로 고치지 않는 것을 원칙으로 한다. 꼭 고쳐야 하면 범위를 좁히고 사용자에게 에디터 확인을 요청한다. 반복되는 구성은 코드나 에디터 스크립트로 만든다.
- 고치지 않는 폴더/파일: `Library\`, `Temp\`, `Logs\`, `UserSettings\`, `obj\`, `*.csproj`, `*.slnx`(모두 Unity·IDE가 만든다).
- 패키지 추가·버전 변경(`Packages\manifest.json`), `ProjectSettings\` 변경은 사용자에게 먼저 묻는다.
- 게임에서 꺼낸 리소스(텍스처, 메시, 시트 JSON 등)를 `Assets\`나 저장소에 넣지 않는다. 시험용 산출물은 git 제외 폴더에 둔다.
- 폴더 구조와 어셈블리 정의(2026-11-01 정함). 프로젝트 이름 폴더(`Assets\MobiAtelier\`)를 따로 두지 않고 `Assets\` 바로 아래에 둔다.

| 경로 | 어셈블리 | 내용 |
|---|---|---|
| `Assets\Scripts\Install\` | `MobiAtelier.Runtime` | 설치 폴더 index·blob·리소스 복호화·SJON 리더, 경로 검사. UnityEngine 비의존 |
| `Assets\Scripts\Sheets\` | 〃 | 게임 시트 리더와 필드 순서(`SheetSchemas.cs`), 시트 조회(`GameData`). UnityEngine 비의존 |
| `Assets\Scripts\Character\` | 〃 | 조립 계획(`PlanResolver`), 메시·번들 읽기, 조립(`CharacterAssembler`), 캐릭터 상태(`CharacterView`) |
| `Assets\Scripts\State\` | 〃 | 장면 상태(`EditState`), 앱 설정, JSON 저장·자동 저장 |
| `Assets\Scripts\App\`, `Assets\Scripts\UI\` | 〃 | 시작 흐름(`AppController`), 카메라, Windows 대화상자, UI Toolkit 오버레이 |
| `Assets\Editor\` | `MobiAtelier.Editor` | 프로젝트 설정·빌드·설치 폴더 시험 스크립트 |
| `Assets\Shaders\` | — | 캐릭터 셰이더 |
| `Assets\Scenes\` | — | 앱 씬 `Main.unity` (`SampleScene.unity`는 템플릿 잔여물) |
| `Assets\UI\` | — | UI 테마(`.tss`)·PanelSettings |

- **네임스페이스는 쓰지 않는다.** 이 프로젝트 전용 코드이므로 모든 타입을 전역 네임스페이스에 둔다. 전역 타입은 `using`으로 가져온 같은 이름의 타입보다 우선하므로 UnityEditor 타입과의 이름 충돌도 생기지 않는다.
  - 다른 패키지의 전역 타입과 이름이 겹쳐 모호성 오류(CS0433)가 나면 우리 쪽 클래스 이름을 바꾼다.
- 어셈블리 정의는 런타임·에디터 두 개로 유지한다. 폴더를 더 나눠도 asmdef는 늘리지 않는다.

- 앱 씬·PanelSettings·Build Settings는 손으로 만들지 않고 메뉴 `MobiAtelier/Setup Project`(`ProjectSetup.Run`)로 다시 만든다.
- `Install\`, `Sheets\`는 UnityEngine을 쓰지 않게 유지한다. 그래야 .NET 콘솔로 설치 폴더를 빠르게 시험할 수 있다.
- 게임 업데이트로 시트 행 크기가 맞지 않으면(`SheetFormatException`) `SheetSchemas.cs`의 필드 순서를 고친다.

## 6. 검증

- 컴파일 확인: `dotnet build MobiAtelier.Runtime.csproj -nologo -v q` (에디터 코드는 `MobiAtelier.Editor.csproj`). 약 5초. csproj 이름은 asmdef 이름을 따른다.
  - csproj는 Unity가 만들며 `.cs` 파일을 하나씩 나열한다. **새 파일은 Unity가 csproj를 다시 만들기 전까지 빌드에 포함되지 않는다.** 이때는 아래 배치 모드를 쓰거나 사용자에게 에디터 포커스(재컴파일)를 요청한다.
- 배치 모드(에디터에서 이 프로젝트가 열려 있으면 실패한다):
  `"C:\Program Files\Unity\Hub\Editor\6000.3.27f2\Editor\Unity.exe" -batchmode -nographics -projectPath D:\MobiAtelier -quit -logFile Logs\batch.log`
  오류는 `Logs\batch.log`에서 `error CS`로 찾는다.
- 설치 폴더 조립 시험(그래픽 필요, `-nographics` 없이): `-executeMethod InstallSmokeTest.Run -maInstall <install> -maOut <저장소 밖 폴더>`. 두 기본 외형과 의상 조합을 촬영하고 `log.txt`를 남긴다.
- Windows 빌드: `-buildTarget Win64 -executeMethod BuildPlayer.Windows -maBuildOut <폴더>`(처음 약 2분, 이후 약 15초).
  - 빌드 실행 시험: `MobiAtelier.exe -maDataDir <임시 폴더> -maScreenshot <png> [-maShotDelay 초]`. `-maDataDir`로 사용자의 실제 자동 저장을 건드리지 않는다.
- 게임 리소스가 찍힌 촬영본·빌드 결과는 저장소에 넣지 않는다.
- 플레이 결과나 화면 확인이 필요한데 직접 볼 수 없으면, 확인할 항목을 구체적으로 적어 사용자에게 요청한다. 확인하지 않은 것을 완료로 쓰지 않는다.

## 7. 기획 문서 규칙

- 기획서는 `Docs\`에, 한국어 "~한다"체, 짧은 문장과 목록·표로 쓴다.
- 각 항목의 상태(확정·제안·미정)를 유지한다. **확정 사항을 사용자 합의 없이 바꾸지 않는다.** 구현 중 충돌을 발견하면 문서에 표시하고 사용자에게 알린다.
- 흐름(`ProgramFlow.md`)과 항목별 세부를 섞지 않는다. 세부는 해당 기획서에 쓰고 흐름 문서에서는 링크한다.
- 새 기획서를 만들면 `README.md`의 기획서 목록에 한 줄(링크 + 요약)을 추가한다.
- 외형·의상 선택은 큰 카테고리를 폴드아웃으로 열고 닫고, 후보형 세부 항목을 검색 가능한 드롭다운으로 고른다. 아이콘 격자는 필수가 아니다. 외형 필수 범위·연속 값 조작의 제안·미정은 `Docs\Appearance.md`를 따른다.
- 착용·숨김은 원본 데이터 규칙을 우선 해석한다. 염색 UI는 실제 염색 파트 표시와 컬러피커 편집을 기본으로 하며, 영역 강조 등 추가 기능을 필수나 사용자 결정 대기로 확대하지 않는다. 기존 HEX 입력 정책은 유지한다.
- 무기 외형은 직업 선택 없이 자유롭게 고른다. EquippableCombatJobs로 선택을 제한하지 않는다.
- 실행 취소·다시 실행 이력은 현재 실행의 장면 편집만 보관한다. JSON 불러오기·초기화 성공 시 모두 비우며, 취소·실패 시 유지한다. 초기화는 현재 성별의 기본 외형·초보자 의상·염색·카메라를 복원하고 확인 팝업을 제공한다(`Docs\Reset.md`).
- 숨김 데이터와 시트 없는 직접 모델은 `UNITY_EDITOR`에서만 탐색·선택·출력한다. 일반·Development 빌드의 JSON·자동 복원으로 우회하지 않는다. 판정 근거·공개 시각의 한계는 `Docs\Equipment.md`를 따른다.
- 아이템 표기는 `데이터ID - 이름`, 숨김 항목은 `[숨김] 데이터ID - 이름`이다. 실제 Id·GroupId를 쓰며 Index·배열 순번으로 대체하지 않는다. 이름과 ID를 모두 검색한다.
- 모델 로딩은 마지막 유효 요청만 확정한다. 늦은 이전 요청으로 장면·이력·자동 저장을 덮어쓰지 않는다.
- 문서 머리의 정리일/조사일은 실제로 고친 날짜로 갱신한다.

## 8. 진행 기록과 인계

- 작업 단위가 끝날 때마다 `Docs\PROGRESS.md`를 갱신한다: 최종 갱신 날짜, 최근 작업, 다음 할 일, 인계 메모.
- 인계 메모에는 다음 에이전트가 모르면 헛걸음할 것만 쓴다: 끝내지 못한 작업, 확인 대기 중인 질문, 사용자에게 받은 결정, 알게 된 함정.
- 사용자에게 받은 결정이 오래 지켜야 할 규칙이면 이 파일의 해당 장에 반영한다.
- 숫자(개수, 크기, 시간)는 실제로 확인한 값만 쓴다. 확인한 것과 추정을 구분한다(`[확인]`, `[추정]`).

## 9. Git

- 원격: GitHub `PieceOfPaper/MobiAtelier`. 기본 브랜치 `main`.
- **커밋과 푸시는 사용자가 "커밋", "푸시"라는 말로 직접 요청할 때만 한다.**
  - 그 말이 없는 요청(예: "마무리해", "정리해", "반영해", "올려")은 커밋·푸시 요청이 아니다. 애매하면 하지 않고 묻는다.
  - 개발이 끝나면 커밋하지 말고 변경 내용을 보고한다. 사용자가 직접 확인한 뒤 커밋을 요청한다.
  - 커밋 요청은 푸시 요청이 아니다. 푸시는 보통 사용자가 직접 하며, "푸시"를 요청받았을 때만 한다.
  - 한 번 받은 커밋·푸시 요청은 그 작업에만 해당한다. 이후 작업에 이어서 적용하지 않는다.
- 커밋에는 요청한 작업의 파일만 경로를 지정해 넣는다. `git add -A`, `git add .`를 쓰지 않는다.
- 커밋 메시지: 한국어 한 줄 요약. 여러 파일이면 빈 줄 뒤에 `- 경로: 내용` 목록을 붙인다.
- 에이전트 표시(Co-Authored-By 등)는 각 에이전트의 기본 규칙을 따른다.
- 줄바꿈은 git 설정(`core.autocrlf`)에 맡긴다. 관계없는 파일의 줄바꿈만 바꾸는 변경을 만들지 않는다.

## 10. 하지 말 것

- 설치 폴더(`<install>`)에 쓰거나 파일을 옮기지 않는다.
- 게임 리소스를 저장소에 커밋하거나 빌드에 포함하지 않는다.
- 안티치트(BlackCipher, NGClient)와 게임 프로세스는 다루지 않는다. 실행 중인 게임 메모리를 읽거나 게임 파일을 고치지 않는다.
- 사용자 데이터 폴더(PlayerPrefs, 계정·캐릭터 데이터)를 읽지 않는다. 실제 계정 ID, 캐릭터 ID를 코드나 문서에 쓰지 않는다.
- 사용자가 만든 파일, 요청과 관계없는 변경을 되돌리거나 고치지 않는다.
