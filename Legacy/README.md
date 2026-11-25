# Legacy

교체된 코드와 초기 설정 도구의 원본을 보관한다. 실행 파일과 Unity 메타데이터는 텍스트로 보관한다. 이 폴더는 프로젝트 루트에 있고 Unity의 에셋, 스크립트 및 패키지 탐색 범위 밖에 있다. 저장소의 기본 ripgrep 검색에서도 제외한다.

| 경로 | 보관 대상 | 현재 작업 위치 |
| --- | --- | --- |
| `Editor/UI` | 초기 TMP 일괄 변환과 1006 레이아웃 초기화 | 런타임 `PixelUiTypography`, Sunyunie Panel, `HudLayoutChecks` |
| `Editor/Map` | 초기 미니맵·지형 설정 도구 | 커밋된 프리팹과 프로젝트 설정, `RoomTilemapCheck`, `MapColliderCheck`, `MiniMapCheck` |
| `Rendering` | 이전 확대 셰이더 | `PixelArtIntegerUpscale.shader` |
| `Runtime/Player` | Unity 참조가 없는 이동 코드 백업 | `PlayerController`, `PixelActorMotion` |
| `Runtime/Actors` | 참조가 없고 스크립트가 제거된 이전 상태 에셋 | `Assets/Scripts/Entities`와 적 AI 패턴 |
| `Runtime/Map` | 배치 중 방을 생성하던 이전 격자형 던전 구현 | `DungeonLayoutPlanner`, `DungeonLayout`, `MapGenerator` |
| `Tools` | 일회성 기록 추출 스크립트 | 보관 기록 |
| `History` | 과거 프롬프트·선호 기록, 플레이 테스트 변경 기록, 날짜별 작업 보고 | `AGENTS.md`와 기능별 사용 안내 |
