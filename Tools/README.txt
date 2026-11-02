개발 보조 도구

Localization/extract_strings.py
  Assets/SOData에서 아이템 이름·설명을 추출하여 Localization/Data/gem_strings.txt에 쓴다.
  실행 위치와 무관하게 이 저장소의 경로를 사용한다.

Localization/translate_gems.py
  과거 젬 번역용 사전을 적용하는 보조 도구다. 현 게임의 젬 기능을 되살리는 도구가 아니다.
  Localization/Data/untranslated.json을 읽고 translated.json에 쓴다.

Localization/Data
  기존 번역 입력·출력을 보존한 위치.
  ui_translated.json은 Unity의 Tools/Localization/5. Populate UI Text Table에서 사용한다.

Archive/extract.py
  과거 외부 대화 기록에서 특정 문구 주변을 추출하던 일회성 도구.
  개인 PC의 고정 경로 대신 입력 파일을 인자로 받는다.
  예: python Tools/Archive/extract.py <transcript.jsonl>
  출력은 프로젝트 Logs/extracted.txt이며 Git에 포함하지 않는다.

이 도구들은 게임 실행 중 사용되지 않는다. 자동 실행하거나 번역 데이터를 다시 생성하지 않았다.
