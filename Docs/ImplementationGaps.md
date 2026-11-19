# 미구현·생략 목록

> 정리일: 2026-11-19
> 구현하지 않았거나 일부러 빼거나 보류한 항목을 한곳에 모은 목록이다. 세부 근거와 확정·제안·미정 상태는 링크한 기획서가 기준이다. 이 문서는 목록과 링크만 가진다.
> 구현 중에 원본 기능을 옮기지 않거나 생략하면 여기에 한 줄을 추가한다(`AGENTS.md` 7장). 구현하면 줄을 지우고 해당 기획서에 기록한다.

## 구분

| 구분 | 뜻 |
|---|---|
| 생략 | 원본에 있으나 판단에 따라 옮기지 않았다. 이유를 적는다 |
| 보류 | 사용자 결정으로 미뤘다 |
| 미구현 | 할 일로 정했으나 아직 하지 않았다 |
| 결정 대기 | 사용자 답이 있어야 진행한다 |
| 미확인 | 원인·연결 방식을 아직 찾지 못했다 |

## 1. 외형·꾸밈

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 문신 발광·반사 | 생략 → 미구현 | 처음에는 기본 머티리얼 값(0)만 보고 빼었다. 일부 문신은 모델 정의(`TextureInfo`)에 발광 세기·발광 텍스처·반사 값이 있다 [확인: 달의 인장·별무리 글리터·새벽 바다의 인도·티어스 오브 쥬얼·운명의 교차] | 사용자 승인 후 구현. "밤에만 발광" 문신은 낮 상태(발광 없음)로 두는 것을 제안한다 | [Appearance.md](Appearance.md) |
| 어두운 피부의 문신 밝기 | 미확인 | 원본 식대로 피부색을 곱해 매우 흐리다. 게임 화면과 비교하지 않았다 | 게임 화면 비교 | [Appearance.md](Appearance.md) |
| 환생 눈장식 색 프리셋 | 미구현 | 눈장식 6개 후보의 보상 프리셋 색 슬롯 대응을 검증하지 않았다. 안경 모델은 의상 › 눈장식으로 고를 수 있다 | 색 필드 순서 검증 | [Appearance.md](Appearance.md) |
| 손바닥 색(ColorCode_Palm) | 미확인 | 연결을 확인하지 못해 원본 값을 유지한다 | 연결 확인 | [Appearance.md](Appearance.md) |
| 게임 기본 헤어 장식 색 | 생략 | 장식 색을 고르지 않으면 모델의 원래 색이다. 꾸미기 기본값 시트(CharacterCustomizeDefaultAppearance의 HairAccessoryColor)는 읽지 않는다 | 게임 첫 화면 색과 다를 때 | [Appearance.md](Appearance.md) |
| 입 색 | 보류 | 사용자 결정(2026-11-01) | — | [Appearance.md](Appearance.md) |
| 나이 범위·키 직접 입력 | 결정 대기 | 나이는 프리셋에 나오는 값만 후보다 | 사용자 결정 | [Appearance.md](Appearance.md) |
| 눈·얼굴 셰이더의 게임 조명 재현 | 미구현 | 앱은 단순 툰 조명 근사다 | — | [PROGRESS.md](PROGRESS.md) 3장 |

## 2. 의상·무기

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 도구 사용 동작의 타임라인(소품·이펙트) | 생략 | 연주·채집 동작의 클립만 재생한다. 행동별 스테이지 타임라인(음표 이펙트 등)·음 길이별 타격 동작(Strike)·연주 소리·채집 대상(물고기·광석)은 붙이지 않는다 | 소셜 모션처럼 연출이 필요할 때 | [WeaponPose.md](WeaponPose.md) |
| 무기·도구 동시 표시 | 생략 | 무기 칸 하나에 무기·도구 중 하나만 둔다(사용자 결정). 불러오기는 무기 우선 | — | [LastCharacterImport.md](LastCharacterImport.md) |
| 신화 오라 추가 연출 | 생략 | 무기 오라는 등급 외곽선만 그린다. 신화 등급의 `Mythic_Aura_Show` 버프(프라이빗 시어터 "신화의 기운 표시")는 연결 방식을 조사하지 않았다 | 신화 연출 요청 시 | [Equipment.md](Equipment.md) "무기 오라" |
| 세트 공명(Resonance) | 생략 | 세트 효과는 활성 연출만 재생한다. 공명 2종(DragonLegendary·HarvestLegendary, 범위 3·허용 로브)의 오라 타임라인은 여러 캐릭터가 모이는 조건으로 보여 붙이지 않았다 [추정] | 조건 확인 시 | [Equipment.md](Equipment.md) "세트 효과" |
| 세트 연출의 소품·IK·셰이더 값·숨김 트랙 | 생략 | 세트 활성 타임라인은 이펙트·덧그림 트랙만 재생한다. 설치본 활성 연출 5종에는 다른 트랙이 없다 [확인] | 새 세트에 다른 트랙이 생길 때 | [Equipment.md](Equipment.md) "세트 효과" |
| 의상 자체 이펙트(전설 의상 FX 등) | 미확인 | 의상 FX 프리팹이 있으나 앱은 붙이지 않는다. 연결 방식을 조사하지 않았다 | 요청 시 | — |
| 후드 규칙의 추정값 | 미확인 | 후드 헤어 단계 5, 후드를 쓰면 모자 숨김, 후드 의상은 로브 우선은 데이터로 정했다. 게임 메서드 본문을 읽지 못했다 | 게임 화면 비교 | [Equipment.md](Equipment.md) "후드·모자 덮개" |
| 좌우 눈·눈썹·장식 숨김 필드 | 미구현 | `HideLeftEye`·`HideRightEye`·`HideLeftEyebrow`·`HideRightEyebrow`·`HideAccessory0~2`(평소·`ByHelmetCover`)를 적용하지 않는다. HideEye 없이 좌우 필드만 있는 의상은 눈을 가리지 않는다 | — | [Equipment.md](Equipment.md) "후드·모자 덮개" |
| 로브 착용 표시(IsRobeWeared) | 미확인 | 불러오기에서 읽지 않는다. 로브를 입었는데 이 값이 거짓인 경우의 의미를 확인하지 않았다 | 실제 캐시 확인 | [LastCharacterImport.md](LastCharacterImport.md) |
| 무기 표시 전용 옵션 | 보류 | 무기를 미장착하면 숨길 수 있다 | — | [StudioFeatureReview.md](StudioFeatureReview.md) |
| 무기 장착점 보정값 | 생략 | 모델 정의 `MeshVisibleStateOverrides`(22개)·`TransformOverrides`(1개)를 적용하지 않았다 | 손·무기 위치 문제 발견 시 | [WeaponPose.md](WeaponPose.md) 4장 |
| 옷끼리 자르기 | 생략 | 몸 자르기만 게임 규칙(`.ClothingUnit.bytes`)으로 한다. 상의를 하의로 자르기(CutTopWithBottom) 등 의상→의상 자르기, 의상 피부 조각 자르기, 자르기 제외 규칙(IsFitIgnoringCase·HasVeryHighestOnCommonJunction)을 옮기지 않았다 | 옷끼리 관통·겹침 문제 발견 시 | [FeasibilityReview.md](FeasibilityReview.md) 4.5 |
| 옷 밀어내기(Pusher) | 생략 | 분리면의 경계 돌출 좌표·밀기 곡선으로 옷 정점을 바깥으로 미는 처리를 하지 않는다 | 이음매 틈·관통 발견 시 | [FeasibilityReview.md](FeasibilityReview.md) 4.5 |
| 모델 보정 정보 | 생략 | `SkirtBoneCorrectionInfos`·`CageMorphInfo`·`BoneLocalScaleDiffInfos`·`BodytoolBoneDisplacements`·`JunctionPointInfo`를 읽지 않는다 [확인: 코드 검색] | 치마·체형별 모양 문제 발견 시 | — |
| 머리·옷 물리 | 생략 | 원본 물리 구성 요소를 재현하지 않는다 | — | [FeasibilityReview.md](FeasibilityReview.md) |
| 게임 전체 공개 판정 | 미확인 | 도감 공개 시각만 쓴다. 다른 공개 참조가 있는지 모른다 | — | [Equipment.md](Equipment.md) |

## 3. 모션·연출

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 소셜 모션 소리·발자국 이펙트 | 보류 | 사용자 결정. 앱에 소리 재생 기능이 없다 | 소리 기능 도입 시 | [SocialMotion.md](SocialMotion.md) 6장 |
| 2인 소셜 모션 | 생략 | 사용자 결정 | — | [SocialMotion.md](SocialMotion.md) |
| 소품 크기 보정(ScaleSensitivity) | 미확인 | 소품 위치 표(PropTransformDataArr)의 외형별 위치·회전은 쓴다(2026-11-19). 크기 보정 값(DefaultScaleSensitivity·행별 ScaleSensitivity, 기준 PropScaleSource FxScale·ActorScale)은 게임 FxScale 계산식(HeightInfo 기반)을 읽지 못해 쓰지 않는다. 소품은 붙은 본의 크기를 그대로 따른다 | 계산식 확인 | [SocialMotion.md](SocialMotion.md) |
| 카메라 쳐다보기(시선)·행동 연출 | 보류 | 데이터·실재생 검증 후 도입한다 | 검증 완료 | [StudioFeatureReview.md](StudioFeatureReview.md) |
| 표정 단독 선택 | 보류 | — | — | [WindowAndCustomizationUI.md](WindowAndCustomizationUI.md) |

## 4. 배경·촬영

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 벽 조명 데칼 | 생략 | D3D12에서 종료 시 충돌해 그리지 않는다. 원인 미정 | 원인 확인 | [Background.md](Background.md) |
| 배경 화면 후처리(Beautify 블룸·채도·대비) | 보류 | 유료 에셋이라 그대로 쓸 수 없다. 노출만 번들에서 읽어 배경 조명 배수로 근사한다 | 블룸·채도가 필요할 때 | [Background.md](Background.md) 8장 |
| 환경 볼륨 일부(앰비언트 그라데이션·SSAO·바람·하늘 Unlit) | 생략 | 켜는 조건·변환식 미확인 | 원본 식 확인 | [Background.md](Background.md) 8장 |
| 배경 접촉 그림자 | 보류 | 게임이 실행 중에 정하는 값이라 데이터에 없다. 값을 넣으면 발밑이 어두워지는 것은 확인했다 | 게임 스크린샷 비교 | [Background.md](Background.md) 8장 |
| 3D 배경 남은 밝기 차이 | 차이 남음 | 공간의 틈 벽 1.1배·반사 바닥 1.3배 게임이 밝다. 벨벳 의상실 씬 앰비언트는 씬 번들 안에 있어 읽지 않는다(UnityBundle 로 읽을 수 있다) | 원인 확인 | [Background.md](Background.md) 4·8장 |
| 색 배경 조명 방향 | 결정 대기 | 게임과 그림자 방향이 반대다. 3D 배경은 볼륨 방향을 따른다. 색 배경은 게임이 필드 조명을 쓰는 것으로 보여 고정값이 없다 [추정] | 사용자 확인 | [Background.md](Background.md) |
| 독립 촬영모드·이미지 비율·촬영 버튼 | 보류 | — | — | [PhotoMode.md](PhotoMode.md) |

## 5. 앱·편의

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 카메라 회전 감도·반전, 시점 초기화 버튼 | 결정 대기 | — | — | [Camera.md](Camera.md) |
| 스튜디오 카메라 근사: H를 현재 포즈 메시로 재고 기준 키는 상수, 휠 한 칸 원값 1로 가정, 거리 추종 시간 임의 | 근사 | 게임은 외형 변형 바인드 자세로 재고 기준 키도 메시로 계산한다. 휠 원값·추종 시간은 정적 분석으로 찾지 못했다 | 휠 칸 수 게임 확인, 측정 경로 이식 | [Camera.md](Camera.md) |
| 내보내기 단축키·덮어쓰기 구분 | 결정 대기 | Ctrl+S·Ctrl+O 미배정 | — | [SaveLoad.md](SaveLoad.md) |
| 경로 외 옵션(감도·UI 크기 등) | 보류(후속 후보) | 필수 범위가 아니다 | — | [Options.md](Options.md) |
| 즐겨찾기·최근 사용·A/B 비교 | 제안 | 합의 전 | — | [ReferenceGapReview.md](ReferenceGapReview.md) |
