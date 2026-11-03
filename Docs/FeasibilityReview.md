# 언팩 데이터 기반 구현 가능성 검토

> 검토일: 2026-11-01
> 보완: 같은 날 초기색을 재검토하고, 자유 RGB 편집·최초 외형 무작위 선택의 사용자 확정 사항을 반영했다.
> 보완: 2026-11-02 키·나이 .bdc 보간식 확인·적용을 반영했다.
> 기획 보정(2026-11-03): 접이식 패널·시스템 메뉴·옵션·꾸미기 전용 이력과 Export/Import 명칭은 최신 항목별 기획이 우선한다. 이 보정은 Unity 재실행·데이터 재검증 결과가 아니다.
> 기획 기준: MobiAtelier 커밋 316e14e0bde2527d0552e81bac99e4902f087497
> 데이터·코드 기준: MabinogiMobileUnpack 커밋 46e94a82112243850d68a39cde73890de016140d
> 게임 결과물 버전: 설치 3.4.40011 / 패치 R840cbf8_A4525d21 (version.json 기준)
> 범위: 현재 항목별 기획서와 추출 시트·모델 정의·C# 런타임 로더·테스트 기록을 대조했다. 이번 검토에서 Unity·게임 설치본을 실행하거나 모든 의상 조합을 렌더링하지 않았다.

## 1. 결론

현재 기획은 Windows 설치본 기반으로 구현을 진행할 수 있다. 핵심 모델·본·염색·시트 데이터가 있고 대표 조합의 Windows 실행 기록과 C# 코드가 있다.

다만 다음을 구분한다.

- **코드·데이터 확인**: 이번에 파일 내용을 직접 읽어 확인했다.
- **기존 실행 기록**: 언팩 저장소 문서의 이전 테스트 결과다. 이번에 재실행한 결과가 아니다.
- **추가 구현**: 데이터가 있어도 제품 기능은 아직 구현해야 한다.
- **추가 분석**: 얼굴 맞춤·의상 접합 등 데이터 해석이나 정확한 동작 검증이 더 필요하다.

기획 전체를 폐기하거나 WebGL 방식으로 돌아갈 이유는 발견하지 못했다. 반대로 모든 외형·의상·직접 리소스가 현재 로더에서 완전하게 출력된다고 판단할 근거도 없다.

## 2. 기획서별 판정

| 기획서 | 판정 | 근거·남은 작업 |
|---|---|---|
| [프로그램 플로우](ProgramFlow.md) | 구현 가능 | 앱 상태 관리이며 게임 전용 정보 불필요. 읽기·조립 실패의 복구 흐름 구현 |
| [시작 설정](Startup.md) | 기반 코드 있음, 추가 구현 | PackageIndex·BlobReader·ResourceCodec·Sjon·SmmReader 존재. 필수 파일 검사·폴더 선택·버전 확인 추가 |
| [창·꾸미기 UI](WindowAndCustomizationUI.md) | 앱 구현으로 가능 | 창·패널 크기와 검색은 데이터 분석의 장애가 아님. 표시 이름·아이콘은 시트와 번들에서 공급 |
| [의상·리소스](Equipment.md) | 데이터 있음, 규칙 구현 필요 | CostumeType·성별 모델·HidingParts·예외 모델. 선택 보존과 실제 출력 분리, 머리·헤어 숨김, 임의 리소스 검증 필요 |
| [염색](Dye.md) | 대표 사례 기반 있음, 예외 처리 필요 | DyeColor0~5 + DyeInfo + SetDye. 원본 초기색 유지·시트 기본색 덮어쓰기·여러 파츠의 같은 Slot 이름 처리 필요 |
| [카메라](Camera.md) | 앱 구현으로 가능 | 기획상 버튼 매핑 구현 가능. 현재 테스트 뷰어는 요구 조작과 다르므로 교체 필요 |
| [내보내기·가져오기](SaveLoad.md) | 구현 가능 | ID·색·카메라 상태를 앱 JSON으로 보존. 현재 C# 로더의 시트 ID 해석은 추가해야 함 |
| [실행 취소](UndoRedo.md) | 앱 구현으로 가능 | 선택 ID·색상 상태를 명령 또는 스냅샷으로 보관. 기존 코드는 이력 기능이 아님 |
| [시스템 메뉴](SystemMenu.md) | 앱 구현으로 가능 | 게임 데이터 불필요. Windows 창 종료 요청·저장·ESC 우선순위 구현 |
| [촬영모드](PhotoMode.md) | 일부 기반 있음, 세부 범위별 판정 | 캡처 코드는 존재. 포즈·표정·배경 전체 재현은 후속 분석 |

이 판정은 구현 가능성이다. MobiAtelier에 해당 기능이 완료돼 있다는 의미가 아니다.

## 3. 시트에서 직접 확인한 근거

### 3.1 패션장비 ID 저장과 선택 목록

Costume 추출 시트는 align=exact, 1,679행이다. 다음 필드를 직접 확인했다.

| 기획 기능 | 필드 |
|---|---|
| 저장·재조회 | Id, DisplayName |
| 부위별 목록 | CostumeType |
| 성별·연령별 모델 | MaleModelName[], FemaleModelName[], BoyModelName[], GirlModelName[] |
| 착용 조건 | EquippableGender, EquippableAgeType, EquippableCombatJobs[] |
| 기본 염색 | DyeColor0~5 (Nullable<Color32>) |
| 다른 부위 숨김 | HidingParts[] |
| 아이콘 | MaleIconPath, FemaleIconPath |
| 외형별 예외 | ExceptionModelName[] → CostumeModelVariant |

- 저장 기준을 Costume.Id로 삼는 기획은 적합하다.
- 추출 JSON의 Id는 심볼을 문자열로 풀어 놓은 결과다. 설치본의 시트를 직접 읽을 때는 심볼 테이블과 스키마를 이용해 해시·문자열을 연결해야 한다.
- 원시 Index나 배열 순번을 저장 ID로 대신 사용하지 않는다.
- 모델 필드는 배열이다. 예: Fashion_HighWarrior_Weapon_06은 검과 방패, Fashion_SwordMaster_Weapon_06은 칼과 칼집 모델을 함께 참조한다. 장비 1개가 렌더러·모델 여러 개를 만들 수 있어야 한다.
- 현재 Costume의 EquippableAgeType은 1,679행 모두 None이었다. 따라서 이 필드만으로 어린 캐릭터 지원 여부를 결정하지 않는다. 모델 배열·외형 시트·예외 규칙까지 확인한다.
- CostumeModelVariant는 132행이며 align=approx다. PartData 해석을 모든 항목에 대해 확정한 것으로 보지 않는다.

### 3.2 원피스는 상의, 하의는 숨김

직접 확인한 행:

| 항목 | 값 |
|---|---|
| Id | Fashion_Healer_OnePiece_F |
| 표시 이름 | 힐링 핸드 유니폼 (여성용) |
| CostumeType | Top |
| HidingParts | Bottom |
| EquippableGender | Female |
| FemaleModelName | Female_Healer_OnePiece_00 |
| MaleModelName | 빈 배열 |
| GirlModelName | Infant_Healer_F_OnePiece_00 |

사용자의 원피스 기획과 데이터가 일치한다. 외형 변경으로 남성 모델이 필요한 경우에는 이 항목을 선택 상태로 유지하면서 미출력·빨간색 안내를 할 수 있다.

다만 숨김 규칙은 원피스만의 문제가 아니다.

- HidingParts가 정확히 Bottom 하나인 행: 329개.
- Bottom을 포함하는 행 전체: 351개.
- Top·Bottom·Gloves·Shoes·Hat 등을 함께 숨기는 조합도 있다.
- 따라서 이름의 OnePiece 여부가 아니라, 실제 적용된 장비의 HidingParts로 다른 부위를 평가해야 한다.
- HidingParts는 의상 렌더러 숨김 정보다. 피부 관통 제거와는 별도 기능이다.

### 3.3 외형 선택과 ID 해석

| 시트 | 행 수 | 확인 내용 |
|---|---:|---|
| CharacterHead | 7 | 얼굴 ID, 연령 제한, 성별 모델, 숨김 여부 |
| CharacterHair | 484 | GroupId + BodyType, 성별 모델, 염색 가능 여부·고정색 |
| CharacterEyes | 482 | GroupId + HeadType, 성별 모델, 색 변경 가능 여부 |
| CharacterMouth | 224 | GroupId + HeadType, 성별 모델 |
| CharacterBodyTypes | 4 | Default, Thin, Fat, Muscular 및 BlendshapeName |
| CharacterPresetAppearance | 119 | 성별·나이·체형·얼굴·헤어·각 색의 완성된 조합 |

- 헤어·눈·입은 화면에 같은 이름이 보이는 행을 그대로 나열하면 중복된다. 논리 선택 ID를 기준으로 묶고 현재 체형·머리형에서 행을 해석한다.
- 모델 값에는 빈 배열뿐 아니라 Invalid 문자열도 있다. 예: CharacterHead의 특정 성인 전용 얼굴에 다른 성별 모델이 Invalid다.
- 데이터에 모델이 없다는 이유로 선택값을 지울 필요는 없다. 선택과 실제 출력 결과를 분리하면 된다.
- CustomizeRestriction 시트는 이번 데이터에서 0행이다. 이를 전체 호환성 판정의 완전한 표로 사용할 수 없다.

## 4. 현재 구현과 기획 사이의 핵심 차이

### 4.1 시트 리더가 가장 먼저 필요하다

MMInstallReaders.cs에는 패키지·blob·SJON·SMM 리더가 있다. 하지만 Costume 등 게임 데이터 시트의 C# 로더는 아직 없다.

MMInstallAssembler.cs의 AssemblySpec.costumes는 현재 패션장비 ID가 아니라 모델 이름 배열이다. slotColors도 외부 인자로 받는다.

제품에서는 다음 단계가 추가돼야 한다.

1. 설치본에서 필요한 Costume·외형 시트 로딩.
2. 심볼 테이블로 ID, 문자열 테이블로 표시 문자열 해석.
3. 패션장비 ID → 조건·예외 모델·모델 배열·기본 염색·숨김 정보 해석.
4. 해석 결과를 기존 조립기에 전달.

이식을 참고할 코드: convert_sheetdata.py와 sheet_schema.json. 스키마는 GameAssembly 버전에 묶이므로 지원 버전을 확인해야 한다.

**기획상 보완**: 시트 없이 모델 이름만으로 실행하는 진단 모드는 가능하지만, 일반 꾸미기 기능은 장비 ID·이름·기본색을 요구한다. 시트 로딩 실패를 정상 일반 모드로 조용히 통과시키지 말고 복구 안내 또는 명시적인 제한 모드로 다룬다.

### 4.2 시트 기본 염색값과 의상의 원본 초기색은 다르다

이전의 “기본색이 없는 의상도 있다”는 표현을 정정한다. Costume에서 DyeColor0~5가 모두 null인 행은 97개지만, 이는 시트의 별도 기본 염색값이 미지정이라는 의미다. 원본 텍스처·머티리얼의 초기색이 없다는 근거가 아니다.

다시 직접 확인한 사례:

| 데이터 | 확인 결과 |
|---|---|
| Costume / Fashion_CreativePrint_Top_00 | DyeColor0~5 모두 null, Male_CreativePrint_Top_00 참조 |
| Male_CreativePrint_Top_00.mat.json | _BaseMap 텍스처 참조, _TintColor=(1,1,1,1), _DyeColor1~3 색상 속성 존재 |
| Human/Texture | Male_CreativePrint_Top_00.tga.png 존재 |
| 같은 모델의 StageObjectModel | DyeInfo에는 Skin 연결만 존재. 머티리얼에 _DyeColor 속성이 있다는 사실만으로 의상용 염색 영역이 활성화됐다고 판단하지 않음 |
| MMInstallAssembler.cs | new Material(m)으로 원본 재질 복사. slotColors에 값이 있을 때만 연결된 색상 속성을 덮어쓰고 GetColor(prop)로 현재 기본값 기록 |

따라서 원본 초기 상태를 보존할 데이터와 로딩 경로가 있다. 개별 의상의 정확한 최종 발색은 셰이더·조명까지 포함해 렌더링 검증해야 한다. 위 표는 파일·코드 확인이며 이번에 재렌더링한 결과는 아니다.

**확정된 초기화 규칙**

1. 원본 머티리얼·텍스처 상태를 읽는다.
2. Costume.DyeColor0~5 중 null이 아닌 값만 실제 DyeInfo 연결에 적용한다.
3. null인 연결은 원본 값을 유지한다. 검정·흰색 등으로 임의 대체하지 않는다.
4. JSON·자동 저장·실행 취소 복원에서는 그 위에 저장된 사용자 색을 적용한다.

**확정된 자유 색상 정책**

- 컬러피커·#RRGGBB 입력으로 임의의 RGB 색을 허용한다. 게임 팔레트·색상 해금·CanChangeColor·고정색 정책으로 색상 입력을 잠그지 않는다.
- 게임의 정책상 제한과 실제 셰이더·머티리얼 반영 지점은 별개다. 원본 연결이 부족한 대상은 추가 연결·셰이더 구현이 필요하다. 색상 선택 자유가 모든 텍스처 영역의 즉시 염색 가능성을 증명하지는 않는다.
- 시트 필드는 Color32 RGBA이며 #RRGGBBAA 형태도 존재한다. UI의 #RRGGBB 입력은 확정이며 원래 알파 보존·저장 규칙은 별도로 구체화한다.

### 4.3 여러 옷의 Slot0는 같은 편집 대상이 아니다

현재 MMSimCharacter.SetDye(slot, color)는 전체 dyes 중 같은 슬롯 이름을 모두 변경한다. 단일 원피스 시험에서는 쓸 수 있지만 상의·하의·신발에 각각 Slot0가 있으면 모두 같이 바뀔 수 있다.

제품의 염색 대상은 최소한 **선택한 파츠 인스턴스/부위 + 염색 슬롯**으로 식별해야 한다. 한 파츠 안의 같은 슬롯이 여러 메시·머티리얼로 연결된 것은 함께 바꾸되, 다른 의상의 같은 슬롯은 별도로 편집한다.

의상 교체 기본색 초기화와 JSON·취소 복원 시 사용자 색 유지 규칙도 이 식별 기준으로 구현한다.

### 4.4 얼굴·키·나이는 완성된 런타임 기능이 아니다

**2026-11-01 기획 범위 추가**: [외형 기획](Appearance.md)에서 사용자 제공 환생 화면의 성별·체형·키·나이·피부색·헤어·눈·입·얼굴형·얼굴꾸밈·눈장식·몸꾸밈을 모두 지원 대상으로 확정했다. 헤어·눈의 색상 편집도 포함한다. 추가로 CharacterFaceTattoo·CharacterBodyTattoo의 성별/어린 외형별 모델 참조와 CharacterCustomizeEyeDeco의 의상 보상 프리셋 참조를 확인했다. 보상 프리셋의 추출 필드 의미·선택 중첩·실제 조립·색상 연결은 추가 검증이 필요하다. 미검증을 이유로 필수 지원 범위를 축소하지 않는다. 키·나이의 범위·단위도 추가 확인한다.

- 머리·몸·헤어·원피스의 조립 시험 기록은 있다.
- 눈·입·눈썹은 기존 시험에서 제외됐다. 관련 모델 시트가 있어 구현 근거는 있지만 얼굴 완성에는 추가 조립·셰이더 처리가 필요하다.
- 체형 블렌드셰이프 변경은 시험됐다.
- 키·나이의 .bdc 보간식은 확인해 적용했다(2026-11-02, PROGRESS 인계 메모). 얼굴 Adolescent 가중치 등은 미확인 항목이다.
- 얼굴형별 헤어·모자 맞춤용 .FacialMorph.bytes는 미해석이다.
- 모자에 따른 헤어 H0~H5와 HideHair/HideHead 등의 규칙은 데이터 근거가 있지만 현재 조립기는 기본 헤어 단계 선택 중심이다.

따라서 최초에는 검증된 성인·기본 키와 체형 범위에서 완성도를 확인하고, 나이·키·얼굴형 조합을 차례로 확장한다. 이를 제품 지원 범위로 확정하려면 별도 결정한다.

### 4.5 숨김과 관통 제거를 구분한다

선택 ID 보존·미출력·빨간색 UI는 앱 상태 관리로 구현할 수 있다.

반면 몸과 옷의 자연스러운 접합은 .ClothingUnit.bytes와 맞춤 알고리즘에 의존한다. 현재 BodyCut은 기본 상태의 몸 정점과 의상 면 사이 레이 검사에 의한 근사다.

- 상하의·장갑·신발 조합이나 체형 변경에 대해 전체 품질이 확인된 것은 아니다.
- 새 의상을 추가하거나 숨기면 실제 적용되는 의상 집합으로 몸 가리기를 재계산해야 한다.
- 미적용 원피스가 하의까지 숨기는 오류, 가려진 하의가 몸 자르기에 참여하는 오류를 피한다.
- 정확도가 부족하면 ClothingUnit 해석 또는 지원 조합별 근사 보완이 필요하다.

## 5. 에디터 전용 추가 리소스의 실제 근거

> 2026-11-02 정책 갱신: 아래 탐색은 `UNITY_EDITOR`에서만 제공한다. 일반 빌드에는 추가 리소스 옵션을 노출하지 않는다. 숨김 필드·공개 시각에 따른 목록 제한과 ID 표기는 [의상 기획](Equipment.md)을 따른다.

직접 확인한 예: Female_Acheron_Top_00.

- StageObjectModel 파일이 있고 AppearanceSlot=Top, ClothingSetCategory=Female, 프리팹 경로·본 목록·DyeInfo가 있다.
- Costume 1,679행의 성별별 모델 배열과 CostumeModelVariant의 성별 모델 필드에 이 이름이 직접 등장하지 않았다.
- 따라서 패션장비 목록에 직접 연결되지 않는 모델을 찾는 기능에는 실제 대상이 있다.
- 다른 데이터(일반 전투 장비 등)에서 참조되지 않는다는 의미는 아니다. 단순히 미사용 리소스라고 부르지 않는다.
- 이 모델의 DyeInfo에는 Skin 슬롯만 있었다. 모든 직접 리소스에 의상용 Slot0~5가 있다고 가정하면 안 된다.

권장 탐색 기준:
- Costume와 예외 모델의 참조 집합을 먼저 만든다.
- ModelParts의 조립용 모델 정의에서 직접 참조되지 않는 후보를 찾는다.
- 부위·스켈레톤·프리팹·메시 존재를 확인한 뒤 선택 대상으로 노출한다.
- 코드의 InstallSource.ModelNames는 현재 전체 모델 정의를 이름으로 수집하므로 필터 기반으로 활용할 수 있다.
- 이번 Top 폴더 목록은 GitHub 응답이 1,000개 항목까지였다. 전체 숨겨진 리소스 개수나 전수 조립 가능 수를 산정하지 않았다.
- 모델 ID·상대 경로로 저장하는 예외 참조 구조는 타당하다. 실제 식별 키와 동명 충돌은 구현 시 확인한다.

## 6. 최초 모델 후보의 추가 확인

이전 Startup.md에서 미확인으로 둔 부분이 일부 해소됐다.

### 생성 미리보기 후보도 Costume 행이 있다

| Costume.Id | 여성 모델 | 기본색 일부 |
|---|---|---|
| CustomizePreview_NewbieB_Top_00 | Female_NewbieB_Top_00 | #FBF8EFFF, #834A3FFF |
| CustomizePreview_NewbieB_Bottom_00 | Female_NewbieB_Bottom_00 | #76A5B5FF, #965B19FF, #FBF8EFFF |
| CustomizePreview_NewbieB_Shoes_00 | Female_NewbieB_Shoes_00 | #9E7A5AFF, #845541FF, #ECE2D7FF |

- 표시 이름은 모두 커스터마이징 전용 의상이다.
- 생성 미리보기용이어도 Costume.Id로 저장·기본색 조회가 가능하다.
- 일반 판매·획득 가능한 패션장비라는 의미는 아니다.
- Female_NewbieB_Top_00의 Starter 모델 정의·본·SMM 파일 존재도 확인했다. 전체 후보 조합을 렌더링한 것은 아니다.
- CharacterCustomizeCostumeSet의 InitialEquipmentSheetId(Clothes_Newbie_*)와 위 Costume.Id는 별도 참조다. 서로 혼동하지 않는다.

### 일반 패션장비 후보

- Fashion_NewbieSword_Top: 트레이닝 코튼 아머.
- Fashion_NewbieSword_Bottom: 트레이닝 코튼 팬츠.
- Fashion_NewbieSword_Shoes: 트레이닝 코튼 슈즈.
- 위 세 행은 남녀·어린 체형별 모델 참조와 기본색을 가진다. 단순한 일반 장비 ID로 시작하고 싶다면 좋은 대안이다.

**사용자 확정**: 마지막 작업이 없는 최초 실행에는 MaleAppearance_Default_01 / FemaleAppearance_Default_01 중 하나를 동일 확률로 무작위 선택한다. 선택한 행의 외형·색상 조합을 사용하고 이후 실행은 마지막 작업을 복원한다. 최초 착용 의상은 2026-11-02에 Fashion_NewbieSword_Top / Bottom / Shoes로 확정했다. 상세 정책은 [시작 설정](Startup.md)을 따른다.

## 7. 앱에서 구현할 기능

다음은 원본 게임 데이터가 추가로 있어야만 가능한 기능이 아니다.

- 경로 입력·Windows 폴더/파일 대화상자.
- 자유로운 앱 창 크기, 접이식 패널(크기 조절 없음)·UI 설정 저장과 화면 안 보정(2026-11-03 기획 갱신).
- 드롭다운·검색·빨간색 미적용 표시.
- 컬러피커·HEX 입력, 기본색 복원.
- 좌/우 회전, 양 버튼 팬, 스크롤 줌.
- JSON 내보내기·가져오기·자동 저장·백업.
- 실행 취소·다시 실행.
- 딤 처리·시스템 메뉴·ESC 입력 우선순위. 종료 확인 팝업은 2026-11-03 결정으로 시스템 메뉴가 대체한다.

현재 테스트 카메라는 좌버튼 회전·우버튼 높이 이동으로 구현돼 있어 확정 기획과 다르다. 게임 코드 복원이 필수는 아니며 제품 카메라 로직으로 새로 구현하면 된다.

메모리 관리도 제품 작업이다. 번들 캐시·Unload(false)가 존재하지만, 반복 교체하는 제품에서는 생성한 SMM Mesh·Material 등의 소유권과 해제를 별도 관리해야 한다. 반복 교체 시 메모리 수치는 이번에 측정하지 않았다.

## 8. 촬영모드의 범위별 가능성

- PNG 촬영: MMSimShots.Shot에서 RenderTexture·ReadPixels·EncodeToPNG를 쓰는 코드가 있다. 사용자 경로·UI 없는 캡처를 제품화하면 된다.
- 정적 포즈: 본별 TRS 포즈 데이터를 사용하는 기반과 시험 기록이 있다.
- 표정·애니메이션·임의 배경: 관련 데이터가 있다는 것과 전체 재생·조명 재현이 완성됐다는 것은 다르다. 후속 분석 대상으로 유지한다.
- 현재 원본 셰이더 표시 기록은 있으나 게임 조명·전역값까지 동일하지 않다. 정확한 인게임 색 재현은 별도 검증한다.

## 9. 권장 선행 작업과 판정 기준

| 순서 | 작업 | 확인할 결과 |
|---|---|---|
| 1 | 필요한 시트·심볼·문자열 C# 로딩 | Costume ID로 이름·모델·기본색·숨김 정보 조회 |
| 2 | 일반/직접 리소스 선택 상태 모델 | 선택 보존·미적용·예외 참조를 JSON으로 복원 |
| 3 | 눈·입을 포함한 기본 외형 완성 | 두 기본 외형 프리셋이 각각 정상 표시되고 최초 선택·재실행 복원 검증 |
| 4 | 상의·하의·신발과 염색 대상 분리 | Slot0를 한 부위에서 바꿔도 다른 옷은 유지 |
| 5 | 성별 변경·원피스·모자 시험 | 미적용·가림·헤어 단계가 선택을 훼손하지 않음 |
| 6 | 체형·얼굴형·키·나이 확장 | 데이터와 실제 변형·접합 결과 비교 |
| 7 | 반복 교체·저장·복원 | 메모리 누적과 상태 불일치 확인 |
| 8 | 촬영모드 확장 | 기능 범위를 정한 뒤 검증 |

## 10. 근거 파일

아래는 검토 시점 커밋으로 고정한 링크다.

- [게임 결과물 버전](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/version.json)
- [기존 실행 결과·남은 작업](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Docs/InstallRuntimeAssembly.md)
- [외형·의상·염색·맞춤 분석](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Docs/CharacterSimulator.md)
- [C# 조립기와 번들 로더](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Scripts/UnityMMSim/Runtime/Install/MMInstallAssembler.cs)
- [C# 패키지·SJON·SMM 리더](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Scripts/UnityMMSim/Runtime/Install/MMInstallReaders.cs)
- [염색・체형・카메라 테스트 코드](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Scripts/UnityMMSim/Runtime/MMSimCharacter.cs)
- [시트 변환기](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Scripts/convert_sheetdata.py)
- [시트 스키마](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/sheet_schema.json)
- [Costume](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/Costume.json)
- [CostumeModelVariant](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CostumeModelVariant.json)
- [CharacterHead](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterHead.json)
- [CharacterHair](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterHair.json)
- [CharacterEyes](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterEyes.json)
- [CharacterMouth](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterMouth.json)
- [CharacterBodyTypes](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterBodyTypes.json)
- [CharacterPresetAppearance](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterPresetAppearance.json)
- [CharacterCustomizeCostumeSet](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CharacterCustomizeCostumeSet.json)
- [직접 모델 사례](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/StageObjectModels/ModelParts/Top/Female_Acheron_Top_00.StageObjectModel.sjon.json)

- [시트 기본색 null 의상의 머티리얼 사례](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/AssetPackages/Assets/PatchableAssets/Character/Player/Human/Materials/Male_CreativePrint_Top_00.mat.json)
- [같은 모델의 염색 연결](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/StageObjectModels/ModelParts/Top/Male_CreativePrint_Top_00.StageObjectModel.sjon.json)
- [같은 이름의 원본 텍스처 추출본](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/AssetPackages/Assets/PatchableAssets/Character/Player/Human/Texture/Male_CreativePrint_Top_00.tga.png)

초기색 보완과 자유 RGB·최초 외형 정책은 사용자 요청을 반영했다. 호환 판정·셰이더 연결·이식 우선순위는 구현 단계에서 구체화한다.
