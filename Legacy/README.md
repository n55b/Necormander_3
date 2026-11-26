# Legacy

교체되거나 더 이상 쓰지 않는 코드·에셋·도구·문서를 보관한다. 이 폴더는 프로젝트 루트에 있어 Unity의 에셋·스크립트·패키지 탐색 범위 밖이다.

## AI와 검색에서 제외

이 목차(`Legacy/README.md`)만 읽는다. 하위 폴더는 다음 장치가 막는다.

| 장치 | 범위 |
| --- | --- |
| `.claude/settings.json`의 하위 폴더별 `Read(/Legacy/<폴더>/**)` 거부 | Claude Code의 Read·Grep·Glob. 경로가 `/`로 시작해 설정 파일 기준 프로젝트 루트에 고정된다. `Read(/Legacy/*/**)` 한 줄로 쓰면 README까지 막히므로 폴더마다 적는다. **새 최상위 폴더를 만들면 규칙을 한 줄 추가한다.** 빠지면 `Invoke-UnityVerification.ps1`이 모든 스위트 시작 전에 실패한다 |
| `.rgignore`의 `/Legacy/` | rg 기반 검색 |
| `AGENTS.md` | 보관 이력을 요청한 경우에만 읽는다는 지시 |

`git grep`이나 스크립트로 참조를 스캔할 때는 `':!Legacy'`를 넣어 제외한다. 보관 이력이 필요하면 사용자가 거부 규칙을 잠시 풀거나 파일을 직접 연다.

이름에 Legacy가 들어 있지만 **활성인 폴더**가 있다. 막거나 옮기지 않는다.

- `Assets/Prefabs/Map/Objects/Legacy`: 방 프리팹과 제작실 카탈로그가 참조
- `Assets/Resources/Sprites/Tile/Legacy`: `ARTIST/manifest.json`과 접지점 굽기가 사용
- `ARTIST/LEGACY`: 현재 씬이 쓰는 그림

## 보관 규칙

- `Tools/Invoke-UnityVerification.ps1`이 막는 실행 확장자(`.cs .dll .exe .asmdef .asmref .shader .shadergraph .compute .ps1 .cmd .bat .sh .py .js .mjs .ts .lua`)는 `.txt`를 붙여 보관한다. 원래 확장자로 두면 모든 검증 스위트가 실패한다.
- Unity 메타데이터는 `.meta.txt`로 보관한다. `.unity`·`.prefab`·`.asset`·`.mat`도 일관성을 위해 `.txt`를 붙인다. png·aseprite·svg는 그대로 둔다.
- 코드 일부만 떼어 보관할 때는 원래 파일 경로를 딴 `.txt`에 원래 위치와 커밋을 적는다.
- 항목을 추가하면 아래 표에 원래 경로, 보관 사유, 대체 위치를 적는다.

## 목록

| 경로 | 보관 대상 | 현재 작업 위치 |
| --- | --- | --- |
| `Editor/UI` | 초기 TMP 일괄 변환과 1006 레이아웃 초기화 | 런타임 `PixelUiTypography`, Sunyunie Panel, `HudLayoutChecks` |
| `Editor/Map` | 초기 미니맵·지형 설정 도구 | 커밋된 프리팹과 프로젝트 설정, `RoomTilemapCheck`, `MapColliderCheck`, `MiniMapCheck` |
| `Editor/Gameplay` | 성장 레지스트리 전체 재스캔(`GrowthRegistryEditor`, `GrowthRegistrySO.RefreshRegistry`와 `specialAbilities`), 소환수 관리의 상태이상 공통 수치 열기·대기 효과 빈 소환수 채우기 메뉴. 2026-11-26 퇴역 | `Tools/기획/레지스트리 새로고침`(`RegistryMenuTools`), 레벨 제작실 > 게임 콘텐츠 > 상태이상 |
| `Editor/Combat` | 상태이상 편집기(`StatusEffectEditorWindow`, Tools/기획/상태이상 편집기). 2026-11-27 퇴역. 기능 대조는 [이관 계획 부록 A](../Docs/Maintenance/제작도구_인게임_이관과_레거시_정리_계획.md#부록-a-상태이상-편집기-대조표) | 레벨 제작실 > 게임 콘텐츠 > 상태이상 |
| `Rendering` | 이전 확대 셰이더와 렌더링 개편 전 구현(카메라 충격, 전투 연출, 층 포털 메시, 지면 그림자, 플레이어 가림, 글자 모션, 나무 바람, 글꼴, 벡터 이펙트, 월드 깊이·효과) | `PixelArtIntegerUpscale.shader`와 `Assets/Scripts/Rendering`·`Effect` |
| `Shaders` | 층 강착 원반 셰이더 | 현재 층 전환 연출 |
| `Runtime/Player` | Unity 참조가 없는 이동 코드 백업 | `PlayerController`, `PixelActorMotion` |
| `Runtime/Actors` | 참조가 없고 스크립트가 제거된 이전 상태 에셋 | `Assets/Scripts/Entities`와 적 AI 패턴 |
| `Runtime/Map` | 배치 중 방을 생성하던 이전 격자형 던전 구현 | `DungeonLayoutPlanner`, `DungeonLayout`, `MapGenerator` |
| `Runtime/Map/*.PhysicsSpread.cs.txt` | 맵 물리 확산 배치와 그린 복도 연결 경로(`useIsaacStylePlacement=false`). `MapGenerator`의 비격자 본문·복도 연결·통로 길이 기록, `RoomInstance`의 Rigidbody2D·`CleanupPhysics`, `MapGenerationDataSO`의 전환 스위치와 펼치기·복도 조절값. 2026-11-26 퇴역 | 격자 배치(`DungeonLayoutPlanner` → `MapGenerator.IsaacStyleGenerationSequence`). 꺾인 복도 칠하기 `CorridorPainter`는 passages 검사용으로 남김 |
| `Runtime/Rewards/RewardProcessor.TreasureCandidates.cs.txt` | 호출자 없는 카테고리별 보상 후보 생성 `RewardProcessor.GenerateCandidatesByCategory`와 `GetValidTreasures`. `GetValidTreasures`는 보물(`TreasureSO`) 보상 후보를 만드는 유일한 코드였다. 2026-11-26 퇴역 | 보물 지급(`RewardManager`의 Treasure 분기)·세이브 복원(`GrowthRegistrySO.ResolveSavedTreasure`)과 `Minion Stat Treasure.asset`은 남김. 보물을 후보로 내는 경로는 지금 없다 |
| `Runtime/Combat` | 붙은 프리팹이 없던 공격 예고 판정 `TelegraphHitbox`와 마법사 원형 장판 `EnemyMagicianCircleAttack`(스크립트와 메타). 2026-11-26 퇴역 | `BaseHitBox`. 궁수 엘리트를 되살리면 포격도 `BaseHitBox` 경로를 쓴다 |
| `Runtime/RightClick` | 우클릭 데이터 시스템(패링·카운터·가드 `RightClickDataSO`·`RightClickConfig`·`RightClickIconFactory`, PlayerPrefs 해금·선택 `RightClickUnlockState`, `SOData/RightClick` 에셋 6개). 2026-11-26 퇴역 | 우클릭은 소환수 스킬(`MinionSkill` → `PlayerSkillController.ExecuteMinionSkill`) |
| `UI` | 버튼 밑줄 호버·컨트롤 강조(스크립트, 설정 SO, 에디터) | `UIChrome`, `RoundedPlate`, `VectorUiImage` |
| `Art/UI/Vector` | 이전 UI 벡터 원본 SVG와 매니페스트 | `ARTIST/UI/Vector` |
| `ArtDensity/Tiles` | 픽셀 밀도 개편 전 타일 원본과 출력 | `ARTIST`의 현재 타일 원본 |
| `Assets` | 반딧불 환경 이펙트 프리팹·머티리얼 | 현재 월드 분위기 입자(`WorldAtmosphere`) |
| `Assets/Animations/Player` | 패링 애니메이션 `Player_AnimClip_Parry`·`Parry_Success`. `Player_Animator.controller`의 고립 상태 2개와 함께 2026-11-26 퇴역 | `Player_Animator.controller` |
| `Data/Dialogue` | CSV 대사 원본과 작업 안내 | `DATA/DIALOGUE`와 제작실 대사 편집 |
| `Tools` | 일회성 기록 추출 스크립트 | 보관 기록 |
| `History` | 과거 프롬프트·선호 기록, 플레이 테스트 변경 기록, 날짜별 작업 보고 | `AGENTS.md`와 기능별 사용 안내 |
| `History/Docs` | 옛 설계·조사 문서(젬·던지기 은퇴 기록, 소환수 구조, 리팩터 인계, 2026-10-26 레거시·조절값 점검, 렌더링 이전 코드 사본). 2026-11-26 `Docs/Archive`에서 옮김 | `Docs/Guides`, `Docs/Maintenance` |
| `History/Screenshots` | 참고 스크린샷. 2026-11-26 `Docs/Screenshots`, `Claude outputs`에서 옮김 | — |
