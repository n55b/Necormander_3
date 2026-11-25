개발 보조 도구

Invoke-UnityVerification.ps1
  Assets, Packages, ProjectSettings, ARTIST를 임시 프로젝트에 복사한다.
  별도 저장 경로로 실행하며 콘솔에 임시 경로를 출력한다.
  예시  pwsh -NoProfile -File Tools/Invoke-UnityVerification.ps1 -Suite Baseline
  성능  pwsh -NoProfile -File Tools/Invoke-UnityVerification.ps1 -Suite Performance
  던전  pwsh -NoProfile -File Tools/Invoke-UnityVerification.ps1 -Suite Dungeon
  항목별 명령과 검사 범위는 Docs/Maintenance/작업_검증.md에서 확인한다.

Localization/extract_strings.py
  Assets/SOData에서 아이템 이름·설명을 추출하여 Localization/Data/gem_strings.txt에 쓴다.
  실행 위치와 무관하게 이 저장소의 경로를 사용한다.

Localization/translate_gems.py
  과거 젬 번역용 사전을 적용하는 보조 도구다. 현 게임의 젬 기능을 되살리는 도구가 아니다.
  Localization/Data/untranslated.json을 읽고 translated.json에 쓴다.

Localization/Data
  기존 번역 입력·출력을 보존한 위치.
  ui_translated.json은 Unity의 Tools/Localization/5. Populate UI Text Table에서 사용한다.

Unity 에디터 밖에서 실행하는 개발 보조 도구다.
