# 진행 상황

> 최종 갱신: 2026-11-01
> Codex와 Claude Code가 번갈아 작업하며 공유하는 인계 문서다. 작업 단위가 끝날 때마다 갱신한다(`AGENTS.md` 8장).

## 1. 현재 단계

- 기획 단계. 프로그램 흐름과 항목별 세부 기획서를 작성했다(`README.md` 기획서 목록).
- Unity 프로젝트는 템플릿 상태다(`Assets\Scenes\SampleScene.unity`, URP 설정, Input System 액션). 구현 코드는 아직 없다.

## 2. 최근 작업

| 날짜 | 내용 |
|---|---|
| 2026-11-01 | 레퍼런스 조사, 프로그램 플로우와 항목별 기획서 분리 |
| 2026-11-01 | 의상 초기색 재검토, 자유 RGB 편집·최초 외형 2종 무작위 선택 확정. Dye·Startup·ProgramFlow·FeasibilityReview 갱신 |
| 2026-11-01 | 에이전트 공통 지침(`AGENTS.md`, `CLAUDE.md`), Claude Code·Codex 프로젝트 설정, 이 문서 추가 |

## 3. 다음 할 일

- 미정 기획 항목 결정: 최초 착용 의상(`Docs\Startup.md`), 카메라의 실행 취소 포함 여부 등.
- 폴더 구조·어셈블리 정의 결정 후 `AGENTS.md` 5장에 기록.
- 설치 폴더 읽기·캐릭터 조립 구조 설계.

## 4. 인계 메모

- 최초 외형은 MaleAppearance_Default_01 / FemaleAppearance_Default_01 중 동일 확률로 선택한다. 마지막 작업이 있으면 무작위 선택 없이 복원한다. 초기 의상은 아직 미정이다.
- 임의 RGB 색상을 허용한다. 게임 팔레트·CanChangeColor 정책으로 입력을 잠그지 않는다. 실제 셰이더 연결 지원 여부는 별도로 검증한다.
- Costume.DyeColor0~5의 null은 초기색 부재가 아니다. 원본 재질·텍스처 상태에 유효한 시트 기본값을 덮어쓰고, 복원 시 저장된 사용자 색을 적용한다.
- 이번 보완은 문서·코드 확인이다. Unity 렌더링을 재실행하지 않았다.
