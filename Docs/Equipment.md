# 의상 선택·표시·숨겨진 리소스 기획

> 정리일: 2026-11-01
> 확정 사항·제안·미정 표기를 유지한다. 구현 완료를 의미하지 않는다.
> 화면·상태 전환은 [프로그램 플로우](ProgramFlow.md)를 참고한다.

## 선택 UI

**확정**
- 의상 선택에도 외형과 같은 폴드아웃·검색 가능한 드롭다운을 사용한다.
- 큰 카테고리를 열고 닫고, 펼친 카테고리의 세부 부위·항목을 이름으로 검색해 선택한다.
- 아이콘·썸네일 격자 배치는 필수로 제공하지 않는다.
- 카테고리를 접어도 착용 선택·염색·모델은 유지한다.
- 카테고리 묶음·명칭과 부위 배치는 후속 결정한다. 공통 규칙은 [창·꾸미기 UI](WindowAndCustomizationUI.md)를 따른다.

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
- **제안**: 하의 선택 ID·염색값은 보존하고 UI에 상의에 의해 가려짐을 표시한다. 원피스를 일반 상의로 바꾸면 기존 하의를 다시 표시한다.
- 외형에 맞지 않아 원피스 자체가 미적용이면 그 원피스의 하의 숨김 효과도 적용하지 않는 것을 제안한다.
- 원피스 판단은 이름에 원피스가 있는지 검색하는 방식이 아니라 시트·모델의 부위 및 숨김 정보(예: HidingParts)로 한다.
- 상의로 가려진 상태와 외형에 맞지 않아 적용할 수 없는 상태는 구분한다.

## 숨겨진 리소스 선택 옵션

**확정**
- 기본은 패션장비 데이터가 있는 항목을 선택한다.
- 숨겨진 추가 옵션을 켜면 패션장비 시트에 없는 모델 리소스도 선택할 수 있게 한다.
- 옵션의 진입 방식·이름·단축키는 아직 미정이다. 임의의 숨김 키를 확정하지 않는다.

**세부 제안**
- 리소스의 모델 ID·부위·경로 등 확인 가능한 정보를 표시하고 일반 패션장비와 구분한다.
- 가능한 조립용 모델 정의를 중심으로 탐색한다. 존재하는 모든 메시 파일이 캐릭터에 착용 가능한 것은 아니다.
- 정상 조립 불가·호환 불가는 기존 선택 보존과 미적용 안내 원칙으로 처리한다.
- 옵션 OFF는 추가 리소스의 목록 노출을 끄는 것으로 한다. 이미 선택·저장된 리소스를 삭제하거나 JSON을 훼손하지 않는 것을 제안한다.
- 옵션 OFF에서 직접 리소스가 든 JSON을 열면 항목을 보존하고 리소스 선택 사용 중임을 표시한다. 추가 탐색 UI는 계속 숨긴다.
- 정확한 로딩 실패는 정상 호환 불가와 구분하고 기존 정상 장면을 보호한다.

## 남은 결정

- 원피스로 가려진 하의 선택 보존·재표시 제안의 채택.
- 숨겨진 옵션의 진입 방식·이름·단축키.
- 옵션 OFF일 때 기존 직접 리소스 선택·불러오기 처리 제안의 채택.

## 검수 시나리오

| 시나리오 | 기대 결과 |
|---|---|
| 외형 변경으로 의상 미적용 | 선택 ID·염색 보존, 모델 미출력, 선택 필드 빨간색 안내 |
| 호환 외형으로 복귀 | 유지한 의상·염색 다시 표시 |
| 원피스 상의 착용 | 하의 미출력, 선택 유지는 제안 정책 검수 |
| 숨겨진 옵션 ON | 시트 없는 모델 리소스도 탐색·선택 |
| 직접 리소스 코디 저장·복원 | 리소스 참조·색 유지, 일반 패션장비 ID와 구분 |

## 참고 자료

- [Costume 시트](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/Costume.json): 부위·조건·모델 배열·숨김을 직접 확인했다.
- [CostumeModelVariant](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CostumeModelVariant.json): 외형별 예외 모델. align=approx에 주의한다.
- [오브젝트 머리 모델](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/StageObjectModels/ModelParts/Helmet/Male_ObjectHead_Helmet_01.StageObjectModel.sjon.json): 머리·헤어·눈·눈썹·액세서리 숨김.
- [힐러 원피스 모델](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/Starter/StageObjectModels/ModelParts/OnePiece/Female_Healer_OnePiece_00.StageObjectModel.sjon.json): 실제 점유 부위·접합·염색 연결.
- [슬롯 충돌 처리 선언](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Dump/Shared/Shared.Characters.cs): AppearanceSlotConflictResolver. 선언만으로 알고리즘 완료를 판단하지 않는다.
- [시험 조립기](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Scripts/UnityMMSim/Runtime/Install/MMInstallAssembler.cs): 모델 배열·기본 헤어 단계 처리의 현재 범위.
- [저장·불러오기](SaveLoad.md): 부위별 패션장비 ID와 직접 리소스 식별.
- [염색](Dye.md): 의상 교체 시 기본색 초기화.
- [언팩 분석 문서](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/main/Docs/CharacterSimulator.md): Costume의 HidingParts·모델 연결.

