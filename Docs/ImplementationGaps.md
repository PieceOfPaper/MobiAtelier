# 미구현·생략 목록

> 정리일: 2026-11-12
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
| 얼굴·몸꾸밈 색 편집 | 결정 대기 | 사용자는 컬러피커로 정했다. 게임 셰이더에 문신 색 칸이 없다(텍스처색 × 피부색) | 사용자 답 | [Appearance.md](Appearance.md) |
| 어두운 피부의 문신 밝기 | 미확인 | 원본 식대로 피부색을 곱해 매우 흐리다. 게임 화면과 비교하지 않았다 | 게임 화면 비교 | [Appearance.md](Appearance.md) |
| 환생 눈장식 색 프리셋 | 미구현 | 눈장식 6개 후보의 보상 프리셋 색 슬롯 대응을 검증하지 않았다. 안경 모델은 의상 › 눈장식으로 고를 수 있다 | 색 필드 순서 검증 | [Appearance.md](Appearance.md) |
| 손바닥 색(ColorCode_Palm) | 미확인 | 연결을 확인하지 못해 원본 값을 유지한다 | 연결 확인 | [Appearance.md](Appearance.md) |
| 헤어 장식 색(DyeSlot HairAccessory) | 미구현 | 헤어의 장식(브릿지 등) 색 칸을 편집·적용하지 않는다. 불러오기는 "지원하지 않는 항목"으로 안내한다 | — | [LastCharacterImport.md](LastCharacterImport.md) |
| 입 색 | 보류 | 사용자 결정(2026-11-01) | — | [Appearance.md](Appearance.md) |
| 나이 범위·키 직접 입력 | 결정 대기 | 나이는 프리셋에 나오는 값만 후보다 | 사용자 결정 | [Appearance.md](Appearance.md) |
| 눈·얼굴 셰이더의 게임 조명 재현 | 미구현 | 앱은 단순 툰 조명 근사다 | — | [PROGRESS.md](PROGRESS.md) 3장 |

## 2. 의상·무기

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 도구(악기·생활 도구) | 미구현 | 의상 › 무기 칸에서 택일하기로 했다(2026-11-12). 불러오기는 안내만 한다 | 다음 작업 순서 | [PROGRESS.md](PROGRESS.md) 1장 |
| 무기 오라 | 미구현 | 등급 직접 선택으로 정했다. 이펙트 연결 방식은 [미확인] | 도구 다음 | [PROGRESS.md](PROGRESS.md) 1장 |
| 의상 세트 효과 | 미구현 | 착용 의상으로 자동 판정하기로 했다(CostumeSetEffect) | 무기 오라 다음 | [PROGRESS.md](PROGRESS.md) 1장 |
| 의상 자체 이펙트(전설 의상 FX 등) | 미확인 | 의상 FX 프리팹이 있으나 앱은 붙이지 않는다. 연결 방식을 조사하지 않았다 | 세트 효과 조사 때 함께 | — |
| 모자 착용 시 헤어 처리 | 미구현 | 모자를 쓰면 헤어가 모두 사라진다. 헬멧 덮개 메시(`Mesh_HelmetCover_On/Off`)·`…ByHelmetCover` 숨김 규칙·모자 접힘(IsCapClosed)을 쓰지 않는다 [확인: 코드 검색] | — | [PROGRESS.md](PROGRESS.md) 3장 |
| 후드·로브 상태 | 미구현 | 게임의 후드 씀/벗음·로브 착용 상태를 다루지 않는다. 불러오기는 후드 씀·모자 접힘을 "지원하지 않는 항목"으로 안내한다 | — | [LastCharacterImport.md](LastCharacterImport.md) |
| 무기 표시 전용 옵션 | 보류 | 무기를 미장착하면 숨길 수 있다 | — | [StudioFeatureReview.md](StudioFeatureReview.md) |
| 무기 장착점 보정값 | 생략 | 모델 정의 `MeshVisibleStateOverrides`(22개)·`TransformOverrides`(1개)를 적용하지 않았다 | 손·무기 위치 문제 발견 시 | [WeaponPose.md](WeaponPose.md) 4장 |
| 몸·옷 접합(관통 제거) | 생략(근사) | 원본 맞춤 데이터(`.ClothingUnit.bytes`) 대신 몸 절단 근사(BodyCut)를 쓴다 | 관통이 심한 조합 발견 시 | [FeasibilityReview.md](FeasibilityReview.md) |
| 모델 보정 정보 | 생략 | `SkirtBoneCorrectionInfos`·`CageMorphInfo`·`BoneLocalScaleDiffInfos`·`BodytoolBoneDisplacements`·`JunctionPointInfo`를 읽지 않는다 [확인: 코드 검색] | 치마·체형별 모양 문제 발견 시 | — |
| 머리·옷 물리 | 생략 | 원본 물리 구성 요소를 재현하지 않는다 | — | [FeasibilityReview.md](FeasibilityReview.md) |
| 게임 전체 공개 판정 | 미확인 | 도감 공개 시각만 쓴다. 다른 공개 참조가 있는지 모른다 | — | [Equipment.md](Equipment.md) |

## 3. 모션·연출

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 소셜 모션 소리·발자국 이펙트 | 보류 | 사용자 결정. 앱에 소리 재생 기능이 없다 | 소리 기능 도입 시 | [SocialMotion.md](SocialMotion.md) 6장 |
| 2인 소셜 모션 | 생략 | 사용자 결정 | — | [SocialMotion.md](SocialMotion.md) |
| 어린 외형 소품 크기 보정 | 미구현 | — | — | [PROGRESS.md](PROGRESS.md) 3장 |
| 카메라 쳐다보기(시선)·행동 연출 | 보류 | 데이터·실재생 검증 후 도입한다 | 검증 완료 | [StudioFeatureReview.md](StudioFeatureReview.md) |
| 표정 단독 선택 | 보류 | — | — | [WindowAndCustomizationUI.md](WindowAndCustomizationUI.md) |

## 4. 배경·촬영

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 벽 조명 데칼 | 생략 | D3D12에서 종료 시 충돌해 그리지 않는다. 원인 미정 | 원인 확인 | [Background.md](Background.md) |
| 배경 화면 후처리(Beautify 블룸·채도·대비) | 보류 | 유료 에셋이라 그대로 쓸 수 없다. 값만 읽어 URP 기본 효과로 근사할 수 있다 | 게임 스크린샷 비교에서 차이가 클 때 | [Background.md](Background.md) 8장 |
| 환경 볼륨 일부(앰비언트 그라데이션·SSAO·바람·하늘 Unlit·구름 색 2~4) | 생략 | 켜는 조건·변환식 미확인 | 원본 식 확인 | [Background.md](Background.md) 8장 |
| 배경 접촉 그림자 | 보류 | 게임이 실행 중에 정하는 값이라 데이터에 없다. 값을 넣으면 발밑이 어두워지는 것은 확인했다 | 게임 스크린샷 비교 | [Background.md](Background.md) 8장 |
| '공간의 틈: 빛' 구름 하늘 | 미확인 | 그려지지 않는다. 볼륨의 하늘·구름 전역 값을 넣어도 그대로였다 | — | [Background.md](Background.md) |
| 색 배경 조명 방향 | 결정 대기 | 게임과 그림자 방향이 반대다. 3D 배경은 볼륨 방향을 따른다. 색 배경은 게임이 필드 조명을 쓰는 것으로 보여 고정값이 없다 [추정] | 사용자 확인 | [Background.md](Background.md) |
| 독립 촬영모드·이미지 비율·촬영 버튼 | 보류 | — | — | [PhotoMode.md](PhotoMode.md) |

## 5. 앱·편의

| 항목 | 구분 | 이유·현재 상태 | 다시 볼 조건 | 문서 |
|---|---|---|---|---|
| 카메라 감도·반전·제한, 시점 초기화 버튼 | 결정 대기 | — | — | [Camera.md](Camera.md) |
| 내보내기 단축키·덮어쓰기 구분 | 결정 대기 | Ctrl+S·Ctrl+O 미배정 | — | [SaveLoad.md](SaveLoad.md) |
| 경로 외 옵션(감도·UI 크기 등) | 보류(후속 후보) | 필수 범위가 아니다 | — | [Options.md](Options.md) |
| 즐겨찾기·최근 사용·A/B 비교 | 제안 | 합의 전 | — | [ReferenceGapReview.md](ReferenceGapReview.md) |
