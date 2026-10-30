# C / V / 미니언 습득 UI

현재 UI 구조는 프리팹에 저장되어 있습니다. 게임 중에는 데이터와 표시 상태를 갱신하고, 주머니 아이템 셀만 보유 종류 수에 맞춰 생성합니다. 기존 UI_Panel 스프라이트를 사용하며 생성형 아트는 사용하지 않습니다.

## C · 플레이어 정보

`SkillExplainUI.prefab / LoadoutPanel`

- `StatsPanel`: 왼쪽 정보창(220×480). C로 독립 토글, 전투 중에도 시간을 멈추지 않습니다.
- `StatsPanel/Summary/Icons`: 장착 아이콘. HorizontalLayoutGroup으로 가운데 정렬하며 비어 있는 아이콘 칸은 숨깁니다.
- `StatsPanel/PlayerStats/Rows`: 18개 스탯 행. 상단 피벗, 상하좌우 안쪽 여백 12, VerticalLayoutGroup 간격 0, 행 높이 18입니다. 회피 횟수 아래 빈 행이 없습니다.
- 각 행의 `Label` / `Value`: 이름과 실제 수치. 앵커 66%에서 두 열을 나눕니다. 수치는 기존처럼 0.2초마다 갱신합니다.
- 각 행의 `SkillTooltipTrigger`: 제목·설명을 수정하는 곳. 같은 행의 Image가 마우스 판정을 받으므로 글자와 별도의 투명 영역을 수동 배치할 필요가 없습니다. 행을 옮기면 툴팁도 따라갑니다.
- 루트 `SkillExplainUI > 스탯 행`: 스탯 종류와 수치 글자 연결. 행 순서를 바꿔도 이 연결로 값이 유지됩니다.
- `StatsPanel/ExpandButton`: 상세 펼침/접힘. C를 닫으면 접힌 상태로 초기화됩니다.
- `DetailsPanel/Equipment`, `Guard`: 왼쪽 장비·가드 정보.
- `DetailsPanel/MinionPanel`: 오른쪽 하나의 장식 프레임 안에 Minion / SpaceSkill / Finisher / Dash를 배치합니다.
- C와 V는 함께 열 수 있고, 펼친 C 상세는 V보다 앞에 표시됩니다. F5 디버그 정보는 별도입니다.

## V · 아이템 주머니

`PouchUI.prefab / PouchWindow`

- 기준 Canvas는 960×540, 너비 기준 스케일. 접힌 C는 x=-460~-240, V는 x=-224~460. 간격 16, 위아래 높이 480으로 맞춥니다.
- `InventoryPanel`: 흰 장식 테두리. 정렬 탭은 왼쪽·위 16 안쪽에 둡니다.
- `InventoryPanel/ItemArea`: 붉은 목록 바탕. 좌우·아래 여백 16, 위 48. 흰 테두리를 가리지 않습니다.
- `InventoryPanel/ItemScroll`: 목록 바탕보다 4 더 안쪽. Viewport/RectMask2D가 아이템 표시 범위를 제한합니다.
- `ItemScroll/Viewport/Content`: GridLayoutGroup. 6열, 칸 52×52, 간격 6, 내부 패딩 4. 종류가 많아지면 세로 스크롤됩니다.
- `PouchItemSlot.prefab`: 공통 아이템 칸·아이콘·수량·선택 테두리.
- `ItemDetails`: 오른쪽 상세. 아이콘, 이름, 등급, 보유 수량, 개당/총 효과, 다음 중첩 효과, 설명을 표시합니다.
- 보유 종류/칸 수 제한은 없으며 아이템별 최대 중첩은 유지합니다. 아이템 버리기·분해·시너지 패널은 사용하지 않습니다.

## 스프라이트·색 교체

`Assets/Resources/UI/UISkinSettings.asset`에서 `큰 창`, `C·V 장식 패널`, `주머니 목록 바탕`, `탭 버튼`, `아이템 칸` 등을 바꿉니다. 각 Image의 `UISkinPart` 역할로 연결되어 있어 Source Image만 바꾸면 다시 설정값으로 덮어써질 수 있습니다.

- C·V 장식 패널: UI_Panel_6(흰 테두리). 투명한 가운데로 큰 창의 보라색 바탕이 보입니다.
- 주머니 목록 바탕: UI_Panel_Red_Half. 탭과 별개 역할이므로 따로 교체할 수 있습니다.
- 테두리는 Sliced/9분할, 그림 픽셀 굵기는 스킨의 `테두리 굵기 배율`로 조절합니다.
- 크기·위치는 위 프리팹의 RectTransform/피벗/앵커/레이아웃 그룹에서 편집합니다.
- `0928`, `1006`의 옛 Apply/Preview/Validate 도구는 초기 이행 당시 구조용입니다. 현재 프리팹 재생성용으로 실행하지 마세요.

## 미니언 선택

`Hand Slot Selection/HandSlotSelectionUI.prefab`은 별도 화면입니다. 미니언이 없으면 클릭으로 장착하고, 보유 중이면 교체/스킵을 각각 1초 누릅니다. 이 화면의 동작은 이번 C/V 변경 대상이 아닙니다.

## 2026-10-30 확인

- EditMode 미리보기에서 C/V, C 상세, 많은 아이템의 스크롤 확인.
- 18개 스탯의 이름·수치 중심을 실제 GraphicRaycaster로 검사해 각각 같은 행의 툴팁 대상으로 판정되는지 확인.
- 붉은 목록/스크롤의 경계가 흰 테두리 안쪽에 있는지 확인.
- 세렌디피티는 SC 애니메이션(보라색 외형), 마리오네트는 AS 애니메이션(갈색 발톱 외형)이 정상 연결되어 있었음. Temp_Icon의 SC/AS 그림 배정이 반대여서, 두 기본 분기와 최종 장비의 icon 참조만 실제 외형에 맞춤.
- 미니언 생성음은 FeedbackCatalog > MinionAppear(27) > audio.volume 0.085→0.02.
- Play Mode는 실행하지 않았습니다. 실제 조작·청감 확인은 사용자 플레이에서 진행합니다.
