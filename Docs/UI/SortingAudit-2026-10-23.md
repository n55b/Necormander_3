# 월드 정렬 조사 — 2026-10-23

첫 조사는 읽기 전용으로 수행했다. 후속 요청에 따라 포탈/소환수에 기존 Y정렬만 연결했다(아래 후속 판단 참고). 타일맵·플레이어·프로젝트 레이어 설정은 변경하지 않았다.

## 범위와 증거

- `Assets/Prefabs/Map/Room Map Prefabs`의 RoomInstance 프리팹 33개. 실제 생성 풀에서의 선택 빈도와 별개로 원본 전체를 검사했다.
- 병합 대상으로 참조되는 타일맵 124개 + BattleScene 전역 타일맵 4개.
- MainMinionDataSO 11개(테스트 포함), 평타/스킬/대시 외형 참조 33개, 고유 외형 6개.
- 상세 읽기 전용 출력: `Logs/RoomRendererAudit.tsv`, `Logs/MinionRendererAudit.tsv`.
- Unity null 참조는 `== null`로 검사한 뒤 RoomInstance.Initialize와 동일한 이름 기반 탐색을 적용했다.

## 원본 타일맵과 전역 타일맵

| 종류 | 원본 설정 | 병합 후 설정 |
| --- | --- | --- |
| Ground 33개 | 모두 Ground / 0 | Ground / 0 |
| Wall 33개 | 27개 Ground / 2, 6개 삭제된 Sorting Layer ID / 0 | Ground / 1 |
| Shadow 참조 31개 | 16개 Shadow / 0, 8개 Default / 0, 6개 삭제된 ID / 0, 1개 Ground / 2 | Shadow / 0 |
| Unsteppable 27개 | 25개 Ground / -1, 1개 Ground / 1, 1개 Default / 0 | Ground / -1 |

머티리얼(Sprite-Lit-Default), Chunk 모드, BottomLeft 순서, 마스크 없음, 흰색 Tilemap.color, Tile Anchor (0.5, 0.5, 0), XY 방향, 애니메이션 속도 1, Renderer 활성, 그림자 투사/수신 설정은 검사한 128개 모두 같았다. 모든 Renderer 설정이 제각각인 상황은 아니며, 주요 차이는 소팅 설정과 물리 Layer다.

원본 물리 Layer도 다르다: Ground는 Default 27/Ground 6, Wall은 Default 10/Wall 23, Shadow 참조는 Default 24/MiniMap 6/Object 1, Unsteppable은 모두 Unsteppable이다. 병합 후에는 전역 오브젝트의 물리 Layer가 사용된다.

`RoomInstance.MergeTilesToGlobal`은 타일 데이터를 전역 맵에 찍고 원본 네 타일맵 오브젝트를 비활성화한다. 원본 Renderer를 삭제하거나 원본 프리팹을 수정하는 것은 아니지만, 합쳐진 결과에는 원본의 머티리얼/소팅 설정을 복사하지 않는다. 개별 셀의 색/변환도 StampTilemap에서 명시적으로 복사하지 않는다. 별도 프리팹 오브젝트 전체의 Renderer를 일괄 덮는 로직은 아니다.

### 구체적으로 다른 원본

- `Normal/Test/Room_Normal.prefab`: Unsteppable이 Ground / 1 → 전역 Ground / -1.
- `Shop/Test/Room_ShopEnhance.prefab`: Unsteppable이 Default / 0 → 전역 Ground / -1.
- **`TestTutorial/Tutorial_04.prefab`의 shadowTilemap 참조가 `Object` 타일맵**이다. Object의 물리 Layer Object, Sorting Ground / 2가 전역 Shadow / 0으로 처리된다. 실제 표시 차이를 조사할 우선 후보이며 이번에는 수정하지 않았다.
- 삭제된 소팅 레이어 ID를 가진 6개는 Elite/Room_Elite, Normal/Room_Normal, Reward/Room_Reward, Room_Test, Shop/Room_Shop, Spawn/Room_Spawn이다.

## Wall / Unsteppable이 Ground인 이유

물리 Layer와 Sorting Layer는 별개다. ConfigureTilemap은 물리 Layer를 각각 Wall/Unsteppable로 유지하고, 렌더링만 Ground 그룹으로 지정한다. 현재 Sorting Layer 목록에는 Wall 자체가 없다.

- `efc85b9c`(2026-05-23): Wall의 Sorting Layer를 Ground / 1로 바꾸는 코드와 BattleScene 변경이 함께 들어갔다. 우연한 한 줄 오타보다는 의도적인 변경이라는 근거다. 바닥보다 위, Default 오브젝트보다 아래에 벽 타일을 묶으려는 목적이 유력하지만 당시 이유를 설명하는 주석/커밋 메시지는 없다.
- `d94b2f1a`(2026-09-03, 레이어 순서 수정): Unsteppable을 Ground / -1로 분리. 코드에 물/낭떠러지 배경을 바닥 아래에 놓는 의도가 명시되어 있다.
- 현재 Wall 원본은 대부분 /2인데 실행 코드는 /1이다. 상대적으로 Ground /0 위라는 점은 같지만 저작값이 그대로 적용되는 구조는 아니다.

## 플레이어와 미니언

`YSortableObject.UpdateSortOrder`의 식은 `baseOffset - RoundToInt(pivot.position.y * 100)`이다. pivotOverride가 없으면 자기 Transform을 기준으로 하며, 동적 오브젝트는 LateUpdate에서 Order를 바꾼다. 따라서 사용자가 말한 피벗/발밑 Y정렬의 개념은 맞지만, 구현은 Order 0끼리 엔진이 비교하는 방식이 아니라 Order를 직접 계산하는 방식이다. SpriteRenderer의 Sort Point 설정과도 구분된다.

수정 전 MainMinionDataSO가 참조하던 Fighter/Knight/Knight_2/Warrior/DashDoll/IceMage 외형은 **전부 YSortableObject 없음, Sorting Default / 0, Sprite Sort Point Center, SortingGroup 없음**이었다. MinionSkillCaster.Spawn의 TryGetComponent는 방금 만든 빈 루트에서 SpriteRenderer를 찾기 때문에 실행되지 않으며, 나중에 붙이는 자식 외형에도 Y정렬을 추가하지 않는다. 세 테스트 외형만의 문제가 아니었다. 후속 수정에서는 캐스터에 런타임 보정을 넣지 않고 외형 프리팹에 정렬을 저작했다.

## 층 포탈 Order 5

`d4fada5c`(2026-05-23)의 최초 포탈은 코드에서 Knob 스프라이트를 생성했다. 그 임시 원형 포탈을 바닥보다 위에 표시하려고 `sortingOrder = 5; // Draw above grounds`를 넣었다. 같은 날 `efc85b9c`에서 프리팹 포탈로 바뀌면서도 이 값은 유지되어, 지금은 프리팹 Order 0을 매번 5로 덮는다. 게임 진행/상호작용 판정에 필요한 값은 아니다. 현재 Default가 Ground보다 위이므로 바닥 위에 그리기 위한 5의 필요성은 낮고, Y정렬과 혼용되는 레거시 값으로 보는 것이 타당하다.

## UI 검증

접힌 C/V의 기존 가로 겹침 -32를 재현한 뒤 +16 간격으로 수정. 프리팹 960×540/600/720 렌더와 4개 실제 씬에서 양옆 경계·상하 정렬 검사를 통과했다. C 확장 패널의 우선 표시는 유지했다. 씬 변경은 C 루트에 남아 있던 Y=10 오버라이드를 0으로 맞춘 것뿐이다.

## 후속 판단: 의도된 차이 / 오래된 설정 / 잠재적 오류

`RoomPrefabData.asset`(GameManager와 MapGenerator가 사용하는 풀), 실제 타일 유무, Git 이력을 추가 대조했다. 풀 등록과 매 층 실제 선택은 별개다. 다음은 수정 제안이 아닌 판정이며 원본 방은 변경하지 않았다.

| 항목 | 판단 | 근거와 현재 영향 |
| --- | --- | --- |
| Wall Ground / 2, 전역 Ground / 1 | 바닥 위에 벽을 그리는 의도. 숫자의 불일치는 저작/실행 설정의 미동기화 | 둘 다 Ground / 0 위이며 현재 결과는 전역 / 1로 통일된다. 이 차이만으로 개별 방의 특수 연출이라고 볼 근거는 없다. 유지. |
| Unsteppable Ground / -1 | 명확한 의도 | 물을 바닥 아래에 두도록 9월 3일 코드·프리팹이 변경됐다. 유지. |
| 원본의 물리 Default와 실행 시 Ground/Wall/Unsteppable | 병합 구조상 의도된 전역 설정 | 원본은 병합 후 비활성화되고 전역 타일맵이 충돌/표시를 담당한다. 원본이 Default라는 사실만으로 실수로 분류하지 않는다. |
| 삭제된 Sorting Layer ID 1777279099를 쓰는 옛 방 6개 | 레거시 설정 잔존, 의도적인 특수 정렬 아님 | 현재 RoomPrefabData 후보 및 튜토리얼 7방에 없다. 옛 Normal 프리팹은 5월 22일 이후 수정되지 않았다. 다시 사용하기 전 정비 대상. |
| Normal/Test/Room_Normal의 Unsteppable Ground / +1 | 물 배경 변경 이전 값이 남은 것으로 판단 | 4칸 존재. 이 프리팹은 현재 일반 방 후보가 아니며 마지막 수정은 8월 27일로 / -1 변경보다 앞선다. |
| Shop/Test/Room_ShopEnhance의 Unsteppable Default / 0 | 미설정/레거시 가능성이 높음 | 등록된 후보지만 Unsteppable은 현재 0칸. 특별히 앞으로 그리려는 내용이 없다. |
| Shadow Default / 0인 8개 | 대부분 빈 템플릿 설정 잔존 | 7개는 빈 타일맵. 정렬 차이를 이용하는 실제 그림이 없다. 나머지 강화상점은 아래 참조 문제도 있다. |
| Tutorial_04.shadowTilemap → Object | 잘못된 참조 또는 이름 변경 후 남은 연결 가능성이 높음 | Object는 물리 Object, 충돌/NavMesh 설정을 갖지만 타일은 0칸. 현재 표시 오류의 직접 원인은 아니지만 여기에 장식을 칠하면 전역 Shadow에 병합되고 원본 Object가 꺼지는 잠재적 오류다. |

### 강화상점의 추가 발견

`Shop/Test/Room_ShopEnhance`의 Shadow에는 **383개 저작 셀**이 남아 있다. 다만 옛 타일 에셋 GUID를 참조한다. 예: `Tilemap_16px_138`의 옛 GUID `5ee6ff73c5bfbc74e9aa365f1d70ac1d`는 복구 전 AssetDatabase에서 경로가 해석되지 않았다. `f93a3449`(10월 9일, 타일맵&나무 업데이트)의 Git rename 추정은 `Tile/Tilemap_16px_138` → `Field_Tilemap/Field_Tilemap_16px_138`로 표시했지만, **후속 원본 이미지 비교 결과 서로 다른 그림이다. 동일 번호나 Git rename 추정으로 대응시키면 안 된다.** 옛 138은 그림자, 현재 Field 138은 물/다리 계열이다.

따라서 이 방의 차이는 단순한 정렬 숫자 문제가 아니라 **오래된 타일 참조가 남은 별도 저작 문제**다. cached sprite/셀 정보가 남아 있다는 사실을 유효한 타일 에셋으로 오해하면 안 된다. 이번 범위에서는 타일 교체나 복원을 하지 않았다.

추가 상세 출력: `Logs/WorldSortingFollowup.tsv`.

## 반영: 포탈/소환수의 공용 Y정렬

- FloorProceedPortal.Start의 강제 Order 5 제거. 기존 색상·상호작용 유지. FloorProceedPortalPrefab에 정적 YSortableObject(precision 100, offset 0, 자기 루트 기준)를 저장했다. 기존 엘리트방의 중첩 포탈에도 상속된다.
- Fighter/Knight/Knight_2/Warrior의 기존 외형 프리팹에 동적 YSortableObject를 저장했다. 기준은 외형 루트이며 캐스터/시퀀스가 루트를 움직이면 정렬도 갱신된다. 내부 스프라이트의 연출용 위치는 기준점과 분리된다.
- DashDoll/IceMage는 `Assets/Prefabs/Minion/`에 원본 Aseprite가 중첩된 편집용 프리팹을 만들고, 평타·스킬·대시의 visual 참조를 모두 전환했다. 기존 컨트롤러/클립/타격 이벤트는 그대로 참조한다. 원본 Aseprite는 변경하지 않았다.
- 새 정렬 시스템이나 런타임 정렬 AddComponent는 없다. YSortableObject와 플레이어 프리팹은 변경하지 않았다.
- `Tools/Diagnostics/Verify Minion and Portal Sorting`: MainMinionDataSO 11개의 33개 참조/고유 외형 6개, 양·음 Y의 동적 정렬, 스프라이트 오프셋, 애니메이터 참조, 포탈 Start가 정렬을 덮지 않는지, 중첩 포탈 2개를 미리보기 씬에서 검사한다. 원본 씬/에셋은 저장하지 않는다.
- 검증 결과: 위 검사 통과, Unity 컴파일 오류 0(기존 DialogueSetupTools 경고 1개 유지). 실제 외형과 플레이어 Body를 앞/뒤로 겹친 임시 렌더 `Logs/WorldSorting-FrontBack.png` 확인. 전체 던전 플레이 검증은 수행하지 않았다. 일회성 에셋 수정/조사 스크립트는 제거했고 회귀 검사만 남겼다.

## 반영: 원본 방의 잘못된 설정/참조 복구

후속 사용자 승인으로 16개 방 프리팹을 수정했다. 런타임 맵 생성/충돌/정렬 로직은 바꾸지 않았다.

- 삭제된 Sorting Layer ID를 사용한 Wall 6개를 원본 방 공통값 Ground / 2로 복구했다. 전역 Wall Ground / 1은 유지한다.
- 실제 Shadow 타일맵 14개(삭제된 ID 6개, Default 8개)를 Shadow / 0으로 통일했다.
- Normal/Test/Room_Normal과 Shop/Test/Room_ShopEnhance의 Unsteppable을 Ground / -1로 수정했다. 나머지 의도된 설정, 물리 Layer, 콜라이더, 셀 좌표는 유지했다.
- Tutorial_04의 shadowTilemap은 null로 수정했다. 이 방에는 실제 Shadow 타일맵이 없으며 잘못 연결된 Object는 빈 상태다. Object 오브젝트와 충돌 설정은 보존했고, 더 이상 Shadow 병합 때문에 비활성화되지 않는다. RoomInstance의 기존 선택적 참조 처리로 동작하며 런타임 보정은 추가하지 않았다.
- 강화상점은 Ground/Wall의 삭제된 타일 26종도 참조하고 있었다. 옛 Field 원본(커밋 f93a3449의 부모)과 현재 Field 원본의 16×16 픽셀을 비교하여 현재 타일로 연결했다. 18종은 완전 동일, 8종은 최신 벽 그림의 대응 조각(256픽셀 중 4~25픽셀 차이)이다. 색/변환/TileFlags/ColliderType은 원래와 같은 것을 확인했다. 프리팹의 캐시 sprite fileID도 같은 대응으로 수정했다.
- 그림자 11종은 현재 Field 타일에 대응되는 그림이 없으므로 기존 Tilemap_16px.png를 그대로 참조하는 **원래 Tile 에셋과 GUID**를 `Assets/Resources/Sprites/Tile/Shadow/`에 복구했다. 이미지 생성/편집은 하지 않았다. 같은 원래 GUID를 쓰던 옛 방의 그림자 참조도 함께 다시 해석된다.
- `Tools/Diagnostics/Verify Room Tilemaps`: 원본 방 33개/지형 Renderer 123개 정렬, 강화상점 Ground 1487/Wall 752/Shadow 383칸의 Tile/Sprite 영속 에셋 참조, 강화상점 및 Tutorial_04의 실제 MergeTilesToGlobal 결과를 검사한다. 현재 씬/프리팹 저장 없이 미리보기 씬에서 실행하며 통과했다. 전체 던전 플레이는 별도다.

Field 인덱스 대응(옛 0820 → 현재): `3→3, 4→4, 5→5, 8→79, 9→80, 10→81, 11→46, 12→47, 13→48, 16→18, 17→19, 21→94, 22→95, 23→96, 24→62, 25→26, 27→30, 28→31, 29→32, 35→109, 36→110, 37→111, 43→122, 54→151, 64→107, 65→108`.

추가 검수: 16개 수정 프리팹의 셀 데이터 블록/물리 Layer가 HEAD와 동일함을 확인했다. 실제 프리팹의 지형만 미리보기 씬에서 Unlit 렌더한 `Logs/RepairedShop-Tilemap.png`로 벽 경계·잔디/흙·그림자의 연결을 확인했다(실제 게임 조명/주변 오브젝트 제외). 일회성 렌더 스크립트는 제거했으며, 원본 머티리얼은 변경하지 않았다.
