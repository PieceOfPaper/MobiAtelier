# CLAUDE.md

이 프로젝트의 작업 지침은 `AGENTS.md`에 있다(Codex와 같이 쓰는 공통 지침).

@AGENTS.md

@AGENTS.local.md

## Claude Code 전용 메모

- Claude Code의 자동 메모리는 Codex가 보지 못한다. 오래 지켜야 할 규칙은 메모리가 아니라 `AGENTS.md`에, 진행 상황은 `Docs\PROGRESS.md`에 적는다.
- 세션 임시 폴더(scratchpad)에서 만든 결과가 다음 작업에도 필요하면 프로젝트 안으로 옮기고 `Docs\PROGRESS.md`에 남긴다.
- Bash 도구는 Git Bash다. `cd /d/MobiAtelier` 형식 경로를 쓰고, 한글이 든 파이썬 heredoc은 `python -X utf8 -`로 실행한다.
- 로컬 참고 폴더는 `.claude/settings.local.json`(git 제외)의 `additionalDirectories`로 읽는다. 설치 폴더 쓰기는 `.claude/settings.json`에서 막아 두었다.
