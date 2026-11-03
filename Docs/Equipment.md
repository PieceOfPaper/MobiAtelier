# 의상 선택·표시·숨겨진 리소스 기획

> 정리일: 2026-11-03
> 확정 사항·제안·미정 표기를 유지한다. 구현 완료를 의미하지 않는다.
> 화면·상태 전환은 [프로그램 플로우](ProgramFlow.md)를 참고한다.

## 선택 UI

**확정**
- 의상 선택에도 외형과 같은 폴드아웃·검색 가능한 드롭다운을 사용한다.
- 큰 카테고리를 열고 닫고, 펼친 카테고리의 세부 부위·항목을 이름으로 검색해 선택한다.
- 아이콘·썸네일 격자 배치는 필수로 제공하지 않는다.
- 카테고리를 접어도 착용 선택·염색·모델은 유지한다.
- **확정 (사용자 결정 2026-11-02)**: 의상은 "의상" 폴드아웃 하나로 묶는다. 순서는 모자 → 상의 → 하의 → 로브 → 장갑 → 신발 → 귀장식 → 눈장식 → 얼굴장식 → 무기다. 공통 규칙은 [창·꾸미기 UI](WindowAndCustomizationUI.md)를 따른다.

## 언팩 데이터의 착용·표시 규칙 검토

> 검토일: 2026-11-01. MabinogiMobileUnpack 커밋 `46e94a82112243850d68a39cde73890de016140d` 기준.
> 시트·모델 정의·덤프 선언·시험 조립기 코드를 읽었다. 이번에 Unity를 실행하거나 모든 의상을 조립하지 않았다.

**방향**
- 착용 부위·호환 모델·다른 부위 숨김은 원본 데이터에 있는 규칙을 해석한다. 동일한 규칙을 의상 이름별로 새로 기획하지 않는다.
- 선택 ID·염색 보존, 목록 노출과 오류 안내는 도구의 UX 정책이다. 원본 데이터의 표시 규칙과 구분한다.
- 데이터에 필드가 있다는 것과 적용 알고리즘을 전부 해석했다는 것은 다르다. 미해석 부분은 추가 분석·구현으로 관리한다.

### 시트에서 직접 확인한 규칙

Costume 시트는 `align=exact`, 1,679행이다.

| 구분 | 확인한 근거 | 도구의 해석 방향 |
|---|---|---|
| 선택 부위 | CostumeType: Hat·Top·Bottom·Shoes·Gloves·Robe·EarDeco·EyeDeco·FaceDeco·Weapon | UI 분류·부위 선택의 데이터 기준으로 삼는다. 구체적인 묶음·명칭은 UI 기획 영역이다 |
| 성별 | EquippableGender와 Male/FemaleModelName 배열 | 성별 조건과 해당 모델 존재를 함께 확인한다 |
| 어린 외형 | Boy/GirlModelName 배열 | 실제 외형 모델 구분에 맞춰 해석한다. EquippableAgeType은 이번 1,679행 모두 None이므로 이것만으로 전 연령 지원을 판단하지 않는다 |
| 다른 의상 숨김 | HidingParts 배열 | 실제 적용되는 의상의 숨김 정보를 사용한다. 원피스만 따로 하드코딩하지 않는다 |
| 외형별 예외 | ExceptionModelName → CostumeModelVariant | 머리형·헤어 등의 조건별 대체 모델을 해석한다. 예외 참조가 있는 Costume은 39행이다 |
| 무기 조건 | EquippableCombatJobs | 제한이 있는 162행은 모두 Weapon이다. 도구에서는 직업 제한을 적용하지 않고 전체 무기 외형을 자유롭게 선택한다 |
| 여러 모델 | 성별 모델 필드는 배열 | 검+방패, 칼+칼집 등 한 선택에서 여러 모델을 조립한다 |

- HidingParts가 비어 있지 않은 행은 351개다. 모두 Bottom을 포함하며, 그중 Bottom 하나만 숨기는 행은 329개다.
- Top·Bottom을 함께 숨기거나 Gloves·Shoes·Hat까지 숨기는 조합도 있다. 실제 배열을 읽어 처리한다.
- 예: Fashion_Healer_OnePiece_F는 CostumeType=Top, HidingParts=[Bottom], EquippableGender=Female이다. 남성 모델 배열은 비어 있고 여성·어린 여성 모델 참조가 있다.
- 예: Fashion_HighWarrior_Weapon_06은 검·방패 모델을 함께 참조하며 HighWarrior 제한이 있다. Fashion_SwordMaster_Weapon_06은 칼·칼집 모델을 함께 참조한다.
- CostumeModelVariant는 132행이며 `align=approx`다. 머리형별 안경 예외 참조는 확인했지만 전체 조건 해석·우선순위를 검증한 것으로 쓰지 않는다.

### 모델 리소스·실행 로직의 규칙

| 구분 | 확인한 근거 | 확인 수준 |
|---|---|---|
| 모델의 점유 부위 | ModelPartInfo.AppearanceSlot | 힐러 원피스는 시트에서 Top이지만 모델에서는 OnePiece다. UI 선택 부위와 실제 점유 부위를 구분한다 |
| 모자에 따른 숨김 | HideHair·HideHead·HideAccessory0~2, 눈·눈썹 숨김과 HelmetCover 조건 필드 | 오브젝트 머리 모델에서 활성 플래그를 직접 확인했다. 구형 HideEye와 좌우 분리 필드가 혼재하므로 모델 스키마를 따른다 |
| 모자·헤어 단계 | 모자의 C 접미사, 헤어의 H0~H5와 헤어 단계 처리 메서드 | 데이터·선언은 확인했다. 단순히 같은 번호 하나만 남기는 규칙 전체가 검증된 것은 아니다 |
| 슬롯 충돌 | AppearanceSlotConflictResolver의 IsConflicted·AddOccupyingSlots·HasConflict | 덤프에 선언이 있다. 메서드 본문은 비어 있어 복합 슬롯의 충돌 순서·우선순위는 추가 해석해야 한다 |
| 관통·접합 | ClothingUnit, JunctionPointInfo, 치마 본 보정 등 | 데이터·기존 분석 근거가 있다. 부위 숨김과 별도이며 전체 조합의 맞춤 알고리즘은 검증되지 않았다 |

- 현재 시험 조립기는 기본 hairLevel=H0로 헤어를 선택한다. 완전한 모자·후드 표시 규칙을 구현한 상태가 아니다.
- 표시할 의상 집합을 결정한 후 그 집합으로 피부 관통 제거·접합을 계산해야 한다. 미적용 의상의 숨김 효과나 가려진 의상의 몸 자르기를 무조건 적용하지 않는다. 세부 평가 순서는 구현에서 검증한다.
- 따라서 원본 착용 규칙은 기획 공백보다 해석·구현·검증 과제로 관리한다.

## 무기 외형 자유 선택

**확정**
- 직업 선택을 요구하지 않고 전체 무기 외형을 자유롭게 탐색·선택하게 한다.
- EquippableCombatJobs를 목록 필터·선택 잠금·미적용 사유로 사용하지 않는다. 원본 게임의 착용 조건을 파악하는 참고 정보로 둔다.
- 검+방패·칼+칼집 등 선택한 항목의 모델 배열은 함께 조립한다.
- 직업 제한을 풀어도 모델 존재·실제 조립 가능 여부는 확인한다. 원본 본 연결·장착점·숨김 규칙은 별도로 해석한다.
- 무기 선택을 위해 직업 필드를 저장 JSON에 새 필수값으로 추가하지 않는다.
- 장착점(넣음·꺼냄)과 직업별 자세는 [무기 자세](WeaponPose.md)를 따른다. EquippableCombatJobs 는 자세 직업의 자동값으로만 쓴다(2026-11-03).

## 선택 상태와 실제 출력의 분리

**확정**
- 외형 변경으로 입힐 수 없어진 의상을 자동 해제하거나 다른 의상으로 대체하지 않는다.
- 해당 패션장비 ID와 사용자 염색값을 유지하되 모델은 출력하지 않는다.
- 선택 필드의 의상명을 빨간색으로 표시하고 적용되지 못한다는 안내를 붙인다.
- **제안**: 색만으로 상태를 구분하지 않도록 미적용 표시와 사유(성별·체형에 맞는 모델 없음 등)를 함께 제공한다.
- 호환 가능한 외형으로 돌아오면 유지한 선택·염색으로 다시 출력한다.
- UI와 JSON은 선택 상태를 보존하고, 렌더러는 현재 외형에서 출력 가능한 결과만 만든다.

## 원피스와 하의

**확정**
- 원피스를 별도 착용 부위로 만들지 않고 상의로 선택한다.
- 원피스 상의가 실제 적용되면 하의는 입지 않은 것처럼 출력하지 않는다.
- **확정**: 하의 선택 ID·염색값은 보존하고 UI에 상의에 의해 가려짐을 표시한다. 원피스를 일반 상의로 바꾸면 기존 하의를 다시 표시한다.
- 외형에 맞지 않아 원피스 자체가 미적용이면 그 원피스의 하의 숨김 효과도 적용하지 않는다.
- 원피스 판단은 이름에 원피스가 있는지 검색하는 방식이 아니라 시트·모델의 부위 및 숨김 정보(예: HidingParts)로 한다.
- 상의로 가려진 상태와 외형에 맞지 않아 적용할 수 없는 상태는 구분한다.

## 공개 범위와 에디터 전용 리소스

**확정 (2026-11-02)**
- 일반 빌드에는 공개 대상인 시트 항목만 제공한다. 숨겨진 데이터와 패션장비 시트에 없는 직접 모델 리소스는 Unity 에디터에서만 탐색·선택·출력한다.
- 개발용 노출·선택·직접 리소스 로딩 진입점은 `#if UNITY_EDITOR`로 제한한다. 일반 빌드의 설정·숨김 단축키·Development Build로 활성화하는 우회 경로를 두지 않는다.
- 에디터에서는 숨김 항목과 직접 리소스도 같은 목록에서 확인할 수 있게 한다. 개발용 목록에는 아래 ID 표기와 숨김 표시를 적용한다.
- 공개 범위는 일반 선택뿐 아니라 자동 복원·JSON 가져오기·실행 취소·초기 외형의 최종 조립에도 동일하게 적용한다. 목록에서만 숨기고 JSON으로 출력할 수 있게 만들지 않는다.
- 빌드에서 제한 항목이 든 파일을 열면 제한 사유를 안내한다. 선택 ID·색상은 미적용 상태로 보존하고 모델은 출력하지 않는다. 부분 복원 승인과 원본 보존은 SaveLoad.md를 따른다. 필수 외형은 공개된 호환 기본값으로 표시한다.
- 에디터 여부·숨김 판정은 파일이 주장하는 값을 신뢰하지 않고 현재 데이터와 실행 환경에서 재평가한다.

### 데이터의 숨김 판정 근거

2026-11-02, MabinogiMobileUnpack 커밋 `46e94a82112243850d68a39cde73890de016140d`의 추출 시트를 직접 확인했다.

| 대상 | 확인한 필드·범위 | 처리 |
|---|---|---|
| 외형 시트 | CharacterHead·Hair·FaceTattoo·BodyTattoo의 `HideOnCustomize` | true인 항목을 숨김으로 분류한다. Eyes·Mouth 등 같은 필드는 시트별 스키마를 확인해 적용한다 |
| 해금 조건 | `UnlockedByRebirth`, `UnlockedByConsumable` | 구매·해금 조건이며 숨김 플래그로 대체 사용하지 않는다. 공개 항목은 잠그지 않는다 |
| 의상 Costume | `MarketCollectionDiscloseTime`과 `SalesStartTime`. 현재 시트에는 `HideOnCustomize` 같은 직접 숨김 플래그가 없다 | 도감 공개 시각이 미래인 의상은 보수적으로 에디터 전용으로 분류한다. 이는 도구의 노출 정책이며 게임 전체의 숨김 알고리즘을 확인했다는 뜻은 아니다 |
| 시트에 없는 모델 | Costume·CostumeModelVariant에 직접 연결되지 않는 조립용 모델 정의 | 에디터 전용 직접 리소스로 분류한다. 미사용·미출시라고 단정하지 않는다 |

- `HideOnCustomize=true` 확인 행 수: CharacterHead 1/7, CharacterHair 32/484, CharacterFaceTattoo 4/41, CharacterBodyTattoo 3/27이다. 헤어 수치는 체형별 변형을 포함한 행 수다.
- CharacterCustomizeEyeDeco 6행에는 직접 숨김 필드가 없다. 실제 참조하는 Costume과 프리셋을 해석해 노출 판정을 연결한다.
- `MarketCollectionDiscloseTime`은 명시된 TimeZone(KST·UTC)을 해석해 현재 시각과 비교한다. `SalesStartTime`은 판매 시작 정보이므로 이것만으로 숨김·공개를 확정하지 않는다.
- 날짜가 지났거나 숨김 필드가 없다는 사실만으로 인게임 실제 노출을 검증했다고 쓰지 않는다. 필드 해석 실패·참조 대상 미확인은 공개 후보에서 제외하고 에디터에서 원인을 확인한다.
- 같은 논리 후보의 변형 행은 현재 외형에 대응하는 행으로 판정한다. 공개되지 않은 변형을 공개 변형 대신 조립하지 않는다.
- `HidingParts`, HideHair 등의 착용 중 가림, 성별·나이 호환 불가는 목록의 숨김 데이터 판정과 다르다.
- 추가로 명확한 노출 제어 필드를 찾으면 시트별 의미와 참조 관계를 검증한 뒤 판정에 추가한다. 이름에 임시·테스트가 있다는 이유만으로 확정 판정을 만들지 않는다.

### 아이템 이름 표기

**확정**
- 목록·선택 필드·상세 UI에서 `데이터ID - 표시 이름` 형식을 사용한다. 숨김 항목은 맨 앞에 `[숨김] `을 붙인다.
- 예: `10101 - 무슨무슨치마`, `[숨김] 10101 - 무슨무슨치마`. 예시 숫자를 실제 데이터 ID로 간주하지 않는다.
- Costume은 실제 `Id`를 사용한다. 문자열 ID라면 `Fashion_NewbieSword_Bottom - 트레이닝 코튼 팬츠`처럼 표시한다. `Index`, 배열 순번, 런타임 심볼 번호를 ID 대신 쓰지 않는다.
- 그룹형 외형은 `GroupId`, 일반 외형은 `Id`, 직접 리소스는 모델 ID를 쓴다. 이름이 없는 직접 리소스는 확인 가능한 상대 경로를 보조 표기로 사용한다.
- 직접 리소스도 에디터 전용 항목이므로 `[숨김]`을 붙인다. 미적용·다른 의상에 가림 표시는 숨김 접두사와 별도로 제공한다.
- 검색은 표시 이름과 실제 데이터 ID의 부분 일치를 지원한다. 내부 변형 행을 별도 아이템으로 중복 나열하지 않는다.

## 남은 검증

- Costume에 게임 전체의 공개 여부를 나타내는 별도 참조가 있는지, 도감 공개 시각의 게임 내 사용처.
- 얼굴·눈·입·꾸밈의 실제 참조에 따른 숨김 판정 연결. 숨김 여부를 완전히 해석했다고 쓰지 않는다.

## 검수 시나리오

| 시나리오 | 기대 결과 |
|---|---|
| 외형 변경으로 의상 미적용 | 선택 ID·염색 보존, 모델 미출력, 선택 필드 빨간색 안내 |
| 호환 외형으로 복귀 | 유지한 의상·염색 다시 표시 |
| 원피스 상의 착용 | 하의 미출력, 선택·염색 보존 후 일반 상의로 복귀 시 재표시 |
| 에디터에서 숨김·직접 리소스 확인 | `[숨김] 데이터ID - 이름` 표기, 탐색·선택 가능 |
| 일반·Development 빌드 | 숨김·직접 리소스 탐색·출력 불가, 설정·JSON으로 우회 불가 |
| 이름 또는 ID 검색 | 실제 이름·ID로 같은 항목 탐색 가능 |
| 직접 리소스 코디 저장·복원 | 에디터에서는 출력, 빌드에서는 참조·색 보존 및 제한 안내 후 미출력 |

## 참고 자료

- [Costume 시트](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/Costume.json): 부위·조건·모델 배열·숨김을 직접 확인했다.
- [CostumeModelVariant](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CostumeModelVariant.json): 외형별 예외 모델. align=approx에 주의한다.
- [오브젝트 머리 모델](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/StageObjectModels/ModelParts/Helmet/Male_ObjectHead_Helmet_01.StageObjectModel.sjon.json): 머리·헤어·눈·눈썹·액세서리 숨김.
- [힐러 원피스 모델](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/Starter/StageObjectModels/ModelParts/OnePiece/Female_Healer_OnePiece_00.StageObjectModel.sjon.json): 실제 점유 부위·접합·염색 연결.
- [슬롯 충돌 처리 선언](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Dump/Shared/Shared.Characters.cs): AppearanceSlotConflictResolver. 선언만으로 알고리즘 완료를 판단하지 않는다.
- [시험 조립기](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Scripts/UnityMMSim/Runtime/Install/MMInstallAssembler.cs): 모델 배열·기본 헤어 단계 처리의 현재 범위.
- [내보내기·가져오기](SaveLoad.md): 부위별 패션장비 ID와 직접 리소스 식별.
- [염색](Dye.md): 의상 교체 시 기본색 초기화.
- [언팩 분석 문서](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Docs/CharacterSimulator.md): Costume의 HidingParts·모델 연결.
- [CharacterHair](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterHair.json), [CharacterHead](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterHead.json): HideOnCustomize·해금 조건 확인.
