# 시작 설정·설치 경로·최초 캐릭터 기획

> 정리일: 2026-11-01
> 확정 사항·제안·미정 표기를 유지한다. 구현 완료를 의미하지 않는다.
> 화면·상태 전환은 [프로그램 플로우](ProgramFlow.md)를 참고한다.

## 앱 시작과 게임 경로 검사

## 검사할 경로

- 최초 기본값: C:\Nexon\MabinogiMobile.
- **제안**: 마지막으로 검사를 통과한 경로를 앱 설정에 저장하고 다음 실행에서 먼저 검사한다. 저장된 경로가 없으면 최초 기본값을 쓴다.
- 검사 성공 없이 입력값만 바뀐 경우에는 마지막 정상 경로를 갱신하지 않는다.
- 원본 게임 파일은 읽기만 한다.

## 정상 구성의 의미

폴더 존재 여부나 실행 파일 하나의 존재만으로 통과시키지 않는다.

| 검사 단계 | 검사 내용 | 실패 시 동작 |
|---|---|---|
| 경로 | 폴더가 있고 접근 가능한가 | 경로 팝업과 이유 표시 |
| 필수 index | 조립기에 필요한 AssetPackages·ResourcesPackage의 루트·Starter index가 있는가 | 누락 파일 안내 |
| index 형식 | 헤더·레코드를 해석할 수 있는가 | 읽기 실패·지원 불가 안내 |
| 필수 데이터 | 시작에 필요한 정의·시트·리소스의 위치를 찾을 수 있는가 | 누락 데이터 안내 |
| 실제 읽기 | 필요한 blob의 범위를 정상적으로 읽을 수 있는가 | 재검사 안내 |

- 필수 파일 목록은 실제 로더가 사용하는 범위로 관리한다. 저장소 문서만으로 모든 버전의 파일 구성을 하드코딩하지 않는다.
- 최초에는 시작에 필요한 범위를 검사하고, 모든 의상 데이터를 전수 검사하지 않는다.
- 경로 검사 성공과 모든 의상 지원은 별개다. 개별 파츠 오류는 편집 중 별도로 처리한다.
- 런처 패치로 파일 구성이 바뀌었거나 잠시 읽을 수 없으면 실패 이유와 재시도 방법을 알려준다.
- 지원 게임 버전 판정 방식과 버전 표시 위치는 구현 시 구체화한다.

## 경로 지정 팝업

## 구성

| 요소 | 내용 |
|---|---|
| 제목 | 마비노기 모바일 설치 경로 |
| 설명 | 설치 폴더를 찾지 못했거나 필요한 파일을 읽을 수 없음을 안내 |
| 경로 입력란 | 기본 경로 또는 마지막 입력 경로 |
| 폴더 선택 버튼 | Windows 기본 폴더 선택창 열기 |
| 검사 결과 | 입력란 아래에 누락·접근·형식 오류 표시 |
| 하단 버튼 | 다음 |

문구 예:
- 설치된 마비노기 모바일 폴더를 선택해 주세요.
- 이 폴더에서 필요한 파일을 찾지 못했습니다.
- 파일을 읽지 못했습니다. 게임 패치가 끝난 뒤 다시 시도해 주세요.

## 상호작용

1. 직접 입력 또는 폴더 선택으로 입력값을 바꾼다.
2. 폴더 선택을 취소하면 기존 입력값을 유지한다.
3. 다음을 누르면 경로 검사를 시작한다.
4. 검사 중 중복 다음 입력을 막고 검사 중 상태를 표시한다.
5. 실패하면 팝업을 유지하고 원인을 표시한다.
6. 성공하면 경로를 기억하고 팝업을 닫은 뒤 꾸미기 씬으로 진행한다.

**제안**
- 입력 앞뒤 공백과 경로를 감싼 따옴표는 정리한다. 경로 중간 공백은 보존한다.
- Enter는 경로 검사 실행으로 연결한다.
- 필수 경로가 정해지기 전에는 팝업 바깥 클릭이나 X로 검사를 우회해 꾸미기 씬에 들어갈 수 없게 한다.
- 시작 화면에서 종료할 수 있는 수단은 제공하되, 시작 화면 종료 UI의 모양은 후속 결정한다.

## 꾸미기 씬 진입과 초기 상태

## 공통 로딩

- 필요한 index·시트·리소스 로딩 진행 상태를 표시한다.
- 현재 작업 데이터를 읽고, 해당 데이터의 파츠를 해석한 뒤 모델을 조립한다.
- 입력·로딩 실패 상태를 모델이 없는 빈 화면으로만 남겨두지 않는다.

## 마지막 작업이 있는 경우

- 마지막 정상 편집 상태의 외형·착용 파츠·염색값을 복원한다.
- 꾸미기 창의 마지막 위치·크기를 복원하고 현재 앱 화면 영역 안으로 보정한다([창·UI 기획](WindowAndCustomizationUI.md)).
- 카메라 조작 상태도 복원한다. 외형·장면 데이터와 UI 설정은 저장 구조상 분리한다.
- 복원된 상태는 자동 복원 데이터다. 사용자가 마지막으로 수동 저장한 JSON을 자동으로 덮어쓰지 않는다.

## 최초 실행

배치할 초기 캐릭터는 **미정**이다.

| 후보 | 장점 | 검토할 점 |
|---|---|---|
| 기본 외형 프리셋 1개 | 바로 조작 가능 | 기본 성별·외형·옷을 정해야 함 |
| 간단한 시작 선택 | 사용자가 외형 기준을 선택 | 시작 단계가 늘어남 |
| 새로 만들기·JSON 불러오기 선택 | 기존 코디 파일로 곧바로 시작 가능 | 신규 사용자에게는 기본 외형이 여전히 필요 |

- 위 후보 중 하나를 아직 확정하지 않는다.
- 2026-11-01 언팩 시트를 추가 조사했다. 아래는 초기 모델 후보의 근거이며 실제 캐릭터 생성 최초 기본값을 확정한 결과는 아니다.

| 후보·자료 | 직접 확인한 값 | 판단 |
|---|---|---|
| CharacterPresetAppearance / FemaleAppearance_Default_01 | Female, Age 17, BodyType Default, HeightModifier 0, Female_Body_00, Head_00, Hair_ShortStraight, Eye_01, Mouth_01, 피부 Color_002·헤어 Color_016·눈 Color_011·입 Default | 기본 여성 외형 후보 |
| CharacterPresetAppearance / MaleAppearance_Default_01 | Male, Age 17, BodyType Default, HeightModifier 1, Male_Body_00, Head_00, Hair_Pompadour, Eye_01, Mouth_01, 피부 Color_002·헤어 Color_009·눈 Color_007·입 Default | 기본 남성 외형 후보 |
| CharacterCustomizeCostumeSet / Female_Archer_01 등 | Top=CustomizePreview_NewbieB_Top_00, Bottom=CustomizePreview_NewbieB_Bottom_00, Shoes=CustomizePreview_NewbieB_Shoes_00 | 단순한 초보자 상하의·신발 조합 참고 |
| CharacterCustomizeDefaultAppearance | Gender·Type·Name별 AdultWeight·ChildWeight 존재 | 단일 고정 외형보다 기본 선택 가중치로 보인다. 실제 사용 알고리즘은 미확인 |
| CharacterPreset | 외형·장비 프리셋 연결, 사내테스트·레벨·치트 등 혼재 | 도구의 초기 상태로 프리셋 전체를 복사하지 말고 외형만 선별 |

- 위 초보자 의상의 InitialEquipmentSheetId는 Clothes_Newbie_Top / Clothes_Newbie_Bottom / Clothes_Newbie_Foot다. 이는 생성 시 초기 장비 참조이며 일반 패션장비 ID라고 확정하지 않는다.
- CustomizePreview_*는 생성 화면 미리보기 항목이다. 신규 사용자 기본 코디로 채택하기 전 일반 Costume 행과의 대응·기본색·실제 조립 가능 여부를 검증한다.
- **추천 후보**: FemaleAppearance_Default_01 외형과 초보자 상하의·신발 스타일. 화려한 직업 의상보다 의상 교체를 확인하기 쉬운 시작점이라는 기획상 제안이며 아직 확정하지 않는다.
- 성인 남녀 두 외형을 기본 프리셋으로 제공하는 대안도 있다. 초기 자동 배치는 하나를 정하고 나머지를 사용자가 고르게 할 수 있다.
- 기술 테스트의 힐러 원피스 모델은 검증용 사례이며 제품 최초 기본값이 아니다.
- 개발 중 임시 프리셋이 필요하면 테스트용임을 구분하고 교체 가능하게 한다.

## 마지막 작업 복원 실패

**제안**
- JSON 손상·지원하지 않는 형식·찾을 수 없는 파츠를 구분한다.
- 손상된 자동 저장 파일을 빈 값이나 기본값으로 즉시 덮어쓰지 않는다.
- 백업 복구, 다른 JSON 불러오기, 새 작업 중 가능한 경로를 안내한다.
- 일부 파츠만 없는 경우에는 사용 가능한 부분을 복원하고 누락 항목을 알린다. 결과가 불완전하다는 안내 없이 정상 복원처럼 취급하지 않는다.
- 새로 시작하거나 불완전 복원을 수용하기 전까지 기존 복원 파일을 보존한다.

## 남은 결정

- 최초 실행의 초기 외형·의상과 선택 화면 유무. 조사한 프리셋은 후보이며 아직 확정하지 않는다.
- 마지막 정상 게임 경로 기억·복구 안내 등 제안의 채택 범위.

## 검수 시나리오

| 시나리오 | 기대 결과 |
|---|---|
| 기본 경로 정상 | 경로 팝업 없이 꾸미기 씬으로 진행 |
| 기본·저장 경로 없음 | 경로 지정 팝업 표시 |
| 잘못된 경로 후 다음 | 원인 안내, 씬 진행 안 함 |
| 폴더 선택 취소 | 기존 입력값 유지 |
| 정상 경로로 변경 후 다음 | 검사 성공 후 진행 |
| 마지막 정상 작업 있음 | 외형·의상·염색 복원 |
| 최초 실행 | 미정인 시작 정책을 따로 결정한 뒤 검수 |
| 자동 저장 JSON 손상 | 기존 파일 보존, 복구 안내 |

## 참고 자료

- [저장·불러오기](SaveLoad.md): 자동 복원·앱 설정 저장 정책.
- [종료 팝업과 입력 우선순위](ExitPopup.md).
- [CharacterPresetAppearance](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Patch/Sheetdata/client/CharacterPresetAppearance.json): 성인 남녀 기본 외형 후보. 이번에 119행의 추출 시트를 확인했다. 실제 최초 선택 알고리즘까지 확인한 것은 아니다.
- [CharacterCustomizeCostumeSet](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Patch/Sheetdata/client/CharacterCustomizeCostumeSet.json): 초보자 생성 미리보기 의상과 초기 장비 연결. 일반 패션장비 ID 대응은 추가 검증한다.
- [CharacterCustomizeDefaultAppearance](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Patch/Sheetdata/client/CharacterCustomizeDefaultAppearance.json): 외형 후보별 AdultWeight·ChildWeight.
- [CharacterPreset](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Patch/Sheetdata/client/CharacterPreset.json): 외형·장비 프리셋 참조. 사내테스트 데이터 혼재에 주의한다.
- [CharacterSimulator 분석 문서](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Docs/CharacterSimulator.md): Costume 기본색, 성별 모델, HidingParts와 원피스 사례 참고.
