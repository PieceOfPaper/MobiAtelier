# 진행 상황

> 최종 갱신: 2026-11-20
> Codex와 Claude Code가 번갈아 작업하며 공유하는 인계 문서다. 작업 단위가 끝날 때마다 갱신한다(`AGENTS.md` 8장).

## 1. 현재 단계

- 옷 밀어내기·도구 장착 본 변위(2026-11-20, Claude Code, 2순위 2번째): 게임 NativeMeshPushingJob·PushVertices·IntersectsPusher·TryGetExtrusion·CreatePusher·SpaceSeparatorParameter.GetDefault 를 디스어셈블해 옮겼다(`ClothingCut.cs` 의 Pusher·Push). 조각마다 자르기 직후 같은 조각이 민다. ClothingUnit 의 돌출 좌표·경계 본 가중치, 모델 정의의 PushCurves·PushIncludeSkin·ExtrusionRatios, 분리면 보정의 Pusher 값을 쓴다. 민 정점에 새 본이 필요하면 렌더러 본에 덧붙인다(`CharacterView.CutMesh`). 티셔츠 밑단 옆 청바지 허리 관통·건틀릿 틈이 사라짐 [확인: 배치 촬영]. 도구 장착 본 변위(BodytoolBoneDisplacements)는 애니메이션 뒤 국소 오프셋으로 건다(`CharacterView.ApplyLocalOffsets`). 정책 시험 60개 통과, Windows 빌드·실행 촬영 정상. 치마 본 보정은 미구현, 물리·얼굴 셰이더는 사용자 결정 대기(아래 다음 할 일).
- 옷끼리 자르기(2026-11-19, Claude Code, 2순위 1번째): 게임 `NativeMorphableAssetFitter.BuildPlans`·`FittingPolicy`·`ClothFitOrderPolicy(V2)`·`ClothJunctionPointSizeResolver` 를 디스어셈블해 옮겼다(`ClothingCut.cs` 의 `ClothFit`, 조립기 `FitClothes`). 몸과 ClothingUnit 이 있는 파츠를 맞춤 순서로 정렬하고, 렌더러 재질(Skin=모든 조각, Cloth=뒤 조각, Steel=VeryHighest 공통 접합부)에 따라 자른다. 설정은 StageConstants(`ClothFitOrderPolicySettings`·`ClothJunctionPointSizeResolverSettings`). 배치 촬영 10조합: 건틀릿 위 소매 관통 사라짐 [확인], 로브 아래 원피스가 크게 잘림(게임과 같은지 [미확인]). 런타임·에디터 dotnet 빌드 오류 0. 정책 시험 60개는 이번에 돌리지 않았다. 남은 2순위: 옷 밀어내기(Pusher, BuildPlans 의 AddPusher 경로 해석함)·모델 보정 정보, 눈·얼굴 셰이더 조명, 머리·옷 물리.
- 카메라 쳐다보기 상체 회전(2026-11-19, Claude Code): 사용자 제보(게임 스크린샷: 뒤에서 찍으면 상체를 틀어 봄). 게임이 꾸미기 캐릭터에 쓰는 상수 `Constants/LookAt/CustomizeActor`(허리·가슴·목 20°씩 + 머리 40° + 눈 39°)를 찾아 적용했다. 이전에는 LookAt 시트의 머리·눈(53°)만 써서 53°를 넘으면 정면으로 돌아왔다. 배치 촬영·정책 시험 60개 통과.
- 의상 자체 연출(2026-11-19, Claude Code): `StageObjectModelTimeline` 시트로 전설 의상 등 90개 모델의 반복·후드 씀 타임라인(이펙트·덧그림)을 재생한다. 타임라인 로더의 중복 제거가 여러 소켓에 붙은 같은 이펙트를 버리던 기존 버그도 고쳤다(소셜 모션에도 해당). 배치 촬영·정책 시험 60개 통과. 앱 움직임·게임 비교는 [미확인].
- 바로 만들 수 있는 것 4개(2026-11-19, Claude Code): ① 좌우 눈·눈썹·장식 숨김(`HideLeftEye`·`HideAccessory0~2` 등, 안대·모자가 가린 눈·귀장식이 뚫고 나오지 않음). ② 문신 발광·반사(사용자 승인, 몸 식은 원본 Skin_Body 디스어셈블). ③ 환생 눈장식 색 프리셋 6개(의상 › 눈장식 목록, 보상 프리셋 시트 필드 순서 확인). ④ 도구 사용 연출(악기 음표·채집 이펙트). 함께 곡괭이 4종이 조립 실패로 안 보이던 문제를 고쳤다. 배치 촬영·정책 시험 60개 통과. 앱 실조작·게임 비교는 [미확인].
- 카메라 쳐다보기(2026-11-19, Claude Code): 사용자 요청으로 보류를 풀고 구현했다. 패널 새 폴드아웃 "연출" › "카메라 쳐다보기"(끔/켬, `lookAtCamera`). LookAt 시트(23행)의 Humanoid 머리·눈 축·각도 제한으로 앱이 근사한다(게임 계산 본문은 못 읽음). 배치 촬영 4외형×7각도·정책 시험 60개 통과. 앱 실조작·게임 비교는 [미확인].
- 미구현 1순위 4개(2026-11-19, Claude Code): ① 모자 쓰면 헤어가 사라지는 버그 수정 — 원인은 헤어 프리팹이 H1~H5 를 꺼 두는데 조립기가 고른 단계를 켜지 않은 것 [확인: 프리팹]. ② 후드 씀/벗음·모자 덮개 올림/내림 구현(`hoodOn`·`helmetCoverOn`, 의상 › 모자·로브 아래 줄). 프리팹이 쓴·벗은 메시를 모두 켜 둬 겹쳐 그려지던 것도 고쳤다. ③ 헤어 장식 색(`CharacterHairAccessoryColor` 10행, 장식형 헤어만). ④ 소셜 모션 소품 위치 표(`PropTransformDataArr`)의 외형별 위치·회전. 소품 **크기** 보정은 게임 FxScale 식을 못 읽어 하지 않았다(ImplementationGaps). 불러오기의 "지원하지 않는 항목" 3개를 연결했다. 배치 촬영 15조합·정책 시험 60개 통과. 앱 실조작·게임 화면 비교는 [미확인].
- 무기 오라·세트 효과·불러오기 연결(2026-11-18, Claude Code): 미구현 A 목록을 마쳤다. 오라는 게임 무기 셰이더의 외곽선 패스를 디스어셈블해 `MobiCharacter.shader` 의 `WeaponGradeOutline` 패스로 옮겼다(색은 설치 폴더 StageConstants `WeaponGradeOutlineColorSettings`, 7등급 [확인]). 상태 `weaponAura`, 패널 "무기 오라" 줄(색 편집 경로, 재조립 없음). 세트 효과는 `CostumeSetEffect` 의 활성 연출 5행 [확인]을 보이는 의상으로 판정해 타임라인을 계속 재생한다(`TimelineEffectPlayer`, 소셜 모션 로더 재사용). 불러오기는 `WeaponAuraGrade` 를 넣고 `CostumeSet` 은 다시 판정한다. 얼굴·몸꾸밈 색은 고르지 않기로 확정(사용자: 게임도 못 고름). 배치 촬영(에픽·전설+·신화 외곽선, JUJU 두 세트 연출)·정책 시험 60개 통과·Windows 빌드 실행 확인. 앱 실조작·게임 화면 비교는 [미확인].
- 정책 시험 실패 4개 수정(2026-11-18): 원인은 시험이 넣은 카메라 거리 4가 새 카메라 최대 거리(3, 키에 따라 줄어듦)를 넘어, 실행 취소로 키가 바뀌면 게임 규칙대로 다시 잘린 것 [확인]. 코드는 그대로 두고 시험 값을 범위 안(거리 2)으로 고쳤다.

- 도구(생활 도구·악기) 구현(2026-11-17, Claude Code): 미구현 A 목록(도구 → 무기 오라 → 세트 효과 → 불러오기 연결)의 첫 항목. `EquipmentTool`·`EquipmentToolType`·`Instrument` 시트를 읽어(설치본 도구 430행 [확인]) 의상 › 무기 칸에 무기 뒤로 넣었다(`CostumeSlotState.KindTool`, 드롭다운 키 `tool:` 접두). 사용자 결정: 도구 자세는 넣음 / 사용(연주·채집 Action 시작→반복), 넣은 상태의 루트 장착 악기(피아노 등) 숨김, 불러오기는 무기 우선. 배치 조립 촬영 11조합·Windows 빌드 실제 캐시 불러오기 확인. 스모크 테스트 `-maOutfit` 에 `tool=도구Id` 키 추가. 앱 실조작은 [미확인]. 남은 A: 무기 오라(이펙트 연결 미확인), 세트 효과.

- 모션 얼굴 텍스처 깜빡임 수정(2026-11-15, Claude Code): 사용자 제보(입·눈 텍스처를 바꾸는 모션에서 여러 텍스처가 빠르게 지나감). 원인은 `FacialXaf` 가 `Step` 키를 선형 보간해 아틀라스 번호(`_EmotionMouthMapAtlasSize.z`·`_EyeballIndexFromOne` 등)가 중간 칸을 거친 것. 게임 `XafTangentTypeExtension.ForceConvert`(Step·Constant→계단, Linear→선형, 나머지→Flat)와 `XafAnimationMigrator` 의 `_EyeballIndexFromOne` 전 키 Constant 를 그대로 옮겼다 [확인: 디스어셈블]. 여성 소셜·감정 xaf 112 트랙에서 지나가는 번호 수가 줄었다(예: Airplane 입 20→3) [확인: 데이터 평가]. 런타임 dotnet 빌드 오류 0. 앱 화면 재생은 [미확인].
- 게임 스튜디오 카메라 구현(2026-11-15, Claude Code): 사용자 결정(좌+우 드래그는 위아래만, 거리·상하각 게임 값, 세로 창은 세로 값)으로 `CameraController` 를 게임 식으로 바꿨다. 값은 설치 폴더에서 읽는다(`Sheets/StudioCameraData.cs`, 새 .cs·.meta 는 Unity 배치 실행이 만듦). `CharacterView.MeasureBodyHeightWithoutHead`(머리 메시 최저점 − 몸 최저점), 조립 확정마다 `UpdateCameraCharacter`. `CameraState.rig`(0 = 이전 저장, 거리·높이 기본으로). 빌드 촬영 옵션 `-maShotUI lens05,near`. Unity 배치 컴파일·Windows 빌드 오류 0, 2560×1440 빌드 촬영 6장(1x·0.5x·3x × 기본·최근접)이 게임 스크린샷 구도와 맞음 [확인]. 마우스 드래그·휠 실조작, 휠 한 칸 크기(게임 칸 수 확인 필요)는 [미확인]. 스펙은 Camera.md "게임 스튜디오 카메라".
- 스튜디오 카메라 값 조사(2026-11-15, Claude Code, 코드 변경 없음): 사용자 게임 스크린샷(0.5x·1x·3x 기본=가장 멀리, 각각 가장 가까이)과 비교했다. 설치 폴더에서 읽을 수 있는 값 [확인]:
  - `CameraSettings/CameraPresetSettings.sjon.bin`(ResourcesPackage Starter)의 `StudioModeCameraSetting` Portrait/Landscape. 가로: Fov 50, InitialPitch 3, Pitch -1~25, DefaultDistance 3, MinDistance 1.3, CameraTargetHeight 0.9, InitialPanningHeight 0.7, 팬 높이 범위(최소 거리 0.2~1, 최대 거리 0.25~1.1). 세로: Fov 50, InitialPitch 10, Pitch 4~25, DefaultDistance 4.2, MinDistance 1.5, CameraTargetHeight 0.2, InitialPanningHeight 0.6, 팬 높이(0.1~1, 0.25~1.1).
  - `StageConstantSettings/StageConstants` 의 `PhotoModeCameraSettings.MouseWheelToDistanceRatioOnStudio` 0.1, `PinchZoomApplyRatioOnStudio` 0.5, 성별·연령별 `StandardHeightReference`(여 성인 1.685396, 남 성인 1.792207, 여 아동 1.651735, 남 아동 1.742354).
  - 코드(GameAssembly 정적 디스어셈블) [확인]: 렌즈 배율 0.5·1·3, `FOV = 2·atan(tan(Fov/2)/배율)`(앱과 같은 식, 기준만 50°). 최대 거리 = clamp(BodyHeightWithoutHead / StandardHeightReference × DefaultDistance, MinDistance, DefaultDistance), 시작 거리 = 최대 거리. 초기 팬 높이 = InitialPanningHeight × BodyHeightWithoutHead. 스튜디오 카메라는 상하 팬 높이만 갖는다 — 앱의 확정 사항(좌+우 드래그 자유 팬)과 다르므로 바꾸려면 사용자 결정이 필요하다.
  - 스크린샷 대조 [추정]: 1x 기본에서 캐릭터가 화면 높이의 약 74%이고 0.5x는 약 38%(비율 약 1.95, 식의 2.0과 맞음). 수직 FOV 50°·거리 약 2.6m 일 때와 맞는다.
  - 후속 조사(같은 날, 디스어셈블) [확인]: ① 휠: `ZoomDisplacement = 휠 원값 × OrbitModifier.MouseWheelToDistanceRatio(2.5, CameraSettings.sjon.bin)`, 스튜디오에서 `목표 거리 -= ZoomDisplacement × 0.1 / 2.5`(= 원값 × 0.1) 후 [MinDistance, 최대 거리]로 자르고 부드럽게 따라간다. 휠 한 칸의 원값은 [미확인](1이면 칸당 0.1m) — 게임에서 가장 먼 곳→가장 가까운 곳 칸 수를 세면 확정된다. ② H = BodyHeightWithoutHead = 머리 메시 기준 높이 − 몸 메시 최저점(머리 메시가 없으면 VisualHeight). StandardHeightReference 는 외형 변형 경로에서는 메시로 다시 계산하고, 아니면 성별·연령 상수를 쓴다. ③ 거리 비율 t = clamp01((거리 − MinDistance)/(최대 거리 − MinDistance)). 팬 높이 범위 = H × lerp(MinPanningHeightWhenMinDistance→WhenMaxDistance, t) ~ H × lerp(MaxPanning…, t). 기준점 y = 캐릭터 y + 팬 높이 − (InitialPanningHeight − CameraTargetHeight)·H + 0.25·H·(1 − t)(0.2초 보간). 리그에는 상향 오프셋 (InitialPanningHeight − CameraTargetHeight)·H 를 따로 넣는다. ④ 렌즈 변경 FOV는 0.4초 보간. Fov 는 `CalcOneToOneFov`(× (w+h)/2h)로 저장했다가 투영에서 되돌린다.
  - 스크린샷 픽셀 측정(2560×1440) [확인]: 0.5x 머리 꼭대기 491·발 1031, 1x 머리 꼭대기 261 → 화면 중심 기준 비율 2.00 = tan43°/tan25°. 렌즈 배율은 수직 FOV 50°에 적용된다(1:1 FOV에 적용하면 1.74가 되어 맞지 않는다). 캐릭터 높이 ≈ 0.70 × 거리 → 키 약 1.74m 이면 거리 약 2.49m(= 1.4/1.685×3, H≈1.4 일 때와 맞음). 기본 화면 중심 높이 ≈ 0.57 × 키 ≈ 0.99m = InitialPanningHeight 0.7 × H 와 맞는다 — 상향 오프셋이 화면 중심을 CameraTargetHeight 위치에서 InitialPanningHeight 위치로 내린다고 본다 [추정].

- 게임 스튜디오 스크린샷 비교(2026-11-13, Claude Code): 색 배경 민트바닐라·스트리트 고스 캣 웨어 조합으로 비교했다.
  - 몸꾸밈 의상 피부 조각 [수정함]: 상의 `Fashion_BlackCat_OnePiece_00_F` 는 가슴 피부를 의상 안의 `Mesh_Skin_Body`(원본 셰이더 `MMN/CH/Skin_Body`, 몸과 같은 DyeMap)로 그리고 몸 메시의 그 자리는 잘린다. 문신을 몸 렌더러에만 걸어 빠졌다. 이제 의상 재질 중 원본 셰이더가 Skin_Body 인 것(`CharacterView.CostumeSkinMaterials`)에도 건다. 배치 촬영으로 매듭 문양이 게임처럼 가슴에 보이는 것을 확인했다.
  - 안티앨리어싱 [수정함, 사용자 승인]: `PC_RPAsset` MSAA 1 → 4(Standalone 기본 품질 PC). 빌드 실행 촬영에서 가장자리 계단이 부드러워진 것을 확인했다. 게임 그래픽 옵션은 끔·MSAA 2x/4x·FXAA·SMAA 중 고른다. 스모크 테스트 촬영은 원래 4x RT 라 이 변경과 무관하다.
  - 바닥 그림자 계단 [수정함, 사용자 승인]: `PC_RPAsset` 그림자 거리 50 → 15m(카메라 최대 거리 10m + 그림자 길이), 캐스케이드 4 → 1, 해상도 2048 → 4096. 템플릿 기본값은 가까운 구간이 1024칸으로 약 6m를 덮어 텍셀이 화면에서 몇 픽셀로 보였다 [확인: 빌드 촬영 비교]. 그다음 원인: 바닥 원본 셰이더(`MMN/MMN_ShadowOnly`)는 URP 소프트 그림자 설정을 켜고 꺼도 결과가 같았다 [확인]. 바닥 재질을 앱 셰이더 `Assets/Shaders/ShadowReceiver.shader`(주광 그림자를 화면에 곱함, 그림자 세기·거리 페이드 반영)로 바꿔 경계가 부드러워졌다 [확인: 빌드 촬영, 그림자 안 밝기 원본과 같음]. `AppController.shadowReceiverShader` 는 `ProjectSetup.Run` 으로 씬에 넣었다. 그림자 안쪽의 가는 밝은 줄은 팔·몸 사이 얇은 틈이다 [추정].
  - 그림자 방향: 앱 색 배경 빛 `Euler(35,155)` 은 화면 왼쪽 뒤로 길게(고도 35°) 드리운다. 게임은 오른쪽 뒤로 짧다. 게임에는 배경 볼륨이 없을 때 쓰는 스튜디오 전용 조명이 따로 있으나 각도 값은 번들에서 찾지 못했다. 게임에서 카메라를 돌릴 때 그림자가 화면 기준으로 고정되는지(카메라 기준 조명) 확인이 필요하다.
  - 벨벳 의상실 색 비교(2026-11-13, 사용자 스크린샷, 코드 변경 없음): 배경은 게임이 채널별 약 1.3~1.8배 밝고 채도가 높다(소파·쿠션·카펫·바닥·창 너머 [확인: 픽셀 비교]). 캐릭터는 흰 소매·치마 보라의 밝기가 거의 같다. 벽 조명 갓은 게임이 발광하는 노랑(254,254,232), 앱은 회색(134,130,125)이다. 원인 후보는 모두 앱이 적용하지 않은 항목이다: Beautify 블룸·채도, 벽 조명 데칼(프리팹에 `DecalLightPositionSetter` 12개, D3D12 종료 크래시로 끔), 앰비언트 그라데이션. 각각의 기여도는 [미확인]이다. 후속(사용자 촬영, 후처리 0~2단계 같은 구도): 단계 간 차이는 2단계의 조명 갓 블룸뿐이고, 배경·캐릭터 색은 0단계에서도 같다(평균 차 2.4/255) [확인]. 색 배경도 단계 차이가 없다. 따라서 배경이 어두운 원인은 후처리가 아니다. 조명 갓은 0단계에서도 흰색으로 빛나 앱에서 발광이 빠진 것이다. 원인 확정(배치 시험): 벨벳 의상실 Beautify 프로파일의 노출 1.5×밝기 1.2(=선형 1.8배)가 옵션 단계와 관계없이 적용된다. 앱 배경색 ×1.8 이 게임 배경색과 거의 같고, 주광·GI 1.8배 촬영도 게임 밝기에 가깝다 [확인]. 앰비언트 프로브·그림자·데칼 패스·앰비언트 그라데이션은 원인이 아니었다 [확인]. 캐릭터는 이 노출을 받지 않는 것으로 보인다 [추정]. 조명 갓은 조명 없는 재질이라 노출만으로 게임 흰색이 되지 않는다(데칼 발광이 남음). 근거 보강: 게임 PC 후처리 옵션 4단계 데이터는 블룸·플레어만 바꾸고 노출·채도 항목이 없다 [확인]. 클래식 피팅룸 프로파일도 노출 1.5·밝기 1.2 [확인]. 피팅룸은 사용자 미해금이라 게임 비교 불가. 대조: 프로파일 없는 민트바닐라(게임 0단계 촬영)는 시트 색보다 5~8% 정도만 밝아(아래 218,200,170 → 229,210,174) ×1.8 이 아니다 [확인] — 노출은 프로파일이 있는 배경에만 걸린다는 해석과 맞는다. 색 배경의 작은 차이는 원인 [미확인]. 두 번째 대조(2026-11-14, 공간의 틈: 빛 게임 촬영, 프리팹에 프로파일 없음): 난간·아래 벽이 게임에서 sRGB 약 1.2배 밝다(149→180, 132→159) [확인: 구도가 달라 대략 비교]. ×1.8(194·172)보다는 작지만 1배도 아니어서 Beautify 외의 밝기 요인이 있을 수 있다. 앱은 이 배경의 구름 하늘과 바닥 반사(게임은 하늘이 비친다)가 없어 같은 조건 비교가 아니다. 1.8배를 바로 구현하기 전에 이 차이를 먼저 설명해야 한다. 후속(2026-11-14, 사용자 결정으로 공간의 틈 차이부터): 구름 미표시 원인은 렌더 순서였고 `StudioBackgroundPass` 를 불투명/반투명(스카이박스 뒤) 두 패스로 나눠 고쳤다 [확인: 배치 촬영, 벨벳 의상실 변화 없음]. 파란 하늘은 NaoZone 씬 스카이박스 재질(`MMN/BG/Sky_Re`)에 볼륨 Skybox 값을 재질 속성으로 넣으면 게임 색에 가깝다 [확인: 앱 143,177,202 ↔ 게임 152,185,217]. 셰이더는 전역 값을 읽지 않는다 [확인]. 스튜디오가 씬을 따로 읽지 않는 것으로 보여(시트에 씬 없음) 씬 번들 읽기 대신 볼륨→스카이박스 재질 방식을 사용자 결정으로 구현했다(`EnvironmentVolume.ApplySky`, `StudioBackground.SkyMaterial`). 배치 촬영: 공간의 틈: 빛 하늘 파랑 [확인], 어둠·벨벳 의상실·피팅룸 변경 전과 픽셀 차 0 [확인]. 앱 실행 화면은 [미확인]. 이어서(사용자 요청 "남은 것 순차 진행"): ① 밝기 재비교 — 하늘 추가 후에도 공간의 틈: 빛 벽은 앱 149 ↔ 게임 180 그대로. ② 구름 — 셰이더 변수 이름 표로 `_Global_CloudColor2~4` 사용을 확인해 넣었다(구름이 희어짐). 앰비언트(SH)는 출처 데이터가 없어 바꾸지 않았다(DynamicGI 시도는 방 배경을 바꿔 되돌림). ③ 바닥 반사 — `StudioReflectionPass`(StudioBackgroundPass.cs) 구현, 클립 +0.04. 배치 촬영·Windows 빌드(D3D12) 촬영에서 빛·어둠 반사·하늘·구름 표시, 종료 크래시·예외 없음 [확인], 벨벳 의상실·피팅룸·색 배경 픽셀 차 0 [확인]. 빌드 촬영 옵션 `-maShotUI bg:배경ID` 추가. 반사 바닥 밝기·구름 그늘 차이, 벨벳 의상실 노출 1.8 적용 여부는 남음. 이어서(사용자 요청 "남은 차이 원인 파악·수정"): 설치 폴더 셰이더를 디스어셈블해(`d3dcompiler_47` + 셰이더 이름 표) SimpleLit·PlanarReflection 식을 확인했다. 배경 밝기가 앰비언트 SH 에 좌우되는 것을 찾아 라이트 프로브(볼륨 키 = GeoData 이름일 때 그 LightProbeAssetPath)를 구현했다. 공간의 틈 벽 149→167(게임 180) [확인: 배치·빌드]. 다른 배경 픽셀 차 0. 남은 차이와 전역 Beautify 프로파일 조사 결과는 Background.md 4·8장. 스모크 테스트 `-maSetGlobal`·`-maLightProbes`·`-maWideCam`·27계수 `-maAmbientSH` 추가. 이어서(사용자 결정: 패치 반영이 쉽도록 번들 직접 읽기): `Install/UnityBundle.cs`·`Install/BeautifyProfile.cs` 로 프리팹 Beautify 노출을 읽어 배경 조명에 곱한다(`EnvironmentVolume.Apply` 의 exposure). 벨벳 의상실·피팅룸 1.8, 공간의 틈·색 배경 1 [확인]. 캐릭터 변화 없음 [확인: 촬영]. Windows 빌드 촬영 정상 종료. 새 .cs 두 개의 .meta 는 Unity 배치 실행이 만들었다. 피팅룸은 사용자 미해금이라 게임 비교 없음. 스모크 테스트 `-maSkyFromVolume` 추가. 구름 색 2~4 는 게임과 달라져 넣지 않았다. 바닥 반사(PlanarReflection)는 미구현. 스모크 테스트에 `-maAmbient sky=재질경로` 추가. 구현 방식은 사용자 결정 대기다(Background.md 8.1). 스모크 테스트 `-maAmbient`(cur·black·probe·noshadow·giX·expX·ag)·`-maLogMaterials` 를 추가했다.
  - 색: 게임 화면 위쪽 어두운 띠는 가로로 균일해 UI 그라데이션으로 보인다 [추정]. 게임은 배경이 시트 원색보다 밝고, 캐릭터 흰색이 초록빛·피부가 덜 붉음·보라가 더 푸르다(픽셀 비교). 조명·환경 색 영향인지 후처리인지는 분리하지 못했다.

- 몸 자르기 게임 규칙화(2026-11-13, Claude Code): 사용자 제보(스트리트 고스 캣 웨어에서 허벅지가 사라짐)의 원인은 기존 근사가 "의상 정점이 많은 쪽"을 덮인 쪽으로 골라, 치마처럼 허벅지 본에 묶여 분리면 아래로 늘어진 의상에서 방향이 뒤집힌 것이었다. 사용자 결정으로 `.ClothingUnit.bytes`를 해석해 게임 규칙으로 바꿨다(`Character/ClothingCut.cs`, FeasibilityReview.md 4.5). 같은 문제의 의상(블룸 코스트 비치웨어·브리즈 트래블 원피스·에델 그레이스 예복·러닝 반바지·몽마 힐 등)과 남성·10세 조합을 배치 촬영으로 확인했다. 앱 실조작은 [미확인]이다.

- 배경 환경 볼륨 2차(2026-11-12, Claude Code): 사용자 결정으로 게임 데이터에 맞춰 캐릭터 빛 방향도 볼륨 주광을 따르게 했다(원본 셰이더 시험으로 확인). 3D 배경 확정 시 EnvironmentConfig 전역 텍스처 4개를 넣는다. 접촉 그림자는 값이 데이터에 없어 게임 스크린샷 비교 뒤 도입한다. 색 배경은 볼륨이 없어 그대로다.

- 배경 환경 볼륨 1차(2026-11-12, Claude Code): 사용자 결정으로 화면 효과 중 환경 볼륨(조명)을 먼저 적용했다. 3D 배경의 `VolumeKey` 볼륨에서 주광·안개·하늘/구름/높이 안개/캐릭터 조명 전역 값을 넣고, 캐릭터 빛 색을 원본 얼굴 셰이더와 같은 규칙으로 맞췄다. 화면 후처리(Beautify)는 게임 스크린샷 비교 뒤 결정한다(Background.md 8장). 배치 촬영으로 전후를 비교했고 게임과 같은지는 [미확인]이다.

- 마지막으로 접속한 캐릭터 불러오기 준비(2026-11-12, Claude Code): 사용자 결정으로 기능 검토를 마쳤다(AGENTS.md 7·10장). 불러오기 전에 아틀리에가 지원하지 않는 항목을 먼저 구현한다. 순서는 ① 꾸밈(수염·얼굴꾸밈·몸꾸밈) ② 도구(악기·생활 도구) ③ 무기 오라 ④ 세트 효과 ⑤ 불러오기 기능이다. ①은 촬영으로 확인했다. 사용자 요청으로 ⑤ 불러오기를 먼저 구현했다(LastCharacterImport.md). ②~④는 불러오기에서 "지원하지 않는 항목"으로 안내된다.

- v0.1.1 사전 공개(2026-11-05): 사용자가 수정 결과를 확인했다. `main`의 `baf065305dc5a5cdf8e4c6c3bae921f47f3a2f58` 기준 Windows 일반 빌드가 오류·경고 없이 성공했다. GitHub Release에 사용 안내가 포함된 ZIP과 SHA-256을 게시하고 서버 해시 일치를 확인했다. 압축 해제본은 별도 데이터 폴더로 실행해 정상 종료했다. 숨긴 창의 자동 촬영은 실패해 배포 빌드 화면은 직접 확인하지 못했다.

- 첫 GitHub Release(2026-11-04): 사용자가 검수와 배포 조건 확인을 마쳤다고 알렸다. `main`의 `b1f6836ba04e69bbef03a862e19f9e7fc250d850` 기준 Windows 일반 빌드가 오류·경고 없이 성공했다. `v0.1.0` 사전 공개에 사용 안내가 든 ZIP과 SHA-256 파일을 게시했다. ZIP 무결성 검사와 압축 해제 후 별도 데이터 폴더로 실행·정상 종료를 확인했다. 자동 촬영 파일은 생성되지 않아 이 빌드의 화면은 직접 확인하지 못했다.

- 모션 스크롤 재수정(2026-11-03): 사용자가 목록 중간의 모션을 선택하고 다시 열면 맨 위로 돌아간다고 확인했다. 배치 전에도 양수일 수 있는 스크롤 최댓값만 확인하던 조건을 실제 목록 높이와 비교하도록 고치고, 항목 클릭 전 위치를 보관한다. dotnet 에디터 빌드 오류 0·기존 경고 3. 실제 선택·재열기 조작은 사용자 확인이 필요하다.

- 사용자 피드백 후속(2026-11-03): 외형·의상·색·배경·무기 자세 직업·모션 목록과 선택 표기에 이름 우선 규칙을 적용하고 ID 검색을 유지했다. 모션 목록은 가상화 목록의 스크롤 범위가 준비된 뒤 마지막 위치를 복원하도록 수정했다. 검색 중 닫으면 보이던 항목의 전체 목록 위치로 돌아간다. dotnet 컴파일 통과. 실제 모션 스크롤 조작은 [미확인]이다.

- 사용자 피드백(2026-11-03): 이름 우선·이름이 없을 때만 ID·숨김 접두 표기로 기획을 변경했다. 표기 코드 반영은 아직 남았다. 모션 목록 스크롤 복원, 키 조절 중 즉시 조립과 드래그 단위 이력, 시작 시 패널 열림을 구현했다. 내보내기 크래시 로그에서 Windows 파일 대화상자 호출 중 StackOverflowException을 확인해 열기·저장 구조체의 문자열 마샬링을 명시적 네이티브 버퍼로 교체했다. 원인으로 마샬링이 의심되지만 수정 후 대화상자 실조작은 [미확인]이다. 런타임·에디터 dotnet 빌드는 오류 0, 기존 참조 충돌 경고 3이다. 에디터에서 파일 열기·저장과 취소, 슬라이더 연속 조작·실행 취소, 모션 목록 재열기, 최초 패널을 확인해야 한다.


- 기획·구현 차이 보완(2026-11-03, Codex): `main` `3b2ffab1dd46104cea7e4cac1824cd3155c7cab5` 위의 Claude Code 미커밋 작업을 이어서 공개 제한·필수 외형 대체·실제 조립 결과 검사·부분 복원을 보완했다. 실패한 의상의 숨김 효과 제거, 실패 편집·이력 복원 거절, 캐릭터·배경 승인 전 준비와 취소 정리를 추가했다. 에디터 정책·컨트롤러 시험 60개 통과. 일반·Development Windows 빌드 및 실행 보고서로 제한 JSON 가져오기 차단·원래 선택 보존을 확인했다. 외형 꾸밈의 미구현 범위와 눈장식의 기존 EyeDeco 연결은 Appearance.md에 정리했다. 실제 파일 대화상자·마우스 조작은 [미확인]이다.

- 소셜 모션(2026-11-03, Claude Code): 카메라 영역 오른쪽 아래 `모션` 버튼(검색 목록·■ 정지)으로 게임 모션 메뉴의 행동을 재생한다. 저장·이력 제외. 시작→반복 클립, 페이드, 무기 넣음·숨김, 소품(Prop·PropAnimation)·이펙트(Effect) 로드, 재조립 시 같은 시점부터 이어 재생. 배치·빌드 촬영으로 확인했고 마우스 조작·이어 재생은 [미확인](`Docs/SocialMotion.md` 5장).

- 무기 자세(2026-11-03, Claude Code): 무기를 모델 정의의 장착점에 붙이고(넣음 허리·등, 꺼냄 손), 의상 › 무기 아래 "무기 자세"(넣음·꺼냄 경계·꺼냄 전투)와 "자세 직업"(자동 = 무기 외형의 원래 직업, 변경 가능)을 추가했다. 꺼냄은 `AnimationStateSet` 의 직업별 AlertStand/CombatStand 클립을 재생한다. 배치 촬영·빌드 촬영으로 확인했고 드롭다운 마우스 조작은 [미확인]이다(`Docs/WeaponPose.md`).

- 새 UI 구현(2026-11-03, Claude Code): WindowAndCustomizationUI·SystemMenu·Options·UndoRedo·ControlsHelp·Reset의 확정 사항을 코드로 옮겼다. 고정 접이식 패널(시작 닫힘)과 카메라 출력 영역 전환, 눈·메뉴 아이콘, 카메라 영역 기준 화각·빠른 조작 안내·첫 사용 자세한 안내, 꾸미기 전용 실행 취소/다시 실행(아이콘·Ctrl+Z/Y), 시스템 메뉴(내보내기·가져오기·초기화·옵션·종료)와 ESC 우선순위, 최초·초기화 초보자 의상. 배치 모드 컴파일·빌드 성공, 빌드 촬영으로 화면을 확인했다. 마우스·키보드 조작은 [미확인](4장 확인 요청 7).

- UI 배치도·문서 연결(2026-11-03): 기존 WindowAndCustomizationUI.md를 배치 기준으로 보강했다. 가로 패널 닫힘/열림·세로 패널·시스템 메뉴/옵션의 SVG 3개를 추가하고, 그림의 A~S 항목을 관련 기획서 링크 표와 연결했다. 텍스트 배치 설명도 함께 둔다. SVG를 렌더링해 한글·겹침을 확인하고 문서 링크를 점검했다. 그림은 구현 완료 화면·픽셀 규격이 아니며 코드 변경은 없다.

- UI 확정·명칭 변경(2026-11-03): 고정 접이식 패널·출력 영역 전환, 눈/F1·하단 중앙 화각·아이콘 조작 안내, 패널 최상단 꾸미기 이력 아이콘, 우측 상단 시스템 메뉴·ESC 열기/닫기를 문서화했다. 수동 JSON 작업은 내보내기(Export)·가져오기(Import)다. 경로 옵션은 검사·저장 후 재시작한다. 이번 작업은 문서만 변경했으며 런타임 UI 구현은 이전 상태다. 아래 2026-11-02의 이동식 창·카메라 포함 이력·종료 팝업 설명은 과거 구현/기획 기록이다.

- UI·플로우 재정리(2026-11-02): WindowAndCustomizationUI.md에 보기 도구·꾸미기 공통 작업·외형/의상/배경·향후 연출의 배치안을 정리하고 ControlsHelp.md에 빠른 안내·첫 사용 도움말·입력 우선순위를 제안했다. 이번 작업은 기획 정리이며 새 배치·도움말의 런타임 구현이 아니다.

- 스튜디오 결정 반영(2026-11-02): UI 숨김·0.5x/1x/3x 화각을 코드·문서에 추가했다. Unity 컴파일·플레이는 [미확인]이다. 시선·행동은 StudioFeatureReview.md에 데이터 근거·조건부 도입·다음 검증을 기록했다. 배경은 다른 에이전트 작업 후 재검토하고, 무기 표시·이미지 비율은 보류한다.

- 구현 1단계(기반·시작 흐름·기본 외형 표시)를 마쳤다.
- 꾸미기 창 1차(외형·의상 선택)를 코드로 만들었다. 컴파일만 확인했고 화면·조작은 [미확인]이다(4장 확인 요청).
- 색 적용(2026-11-02): 피부·헤어·눈 색 시트 선택, 의상 염색 파트 견본과 컬러피커 창. 빌드 촬영으로 표시만 확인했고 마우스 조작은 [미확인]. 키·꾸밈 항목은 아직 없다.
- 스튜디오 배경(2026-11-02): 색 배경 7개·3D 배경 4개 선택, 캐릭터의 Unity 그림자 받기 끔. 빌드 촬영으로 표시만 확인했고 드롭다운 조작은 [미확인]이다(4장 확인 요청 6).
- 폴더 구조·어셈블리 정의·네임스페이스 규칙은 `AGENTS.md` 5장, 검증 명령은 6장에 있다.

## 2. 최근 작업

| 날짜 | 내용 |
|---|---|
| 2026-11-19 | 카메라 쳐다보기 상체: `GameData.CustomizeLookAt`(CustomizeActor 상수), `plan.CustomizeLookAt`, `CharacterLookAt` 재작성(몸 사슬 좌우 분배·눈 본 회전 비율), 스모크 시선 각도 100°·130°와 뒤쪽 전신 촬영 |
| 2026-11-19 | 의상 자체 연출: `SheetSchemas.StageObjectModelTimeline`, `GameData.ModelTimelines`, `plan.ModelTimelines·CostumeEffects`, `CharacterAssembler.AttachCostumeEffects`(보이는 의상만, 후드 씀 연출), `CharacterView.CostumeEffects`. `SocialMotionLoader` 중복 제거 키에 붙는 자리(`PlacementKey`) 추가. 스모크 촬영 전 의상 연출 1.5초 진행 |
| 2026-11-19 | 눈·장식 숨김: `CharacterAssembler.HideEyeSide`·`AccessorySlot`·`plan.HiddenAccessories`, 패널 장식 숨김 안내. 문신 발광: `ApplyTattoo` 발광·반사 값, `MobiCharacter.shader` 문신 발광·반사 식. 환생 눈장식: `SheetSchemas.CharacterCustomizeEyeDeco·CostumeRewardPreset`, `GameData.EyeDecoPresets`, 패널 눈장식 프리셋 항목(`eyedeco:` 키, 착용 염색이 같으면 프리셋 이름 표시). 도구 연출: `plan.ToolStartTimelines·ToolLoopTimelines`, `AttachToolEffects`, `TimelineEffectPlayer.Clock·Suspend`, `CharacterMotion.LoopLength·PlayTime`, `CharacterView.ToolEffects`. 곡괭이: `AddPrefabToolMeshes`. 스모크 `eyeDeco=` 키, 조립 문제 로그, 모션 시점의 도구 연출 맞춤 |
| 2026-11-19 | 카메라 쳐다보기: 새 파일 `Character/CharacterLookAt.cs`(+ Unity 생성 .meta), `SheetSchemas.LookAt`·`GameData.LookAt`(선택 시트), `AssemblyPlan.LookAtCamera·LookAtTable·LookAtUnavailable`, `CharacterAssembler.AttachLookAt`, `CharacterMotion.Sample` 의 시선 보정 위치, `EditState.lookAtCamera`, 패널 "연출" 폴드아웃·`LookAtRow`(색 편집 경로), 스모크 `look=1`(7각도 머리 촬영과 각도 로그). Unity 배치 컴파일 오류 0, dotnet 빌드 오류 0 |
| 2026-11-19 | 헤어 단계·후드·모자 덮개: `CharacterAssembler`(후드 의상·모자 판정, `Hide*ByHelmetCover`, 헤어 단계 켜기 `ShownHairLevel`, `CostumeVariantVisible`, `CarryBuildResults` 에 결과 전달), `AssemblyPlan` 요청·결과 필드, `EditState.hoodOn·helmetCoverOn`. 헤어 장식 색: `SheetSchemas.CharacterHairAccessoryColor`(선택 시트), `GameData.HairAccessoryColors`, `appearance.hairAccessoryColor`, 계획 전역 색 `HairAccessory`, 패널 `ColorRow` 의 "기본 색" 항목. 패널 `VariantRow`(후드·모자 덮개). 불러오기 `IsHoodOn`·`IsCapClosed`·염색 18 연결. 소셜 소품 `PropPlacement`. 스모크 `hair=`·`hairAcc=`·`hood=`·`cover=` 키와 머리 근접 촬영. 런타임·에디터 dotnet 빌드 오류 0(기존 경고 3), Unity 배치 컴파일 오류 0 |
| 2026-11-18 | 무기 오라: `WeaponAuraData`(새 파일, StageConstants 색), `EditState.weaponAura`, `PlanResolver.ResolveWeaponAura`, `CharacterView.ApplyWeaponAura`(무기 메시 자식 외곽선 렌더러, `OutlineFollow`), 셰이더 `WeaponGradeOutline` 패스(일반 재질은 `SetShaderPassEnabled` 로 끔), 전역 `_Global_Weapon_Texture` 를 조립 때 넣음. 세트 효과: `SheetSchemas.CostumeSetEffect`, `GameData.ActivationSets`, `ResolveSetEffect`, `SocialMotionLoader.LoadTimelines`·`TimelineEffectPlayer`(이펙트 갱신을 `ActionMotion.UpdateEffects` 로 옮겨 공유). 패널 "무기 오라"·"세트 효과" 줄, 불러오기 오라 연결, 스모크 `aura=` 키. 정책 시험 카메라 값 수정 |
| 2026-11-17 | 도구: `SheetSchemas` EquipmentTool·EquipmentToolType·Instrument, `GameData.Tools/ToolList/LoadTools/LinkGatheringActions`, `CostumeSlotState.KindTool·Row()`, `PlanResolver.ResolveToolUse`·`WeaponIsTool`, 루트 장착 도구 넣음 숨김(`CharacterAssembler.Mount`), 패널 무기 목록·도구 자세, 불러오기 도구 역조회(무기 우선·건너뛴 도구 한 줄 안내). 런타임·에디터 dotnet 빌드 오류 0, 정책 시험 실패 4개는 변경 전과 같음(아래 인계 메모) |
| 2026-11-15 | 에디터 전용 프리 카메라: 화각 버튼 아래 버튼(`#if UNITY_EDITOR`, 빌드 제외). `CameraController.SetFreeMode` 는 각도·거리·높이 제한을 무시하고 따로 움직이며 저장·자동 저장에 남기지 않고, 끄면 켜기 전 시점으로 돌아간다. 런타임 dotnet 빌드(에디터·비에디터 정의) 오류 0. 에디터 플레이 확인 전 |
| 2026-11-13 | 몸 자르기: `ClothingCut.cs`(ClothingUnit 읽기, 게임 FittingCutPolicy·자르기 규칙, 걸친 삼각형 잘라 새 정점 보간), `SmmReader` 삼각형 파일 순서 반환, 기존 근사(`BodyCut.LimbCuts`·면 근접) 삭제. 설치본 ClothingUnit 3,512개 읽기 [확인: 덤프 전수], 전후 11벌·회귀 4조합 촬영 |
| 2026-11-12 | 배경 환경 볼륨 2차: 캐릭터 빛 방향을 주광(볼륨) 방향으로, `StudioBackground` 에 EnvironmentConfig 전역 텍스처 4개. 원본 셰이더 임시 시험으로 캐릭터가 주광 방향을 쓰는 것 확인, 접촉 그림자 값 임시 시험(도입 보류). `PhotoStudio` 볼륨은 별도 가상 방 맵 것이라 색 배경에 쓰지 않음. Unity 배치 컴파일 오류 0, 3D 4종 촬영 비교. Background.md 8장·ImplementationGaps 갱신 |
| 2026-11-12 | 배경 환경 볼륨: `Sheets/EnvironmentVolumeData.cs`(VolumeData SJON 읽기, 그룹 덮어쓰기, 시각 보간), `App/EnvironmentVolume.cs`(주광·안개·전역 셰이더 값·캐릭터 빛), `StudioBackground` 준비 시 볼륨 읽기·확정 시 적용·해제 시 복원, `StageBuilder.MainLight`, `BundleCache.Files`, `MobiCharacter.shader` 의 `_MobiCharLightDir/Color`. 스모크 테스트 `-maNoVolume`·`-maVolumeSkip` 과 볼륨 로그. Unity 배치 컴파일 오류 0, 런타임 dotnet 빌드 오류 0. 공개 3D 4종 전후 촬영 비교. Background.md 8장·ImplementationGaps 갱신 |
| 2026-11-12 | 마지막으로 접속한 캐릭터 불러오기: `State/LastCharacterImport.cs`(캐시 → 장면 상태, 역방향 조회·가장 가까운 시트 색·후보 결정 규칙), 시스템 메뉴 항목·확인·결과 안내/실패 팝업, 시험 옵션 `-maShotLastCharacter`·`-maTitleDisplay`·`-maShotUI dismiss`. Unity 배치 컴파일·Windows 빌드 오류 0·경고 0, 빌드 실행으로 실제 캐시 불러오기와 변조 사본의 실패 팝업 촬영 확인. 기획서 LastCharacterImport.md, SystemMenu·AGENTS·README·ImplementationGaps 갱신 |
| 2026-11-12 | `Docs/ImplementationGaps.md` 추가: 각 기획서에 흩어진 미구현·생략·보류·결정 대기 항목과 문신 발광·반사 누락 등 문서에 없던 항목을 모았다. README 목록·AGENTS.md 7장 규칙 추가 |
| 2026-11-12 | 꾸밈 구현: CharacterBeard·FaceTattoo·BodyTattoo 시트(설치본 20·47·29행 확인), `appearance.beard·faceDeco·bodyDeco`, 프리셋 꾸밈 반영, 외형 폴드아웃 드롭다운 3개, 수염 메시(HideBeard·머리 가림 시 숨김, 색은 Hair 슬롯), 얼굴·몸 `_TattooMap` 합성(얼굴 원본 셰이더, 몸은 앱 셰이더에 원본 Skin_Body 식 이식, 흉터 모드 포함). 스모크 테스트 `-maOutfit` 에 beard·faceDeco·bodyDeco 키와 얼굴·등 촬영 추가. 런타임·에디터 dotnet 빌드 오류 0. 재시도한 Unity 배치 촬영이 성공했다: 남성 수염(헤어색 따름)·눈 흉터, 여성 얼굴 문신·점, 몸 뱀 문신(팔)·흉터를 확인했다. 피부색(Color_001↔Color_018)에 따라 흉터·문신 색이 바뀌는 것도 확인했다. 스모크 테스트 `skin=` 키를 추가했다. 어두운 피부에서 몸 문신이 매우 흐린 것이 원본 식 그대로인지는 [미확인]이다 |
| 2026-11-12 | 마지막 접속 캐릭터 불러오기 검토와 사용자 결정: 타이틀 외형 캐시 하나만 읽기 예외, 역방향 조회 규칙, 실패 부위 미장착+오류 팝업, 시트 없는 색은 가장 가까운 시트 색, 숨김 항목 미장착, 메뉴 이름. AGENTS.md 7·10장에 반영 |
| 2026-11-12 | 게임 패치 후 동작 점검(코드 변경 없음): v0.1.1 빌드를 별도 데이터 폴더로 실행해 시작 검사·시트 로딩·기본 외형(여·남) 조립, 무기(꺼냄 경계)·배경·소셜 모션(전통댄스) 촬영 확인, 로그 예외 없음. Unity 배치 모드는 라이선스 토큰 없음(종료 코드 198)으로 실행 못 해 조립 시험·Policy 시험은 미실행. 남성 기본 프리셋의 키 값이 +1.00으로 읽힌다 [확인], 게임 값과 같은지는 [미확인] |
| 2026-11-05 | GitHub `v0.1.1` 사전 공개: 새 게임 외형 시트 형식 대응, Windows 일반 빌드 오류·경고 0, ZIP 무결성·업로드 해시·별도 데이터 실행 종료 확인. 숨긴 창의 자동 촬영은 실패 |
| 2026-11-05 | 사용자가 수정 결과를 확인했다. v0.1.1 배포를 위해 버전을 갱신한다. |
| 2026-11-05 | 게임 시트 형식 대응: CharacterHair·Eyes·Mouth 행 끝의 HideForNonOwnerTime을 읽고 이전 스키마도 지원한다. 설치본 시작 검사 통과, 세 시트 512·524·252행의 주요 필드 대조 불일치 0건. 배경·무기 자세·소셜 시트 로딩 오류 없음. 에디터 dotnet 빌드 오류 0·기존 경고 3. 이전 버전 설치본과 실제 조립·화면은 미확인. 추가 시간 필드와 공개 판정의 관계는 미확인이다. |
| 2026-11-04 | GitHub `v0.1.0` 사전 공개: Windows 일반 빌드·사용 안내·ZIP·SHA-256 게시. ZIP 검사와 별도 데이터 실행 종료 확인, 자동 촬영 파일 미생성 |
| 2026-11-03 | 모션 선택 후 재열기 스크롤 재수정: 목록 전체 높이와 스크롤 범위 확인 후 복원, 클릭 전 위치 저장. 컴파일 통과, 실제 조작 확인 대기 |
| 2026-11-03 | 이름 우선 표기 코드 반영, 이름·ID 검색 유지, 모션 목록 스크롤 복원 시점을 배치 이후로 수정. dotnet 빌드 통과, 실제 UI 조작 확인 대기 |
| 2026-11-03 | 사용자 피드백: 이름 우선 표기 기획(구현 대기), 모션 목록 스크롤 유지, 키 즉시 반영, 초기 패널 열림, Windows 파일 대화상자 네이티브 버퍼 처리. dotnet 빌드 통과, 실제 조작 확인 대기 |
| 2026-11-03 | 기획·구현 보완 인계: 기존 Disclosure·대체 기록·ID 표기를 유지하고 실제 프리팹/메시/입 텍스처 실패의 재조립·부분 복원 안내, 실패 의상 가림 제거, 준비본 폐기, 배경 준비/확정 분리, 실패한 실행 취소의 장면/이력 보존, 원본 백업 실패 시 교체 중단을 추가했다. 일반·Development 빌드 오류/경고 0, 에디터 정책 시험 60개 통과. 공개 빌드 실행 보고서 4개 시나리오(가져오기 승인 대기·일반/Development 승인·시작 부분 복원) 확인. 관련 기획서 갱신. 커밋·푸시 없음 |
| 2026-11-03 | Codex 작업 재개 점검: 공통 지침·로컬 지침 연결, 프로젝트 trusted 등록, `.codex/config.toml`의 workspace-write/on-request, 로컬 설정 git 제외를 확인했다. Unity·.NET·Python 설치와 csproj 소스 포함을 확인하고 에디터 프로젝트 빌드(런타임 포함)가 오류 0·경고 3으로 통과했다. AGENTS.md의 Input System 버전을 실제 1.20.1로 정정하고 PowerShell의 codex.cmd 실행 방법을 추가했다. 기존 C# 미커밋 변경 10개는 보존했다 |
| 2026-11-03 | 소셜 모션 연출 트랙: 손 IK(IKControl, 2본 IK), 덧그림(AdditiveRenderer, 겹친 SkinnedMeshRenderer), 숨김(ObjectVisibility, forceRenderingOff), 셰이더 값(ShaderProperty, MaterialPropertyBlock). 클립·행동 타임라인을 합쳐 읽고 같은 프리팹·시각은 하나만. 소리(Sound·Footstep)는 사용자 결정으로 보류하고 SocialMotion.md 6장에 목록을 남겼다. 스모크 테스트 outfit 모드에 배경 패스 설치, 소셜 촬영에 상반신 사진 추가 |
| 2026-11-03 | 소셜 모션 구현: 시트 `SocialAction`·`ActionStageTimeline` 추가, `Action` 의 FadeIn/Out 읽기(`CustomizeActionRow`→`ActionRow`, `CustomizeAction`→`FindAction`), 소셜 그룹 행도 읽음. `Character/SocialMotion.cs`(행동 해석·클립·타임라인 소품·이펙트 로드), `CharacterMotion` 에 행동 층(기본/행동 믹서, 페이드, 무기 넣음·숨김, 소품 클립, 이펙트), 조립기에서 재질 변환·클립·얼굴 로더를 정적 함수로 분리, `plan.Clothing`(ModelPartInfo.ClothingCondition). `UI/SocialMotionControl.cs`, `AppUI.AddMotionControl`, AppController 재생·정지·재조립 이어 재생. 스모크 테스트 `social=ID [act=행동]`(촬영은 `SampleAll`), 빌드 촬영 `-maShotUI motion:ID` |
| 2026-11-03 | 소셜 모션 기획: SocialMotion.md 추가. 저장·이력 제외 보기 도구, 카메라 영역 오른쪽 아래 `모션` 버튼(배치 항목 T) 확정. AGENTS·UndoRedo·UI 배치·플로우·README 반영. 구현 전 결정 12개(4장)는 사용자 답 대기. 코드 변경 없음 |
| 2026-11-03 | 행동(모션 메뉴) 재생 가능성 시험: 스모크 테스트 `-maOutfit` 에 `intro=클립`·`loop=클립` 추가. 화남1(여·남·12세)·전통댄스(여·남)를 기존 `CharacterMotion` 으로 재생·촬영해 몸·얼굴 재생 확인. 결과·남은 확인은 StudioFeatureReview.md 3장. 행동 선택 UI는 없다 |
| 2026-11-03 | 무기 자세 2차: 꺼내기·넣기 전환(ToolDraw/ToolUndraw 도입 + StageTimeline ToolState 구간으로 장착점 전환), 무기 자체 클립(ModelPartAnimation), 꾸미기 포즈 1~3(Action Customize_Costume_04~06 시작→반복, 얼굴 클립 포함), 부분 복원에 없는 자세 직업·모르는 자세 값 안내. 시트 연산 `D`(double), Action·ModelPartAnimation·StageTimeline 시트. `CharacterMotion` 을 도입/반복 구간·무기 장착(ToolMount)·구간별 얼굴 구조로 다시 짰다. 스모크 테스트 `-maOutfit` 의 `prev=자세`, outfit 모드 `-maMotionTimes`, `-maProbeClips`, 빌드 촬영 `-maShotUI draw` |
| 2026-11-03 | 무기 자세: 시트 리더에 구조체 배열 원소(`[S+S]`) 추가, `CombatJob`·`AnimationStateSet` 시트(시작 필수 아님, 실패 시 `MotionError`), `Costume.EquippableCombatJobs`. 조립기가 무기 프리팹 본을 복제해 `AttachPointInfo`(상태·성별·어린 외형별 위치·회전·크기, `AlwaysInvisible` 숨김)로 붙이고 무기는 몸 가리기에서 뺀다. `weaponPose { stance, job }` 저장. 직업 클립은 얼굴 클립이 없어 꾸미기 아이들 얼굴을 따로 반복(`CharacterMotion` faceLoop). 스모크 테스트 `-maOutfit` 에 `stance=`·`job=`, 빌드 촬영 `-maShotUI weapon`. 촬영 확인 목록은 WeaponPose.md 4장 |
| 2026-11-03 | 키 조절 슬라이더: 외형 "나이" 아래 `키` 슬라이더(HeightModifier -1~+1, 0.01 단위, 값 두 번 누르면 0). 손을 뗄 때 한 번 조립·이력·자동 저장. 시험 옵션 `-maShotUI short,tall`. 빌드 촬영으로 -1·+1 몸 크기 변화, `autosave.json`의 `heightModifier: 1.0` 저장과 재실행 복원 확인. 마우스 끌기·키보드 조작은 [미확인] |
| 2026-11-03 | 체형별 모션 점검: 스모크 테스트 `-maBodyType 체형ID`(Default·Thin·Fat·Muscular) 추가. 17·12세 × Thin·Fat·Muscular × 남녀에서 체형별 클립 선택(여성 Muscle 11.00초 포함)·얼굴 연결, 0~8초 전신·얼굴 촬영으로 깨짐 없음 확인. 체형마다 몸 클립·자세가 다르다(Fat은 팔을 벌림). 표정 xaf는 남성·어린 외형은 체형 공통, 여성은 Fat·Muscle만 별도 |
| 2026-11-03 | 어린 외형 모션 점검: 스모크 테스트 `-maAge 나이` 추가. 12세 남녀 기본·초보자 의상·원피스에서 HC 클립(9.33초)·얼굴 본 클립·xaf 연결, 의상 전용 본 4~5개 처리, 전신 비율·발 높이·치마/상의 모양, 3.70·7.27초 눈 감김을 촬영으로 확인 |
| 2026-11-03 | 아이들 얼굴 표정: `Character/FacialXaf.cs`(xaf Morpher·Visibility·Shader 트랙 시간 평가, Constant 계단·Flat 부드러운 보간), `CharacterMotion`에 `_Facial.fbx` 얼굴 본 클립 레이어(AvatarMask: `_Head` 하위 본) 추가. 스모크 테스트 `-maMotionTimes 초;초`로 시점별 전신·얼굴 촬영. 여성 1.8~2.0초·남성 2.0초 눈 감김 촬영 확인, 빌드 실행 예외 없음 |
| 2026-11-03 | 기본 아이들 모션 재생: 계획에 `Idle` 클립 경로(`<접두>@Customize_Stand_<체형>`, 없으면 Default), 조립 끝에 `Character/CharacterMotion.cs`(Animator + 수동 PlayableGraph, LateUpdate 반복, 위치·스케일·_COM 높이 보정, 파츠 전용 본 회전 복사). 배치 조립 시험·빌드 실행에서 성인 여성·12세 두 시점 촬영으로 움직임과 비율 유지 확인. 결과·함정은 4장 인계 메모 |
| 2026-11-03 | UI 연출: 패널 슬라이드(카메라 출력 영역 동기), 메뉴·팝업·드롭다운·컬러피커·툴팁·알림·조작 안내 페이드, F1 숨김/표시 페이드, 폴드아웃 펼침 페이드, hover 색 전환(`UI/UIAnim.cs`, UI Toolkit 트랜지션). 중간 프레임 촬영으로 보간 확인 |
| 2026-11-03 | 사용자 결정으로 패널 크기 조절 손잡이 제거(크기는 앱 창에서 계산, 설정 저장 안 함). 남은 과제 구현: 공개 빌드의 창 닫기·Alt+F4 → 시스템 메뉴, 시작 복원·가져오기의 부분 복원 안내(RestoreCheck), 배경 실패 시 이전 배경 유지·대체 사유·`ID - 이름` 표기, 아이콘 키보드 포커스 이름·테두리, 경로 표시 글꼴(₩ 문제). 빌드 촬영·창 닫기 메시지 시험 확인 |
| 2026-11-03 | 새 UI 구현: 고정 접이식 패널·출력 영역(Camera.rect)·안쪽 가장자리 크기 조절, 눈/메뉴 아이콘·벡터 아이콘·툴팁, 화각·빠른 안내·자세한 안내, 꾸미기 이력(EditHistory), 시스템 메뉴·내보내기·가져오기·초기화 확인·경로 옵션·저장 후 종료, ESC 소유권, 초보자 의상. 빌드 촬영 확인 |
| 2026-11-03 | 기존 UI 배치 기획서에 가로·세로·시스템 메뉴/옵션 SVG 배치도와 A~S 항목별 기획서 링크 표 추가. README 탐색 경로 갱신. 코드 변경 없음 |
| 2026-11-03 | 접이식 패널·출력 영역·아이콘 배치·꾸미기 전용 이력·시스템 메뉴/ESC·경로 옵션 확정과 Export/Import 명칭 반영. SystemMenu.md·Options.md 추가, 기존 종료 팝업 문서는 대체 안내로 정리. 코드 변경 없음 |
| 2026-11-02 | UI·플로우 재정리: 단일 꾸미기 창 유지, 보기 도구·공통 작업·본문 카테고리·조작 안내 배치안, 첫 사용 안내와 ESC 처리 제안. 배경 기획 상태를 최신화하고 README·플로우·종료 안내 연결. 코드 변경 없음 |
| 2026-11-02 | 스튜디오 화면 후속 결정: F1·버튼 UI 숨김/복귀, 거리와 독립적인 0.5x·1x·3x 렌즈 화각과 camera.lensMultiplier 복원 추가. 시선·행동 데이터 검토 문서 추가, 배경 별도 진행·무기 표시·촬영 비율 보류 반영. Unity 실행·컴파일은 미확인 |
| 2026-11-01 | 레퍼런스 조사, 프로그램 플로우와 항목별 기획서 분리 |
| 2026-11-01 | 의상 초기색 재검토, 자유 RGB 편집·최초 외형 2종 무작위 선택 확정. Dye·Startup·ProgramFlow·FeasibilityReview 갱신 |
| 2026-11-01 | 에이전트 공통 지침(`AGENTS.md`, `CLAUDE.md`), Claude Code·Codex 프로젝트 설정, 이 문서 추가 |
| 2026-11-01 | 구현 1단계: 설치 폴더 리더, 시트 리더, 경로 검사·지정 팝업, 자동 저장·복원·복구 안내, 조립기, 카메라, 앱 씬·빌드 설정 |
| 2026-11-01 | 입 표시: 입 텍스처를 얼굴 머티리얼에 합성(머리 원본 셰이더), 얼굴 애니메이션의 셰이더 값 첫 프레임 적용. 에디터 배치 촬영·빌드 실행 확인 |
| 2026-11-01 | 눈 표시 수정: 프리팹 켜짐·꺼짐 반영, 얼굴 애니메이션 첫 프레임 Visibility 적용(감은 눈 평면이 보이던 문제) |
| 2026-11-01 | 폴더 정리(사용자 요청): `Assets\MobiAtelier\` 제거 → `Assets\Scripts·Editor·Shaders·Scenes·UI`, 네임스페이스 제거(전역). 정리 후 조립 시험·빌드·첫 실행 재확인 |
| 2026-11-01 | 환생 화면 기준 외형 기획(Appearance.md) 추가. 전체 선택 범위와 외형·의상의 폴드아웃·검색 드롭다운 확정, UI·의상·구현 검토·README 갱신 |
| 2026-11-01 | Costume 전수·모델 숨김·충돌 처리 선언을 재검토하고 Equipment.md에 근거 추가. 염색 UI는 실제 파트 표시·컬러피커로 범위 정리. 얼굴·몸꾸밈·눈장식 시트 연결 추가 확인 |
| 2026-11-01 | 초기화 버튼, 실행 중 장면 편집 이력과 JSON 불러오기·초기화 성공 시 이력 삭제, 무기 직업 제한 해제 확정. Reset.md 추가와 관련 문서 갱신 |
| 2026-11-02 | 꾸미기 창 1차: 이동·크기 조절·배치 저장과 화면 안 보정, 폴드아웃, 검색 드롭다운, 외형(성별·체형·나이·얼굴형·헤어·눈·입)·의상 10부위 선택, 미적용 빨간색·가림 표시 |
| 2026-11-02 | 나이·키에 따른 몸 크기: 몸 .bdc(나이 × 체형 × 키 단계 본 스케일·위치)를 게임 규칙대로 보간·적용, 발바닥 높이 보정 |
| 2026-11-02 | 사용자 피드백 반영: 세로 창은 아래쪽 배치, 크기 손잡이를 하단 줄로, 카테고리를 "외형"·"의상" 둘로·들여쓰기 정리. 몸 관통 수정: 의상 절단면(Mesh_SP_*)으로 팔다리 몸 자르기, 기본 회전이 다른 같은 이름 본을 파츠 전용 본으로 분리 |
| 2026-11-02 | 색 적용: 피부·헤어·눈 색을 게임 색 시트에서 고르는 드롭다운(색 견본), 의상 염색 파트 견본과 컬러피커 창(HEX 입력, 미리보기·적용·취소·기본색), 색만 바뀌는 편집은 재조립 없이 반영 |
| 2026-11-02 | 공개 범위·에디터 전용 숨김 데이터·ID 표기 확정. 초기화·초기 의상·외형 호환·업데이트 부분 복원·마지막 로딩 요청 우선 정책을 기획서에 반영. 런타임 코드는 이번 작업에서 변경하지 않음 |
| 2026-11-02 | 스튜디오 배경: Background.md 추가, 배경 시트 읽기, 색 배경(화면 고정 그라데이션 + 게임 그림자 바닥), 3D 배경(원본 셰이더 전용 패스 렌더 패스, 가리는 벽 숨김), 꾸미기 창 "배경" 폴드아웃, 장면 상태 `background` 저장. 캐릭터는 Unity 그림자를 받지 않게 수정(이중 그림자) |
| 2026-11-02 | 공개 레퍼런스 재확인과 현재 기획 비교. ReferenceGapReview.md에 차이·우선순위·검수 예를 제안으로 추가하고 초기 조사와 최신 확정 사양의 차이를 명시. 코드·확정 범위 변경 없음 |

### 구현 1단계 내용

| 기능 | 구현 | 확인 |
|---|---|---|
| 설치 폴더 읽기 | index·blob·리소스 복호화·SJON·SMM, 번들 직접 열기 | [확인] index 246,782 / 135,796 경로, 모델 정의 12,387개 |
| 시트 리더 | Costume, CostumeModelVariant, CharacterPresetAppearance, Head/Hair/Eyes/Mouth, BodyTypes, 색 시트 4종. 행 크기 불일치 시 `SheetFormatException` | [확인] Costume 1,679행, 프리셋 119행, 변형 132행, 시트 기본색 전부 null 97행, HidingParts=Bottom 단독 329행 (기획서 수치와 일치), 읽기 약 200ms |
| 경로 검사 (Startup.md) | 경로 → 필수 index 4개 → index 형식 → 시트·심볼 존재 → 시트 해석. 실패 단계별 문구 | [확인] 빌드 실행: 잘못된 경로에서 팝업·원인 표시 |
| 경로 지정 팝업 | 입력란, 폴더 선택(Windows 대화상자), 다음·Enter, 종료, 검사 중 버튼 잠금, 공백·따옴표 정리. 성공한 경로만 설정에 저장 | 화면 [확인]. 폴더 선택 대화상자·클릭 조작은 [미확인] |
| 최초 외형 | 두 기본 프리셋 50% 무작위, 의상 없이 시작, 즉시 자동 저장 | [확인] 빌드 실행: 첫 실행 저장 → 재실행 시 같은 프리셋 복원 |
| 복원 실패 | 손상·형식 미지원 구분, 기존 파일 보존(`autosave.broken-*.json`), 백업 복구·다른 JSON·새 작업·종료 | 안내 화면 [확인]. 각 버튼 동작은 [미확인] |
| 자동 저장 | `persistentDataPath\autosave.json`, 임시 파일 후 교체 + `.bak`, 1초 유휴 후 저장, 종료 시 대기분 저장 | 첫 실행 저장 [확인] |
| 조립 | 몸·머리·헤어(체형별 행)·눈(머리형별 행)·의상(성별·나이별 모델), 포즈, 체형 블렌드셰이프·체형 머티리얼, 몸 가리기 근사, 모자 헤어 단계·HideHair 등 | [확인] 촬영: 두 프리셋, 초보자 상하의·신발, 원피스 |
| 의상 상태 | 적용 / 다른 의상에 가림 / 성별 불일치 / 외형에 맞는 모델 없음 / 없는 ID / 리소스 없음. 미적용 의상은 가림 효과도 없음 | [확인] 남성+여성 전용 원피스 → 미적용, 하의 유지. 여성+원피스 → 하의 가림 |
| 염색 | 파츠 키 + 슬롯으로 연결. Slot0~5는 파츠별, Skin·Hair·Hair1·Eye·Mouth는 외형 색(전체). 시트 null 슬롯은 원본 머티리얼 값 유지 | [확인] 원피스 Slot1만 #FF00FF 지정 시 해당 영역만 변경 |
| 카메라 | 좌·우 단독 회전, 좌+우 팬(화면 오프셋, 회전 중심은 캐릭터), 스크롤 줌, UI에서 시작한 드래그 차단, 포커스 잃으면 해제, 상태 저장 | 코드만. 조작은 [미확인] |
| 창 | 창 모드·크기 변경 허용, 기본 1280×800. 저장된 시점이 없으면 화면비에 맞춰 기본 거리 결정 | [확인] 1280×800, 560×1000 촬영 |

### 꾸미기 창 1차 내용 (2026-11-02)

| 기능 | 구현 | 확인 |
|---|---|---|
| 창 | `CustomizePanel`. 제목 영역 드래그 이동, 오른쪽 아래 ◢ 크기 조절, 기본 오른쪽 폭 340·높이 화면-24. 놓을 때 `settings.json`의 `customizePanel`에 저장 | 컴파일만 |
| 화면 안 보정 | 앱 창 크기 변경 시 먼저 크기를 줄이고 위치를 옮긴다. 보정된 배치는 저장하지 않는다(앱 창을 키우면 원래 배치로 복귀) — 미정 항목을 구현에서 고른 것 | 컴파일만 |
| 폴드아웃 | 기본 외형·얼굴형·헤어·눈·입 / 의상(모자·상의·하의·로브·장갑·신발)·액세서리(귀·눈·얼굴장식)·무기. 동시 펼침, 펼침 상태는 앱 설정(`openSections`) | 컴파일만 |
| 검색 드롭다운 | `SearchDropdown`. 이름·ID 부분 일치, 방향키·PageUp/Down·Enter·ESC, 바깥 클릭 닫기, 후보 9개 이상일 때만 검색란. 목록이 열려 있으면 투명 막으로 카메라 입력 차단. 결과 없음 문구와 "(착용 안 함)" 구분 | 컴파일만 |
| 외형 후보 | 꾸미기 숨김 행, 현재 성별·나이에 모델 없는 행, MinAge 미달 행은 후보에서 뺀다. 현재 선택은 조건에 안 맞아도 흐리게 남기고 빨간색·사유 표시 | 데이터 [확인]: 얼굴형 6, 헤어 113, 눈 61, 입 32그룹(숨김 제외) |
| 나이 | 프리셋에 나오는 나이(10~17 [확인])를 후보로 쓴다 [제안 구현]. 14 미만은 어린 외형 [추정] | 컴파일만 |
| 의상 후보 | 부위별 전 항목(Hat 291, Top 423, Shoes 344, Gloves 240, Weapon 181, Bottom 81, Robe 44, EyeDeco 31, EarDeco 26, FaceDeco 18 [확인]). 현재 외형에 안 맞는 항목은 흐리게. 같은 부위 안 같은 이름 42건이 있어 ID를 보조 표시 | 컴파일만 |
| 편집 반영 | `AppController.Edit`: 복사본 변경 → 조립 성공 시 교체·자동 저장 표시, 실패 시 이전 선택·장면 유지와 창 상단 오류. 카메라 시점·기준점은 유지. 의상 교체 시 그 부위 사용자 색 삭제(Dye.md 확정) | 컴파일만 |
| 상태 표시 | 미적용(성별·모델 없음 등) 빨간색 + 사유, 다른 의상에 가림은 노란색. 화면 구석 메모에서는 의상 상태 줄을 뺐다 | 컴파일만 |


## 3. 다음 할 일

- 공개 제한·호환 대체·부분 복원의 우선 보완은 아래 최신 인계 메모까지 완료했다. 다음은 실제 파일 대화상자·마우스/키보드 검수와 Appearance.md의 남은 꾸밈 범위다. 과거 항목의 미구현 설명보다 최신 구현·검증 기록을 우선한다.

0. 배경 환경 볼륨(Background.md 8장): 사용자에게 공개 3D 배경 4종의 게임 스튜디오 스크린샷을 받아 앱 촬영과 비교한다. 블룸·채도 차이가 크면 Beautify 값 읽기 + URP 근사를, 작으면 생략으로 확정한다. 접촉 그림자 값(발밑 어둡기)도 함께 맞춘다.
0. 스튜디오 배경의 실패 처리·복원 안내·표기 보완과 실제 조작 검증. 배경은 꾸미기 이력에 포함하고 코디 초기화 시 유지한다(2026-11-03). 조명 방향·원본 재현은 확인 대기다.
1. 사용자 확인: 에디터 Play·빌드에서 마우스 조작(카메라, 팝업 버튼, 폴더 선택 대화상자). 아래 4장의 확인 요청 참고.
2. 사용자 확인: 꾸미기 창 1차(4장 확인 요청 4). 문제 수정.
   - 발견한 문제: 모자 착용 시 헤어가 모두 사라짐 → 2026-11-19 수정. (무기 장착점은 2026-11-03 구현)
3. 사용자 확인: 색 적용(4장 확인 요청 5). 문제 수정.
4. ~~새 UI 구현·UI 연출~~ (2026-11-03 구현). 사용자 조작 확인 후 문제 수정(4장 확인 요청 7). 연출 시간은 체감에 따라 `UIAnim`의 Fast·Normal·Slow, 패널 `SlideSeconds`로 조정한다.
5. ~~시스템 메뉴·ESC·경로 적용 후 재시작·부분 복원 안내~~ (2026-11-03 구현). 사라진 염색 파트는 실제 조립 뒤 검사하도록 보완했다. 다른 설치 경로로 실제 재시작 시험은 남아 있다.
6. 눈·얼굴 셰이더의 게임 조명 재현, 얼굴형·키·나이.
7. 외형 꾸밈 범위(`Appearance.md`): 얼굴·몸꾸밈·수염은 2026-11-12에 구현했다. 화면 검증과 꾸밈 색 결정이 남았다. 환생 눈장식 6개는 기존 EyeDeco 의상 2종에 연결된다. 별도 안경 상태를 추가하지 않고 보상 프리셋의 색 슬롯 대응을 먼저 검증한다. 미정 규칙은 임의로 확정하지 않는다.
8. ~~초기화(`Reset.md`)와 꾸미기 이력(`UndoRedo.md`)~~ (2026-11-03 구현).
9. ~~외형 선택 보관·공개 호환 기본값 대체·부분 복원·ID/숨김 표기·UNITY_EDITOR 제한~~ (2026-11-03 보완·자동 검증). 전체 외형·의상 조합의 육안 검수는 별개다. 최초 초보자 의상은 구현돼 있으며 기존에 저장된 빈 의상은 강제로 채우지 않는다.
10. 남은 검증·조정: 키·나이 범위·조작 방식, 꾸밈 중첩·색상 연결, 카테고리 묶음·펼침 정책, 의상의 추가 공개 제어 참조. 게임 전체 공개 판정을 해석한 것으로 취급하지 않는다.
11. 소셜 모션 사용자 확인(`SocialMotion.md` 5장): 목록 열기·검색·선택·■·ESC, 재생 중 의상·성별 변경 이어 재생, 페이드·이펙트 모양. 남은 구현: 어린 외형 소품 크기 보정.
13. 2순위 남은 것(2026-11-20): ① 치마 본 보정(켜는 조건 해석 필요) ② 눈·얼굴 셰이더 게임 조명 — 게임 스크린샷 비교가 필요 ③ 머리·옷 물리 — 게임은 프리팹의 MagicaCloth 구성 요소를 쓴다. 전체 이식은 큰 작업이라 간이 스프링 본으로 할지 사용자 결정이 필요하다.
12. 마지막으로 접속한 캐릭터 불러오기와 미지원 항목(도구·무기 오라·세트 효과)을 구현했다. 남은 확인: 앱에서 오라 등급 바꾸기·실행 취소, 세트 연출 화면을 게임과 비교, 오라가 있는 실제 캐시·남성·어린·로브 캐릭터 불러오기 [미확인].

## 4. 인계 메모

- 함정(2026-11-19): `CharacterAssembler.BuildCore` 는 조립 중 리소스 실패(`Fail`)가 하나라도 있으면 모션·시선·연출을 붙이기 전에 돌아간다(앱은 `BuildValidated` 가 실패 파츠를 빼고 다시 조립한다). 스모크 테스트에서 모션 로그가 "입:" 뒤에 끊기면 이 경우다. 이제 스모크 로그에 "조립 문제" 줄을 남긴다.
- 셰이더 디스어셈블(2026-11-19): 번들 Shader(ClassId 48)의 compressedBlob 을 offsets·compressedLengths·decompressedLengths 로 LZ4 해제하면 DXBC 가 나온다. `d3dcompiler_47.dll` 의 D3DDisassemble 로 풀린다. 변수 이름표(RDEF)가 지워져 있어 이름으로는 못 찾고, 상수(예: 0.017453)·텍스처 샘플 순서로 위치를 찾는다. 도구는 저장소에 두지 않았다.
- 확인 요청(2026-11-19): 앱에서 ① 안대(레이어드 아이 밴디지·카마인 에버모어 로즈 패치)·나나미 켄토의 안경 착용 시 눈, ② 모자 + 귀장식(달걀 모자 등)에서 귀장식 줄 안내, ③ 별무리 글리터·운명의 교차 문신의 반짝임, ④ 의상 › 눈장식 목록의 "라운드 글래시스 (세피아)" 등 6개, ⑤ 류트 등 악기 사용 시 음표와 채집 도구 이펙트, 곡괭이 표시.

- 카메라 쳐다보기(2026-11-19): 보정은 `CharacterMotion.Sample` 안에서 파츠 전용 본 동기화 **앞**에 한다. 순서를 바꾸면 머리 전용 본(파츠별 다른 기본 회전)이 시선을 따라가지 않는다. 시험 촬영은 `CharacterLookAt.TargetOverride` 로 대상을 넣고 `SampleAll` 로 바로 맞춘다(dt < 0). 확인 요청: 앱에서 켜고 카메라를 좌우·위아래로 돌릴 때 부드럽게 따라보는지, 53° 넘게 돌리면 정면으로 돌아오는지, 소셜 모션 중 쉬었다가 끝나면 다시 보는지, 실행 취소·재시작 후 유지되는지.

- 헤어 단계·후드(2026-11-19): 게임 `StageObjectCostumeVariantsComponent`·`CostumeVisibleSetter` 는 선언만 있고 본문을 못 읽었다. 후드 헤어 단계 5·후드를 쓰면 모자 숨김·로브 우선은 추정이다(Equipment.md "후드·모자 덮개"). 게임에서 후드+모자 화면을 보면 먼저 맞춰 본다. 후드·덮개 결과(`HasHood` 등)는 조립기가 채우므로 색 편집 경로는 `CarryBuildResults` 로 넘긴다. 새 조립 결과 필드를 더하면 거기에도 넣어야 패널 줄이 사라지지 않는다.
- 확인 요청(2026-11-19): 앱에서 ① 모자(C1~C5)를 쓰면 헤어가 보이는지, ② 로브·원피스 후드 씀/벗음 줄과 모자 숨김 안내, ③ 기사 투구·노블리스 아너 바이저의 덮개 올림/내림, ④ 브레이드 트윈테일·러블리 트윈리본 롱헤어의 "헤어 장식 색" 줄, ⑤ 실행 취소·자동 저장 후 재시작에서 후드·덮개·장식 색이 유지되는지.

- 무기 오라(2026-11-18): 외곽선 재질은 `MobiCharacter` 셰이더 복사본에서 외곽선 패스(LightMode `SRPDefaultUnlit`)만 켠 것이다. 다른 캐릭터 재질은 `ConvertMaterial` 이 이 패스를 끄고, 셰이더도 등급 번호(`_WeaponGradeColor.w`) 4 미만이면 삼각형을 모은다. `ApplyPlanColors` 가 조립 중에도 불리므로 외곽선은 셰이더를 넘긴 뒤(조립 끝) 한 번만 만든다. 이 순서를 바꾸면 색이 0으로 남아 안 보인다(실제로 겪음). 원본 식대로라 외곽선은 희미하고 노이즈로 반짝인다(알파 평균 약 0.05).
- 세트 효과(2026-11-18): 시각 연출이 있는 세트는 설치본 5행뿐이다(JUJU 콜라보 4 + EmptyTimeline 1). 나머지 256행은 능력치만 있다. 에픽·전설 행은 의상·연출이 같아 시트 순서 첫 행(에픽)을 고르고 이름의 "(에픽)"을 뗀다.
- 도구(2026-11-17): 도구 행은 `CostumeData`(IsTool=true, CostumeType="Weapon")로 담는다. 의상 칸에서 행을 찾을 때는 `data.Costumes` 를 직접 보지 말고 `slot.Row(data)` 를 쓴다. 생활 도구의 사용 동작은 시트 직접 연결이 없어 이름 규칙(ToolIndices = 도구 종류, `Gathering_` 중 가장 짧은 이름)으로 고른다(WeaponPose.md).

- 몸 자르기(2026-11-13): 분리면 평면은 의상 자기 바인드 공간 값이라 의상마다 자세가 다르다(T자세 장갑 등). `Mesh_SP_<Id>` 메시의 주 본 × 바인드포즈로 기본 자세 월드로 옮겨 몸(기본 자세 BakeMesh)과 비교한다 [추정: 게임이 공간을 맞추는 방식은 미해석]. 몸 삼각형 번호는 SMM 파일 순서다(서브메시로 묶기 전). 일부 장갑(예: Female_Newbiecombat_Gloves_00)은 분리면 이름 L/R과 실제 위치가 반대라 손을 자르지 않는다 — 게임 데이터 그대로다. 디버그: `InstallSmokeTest -maOutfit` 로그의 "몸 자르기 …" 줄에 의상별 평면·부위·제외 수가 나온다.

- 배경 환경 볼륨(2026-11-12): 원본 얼굴 셰이더(Skin_Face)는 빛 색을 `_Global_Character_Light_Color`(알파 > 0)에서, 아니면 주광 색에서 가져온다(실험으로 확인). 주광 세기는 얼굴 밝기에 영향이 없었다. 볼륨을 바꿀 때 얼굴과 몸 색이 어긋나면 이 규칙(`EnvironmentVolume.SetCharacterLight`)부터 본다. `_Global_DimFog_*` 를 0 으로 두고 안개를 켜면 얼굴이 안개색으로 덮인다. 게임 실행 파일의 전역 이름 문자열은 il2cpp 이름 캐시가 패치와 맞지 않아 디스어셈블로 읽지 못했다. 전역 이름은 셰이더 속성 표의 이름과 볼륨 필드 이름의 대응이다.

- **마지막 접속 캐릭터 불러오기와 미지원 항목 구현 (2026-11-12, Claude Code)**
  - 사용자 결정(전부 AGENTS.md 7·10장): 메뉴 이름 "마지막으로 접속한 캐릭터 불러오기", 대상은 `<게임 사용자 데이터>\TitleDisplay.bin` 하나(SJON, 기존 `Sjon.Decode`로 읽힌다). 의상은 모델 이름만 있어 Costume 모델 이름 배열을 역조회한다. 색은 원색이라 가장 가까운 시트 색을 고른다. 불러오지 못한 부위는 미장착+오류 팝업, 숨김 항목은 일반 빌드에서 미장착.
  - 캐시 구조 [확인: 게임 타입 정보]: `ActorDisplayData` = Age·Gender·HeightModifier·SkinColor(RGBA)·BodyType·HeadType·HairType(시트 ID 그대로), BodyParts·Clothes·Accessories(각 `ModelParts[모델 이름]` + `DyeSlots{번호: RGBA}`), Tools{ToolIndex: part}, WeaponAuraGrade(Rarity), IsRobeWeared·IsHoodOn·IsCapClosed·IsWeaponHidden, CostumeSet. 실제 파일은 아직 열지 않았다.
  - 사용자 결정(구현 방식): 도구(악기·생활 도구)는 의상 › 무기 칸에서 무기와 택일한다. 모두 MainTool 슬롯이고 넣음·꺼냄 장착점이 있다 [확인: 모델 정의]. 무기 오라는 등급을 직접 고른다. 게임은 실제 장비 등급으로 정하며, 이펙트 연결 방법은 아직 찾지 못했다. 세트 효과는 착용 의상으로 자동 판정하기로 정했다(CostumeSetEffect 시트의 부위별 세트 목록·Set*ActivationEffectFX).
  - 확인 대기: 사용자는 얼굴·몸꾸밈 색을 컬러피커로 편집하기로 했다. 그런데 셰이더에 문신 색 칸이 없어 구현하지 않았다(Appearance.md "꾸밈 구현"). 사용자 답을 기다린다.
  - 함정: 현재 게임 `GameAssembly.dll` 은 기존 IL2CPP 덤프와 주소가 맞지 않아 메서드 디스어셈블이 깨진다. 셰이더 DXBC 디스어셈블은 된다(D3DDisassemble).
  - 함정: Unity 배치 모드가 가끔 라이선스 토큰 없음(`com.unity.editor.headless` not found, 종료 코드 198)으로 실패한다. 같은 날 다시 실행하면 성공했다. 실패하면 한 번 더 시도한다.

- **기획·구현 보완 (2026-11-03, Codex)**
  - 검토 기준과 현재 HEAD는 `main` `3b2ffab1dd46104cea7e4cac1824cd3155c7cab5`다. Claude Code가 남긴 C# 10개의 미커밋 작업을 보존하고 이어서 보완했다. 앞선 에이전트 설정 점검의 AGENTS.md 변경도 그대로다.
  - `CharacterAssembler.BuildValidated`: 실제 읽기에 실패한 모델을 메모리의 제외 집합에 넣고 계획을 다시 푼다. 필수 외형은 공개 기본값으로, 실패 의상은 미출력으로 재조립한다. 의상 한 선택의 모델 중 하나가 실패해도 해당 의상 전체와 가림/절단 효과를 제거한다. 몸이나 모든 기본값을 준비하지 못하면 예외로 거절한다. 일반 편집은 새로 생긴 실패를 승인하지 않는다.
  - `AppController.Prepare/Stage/Commit`: 준비본은 비활성 상태이며 승인 전 기존 장면을 건드리지 않는다. 취소·실패·종료 시 임시 캐릭터·배경을 정리한다. `StudioBackground.Prepare/Commit`으로 실제 프리팹 실패도 승인 전에 알 수 있다. 이력 복원은 배경까지 준비한 뒤 함께 확정한다.
  - 논리 선택과 색은 기존 필드에 남고 `appearance.substitutes`는 현재 표시 결과의 참고 기록이다. 매 조립마다 재계산한다. 색만 바꾸는 요청에서도 표시 모델이 달라지면 전체 조립 검사를 거친다. 시트에 현재/기본 변형이 없으면 관계없는 첫 행으로 대체하지 않는다.
  - 원본 보존은 준비·승인 뒤 교체 직전에 한다. 보존 실패는 중단·재시도로 처리한다. 가져오기·초기화에서 대기 중 이전 작업 저장이 실패해도 작업/이력을 교체하지 않는다. 중복 의상 부위 JSON은 염색 대응이 모호하므로 읽기 오류로 거절한다.
  - [확인] `dotnet build MobiAtelier.Editor.csproj -nologo -v q`: 런타임 포함 오류 0·경고 3. Unity 일반·Development Windows 빌드: 각각 오류 0·경고 0. 패키지·ProjectSettings는 변경하지 않았다.
  - [확인] `InstallSmokeTest.Policy`: 60개 항목 통과. 공개 판정·시각 해석, 숨김/직접 리소스 조립 차단, 남녀·나이·체형별 필수 파츠, 실제 프리팹/메시/입 텍스처 누락, 실패 의상 가림 해제, 부분 복원 승인/취소, 실패 편집/가져오기/이력의 장면·파일·이력 보존, 카메라 유지와 배경 이력을 검사한다. 설치 파일은 바꾸지 않고 시험 중 메모리 index/메시만 바꾼 뒤 복구한다. 실패 주입에 따른 예상 PlanException 로그는 시험 실패가 아니다.
  - [확인] 실제 일반 실행 파일의 가져오기 승인 대기에서는 원래 자동 저장 파일이 바이트 단위로 유지됐다. 일반·Development의 승인 후에는 직접 모자·미공개 상의의 파츠와 렌더러가 없고 숨김 머리는 Head_00으로 대체됐으며 원래 선택·염색은 보존됐다. 일반 빌드 시작 부분 복원의 원본 사본도 원래 파일과 일치했다. 실행 4개 모두 종료 코드 0·런타임 예외 없음.
  - 시험 인자: 빌드 `-maBuildKind development`; 촬영 실행에서 `-maDataDir`와 함께 `-maShotImport <JSON>`·`-maShotReport <텍스트>`를 쓸 수 있다. `-maShotUI partial`로 시험용 승인 경로를 선택한다. 공개 판정을 켜는 우회 옵션은 아니다. 촬영 실행만 `Application.runInBackground`를 켠다.
  - [확인] 에디터 정책 촬영의 공개 제한 모델 미출력·남성 머리 대체를 육안 확인했다. [미확인] 숨긴 Windows 창에서는 ScreenCapture가 실패했다. 실행 파일의 최종 결과는 조립/렌더러 보고서로 확인했으며 실제 팝업 배치·마우스/키보드·Windows 파일 대화상자를 확인한 것으로 쓰지 않는다.
  - 남은 꾸밈: 얼굴·몸꾸밈은 아직 상태/UI/렌더링 미연결이다. 환생 눈장식은 기존 EyeDeco에 연결된 6개 색 프리셋임을 확인했다. 색 필드의 정확한 런타임 슬롯 대응, 꾸밈 중첩/선택 개수/색 편집 규칙은 미정·미검증으로 유지한다.

- **Codex 재개 점검 (2026-11-03)**
  - [확인] 현재 Windows 환경에 Unity 6000.3.27f2·.NET SDK 10.0.403·Python 3.13.15가 있다. 아래 과거 기록의 "Unity·.NET SDK가 없다"는 이번 환경에 해당하지 않는다.
  - [확인] `dotnet build MobiAtelier.Editor.csproj -nologo -v q`: 런타임 포함 오류 0·경고 3. 두 csproj에 각 소스 폴더의 C# 파일이 모두 포함돼 있다. Unity 실행·빌드 촬영·사용자 조작은 이번 점검에서 하지 않았다.
  - [확인] `codex.cmd --version`은 0.166.0을 반환한다. 도구 실행 환경에서는 홈 디렉터리를 찾지 못해 PATH 별칭을 만들지 못했다는 경고가 함께 나왔다. 별도 CLI 대화 시작은 시험하지 않았으며 현재 Codex 세션의 파일 읽기·수정·컴파일은 가능하다.
  - 기존 미커밋 C# 10개는 이번 점검 전에 있던 작업이다. 공개 범위·호환 대체·부분 복원·ID 표기 관련 변경이 있으므로 과거 할 일만 보고 중복 구현하지 않는다. 이번 점검에서는 수정하거나 커밋하지 않았다.

- **소셜 모션 (2026-11-03, Claude Code)**
  - 시트 필드 순서: `SocialAction` 행은 `DisplayName:S EmotionBehaviorType:E DisplayPriority:I IconPath(S+S) BehaviourType:E Actions:[S+I] UnlockType:E SettingGroups:[K] U U [S] [S] PreviewActionName:S K S S S N`. `ActionStageTimeline` 은 preload 가 없고 마지막 두 문자열이 시작·반복 클립의 타임라인 이름(쉼표 구분)이다.
  - 소품의 캐릭터 셰이더(MMN/CH) 재질을 원본 그대로 쓰면 검게 나온다. `CharacterAssembler.ConvertMaterial` 로 바꾼다. 배경 셰이더(MMN/BG)·이펙트 셰이더(MMN/FX)는 원본 그대로 그려졌다 [확인].
  - 이펙트는 실행 중에는 실시간으로 재생하고, 시험 촬영(`CharacterMotion.SampleAll`)에서만 `ParticleSystem.Simulate` 로 시점을 맞춘다.
  - 행동 소품·이펙트는 캐릭터 아래에 만들어 캐릭터와 함께 없어진다. 다시 조립하면 `AppController.Build` 가 새 캐릭터에 다시 읽어 같은 시점부터 잇는다.
  - 덧그림 재질을 렌더러 재질 배열 끝에 붙이면 Unity 는 마지막 서브메시에만 그린다(머리는 서브메시 2개라 얼굴에 안 보였다). 같은 메시·본의 렌더러를 하나 더 만들어 그린다.
  - 마시멜로·팝콘 등 일부 소품은 MMN/BG 셰이더(LightMode BG)라 배경 패스(`StudioBackgroundRendering`)가 있어야 보인다. 앱은 배경 생성 때 설치되고, 스모크 테스트 outfit 모드도 설치한다.
  - 아직 안 한 것: 소품 크기 보정(`DefaultScaleSensitivity`), 소리(보류, `SocialMotion.md` 6장). 마우스 조작·이어 재생·전체 159개 재생은 [미확인](`Docs/SocialMotion.md` 5장).

- **무기 자세 (2026-11-03, Claude Code)**
  - 무기 모델에는 BoneTemplate 이 없다. 메시 본은 프리팹 Transform 에만 있어 `CharacterAssembler.CloneBones` 로 복제하고 그 파츠 전용 본(`partBones`)으로 찾는다. 복제 본은 `view.Bones` 에 넣지 않으므로 모션 후처리(위치·스케일 복원)를 받지 않는다.
  - 장착 본 회전은 `Quaternion.Euler(Rotation)` 로 넣었다. 촬영상 손·허리·등 위치가 맞았다. 축 순서가 게임과 같은지는 코드로 확인하지 않았다 [추정].
  - 무기 프리팹의 `Mesh_MainBuff`·`Mesh_SubBuff` 는 버프 효과용으로 프리팹에서 꺼져 있다(조립기는 프리팹 켜짐 상태를 따른다). 스모크 테스트 `Capture` 가 렌더러를 모두 다시 켜서 흰 껍데기가 찍히던 문제를 고쳤다(앱에는 영향 없음).
  - 조건 선택은 "조건 항목이 모두 맞는 행 중 항목 수 최대"다. 게임은 결정 트리(`AnimationStateSetSheetDataHolder`)를 쓴다 [추정 근사]. `Additional:`·`Equipment:` 조건 행은 사실이 없어 고르지 않는다.
  - 자세 변경은 캐릭터를 다시 조립한다(장착점이 조립 중에 정해진다).
  - 2차(같은 날): 전환·무기 자체 클립·꾸미기 포즈·부분 복원 안내를 구현했다. 남은 것은 `MeshVisibleStateOverrides`·`TransformOverrides` 와 실시간 앱의 전환 클립 육안 확인이다.
  - 전환은 `AppController.Build(next, transition: true)` 가 현재 계획을 `PlanResolver.Resolve(..., previous)` 로 넘길 때만 붙는다(편집·실행 취소·다시 실행). 무기 ID·성별·어린 외형이 같고 넣음↔꺼냄만 바뀐 경우다. 꺼냄↔꾸미기 포즈 사이에는 전환이 없다.
  - ToolState 트랙 해석은 "구간 안은 그 상태, 밖은 목표 상태"다 [추정]. 트랙 없는 클립은 처음부터 목표 상태라 검술사 꺼내기는 0초에 칼이 손으로 간다(손이 허리에 있어 자연스러웠다).
  - 꾸미기 포즈 조건은 계열 기본 직업 ID(`CombatJob:Warrior` 등)다. 자세 직업의 `CombatJobLineType` 을 사실로 더해 고른다(AnimationStateSet 조회에는 넣지 않는다. 넣으면 검술사가 견습 전사 행과 동점이 된다).

- **기본 아이들 모션 (2026-11-03, Claude Code)**: 사용자 결정으로 아이들 모션을 기본으로 항상 재생한다. 끄기·행동 선택 UI는 없다. 조립 직후 `Sample(0)`을 적용하므로 배치 촬영(InstallSmokeTest)도 아이들 첫 프레임으로 찍힌다(정지 포즈 T자 아님).
  - 파츠 전용 본은 Animator 바인딩이 겹치지 않게 조립 뒤 이름에 `#part`를 붙인다. 이름으로 본을 찾는 새 코드는 이 점에 주의한다.
  - `_COM` 높이 비율 = 보정 후 정지 포즈 `_COM` 높이 ÷ 포즈 데이터 원래 높이(로그 "아이들 모션 … _COM 비율"). 성인 남성 1.091, 성인 여성 1.000 [확인].
  - 얼굴: 조립 때 정지 포즈의 `_Facial.xaf` 첫 프레임을 적용한 뒤, 아이들 클립 이름의 `_Facial.xaf`·`_Facial.fbx`를 매 프레임 같은 시간으로 평가한다. 얼굴 본은 클립 위치를 그대로 쓴다(표정 이동이 위치에 있음). 남성은 얼굴 클립(5.67초)이 몸(5.83초)보다 짧아 끝 값에 멈춘다.
  - xaf 키의 Flat 탄젠트에 0이 아닌 탄젠트 값이 있으나 단위를 몰라 기울기 0 Hermite로 근사했다 [추정]. 어린 외형(HC)은 12세 촬영으로 [확인]. 체형별 클립은 배치 촬영으로 [확인]. 실시간 재생의 부드러움·프레임 비용은 [미확인].
- **모션 재생 가능성 시험 (2026-11-03, Claude Code)**: 결론은 [확인] 가능.
  - 클립: 몸 클립은 `Animation/Player/Human/<Female|Male|Infant>/Customize/<HF|HM|HC>@Customize_Stand_<Default|Fat|Muscle|Thin>.fbx`의 AnimationClip 서브에셋이다. `bundles.BundleOf(path).LoadAssetWithSubAssets<AnimationClip>(path)`로 열린다. Generic(휴머노이드 아님), 레거시 아님, 루프. 여성 7.77초·남성 5.83초 30fps [확인]. 본 82개의 회전·위치 + 78개 스케일 곡선, 경로는 `_COM/_Pelvis/...`(조립 루트 `Character` 기준)라 현재 본 계층과 그대로 맞는다.
  - 재생: Animator(아바타·컨트롤러 없음) + PlayableGraph(AnimationClipPlayable)로 된다. `AnimationClip.SampleAnimation`은 번들 클립에서 아무것도 바꾸지 않았다. 에디터의 `AnimationUtility.GetCurveBindings`도 0개라 곡선을 읽어 직접 재생하는 방식은 안 된다.
  - 함정: 클립이 위치·스케일까지 덮어써 나이·키(.bdc) 본 스케일과 발바닥 보정이 사라진다(12세가 성인 비율로, 남성 `_COM` 스케일 1.092 → 1). 시험에서는 평가 뒤 회전만 남기고 위치·스케일을 조립 값으로 되돌렸고, `_COM` 위치는 (정지 포즈 높이 ÷ 클립 높이)로 줄여 정상 비율로 보였다. 구현 때는 LateUpdate 후처리로 하고 비율 기준을 성별·나이별로 정확히 잡는다. `CharacterView.ApplyBones`와 매 프레임 충돌하지 않게 한다.
  - 남은 확인: 파츠 전용 본(같은 이름 본, 12세에서 3개)은 클립이 한쪽만 바인딩하므로 변화량 복사 필요. 얼굴은 `_Facial.fbx` 클립과 기존 xaf 중 무엇을 쓸지 미정. 헤어·치마 PhysX 본은 클립 값만 따른다(물리 없음). 시작 연출용 `Customize_Stand_Start_*`, 다른 행동 클립은 미시험.
- **UI 연출 (2026-11-03, Claude Code)**: 사용자가 DoTween(에셋 스토어) 임포트를 제안했으나 쓰지 않았다. UI가 UI Toolkit이라 자체 스타일 트랜지션으로 충분하고 패키지 추가도 필요 없다. `UI/UIAnim.cs`: FadeIn(투명·오프셋→제자리), FadeOutAndRemove, SetShown(display 요소 페이드, 사라지는 동안 클릭 차단, 의도 상태는 `IsShown`으로 확인), SmoothColor. 패널은 `CustomizePanel.StepSlide`가 0.22초 동안 진행도를 올리며 dock translate·탭·카메라 출력 영역을 매 프레임 같이 갱신한다(Camera.rect가 같이 움직여 캐릭터가 부드럽게 옆으로 간다). 모달을 연달아 열 때(메뉴→옵션)는 딤을 다시 페이드하지 않는다. F1 숨김 때 카메라 영역은 즉시 전체로 바뀌고 패널은 페이드로 사라진다. 시험 인자 `-maShotUIWait 초`로 연출 중간 프레임을 찍을 수 있다.
- **새 UI 구현 (2026-11-03, Claude Code)** — 아래 '최신 UI 확정'의 런타임 구현이다.
  - 파일: `UI/AppUI.cs`(층·A/B/C/D·모달·메뉴·옵션·알림), `UI/CustomizePanel.cs`(고정 패널·탭·크기 조절·이력 아이콘·상태 줄. 본문 항목은 그대로), `UI/ControlsHelpView.cs`, `UI/UIIcons.cs`(Painter2D 아이콘·툴팁), `UI/UIEscape.cs`, `State/EditHistory.cs`, `App/AppController.cs`(이력·ESC/F1/Ctrl+Z/Y·메뉴 동작·출력 영역), `WindowsDialogs.SaveFile`, `JsonStore.WriteUserFile`, `StateFactory.ResetFor/WithStarterCostumes`. 새 `.cs`의 `.meta`는 Unity가 만들었다(같이 커밋).
  - 구현 선택: 출력 영역은 `Camera.rect`로 나눈다(사용자 구도·FitSize 유지). 가로 패널은 위 54px를 비워 메뉴 아이콘 자리로 쓴다. 세로 창에서는 탭 높이만큼 화각·안내를 올린다.
  - 사용자 결정(2026-11-03): 패널 크기 조절 손잡이를 없앴다. 가로 창 폭 360, 세로 창 높이 45%(카메라 영역이 최소 160px 또는 30% 남도록 보정). 설정의 `customizePanel`·`customizePanelTall`·`PanelLayout`도 지웠다(옛 settings.json의 해당 키는 무시된다). 관련 기획서·AGENTS.md를 고쳤다.
  - 구현 선택: 경로 적용 후 '다시 시작'은 앱 씬을 다시 불러오는 앱 내부 재시작이다(프로세스를 새로 띄우지 않는다). 시작 흐름 전체(경로 검사·자동 저장 복원·패널 닫힘·빈 이력)가 다시 실행된다. 씬이 Build Settings에 없으면 실패로 보고 이전 경로를 되돌린다.
  - 이력: 꾸미기 값 스냅숏(카메라 제외)을 쌓고, 복원 시 현재 카메라를 덮어 쓴다. 외형·의상·염색이 같고 배경만 다르면 캐릭터를 다시 조립하지 않는다. 개수 제한은 두지 않았다. 피커 미리보기는 이력에 넣지 않는다(적용만 기록).
  - ESC: 모달 → 드롭다운 → 컬러피커(취소) → 자세한 안내 → 메뉴 열기. 드롭다운·피커가 자기 키 이벤트로 ESC를 처리하면 `UIEscape.Consume()`으로 같은 ESC가 메뉴를 열지 않게 한다. 메뉴를 열 때 열린 피커는 적용 후 닫는다(SystemMenu.md 제안).
  - 메뉴 바깥 클릭은 버튼을 놓을 때 닫는다(닫는 클릭이 카메라 드래그로 이어지지 않게).
  - 런타임 UI Toolkit은 `tooltip`을 표시하지 않아 `Tips`로 직접 띄운다. 키보드(Tab) 포커스가 오면 같은 이름을 바로 보이고 테두리를 강조한다(마우스 클릭으로 생긴 포커스는 제외). 화면 낭독기용 접근성 API는 쓰지 않았다.
  - 맑은 고딕은 `\`를 `₩`로 그린다. 경로 입력란·현재 경로 표시는 Segoe UI(한글은 맑은 고딕 대체)로 바꿨다(`AppUI.ApplyPathFont`). [확인: 옵션 화면 촬영]
  - 최초 실행·초기화는 초보자 상의·하의·신발을 입힌다. 이미 저장된 빈 의상 작업은 바꾸지 않는다.
  - 시험 인자: `-maShotUI open,menu,options,hidden,nohelp,edit,bg,lens3,undo`(촬영 1초 전 적용). 빌드 촬영 [확인] 1280×800·560×1000: 닫힘+첫 사용 안내, 열림(왼쪽 출력 영역), 메뉴, 세로 아래 패널(탭·안내 겹침 없음), F1 숨김(눈만 남음), 옵션(같은 경로면 적용 비활성), 상의 해제·배경·3x 뒤 실행 취소 → 배경만 복원·3x 유지·다시 실행 활성.
  - 창 닫기·Alt+F4: 공개 빌드에서 `Application.wantsToQuit`가 false를 돌려주고 메뉴를 연다. 앱이 정한 종료(`Quit()`·시험 촬영)만 `quitApproved`로 통과한다. 작업 화면 전(state 없음)과 에디터는 막지 않는다. [확인: 빌드에 CloseMainWindow 보낸 뒤 프로세스 유지·메뉴 표시 촬영]
  - 부분 복원(`App/RestoreCheck.cs`): 형식이 정상인 파일에서 없는 외형·색·의상 ID, 공개 빌드의 직접 리소스, 표시할 수 없는 배경을 찾는다. 시작 복원은 부분 복원/백업(문제 없을 때만)/다른 JSON/새 작업/종료, 가져오기는 부분 복원/취소. 승인 전에는 자동 저장·작업·이력을 건드리지 않고, 시작 복원 승인 시 원본을 `autosave.partial-*.json`으로 보존한다. ID·색은 지우지 않는다. [확인: 없는 상의·배경 ID 자동 저장으로 안내 촬영] 부분 복원 승인 뒤 화면은 [미확인].
  - 배경: `StudioBackground.Apply(b, keepOnFailure)`가 새 배경을 먼저 준비하고 성공해야 교체한다. 편집·실행 취소 중 실패는 이전 배경·저장·이력을 유지한다(LastFailed). 시작·가져오기의 실패는 기존처럼 색 배경 대체. 패널의 배경 줄에 저장 ID를 표시할 수 없는 사유와 실제 대체 배경을 노란 글자로 보인다. 표기는 `ID - 이름`, 3D는 보조 표시. 실제 리소스 누락 조작은 [미확인].

- **최신 UI 확정 (2026-11-03, 아래 과거 기록보다 우선)**
  - WindowAndCustomizationUI.md: 패널은 고정 접이식, 가로 오른쪽/세로 아래. 시작 시 닫힘. 드래그 이동은 제외, 크기·펼침 기억은 유지. 출력 영역만 바꾸며 회전·팬·거리·화각은 유지한다. 눈/F1 전체 숨김과 패널 접기는 구분한다.
  - UndoRedo.md: 외형·의상·염색·배경 및 도입 후 연출 편집값만 이력에 포함한다. 카메라·UI·옵션은 제외한다. 전체 EditState 교체로 카메라까지 되돌리지 않는다. 제목/탭 영역의 아이콘과 Ctrl+Z/Y를 쓴다.
  - SystemMenu.md: 메뉴 항목은 내보내기(Export)·가져오기(Import)·초기화·옵션·종료. ESC는 메뉴 열기/닫기이며 종료 확인 팝업은 폐기한다. 초기화 확인·저장 실패·부분 복원은 유지한다. ExitPopup.md는 이전 링크용 대체 안내다.
  - Options.md: 경로만 우선 확정. 정상 경로 검사·현재 작업 저장·설정 기록 성공 후 재시작한다. 실패 시 현재 실행을 유지한다. 새 실행에서 작업은 복원하되 이력은 비운다. 감도·휠 반전·UI 크기·힌트 표시는 후보일 뿐이다.
  - 명칭은 UI·도움말·기획에서 Export/Import로 통일한다. 자동 저장·시작 복원은 그대로다. 코드 식별자·SaveLoad.md 파일명은 변경하지 않았다. 새 UI·명칭의 런타임 구현과 검증은 아직 하지 않았다.
  - 정적 확인: 변경·추가된 Markdown의 로컬 문서 링크·코드 블록 닫힘·신규 기획서 README 목록을 확인했고 git diff --check를 통과했다. 런타임 소스 변경은 없다.


- **UI·플로우 재정리 (2026-11-02)**: 신규 배치·도움말은 제안이다. 이동·리사이즈 가능한 꾸미기 창 하나와 가로 오른쪽/세로 아래 기본 배치를 유지한다. 공통 파일·이력·초기화 작업은 창 상단, 외형·의상·배경은 본문, F1·화각·조작 안내는 보기 도구다. 염색은 각 의상 아래의 파트 견본에 둔다. 연출은 시선·행동 검증 후 추가하며 미지원 빈 메뉴는 노출하지 않는다. ControlsHelp.md의 첫 정상 진입 안내·확인 기억·ESC 순서도 미구현 제안이다. Ctrl+S/O 같은 미정 바인딩은 추가하지 않았다. 배경 undo·초기화 유지 여부와 원본 재현 제한은 이전 제안 상태를 유지한다. 이번 작업은 문서만 수정했으며 커밋·푸시는 별도 요청 전이다.

- **배경 재검토 (2026-11-02, main fb22b2e)**: Background.md 7장에 검토 결과를 기록했다. 우선 수정은 배경 교체 실패 시 이전 장면 보존·실패 ID 자동 저장 방지, 누락/제한 ID 대체 사유와 부분 복원 안내, 공통 ID-이름 표기다. 배경 undo 포함·코디 초기화 시 배경 유지는 제안으로 유지한다. 구름 렌더 순서·광각/팬 벽 숨김·원본 얼굴 셰이더 조명 영향은 실제 확인 대기다. 이번 재검토에서는 런타임 코드를 수정하거나 Unity·빌드를 재실행하지 않았다.

- **스튜디오 후속 결정·부분 구현 (2026-11-02)**
  - AppUI의 버튼·F1으로 꾸미기 층·메모·화각 버튼을 숨긴다. 복귀 버튼과 모달·로딩은 남긴다. 컬러피커·미확정 미리보기는 숨겼다가 복원한다. UI 숨김은 장면·JSON·이력에 넣지 않는다.
  - CameraController는 기존 30도 수직 FOV를 1x로 두고 초점거리 배율 관계로 0.5/1/3x를 계산한다. 거리·회전·팬은 유지한다. 원본 게임 렌즈 수치 재현은 아니다. CameraState.lensMultiplier가 없거나 0·미지원 값이면 1x로 읽는다. 기본 거리 계산·ResetView도 1x 기준이다.
  - 화각 변경은 기존 Committed 경로로 자동 저장한다. 실행 취소 기능 자체는 기존처럼 후속 구현이므로 화각 undo 완료로 보고하지 않는다.
  - 시선 근거: exact LookAt 시트의 Humanoid 머리·양쪽 눈 설정, 카메라 타깃 보정 선언, 앱 본 Transform 접근. 아직 런타임 시선 추적을 추가하지 않았다. 메서드 선언은 보정식 검증이 아니다.
  - 행동 근거: approx SocialAction 159행, 정적 본 TRS·AssetPath, 얼굴 XAF 시간 트랙·몸/얼굴 대응 시트. 정적 본 JSON은 애니메이션 클립이 아니다. 몸 클립의 설치본 번들 로딩·시간 평가, 얼굴 XAF 전체 평가, 체형·나이·파츠 전용 본 보존이 선행이다. StudioFeatureReview.md를 따른다.
  - 다른 에이전트가 진행 중인 배경 기능·기획은 이번에 수정하지 않았다. 별도 무기 표시 옵션·촬영 프레임/화면비는 보류다. 원격 main과 합칠 때 배경 관련 다른 변경을 보존한다.
  - 이 환경에는 Unity·.NET SDK·게임 설치본이 없다. Unity 컴파일·빌드·플레이 검증이 필요하다. 검수: F1/버튼 숨김·복귀, 컬러피커 미리보기 보존, 모달 중 차단, 화각 변경과 휠 독립, 렌즈 포함/누락 저장 복원, 세로·가로 창에서 버튼 접근. 커밋·푸시는 이번 요청에 포함되지 않았다.
  - 정적 검증 [확인]: 변경한 C# 4개 파일의 Tree-sitter 구문 분석 오류 0, 렌즈별 FOV 계산·투영 배율 관계 확인, 변경 문서의 로컬 링크·git diff --check 통과. Unity API 타입 검사·실제 JSON 역직렬화·UI 조작 시험을 대신하는 검증은 아니다.

- **레퍼런스 비교 보강 (2026-11-02)**: ReferenceGapReview.md의 즐겨찾기·최근 사용, 시점 버튼, PNG·코디 목록 복사, 코디만 불러오기, A/B 비교는 모두 제안이다. 사용자 채택 전 필수 구현 목록에 넣지 않는다. 색 팔레트·부분 색 복원 등 기존 제외 범위를 다시 결정 대기로 만들지 않는다.


- **최신 기획 결정 (2026-11-02, 아래 과거 기록보다 우선)**
  - 숨김 데이터·시트 없는 직접 리소스는 UNITY_EDITOR 전용이다. 공개 옵션은 없고 JSON·자동 복원·Development Build도 출력 우회 수단이 아니다.
  - HideOnCustomize=true는 숨김이다. Costume에는 같은 플래그가 없으며 미래 MarketCollectionDiscloseTime을 보수적인 에디터 전용 기준으로 사용한다. 도감 공개 시각을 게임 전체의 숨김 플래그로 단정하지 않는다.
  - 아이템 표기: 실제 ID - 이름, 숨김은 [숨김] ID - 이름. Costume.Id는 문자열일 수 있으며 Index를 쓰지 않는다.
  - 최초 의상은 Fashion_NewbieSword_Top/Bottom/Shoes다. 초기화는 현재 성별 기본 프리셋·같은 초보자 의상·기본색·기본 카메라로 복원하고 UI·환경 설정은 유지한다. 확인 팝업을 제공한다.
  - 호환되지 않는 필수 외형은 이전 선택·색을 보관하고 공개된 호환 기본값으로 표시한다. 제거 가능한 장식은 선택을 보존하고 미출력한다. 상세는 Appearance.md를 따른다.
  - 구매·해금 조건은 공개 항목의 선택을 잠그지 않는다. 일부 누락·제한 항목 복원은 안내 후 승인하며 ID·색과 원본을 보존한다.
  - 경로 기억·손상 파일 보존·복구 안내·자동 저장 유휴 합치기·임시 교체·백업을 확정했다. 연속 모델 로딩은 마지막 유효 요청만 확정한다.
  - 이번 변경은 기획 문서만 반영했다. 에디터·빌드 동작을 구현·검증한 결과가 아니다.

- **사용자에게 받은 결정 (2026-11-01)**
  - `com.unity.inputsystem` 1.12.0 → 1.20.1. 1.12.0은 Unity 6000.3.27f2에서 패키지 자체가 컴파일되지 않았다(CS0117 `BuildTarget.ReservedCFE`).
  - `com.unity.modules.assetbundle` 추가(번들 직접 열기에 필요).
  - ProjectSettings: 창 모드(Windowed)·`resizableWindow` 켬·기본 1280×800, Build Settings 씬을 `Assets/Scenes/Main.unity` 하나로. `ProjectSetup.Run`이 설정한다. Unity가 `ProjectSettings/SceneTemplateSettings.json`을 새로 만들었다.
- **기획 결정 (2026-11-01, 원격 기획 보강 커밋)**
  - 초기화 버튼을 제공한다. 당시 미정이던 초기화 기준은 위 2026-11-02 결정과 Reset.md로 갱신했다.
  - 실행 후 장면 편집 이력은 현재 실행 중에만 유지한다. JSON 불러오기·초기화 성공 시 실행 취소·다시 실행 이력을 모두 비우며 해당 동작 자체도 되돌릴 수 없다. 취소·실패 시 기존 이력을 유지한다.
  - 무기는 직업 선택 없이 전체 외형을 자유롭게 선택한다. EquippableCombatJobs로 제한하지 않으며 실제 모델 호환·장착점 처리는 별도로 검증한다.
  - 착용·숨김 규칙 대부분은 원본 시트·모델 정의를 해석할 구현 과제다. 복합 슬롯 충돌의 정확한 적용 순서는 덤프 선언만으로 확인하지 못했다. 근거는 Equipment.md에 정리했다.
  - 염색 UI는 실제 염색 파트만 표시하고 컬러피커로 편집하면 된다. 기존 HEX 입력을 유지하며 영역 강조·부분 복원·별도 RGB 입력은 추가 결정 대기에서 제외했다.
  - 외형 필수 범위는 Appearance.md를 따른다. 환생 화면에 나온 항목 전체가 대상이다. 눈장식은 눈썹이 아니라 안경 등의 선택이다.
  - 외형·의상의 큰 카테고리는 폴드아웃, 후보형 세부 항목은 검색 가능한 드롭다운이다. 아이콘 격자는 필수가 아니다. 카테고리 묶음과 키·나이 조작 방식은 제안·미정으로 구분했다.
  - 얼굴꾸밈·눈장식·몸꾸밈의 기술 연결은 추가 검증해야 한다. 이번 작업은 기획 문서 보강이며 Unity 구현·렌더링을 확인한 결과가 아니다.
  - 최초 외형은 MaleAppearance_Default_01 / FemaleAppearance_Default_01 중 동일 확률로 선택한다. 마지막 작업이 있으면 무작위 선택 없이 복원한다. 당시 미정이던 초기 의상은 위 2026-11-02 결정으로 갱신했다.
  - 임의 RGB 색상을 허용한다. 게임 팔레트·CanChangeColor 정책으로 입력을 잠그지 않는다. 실제 셰이더 연결 지원 여부는 별도로 검증한다.
  - Costume.DyeColor0~5의 null은 초기색 부재가 아니다. 원본 재질·텍스처 상태에 유효한 시트 기본값을 덮어쓰고, 복원 시 저장된 사용자 색을 적용한다.
- **채택한 제안 (2026-11-02 확정으로 갱신)**: 마지막 정상 경로 기억, 손상 파일 보존과 복구 안내(백업·다른 JSON·새 작업), 자동 저장 유휴 합치기·임시 파일 교체·백업. 관련 기획서에 확정으로 반영했다.
- **추정으로 구현한 것**
  - 14세 미만을 유아 모델(Infant*/Boy/Girl)로 본다. 성인 몸 .bdc 는 14~17세, 어린 몸 .bdc 는 10~13세만 있다 [확인 2026-11-02].
  - 외형 색 연결: Skin=SkinColor.ColorCode, Hair=HairColor.ColorCode1, Hair1=ColorCode2(HairType TwoColor일 때만), Eye=EyeColor, Mouth=MouthColor. Skin_Details0/1(손바닥 등)은 원본 값 유지 [추정].
  - 포즈: `H{M|F|C}@Customize_Pose_08_{체형}`, 없으면 `_Default`.
  - 모자 헤어 단계: 모자 메시 `Mesh_Helmet_*_C<n>` → 헤어 `H<n>` [추정].
- **알게 된 함정**
  - 눈 흰 사각형의 원인은 셰이더가 아니라 표시 여부였다. 감은 눈 평면 `Mesh_EyeBasePlane`은 프리팹에서 꺼져 있는데 조립기가 켜고 있었다. 이제 프리팹 메시의 켜짐·꺼짐을 따르고, 포즈의 얼굴 애니메이션(`<포즈>_Facial.xaf`)에서 첫 프레임의 Visibility 값을 적용한다. 게임은 눈·입술 메시 표시를 이 애니메이션으로 정한다(`EyeVisibleSetter`).
  - 눈 셰이더는 원본을 쓴다(`CharacterAssembler.KeepOriginalEyeShader`). 대체 셰이더는 눈 테두리·그늘이 두껍고 검게 나왔다. 원본도 게임 조명 전역값이 없으므로 게임 화면과 똑같다고 보지는 않는다. 비교는 `InstallSmokeTest`의 `-maEyeShader original|mobi`로 한다.
  - 얼굴 애니메이션에는 눈 깜빡임 블렌드셰이프(MT_Blink*)도 있다. 첫 프레임 값은 모두 0이라 적용하지 않는다. 셰이더 값 트랙(`_MouthShowType`, `_EmotionMouthMap*` 등)은 첫 프레임 값을 적용한다.
  - 입(CharacterMouth)은 메시가 아니라 텍스처 모델이다. 모델 정의의 `AssetPathToLoad`(예: `Female_Lips_01.tga`)를 얼굴 머티리얼 `_MouthMap`에, `TextureInfo`의 BaseTexcoord·Rotation·PushStrength를 `_MouthMapScalePosition`·`_MouthMapRotation`·`_MouthPushStrength`에 넣는다(게임의 `MaterialPropertySetter.ApplyTextureModelPart`와 같은 대응). 꾸미기 포즈 첫 프레임은 `_MouthShowType = 0`(기본 입 맵)이다.
  - 입 합성 계산이 원본 `MMN/CH/Skin_Face` 셰이더 안에 있어 머리 파츠도 원본 셰이더를 쓴다(`CharacterAssembler.KeepOriginalHeadShader`). 대체 셰이더로는 입이 보이지 않는다. 얼굴 피부색(_DyeColor1)은 그대로 반영되며 몸과 톤 차이는 크지 않았다(촬영 비교, `-maHeadShader original|mobi`). 원본 셰이더는 그림자를 받지 않아 얼굴이 몸보다 평평하게 보인다.
  - 입 모델의 DyeInfo `Mouth` 슬롯은 메시 번호 0을 가리키지만 입 모델에는 메시가 없다. 입 색(CharacterMouthColor)의 연결 속성은 모른다[미확인]. **사용자 결정(2026-11-01): 입은 색을 선택하지 않으므로 입 색은 다루지 않는다.** 입 텍스처의 원래 색으로 표시한다.
  - 얼굴형 맞춤(FacialMorph), CostumeModelVariant 예외 모델은 아직 적용하지 않는다(시트는 읽음).
  - **키·나이 (2026-11-02)**: 몸 모델 옆 `<몸>.bdc.sjon.bin`을 쓴다(`BodyDiversity`).
    - 규칙 [확인: 덤프·디스어셈블]: (나이, 체형 BlendshapeName)별 Short·Medium·Tall 데이터. `GetHeightStateFromHeightModifier`는 min -1, mid 0 — HeightModifier ≤ 0 이면 Short→Medium t = HM + 1, > 0 이면 Medium→Tall t = HM. 보간은 a + (b − a)·t. HeightEnum 값은 Tall 0, Medium 1, Short 2.
    - 함정: .bdc 스케일은 본마다의 누적(최종) 스케일이다(14세는 몸통·팔다리 모두 0.787, 머리 0.97). Unity 로컬 스케일 = 자기 값 ÷ 데이터가 있는 가장 가까운 조상 값. 위치는 그대로 로컬 위치. 그대로 로컬 스케일로 넣으면 곱해져 극단적으로 작아진다.
    - 함정: `_COM` 데이터 위치는 (0,0,0)이다. 게임은 COM을 따로 맞춘다(comFollowTransforms) [추정]. 데이터 위치를 쓰지 않고, 적용 전후 발바닥 본(`_L/_R Sole`) 높이 차이만큼 `_COM`을 올리거나 내린다 [구현 선택].
    - 촬영 확인 [확인]: 10·13·14세, 17세 작게·보통·크게, 남성 17세 보통·크게, 의상 입은 10·12·14·17세. 17세 보통은 스케일 1·보정 0이라 이전과 같다. 키 참고값(HeightHint) 예: 여성 17세 159.6/167.8/180.5, 14세 보통 138.0, 10세 보통 100.4.
    - 키 조절 UI: 2026-11-03 외형 "키" 슬라이더(-1~+1)로 추가했다(아래 최근 작업). 얼굴 `Adolescent` 블렌드셰이프(나이 표현 [추정])는 적용하지 않았다. 편집으로 나이가 바뀌어도 카메라 거리·기준점은 그대로다.
  - (2026-11-13 갱신) 몸 자르기는 `.ClothingUnit.bytes` 규칙으로 바꿨다. 옷끼리 자르기·옷 밀어내기는 생략이다(ImplementationGaps.md).
  - UI Toolkit 기본 글꼴에 한글이 없어 Windows의 맑은 고딕을 런타임에 불러온다(`AppUI.ApplySystemFont`). 글꼴 파일은 포함하지 않는다.
  - 폴더·네임스페이스 규칙은 사용자 결정이다: 프로젝트 이름 폴더 없이 `Assets\` 바로 아래, 네임스페이스 없음(`AGENTS.md` 5장).
  - Unity가 패키지 재임포트·빌드 때 `Assets/Settings/`의 URP 설정 에셋 4개와 `ProjectSettings/UnityConnectSettings.asset`을 스스로 다시 썼다. 커밋 포함 여부는 사용자가 정한다.
  - 에디터 배치 시험의 촬영본·빌드는 세션 임시 폴더에 두었고 저장소에 넣지 않았다.
- **꾸미기 창 1차 (2026-11-02)**
  - 배치 모드가 라이선스 오류로 시작되지 않은 적이 있다(`No valid Unity Editor license found`, 종료 코드 198). PC 잠금은 원인이 아니었고, 사용자가 에디터를 GUI로 한 번 켰다 끄자 해결됐다. 같은 오류가 나면 사용자에게 에디터 실행(로그인)을 요청한다.
  - 빌드 실행 촬영 [확인] 1280×800: 창 배치·폴드아웃·드롭다운 필드 표시, 원피스 착용 시 하의 노란색 가림, 남성 전환 시 여성 전용 원피스 빨간색·하의 재표시. 드롭다운 열기·검색·드래그 등 조작은 [미확인].
  - 세로 창(560×1000)에서는 기본 배치(오른쪽, 폭 340)가 캐릭터를 대부분 가리고, 패널 뒤로 캐릭터가 비쳐 보인다(PanelBg 알파 0.98인데 투명도가 더 커 보임, 원인 [미확인]). 기본 위치·크기는 기획상 미정 → 사용자 결정 필요.
  - 새 `.cs`가 csproj에 없을 때의 대안: `MobiAtelier.Runtime.csproj`를 세션 임시 폴더로 복사해 상대 경로를 `D:/MobiAtelier/...` 절대 경로로 바꾸고 새 파일 `<Compile>`을 더한 뒤 `dotnet build`. 이번 두 파일(`UI/SearchDropdown.cs`, `UI/CustomizePanel.cs`)은 그렇게 오류 0으로 확인했다. 두 파일의 `.meta`는 Unity가 만들었다(미커밋) → 같이 커밋.
  - **피드백 반영 (2026-11-02)**
    - 사용자 결정: 카테고리는 "외형"(성별·체형·나이·얼굴형·헤어스타일·눈 모양·입 모양)·"의상"(모자·상의·하의·로브·장갑·신발·귀장식·눈장식·얼굴장식·무기) 둘. 세로로 긴 창은 꾸미기 창을 아래쪽에 배치. Appearance·Equipment·WindowAndCustomizationUI에 반영.
    - 예전 설정의 폴드아웃 키(basic, clothes 등)만 저장돼 있으면 무시하고 기본값(둘 다 펼침)을 쓴다. 가로·세로 배치는 `customizePanel`·`customizePanelTall`로 따로 저장한다.
    - 빌드 실행 촬영 [확인] 1280×800, 560×1000: 두 카테고리·들여쓰기, 세로 창 아래쪽 배치, 스크롤바와 손잡이가 겹치지 않음. 끌기 조작은 [미확인].
  - **몸 관통 수정 (2026-11-02)** — 사용자 촬영: 매혹의 몽마 모자·원피스·장갑·힐 조합에서 팔이 꺾이고 맨발이 하이힐 밖으로 보임.
    - 발: 몸 메시의 평평한 발이 하이힐을 뚫고 나왔다. 몸을 끈 진단 촬영에서 하이힐만 정상으로 보였다 [확인].
    - 팔: 장갑 BoneTemplate의 `_L/_R Forearm Twist` 기본 회전이 몸과 약 60° 다르다(장갑 (0.005, 0.506, -0.001, 0.862), 몸 회전 없음). 이름 기준 병합으로 몸 본 하나만 쓰면서 장갑 아래팔이 꺾였다. 이제 기본 회전이 다른(|내적| < 0.999) 같은 이름 본은 파츠 전용 본으로 두고, 포즈는 기존 본 기본 자세 대비 변화량만 더한다(`CharacterAssembler` "파츠 전용 본"). 같은 조건의 템플릿은 의상·헤어 등 3,747개 중 609개, Forearm Twist는 9개다 [확인: 덤프 JSON 전수].
    - 몸 자르기: 게임은 몸 삼각형의 부위(PoseSegment: 위팔·아래팔·손·허벅지·종아리·발 등)와 의상 절단면(NativeSlicer: 평면 + 부위 마스크)으로 자른다(`FittingCutPolicy`). `.ClothingUnit.bytes`는 아직 해석하지 않았으므로 근사한다: 부위는 몸 정점의 주 본 이름, 절단면은 `Mesh_SP_{L|R}{Hand|Arm|Foot|Leg}` 정점의 중심·면 노멀, 덮는 쪽은 같은 의상의 그 팔다리 정점이 많은 쪽, 적용 부위는 의상이 정점을 가진 부위만(`BodyCut.LimbCuts`). 하이힐 발 절단면은 발바닥 아래 수평면이라 부위 제한이 없으면 다리 전체가 잘린다.
    - 의상 피부 조각(`ClothingMaterialType=Skin`, 예: 원피스 `Mesh_Skin_Body`)도 기존 면 근접 판정에 넣었다. 그 자리의 몸 대신 그려지는 조각이다 [추정: 덤프 구조와 촬영].
    - 촬영 확인 [확인]: 몽마 4종 조합(맨발 사라짐, 장갑 팔 정상), 장갑만, 하이힐만, 힐러 원피스, 초보자 상하의·신발(변화 없음). 시험: `InstallSmokeTest -maOutfit "이름:Top=ID,Gloves=ID;이름2:..." [-maOutfitVariants]`.
    - 같이 발견했지만 고치지 않은 것: 모자 착용 시 헤어 H0~H5가 모두 꺼진다(로그: 헤어 단계 H1인데 H0~H5 렌더러 모두 꺼짐. 프리팹 켜짐·꺼짐을 따르는 처리 때문으로 보임 [추정]). 무기는 손에 붙지 않고 바닥 근처에 놓인다(장착점 미구현).
  - "얼굴형"은 CharacterHead(계란형·갸름한·동그란·각진·날렵한(여)·다부진(남))로 연결했다. 얼굴 맞춤(FacialMorph)과는 다르다.
  - Head_04_F/M은 성별 전용·MinAge 17이다. 성별을 바꾸면 선택은 유지되고 머리 모델이 출력되지 않는다(빨간색 표시). 외형 파츠 호환 정책이 미정이라 대체하지 않았다 → 사용자 결정 필요.
  - 의상 EyeDeco(31행)는 의상 > 액세서리에 넣었다. 외형 기획의 눈장식(CharacterCustomizeEyeDeco → 보상 프리셋 → Fashion_Glasses)과 같은 슬롯일 가능성이 높다 [추정]. 중복 상태를 만들지 않도록 외형 눈장식 구현 때 정리한다.
  - 카테고리 묶음·이름·첫 펼침 상태(기본 외형·헤어·의상 펼침)는 기획상 제안·미정이다. 구현값일 뿐 확정이 아니다.
- **사용자 확인 요청** (직접 조작해 보지 못함)
  1. 에디터에서 `Assets/Scenes/Main.unity` Play: 캐릭터가 뜨는지, 좌·우·좌+우 드래그와 휠이 기획대로인지.
  2. `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MobiAtelier\settings.json`의 installPath를 없는 경로로 바꾸고 Play: 팝업의 폴더 선택 버튼이 Windows 폴더 대화상자를 여는지, 취소 시 입력값이 유지되는지, 정상 폴더 선택 후 다음으로 진행되는지.
  3. 위 폴더의 `autosave.json`을 망가뜨리고 Play: 복구 안내의 각 버튼이 동작하는지.
  4. 꾸미기 창: 오른쪽에 창이 뜨는지, 제목 드래그 이동·◢ 크기 조절·재실행 시 복원, 앱 창을 줄였을 때 창이 화면 안에 남는지. 드롭다운 열기·검색·방향키·Enter·ESC·바깥 클릭, 목록 위 휠이 카메라를 움직이지 않는지. 성별·체형·나이·헤어·의상 변경이 캐릭터에 반영되는지, 여성 전용 원피스를 고른 뒤 남성으로 바꾸면 빨간색·사유가 나오는지, 원피스 착용 시 하의가 노란색 "가려짐"인지. 글자색·대비가 읽을 만한지.
  6. 스튜디오 배경: 꾸미기 창 "배경"을 펼쳐 11개가 보이는지, 고르면 바로 바뀌는지. 3D 배경(특히 클래식 피팅룸·벨벳 의상실)에서 카메라를 돌리고 멀리 했을 때 앞을 가리는 벽·소품이 숨겨지는지, 바닥 그림자 방향·진하기가 괜찮은지, 3D 배경 상태로 앱을 닫아도 문제없는지. 캐릭터 몸에 각진 그림자가 더 이상 겹치지 않는지.
  7. 새 UI(2026-11-03): 연출 속도·끊김 체감(패널 슬라이드, 메뉴·팝업·드롭다운·피커·안내 페이드, F1), 가장자리 탭으로 패널 여닫기(카메라 구도 유지), 창 X·Alt+F4가 메뉴를 열고 메뉴의 종료로만 끝나는지, Tab으로 아이콘 이동 시 이름·테두리가 보이는지, 눈 아이콘·F1(눈만 남고 다시 누르면 복귀), 툴팁, 화각 버튼, ⓘ로 조작 안내 열고 닫기·'확인했어요' 뒤 재실행 시 안 뜨는지. Ctrl+Z/Y와 아이콘(옷 변경·카메라 조작 뒤 옷만 복원), 검색란 입력 중 Ctrl+Z가 이력을 건드리지 않는지. ESC: 드롭다운·피커·안내가 먼저 닫히고 그다음 메뉴 열기/닫기. 메뉴의 내보내기(파일 저장·취소·다음 기본 폴더), 가져오기(성공 시 이력 비활성, 잘못된 파일은 현재 작업 유지), 초기화 확인·취소, 옵션(다른 경로 적용 시 재시작·작업 복원), 종료(바로 저장 후 종료). 메뉴 바깥 클릭으로 닫을 때 카메라가 돌지 않는지.
  5. 색 적용: 피부색·헤어 색상·눈 색상 드롭다운에서 고르면 바로 바뀌는지. 의상을 입히면 그 아래 염색 파트 견본이 나오는지, 견본을 누르면 컬러피커가 뜨고 사각형·색상 막대를 끄는 동안 옷 색이 바로 바뀌는지, HEX 입력(#FF00FF, FF00FF, 잘못된 값)과 Enter, [적용]·[취소]·ESC·[기본색], 피커를 연 채 카메라 회전, 피커가 열린 상태에서 다른 견본·드롭다운을 누르면 이전 색이 적용되는지, 재실행 후 염색이 복원되는지.
- **스튜디오 배경 (2026-11-02)** — 상세·남은 문제는 `Docs/Background.md`.
  - 배경 Id는 처음엔 앱 설정에 넣었다가 SaveLoad.md 확정 원칙(장면 상태 전체 저장)과 충돌해 `EditState.background`로 옮겼다. 형식 버전은 그대로(필드 없으면 기본 배경).
  - 3D 배경의 원본 셰이더는 LightMode `BG`·`SkyLit`·`ExcludeDecal`·`Decal` 패스만 있어 URP 기본 렌더러가 그리지 않는다. `StudioBackgroundPass`가 `RenderPipelineManager.beginCameraRendering`에서 렌더 그래프 패스를 끼운다(렌더러 에셋 수정 없음). 런타임 asmdef에 `Unity.RenderPipelines.Core.Runtime`·`Universal.Runtime` 참조를 추가했다(패키지 변경 아님).
  - 함정: 에디터 어셈블리는 URP를 참조하지 않으므로 `ScriptableRenderPass` 파생 타입을 직접 부르면 CS0012가 난다. 등록은 `StudioBackgroundRendering.Install()`로 한다.
  - 함정: 일반 빌드에서 `ScriptableRenderPass.profilingSampler.name`이 null 예외를 매 프레임 냈다. 패스 이름은 자체 필드로 넘긴다.
  - 함정: `Decal` 패스를 그리면 D3D12 빌드에서 종료 시 `D3D12Core.dll` 접근 위반(벨벳 의상실 3회 재현, `-force-d3d11`은 정상). 데칼 패스를 그리지 않는다.
  - 카메라 충돌(벽 콜라이더로 카메라 당기기)은 벨벳 의상실의 소품 콜라이더에 걸려 너무 가까워져 버렸다. 대신 카메라~기준점 사이를 가리는 배경 렌더러를 숨긴다(`StudioBackground.UpdateOccluders`).
  - 그림자: `MobiCharacter.shader`가 그림자 맵(`shadowAttenuation`)을 쓰지 않고 SMR `receiveShadows = false`. 그림자 드리우기는 유지(바닥 그림자). 방향광 그림자 세기 0.45. 원본 셰이더를 쓰는 머리·눈은 원래 그림자를 받지 않았다.
  - `ProjectSetup.Run`을 배치로 다시 실행해 `Main.unity`에 `gradientShader`를 연결했다(씬 변경 포함).
  - 시험: `InstallSmokeTest -maBackgrounds all|ID1;ID2`(앱과 같은 경로로 배경 적용 후 촬영), `-maProbePrefab 경로;경로 [-maBgPass]`(프리팹 구성·셰이더 패스 로그).
  - 빌드 실행 [확인] 1280×800: 민트바닐라 기본, 3D 4종 표시와 정상 종료(종료 코드 0, 예외 0), 자동 저장의 `background` 복원. 드롭다운 선택·카메라 회전 시 벽 숨김은 [미확인].
- **색 적용 (2026-11-02)**
  - 사용자 결정: 의상 염색은 염색 가능한 의상의 염색 파트만 컬러피커 창(HEX 입력 포함)으로. 피부·헤어·눈 색은 컬러피커가 아니라 데이터 근거가 있는 색(색 시트)을 고른다. Dye·Appearance·`AGENTS.md` 7장에 반영했고, 이전 "외형 색도 자유 RGB" 확정을 대체했다.
  - 염색 가능 판정: Costume.DyePolicy는 1,679행 모두 Default라 판별력이 없다 [확인]. 실제 판정은 모델 DyeInfo의 Slot0~5 중 연결(Wiring)이 있는 슬롯이다(`CharacterView.DyeParts`). AlwaysCantDye는 코드에서 막아 두었다.
  - 파트 견본은 조립된 장면(머티리얼 현재 색)에서 구한다. 그래서 가려졌거나 미적용인 의상은 파트를 보이지 않는다(저장된 염색은 유지).
  - 색만 바꾸는 편집은 `AppController.EditColors`: 상태 복사 → 계획 재계산 → `ApplyPlanColors`. 모델·카메라는 그대로다. 모양 편집(`Edit`)과 섞지 않는다.
  - 컬러피커는 비모달이다. 다른 편집이 시작되면 `ColorPicker.ApplyAny()`로 지금 색을 적용하고 닫는다. `Refresh`는 남은 피커를 `CancelAny`로 닫는다.
  - 투톤 헤어에 한 가지 색을 고르면 Hair1도 첫 색으로 칠한다 [추정]. 이전에는 ColorCode2(#000000)를 넣어 두 번째 영역이 검게 나왔을 것이다.
  - 빌드 촬영 [확인] 1280×800, 560×1000: 색 드롭다운 견본·HEX, 상의(힐링 핸드 유니폼) 파트 1~6 견본과 사용자 색 파트 테두리, 피커 창(넓은 창은 꾸미기 창 왼쪽, 세로 창은 위). 피커는 촬영용 임시 코드로 열어 확인했고 그 코드는 지웠다. 마우스 조작은 [미확인].
  - 새 파일 `UI/ColorPicker.cs`와 Unity가 만든 `.meta`는 같이 커밋한다.
