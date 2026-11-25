# Necromander 작업 안내

## 실행 환경

- Unity 버전은 `ProjectSettings/ProjectVersion.txt`의 `6000.0.59f2`를 사용한다. 빌드 진입점은 `Assets/Scenes/StartScene.unity`다. 마을은 `VillageScene`, 튜토리얼과 던전은 `BattleScene`, 옵션은 `OptionScene`을 사용한다.
- Unity MCP는 `Window > MCP For Unity > Toggle MCP Window`에서 `HTTP Local`로 연다. 서버가 꺼져 있으면 `Start Server`, `No Session`이면 `Start Session`을 사용한다. Codex 주소는 `.codex/config.toml`에 있다. Unity가 여러 개 열려 있으면 씬이나 프리팹을 수정하기 전에 대상 프로젝트를 확인한다.

## 코드와 에셋

- 픽셀아트·렌더링 개편에서는 기존 런타임 코드, 셰이더, 에디터 도구를 전면 재작성하거나 퇴역시킬 수 있다. 교체 단위마다 게임 동작, 콘텐츠 참조, ARTIST 편집 경로를 검증하고 퇴역본은 루트 Legacy에 보관한다. 선택 기준과 단계별 완료 조건은 [픽셀아트 제작과 렌더링 계획](Docs/Maintenance/픽셀아트_제작과_렌더링_계획.md)을 따른다.
- 런타임 코드는 `Assets/Scripts`, 제작·검사 도구는 `Assets/Editor`에 있다. 밸런스와 콘텐츠 설정은 `Assets/SOData`, 씬에 연결되는 실물은 `Assets/Prefabs`와 `Assets/Animations`에서 확인한다.
- 에셋이나 스크립트를 이동할 때 `.meta`를 함께 이동한다. 이름을 바꿀 때는 `Resources.Load` 경로와 세이브에 기록되는 소환수·장비·아이템 이름을 확인한다.
- 현재 전투는 두 소환수 슬롯을 사용한다. 우클릭은 활성 소환수 스킬, `Space`는 교대이며, 대기 소환수는 평타 상태이상과 대시 공격에 관여한다. `Docs/Archive`는 이전 구현 기록이다.
- 제작 도구는 [툴 사용 가이드](Docs/Guides/툴%20사용%20가이드.md), 에셋 위치는 [폴더 찾기](Docs/Guides/폴더_찾기.txt)를 참고한다.
- 스프라이트 원본은 [ARTIST](ARTIST/README.md)에서 편집한다. `manifest.json`에 등록된 출력은 Unity가 동기화한다. 원본과 갱신된 출력은 함께 검토한다.
- 메뉴·HUD 아이콘과 TMP 인라인 키캡은 `ARTIST/manifest.json`에 연결된 Aseprite 스프라이트를 표시한다. 캐릭터 정보·주머니·지도 버튼의 원본은 `ARTIST/UI/UI_Icon.aseprite`다. 창·버튼 바탕·게이지의 벡터 원본은 `ARTIST/UI/Vector`에 있다. `VectorUiImage`는 벡터 매니페스트에 등록한 스프라이트만 메시로 대체한다.
- 픽셀 출력·캔버스 연결·좌표 변환은 `Assets/Scripts/Rendering`에 있다. 마우스는 게임 창의 실제 해상도, 월드·HUD 합성은 640×360 논리 좌표를 사용한다. `PixelViewport`의 `TryPointerToWorld`는 조준, `PointerForCanvas`는 UI 입력, `TryWorldToCanvasPoint`는 월드 표시의 부모 기준 위치를 구한다.
- UI의 기준 캔버스는 640×360이며 UI 원본 1픽셀은 논리 화면 1픽셀이다. 월드와 HUD의 렌더 타깃은 실제 게임 뷰포트 해상도를 따른다. 아이콘·비트맵 글꼴·9분할 테두리는 같은 논리 밀도를 사용하고 위치와 애니메이션에는 소수점을 허용한다.
- UI 패널과 조작부에는 약간 둥근 단색 면을 사용한다. 선택과 호버는 같은 윤곽에서 표시한다. 공용 아트는 `ARTIST/UI/Vector`, 런타임 단색 도형은 `RoundedPlate`에서 찾는다. 적 공격 예고선의 폭은 CameraSettings의 `vectorTelegraphStrokePixels`에서 논리 픽셀 단위로 조절한다. `PixelVectorShape.ThreatCue`에는 피해 판정을 구동하는 선딜 시간과 진행률을 전달한다.
- 효과·파티클 기준 색상은 `ARTIST/PALETTE/necrohanderPalette.aseprite`다. `Assets/Resources/WorldAtmosphere`의 견본을 월드 안개, GPU 파편, 층 전환 및 HUD 체력 액체 셰이더가 공유한다. 기본 이동은 플레이어·카메라·적·소환수 모두 연속이며 Sunyunie Panel에서 바꾼다.
- 월드 앞뒤 정렬은 `WorldDepthOrder`가 담당한다. 새 입체 오브젝트는 `YSortableObject`의 지면 접점을 맞추고, 몸·손처럼 함께 그릴 그림은 같은 `SortingGroup`에 넣는다. 바닥 타일과 그림자는 `Ground`·`Shadow`, 발밑 예고는 `Ground Effects`를 사용한다.
- 바닥 그림자는 `GroundShadowField`가 `YSortableObject`의 현재 스프라이트와 지면 접점을 사용한다. 일반 캐릭터와 사물은 원본에서 구운 포즈별 불투명 픽셀을 접점으로 사용한다. 특수 연출의 수동 기준점은 `shadowContact`로 지정한다. 분리된 손과 공중 연출은 `AirborneShadow`로 실제 표시 자세를 등록한다. 새 월드 광원에는 `Light2D`와 `GroundShadowLight`를 함께 붙이고, 절벽 앞면·밑단은 `TerrainRelief.asset`에 연결한다. `WallContactShadows`가 열린 문을 포함한 최종 타일 배치에서 높이와 수광면을 계산한다.
- 플레이 중 F8 조명 패널은 `LightingDebugPanel`, 세션 조정값은 `LightingPreview`에 있다. 태양·지형 그림자·하이라이트·블룸·안개는 같은 값을 사용한다. 조작과 좌표 기준은 [툴 사용 가이드](Docs/Guides/툴%20사용%20가이드.md)에 있다.
- 화면과 월드의 TMP 텍스트는 `PixelText`를 사용한다. `logicalSize`는 640×360 프레임의 글꼴 크기다. 값을 0으로 두면 저작된 크기를 가장 가까운 비트맵 크기로 맞춘다. 번역이나 런타임 문구를 추가하면 `Tools > UI > Rebuild Pixel Font Atlases`로 `Assets/Resources/PixelFonts`를 갱신한다.
- 격자형 던전의 배치 계산은 `DungeonLayoutPlanner`, 결과는 `DungeonLayout`, 씬 적용과 실제 미니맵 연결은 `MapGenerator`에서 찾는다. 공용 방 연결 규칙은 `RoomConnectivity`, 글로벌 타일맵에 셀을 쓰고 지우는 코드는 `RoomTileTransfer`가 담당한다. 피해 규칙은 `DamageRules`, 체력과 피해 이벤트는 `CharacterHealth`, 피해 숫자 표시는 `TextFloating`에서 찾는다.
- 플레이어 입력 콜백은 `PlayerController.Input.cs`, 물리 업데이트와 대시는 `PlayerController.Movement.cs`, 보간은 `PlayerLocomotion`, 상호작용은 `PlayerController.Interaction.cs`에서 찾는다. 평타 콤보·시작 조건·히트박스는 `MeleeCombatController.PlayerAttack.cs`에 있다. `PlayerInput`과 애니메이션 이벤트가 호출하는 메서드명은 프리팹과 클립에 직렬화돼 있다.
- 일반 적·돌진 엘리트·본마스터의 상태와 행동 선택은 `Assets/Scripts/Combat/EnemyAIDecisions.cs`에서 찾는다. 타깃과 시각을 읽고 NavMesh·애니메이션·코루틴을 제어하는 코드는 각 `AIPatternSO`에 있다. 공격 거리와 회복 시간의 조절값은 기존 패턴 에셋을 사용한다.
- 런의 진행 상태 수집과 복원 순서는 `GameManager.RunProgress.cs`, 디스크 읽기·교체·백업은 `SaveSystem`, JSON 필드 계약은 `SaveData`에 있다. 저장된 에셋 이름과 소환수의 이전 이름은 `GrowthRegistrySO`에서 해석한다. 튜토리얼 완료와 우클릭 해금·선택은 PlayerPrefs에 기록한다.
- 일반 작업과 AI 검색은 `Assets/Scripts`, 관련 `Assets/Editor`, `ProjectSettings`, `Docs/Guides`부터 확인한다. 보관 이력을 요청한 경우에만 루트 `Legacy`와 `Docs/Archive`를 읽는다. 보관 코드와 도구는 Unity 및 도구 실행 경로 밖의 `.txt`로 이동하고 메타데이터도 함께 텍스트로 보관한다. `ARTIST/LEGACY`와 연결된 Unity 아트에는 기존 씬이 사용하는 그림이 들어 있다.
- 프롬프트 원문과 작업 일지는 `Legacy/History`에 보관한다. 활성 주석은 좌표 단위, 직렬화 값, 호출 시점과 같이 현재 코드 수정에 필요한 이유를 설명한다. UI 배치의 현재 계약은 프리팹과 [인벤토리 UI 안내](Assets/Prefabs/UI/InventoryUI_README.md)에서 확인한다.

- 인게임 방 제작은 `Assets/Scripts/World/Workshop`, 원본 카탈로그 생성과 프리팹 변형 가져오기는 `Assets/Editor/Map/WorkshopBuild.cs`·`WorkshopRoomImport.cs`를 사용한다. Unity에서는 Sunyunie Panel의 제작실 버튼이나 `Tools > Level Workshop > Open in Play Mode`로, 제작용 Windows 빌드에서는 메인 메뉴로 들어간다. 원본 방과 팀 도구는 제작실의 Legacy 탭에 있다. 팀 전달 파일(.levelpack)의 참조 검증은 `WorkshopPackage`, 개정 비교는 `WorkshopPackageReview`, 작업 공간 저장과 전환은 `WorkshopWorkspace`에서 확인한다.

## 검증

- 사용자가 빌드 생성을 명시적으로 요청한 경우에만 실행용 Windows 빌드를 갱신한다. 일반 작업은 Unity 플레이 모드에서 검증한다.

- 인게임 제작 데이터의 원본은 루트 `DATA`다. 영역·편집 화면·반영 시점은 [DATA 안내](DATA/README.md)에 있다. `ProjectContentStore`는 문서별 병합·복구, `ProjectRooms`는 방과 일반 던전 연결, `ProjectContentBuild`는 빌드 검증·포함을 담당한다. 그림 원본은 기존 `ARTIST` 경로를 사용한다.
- `-Suite ProjectContent`는 강제 종료 복구, 두 Git 체크아웃의 지형·보상 독립 커밋과 푸시·병합, 실제 에디터 및 Windows 게임 반영, 동일 필드 충돌 해결을 검사한다. 팀 원격 저장소 대신 임시 bare 저장소를 사용한다.

- `-Suite WorkshopPlayMode`는 Unity에서 제작실 바로 실행, 시험 전투·복귀, 플레이어·조명·무대 값 변경, 작업 씬 복원을 확인한다. `-Suite LevelWorkshop`은 이 검사와 제작용 Windows 플레이어의 방 편집·전투·클리어·복귀·저장과 층 시험을 실행한다.

- [작업·검증표](Docs/Maintenance/작업_검증.md)에서 요청의 증상, 소유 파일, 재현 씬과 확인 명령을 연결한다. 여러 요청이 섞여 있으면 확인할 조건과 결과를 각각 남긴다.
- `pwsh -NoProfile -File Tools/Invoke-UnityVerification.ps1 -Suite Baseline`은 격리 프로젝트에서 에셋·화면·월드·전투 기준선을 확인한다. 구조 이동 전후에는 `-CompareTo`에 이전 `asset-inventory.json`을 전달한다.
- 픽셀 밀도·글자 모션·분위기·입자 변경은 `-Suite ArtDirection`으로 검사한다. `Tools > Sunyunie Panel`의 아트, 글자 모션, 분위기, GPU 입자 탭에서 같은 원본과 프로필을 편집한다. 검사 결과와 작업 위치는 [렌더링 검수 결과](Docs/Maintenance/픽셀아트_렌더링_검수.md)에 있다.
- 적 등장·피격, 밀착 공격, 펫 소환, 바닥 보상, UI 대기 연출과 혼전 성능은 `-Suite CombatPolish`로 검증한다. 단계와 재현 조건은 [전투·보상 계획](Docs/Maintenance/전투_보상_연출과_혼전_성능_계획.md)을 따른다.
- 독립 Windows 플레이어의 출력과 성능은 `-Suite RenderBudget`으로 측정한다. 이 검사용 빌드에만 D3D11 타임스탬프 플러그인을 포함한다. 하드웨어별 측정 파일에는 GPU와 CPU 모델을 기록한다.
- 미니맵과 던전 생성 성능은 같은 명령의 `-Suite Performance`로 비교한다. 결과에는 큰 방의 UI 오브젝트 수와 생성 시간, BattleScene의 시드 907 로그와 640×360 캡처가 남는다.
- 방 배치와 연결을 바꾸면 `-Suite Dungeon`으로 튜토리얼·일반 던전·엘리트 아레나·보스 층을 확인한다. 같은 시드로 두 번 로드하고 현재 씬에서도 다시 생성한다. 프리팹 오류와 기존 타일 아트까지 보려면 `-Suite World`를 실행한다.
- 플레이어 변경은 `-Suite Combat`에서 조작감과 공격 타이밍을 검사하고, `-Suite Presentation`에서 마을의 실제 플레이어 프리팹에 키보드·마우스 이벤트를 넣어 확인한다.
- UI 글꼴 변경은 `-Suite Typography`에서 씬·프리팹의 실제 라벨, 비트맵 아틀라스, 언어 선택과 피해 숫자, 인벤토리 입력을 검사한다.
- UI 레이아웃이나 출력 배율을 바꾸면 `-Suite UIResolution`으로 7개 Game View 크기에서 논리 배치, 최근접 픽셀 출력과 버튼 입력 위치를 검사한다.
- 적 AI 변경은 `-Suite Combat`에서 상태 전환, 본마스터의 공격 규칙, 돌진 엘리트의 특수기 선택과 코루틴 중단 시 전조 정리를 확인한다.
- 성장·저장 변경은 `-Suite Save`에서 이전 v2 저장 데이터, 백업 복구, 실제 이어하기와 층 이동, Unity 재실행 후 복원을 확인한다.
- 씬 전환과 로딩은 `Tools > Loading > Run Scene Transition Check`, 시작 층 결정은 `Tools > Map > Verify Debug Start Floor`로 확인할 수 있다. 배포 동작을 확인할 때는 `GameManager.prefab`의 `debugStartFloor`, `BattleScene`의 상점 강화 플래그, Windows 데모 프로필의 `DEMO_BUILD`를 확인한다.
- `SummonCombatPlaytest`는 중첩 입력 루틴을 순서대로 실행한다. 가상 입력이 도착하지 않아 직접 호출로 전환한 실행은 입력 경로 검증으로 집계하지 않는다.
- 새 게임과 일부 런 테스트는 저장 데이터를 바꿀 수 있다. 별도의 저장 위치가 필요한 검사는 임시 프로젝트에서 제품명과 회사명을 구분한다. Unity 종료 후 동적 TMP 글리프 데이터만 바뀐 에셋이 코드 변경과 함께 섞이지 않았는지 확인한다.
