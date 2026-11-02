# 스튜디오 시선·행동 기능의 구현 가능성 검토

> 검토일: 2026-11-02
> 사용자 제공 촬영모드 스튜디오 화면과 MabinogiMobileUnpack `46e94a82112243850d68a39cde73890de016140d`의 파일을 확인했다.
> 코드·데이터 검토다. 이 환경에서 Unity·게임 설치본을 실행하거나 행동 재생·시선 결과를 촬영하지 않았다.

## 1. 이번 결정과 범위

| 항목 | 상태 | 처리 |
|---|---|---|
| UI 숨기기 | 확정 | 꾸미기에서 바로 제공한다. WindowAndCustomizationUI.md를 따른다 |
| 0.5x·1x·3x | 확정 | 거리 줌과 별도의 렌즈 화각으로 구현한다. Camera.md를 따른다 |
| 카메라 쳐다보기 | 조건부 도입 | 구현 가능성을 검증하고 도입한다 |
| 행동 | 조건부 도입 | 실제 재생 가능한 범위를 검증하고 도입한다 |
| 배경 | 구현 초안·재검토 완료 | 현재 꾸미기에 통합한다. Background.md의 확정·제안 구분과 실패 처리·복원 안내 보완을 따른다 |
| 무기 표시 전용 옵션 | 보류 | 현재는 무기를 미장착해 표시하지 않을 수 있다 |
| 이미지 구도·비율 | 보류 | 별도 촬영 버튼을 검토할 때 다시 다룬다 |

표정 선택·펫·AR·최근 사용·즐겨찾기는 이번 결정으로 필수 구현에 추가하지 않는다. 원본 게임의 메뉴를 모두 복제하지 않는다.

## 2. 카메라 쳐다보기

**판정: 구현 근거가 있다. 실제 조립 모델에서 검증한 뒤 지원을 확정한다.**

### 직접 확인한 근거

- `LookAt.json`은 `align=exact`, 23행이다. Humanoid 그룹에 머리와 양쪽 눈의 설정이 있다.
- 머리는 `_Head`다. Stand·Walk·Run 설정에서 YawMax=40, PitchMin=-20, PitchMax=28을 가진다.
- 눈은 `_EyeBallRot_L`·`_EyeBallRot_R`다. YawMax=13, PitchMin=-3, PitchMax=3, IsEye=true다.
- 머리와 눈의 ForwardAxis·UpAxis·RightAxis가 다르다. 머리의 정면을 Unity의 +Z로 고정해 같은 LookRotation을 넣으면 안 된다.
- `LookAtCameraHelper.GetTargetPosition`은 카메라 위치·카메라 바라보는 점·머리 위치·캐릭터 정면을 입력으로 받는다. 카메라 추적을 위한 전용 보정 경로가 있음을 확인했다. 덤프에는 메서드 본문이 없어 실제 보정식은 확인하지 않았다.
- `CharacterView.FindBone`과 본 Transform 배열이 있어 조립된 캐릭터에 후처리를 추가할 위치는 있다. 원본 시트에 적힌 본이 모든 외형에 존재하거나 같은 축을 사용한다고 전수 검증하지 않았다.

### 구현 방향과 검증 조건

- 캐릭터 전체를 돌리지 않고 머리·눈을 카메라 방향으로 보정하는 토글을 검토한다. 꺼지면 원래 포즈의 회전을 복원한다.
- 포즈·체형 적용 이후의 본 자세를 기준으로 추적한다. 이전 프레임 보정에 계속 보정을 곱해 회전이 누적되지 않게 한다.
- 카메라 위치와 시트의 축·각도 제한을 해석한다. 원본 게임의 정확한 보정식 재현과 앱 자체의 제한된 시선 추적 구현은 구분한다.
- 남·여·어린 외형에서 머리·눈 본 존재와 축을 확인한다. 모자·헤어·안경과 눈 메시도 함께 움직이는지 확인한다.
- 카메라가 옆·뒤·위·아래로 갔을 때 목이 꺾이거나 눈이 뒤집히지 않아야 한다. 범위 밖 대상을 강제로 따라보게 하지 않는다.
- 행동과 동시에 쓸 때 포즈·시선의 적용 순서를 확인한다. 원본 동작이 시선을 잠그는 경우의 처리도 확인한다.
- 유효한 본·축·설정이 없으면 미지원 사유를 표시하고 기존 장면을 유지한다. 동작하지 않는 토글을 정상 기능처럼 제공하지 않는다.
- 도입 후에는 토글을 장면 JSON·자동 저장·실행 취소 대상에 포함한다. 카메라 위치에서 계산한 매 프레임 본 회전은 저장하지 않는다.

## 3. 행동

**판정: 행동 선택과 연결 데이터가 있다. 기존 고정 포즈 적용만으로 움직이는 행동을 지원했다고 볼 수 없다.**

### 직접 확인한 근거

| 근거 | 확인 내용 | 한계 |
|---|---|---|
| SocialAction | 159행, 이름·Actions·PreviewActionName 등이 있다. Bye는 손인사1/Bye_01, Heart는 하트/MakeHeart, Dance는 전통댄스/Dance_Shuffle_01이다 | `align=approx`다. 현재 C# 시트 리더에 추가할 때 필드 순서·행 크기 검증이 필요하다. 전체 159행이 스튜디오에서 지원된다는 뜻은 아니다 |
| CustomizeEmotionAction | exact 11행, Customize_Pose_01~11 연결 | 커스터마이즈 포즈 목록이다. 스튜디오 행동 전체 목록과 동일하다고 보지 않는다 |
| Animation/Bones | 여성 손인사·전통댄스 예제에 AssetPath와 본별 TRS가 있다 | 시간별 본 키가 없는 정적 Transform 데이터다. 이것만으로 춤·손인사를 재생할 수 없다 |
| 몸 에셋 경로 | 손인사 예제는 Animation/Player/Human/Female/PlayerCommon/_Emotion/HF@PlayerCommon_Emotion_Bye_01.fbx를 참조한다. 대응 추출 JSON도 있다 | 확인한 추출 JSON은 GameObject 계층이다. 실제 설치본 번들의 AnimationClip 서브에셋·커브 로딩은 별도 검증해야 한다 |
| 얼굴 XAF | 여성 손인사 예제에 StartFrame=0, EndFrame=66, FrameRate=30과 시간별 셰이더 트랙이 있다 | 현재 조립기는 얼굴 XAF의 첫 값만 적용한다. 전체 시간 샘플링·보간·메시 표시·블렌드셰이프 처리는 추가 구현이다 |
| BodyFacialAnimationPair | exact 시트에 몸·얼굴 애니메이션 경로의 대응이 있다 | 대응 시트 존재만으로 몸·얼굴 동기 재생을 확인했다고 쓰지 않는다 |
| 현재 앱 | 기본 포즈는 PlanResolver에서 선택하고 CharacterAssembler가 본별 TRS를 한 번 적용한다. BundleCache는 번들을 읽을 수 있다 | 행동 시트 리더·행동별 클립 해석·실시간 재생·정지 UI는 없다 |

### 권장 구현 순서

1. 설치본에서 SocialAction·Action 등 필요한 시트의 실제 필드·참조를 검증한다. PreviewActionName을 최종 클립 경로로 직접 취급하거나 이름 접두사만으로 추정하지 않는다.
2. 손인사·하트·춤 같은 단독 행동을 골라 성별·어린 외형별 몸 클립과 얼굴 XAF를 찾는다. 설치본 번들에서 실제 시간 샘플링이 가능한지 확인한다.
3. 몸의 본 모션과 얼굴의 셰이더·Visibility·블렌드셰이프를 같은 시간으로 평가한다. 현재 키·나이·체형 보정과 파츠 전용 본의 회전 차이를 보존한다.
4. 원점 이동·회전이 있는 동작의 제자리 미리보기 방식을 검증한다. 바닥 아래로 내려가거나 캐릭터가 화면 밖으로 달아나지 않게 한다.
5. 검증된 행동부터 검색 드롭다운으로 제공한다. 행동 없음은 기존 기본 포즈로 복귀한다. 최소 재생·일시정지·처음부터 재생을 제안한다.
6. 소품·이펙트·짝 행동은 추가 모델·리소스 연결을 따로 검증한다. 몸 모션만 재생한 것을 원본 연출 전체 지원이라고 표시하지 않는다.

### 상태·호환 정책

- 선택 행동은 안정적인 시트 ID로 보관한다. 실제 재생 경로·성별 변형은 현재 외형과 데이터에서 해석한다.
- 일반 빌드의 공개 제한을 우회하지 않는다. 행동 시트의 공개·숨김 의미를 확인하지 않은 상태에서 모든 리소스를 공개 목록에 추가하지 않는다. 해금 조건을 곧바로 숨김 조건으로 대체하지 않는다.
- 실패·미지원 행동 선택 시 이전 정상 장면을 유지한다. 성별·외형 변경 후 대응 리소스가 없으면 사유와 대체 정책을 정한다.
- 도입 시 선택 행동과 재생·정지 상태, 정지 프레임을 저장하는 방식을 구체화한다. 매 프레임 자동 저장하거나 실행 취소 이력을 쌓지 않는다.

## 4. 다음 검증에서 확인할 결과

| 검증 | 통과 기준 |
|---|---|
| 시선 켜기·끄기·카메라 이동 | 머리·눈이 제한 안에서 따라보고 껐을 때 원래 포즈로 돌아온다 |
| 시선과 외형·의상 변경 | 본 참조를 다시 연결하고 머리 장식·눈 출력이 유지된다 |
| 단독 행동 1종 | 몸과 얼굴이 같은 시간으로 재생되고 정지·재시작·기본 자세 복귀가 된다 |
| 체형·나이·의상 조합 | 키·체형이 초기값으로 돌아가지 않고 파츠 전용 본이 꺾이지 않는다 |
| 시선과 행동의 동시 사용 | 프레임마다 포즈와 시선이 서로 덮어쓰거나 회전이 누적되지 않는다 |
| 저장·복원 | 실제 지원 상태만 복원하며 없는 리소스·부분 복원 안내가 동작한다 |

## 5. 근거 파일

게임 데이터는 아래 커밋 기준이다. 저장소에 게임 리소스를 복사하지 않는다.

- [LookAt 시트](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/LookAt.json)
- [카메라 시선 보정 선언](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Dump/MM.Client.Presentation/MM.Client.Presentation.LookAt.cs)
- [시선 컴포넌트 선언](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Dump/MM.Client.Presentation/MM.Client.Presentation.Components.LookAt.cs)
- [SocialAction 시트](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/SocialAction.json)
- [CustomizeEmotionAction 시트](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/CustomizeEmotionAction.json)
- [손인사 정적 본 데이터](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/Animation/Bones/HF@PlayerCommon_Emotion_Bye_01.sjon.json)
- [손인사 얼굴 XAF](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/ResourcesPackage/Animation/XafAnimation/Animation/Player/Human/Female/PlayerCommon/_Emotion/HF@PlayerCommon_Emotion_Bye_01_Facial.xaf.sjon.json)
- [몸·얼굴 대응 시트](https://github.com/PieceOfPaper/MabinogiMobileUnpack/blob/46e94a82112243850d68a39cde73890de016140d/Patch/Sheetdata/client/BodyFacialAnimationPair.json)
