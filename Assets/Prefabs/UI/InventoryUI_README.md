# C / V / 미니언 습득 UI

모든 화면 구조는 프리팹에 저장되어 있습니다. 게임 중에는 데이터와 표시 상태만 바꿉니다. 1006 문서의 배치와 색상을 적용했고, 미제작 프레임은 스프라이트 없는 기본 사각형(Image)입니다. 기존 미니언 카드·폰트·아이콘은 유지하며 생성형 아트는 사용하지 않습니다.

## 1006 외형 편집

씬 Canvas는 960×540 기준이므로 화면 치수를 **절반**으로 입력합니다. C 스탯 220×480, 펼친 상세 660×480, V 가방 360×375, 오른쪽 세트 효과 180×240, 교체/스킵 300×60입니다.

- `SkillExplainUI.prefab / LoadoutPanel/StatsPanel`: C로 켜는 왼쪽 스탯. `Summary`에는 보유 아이콘, `PlayerStats/Values`에는 0.2초마다 갱신되는 실제 스탯을 표시합니다. `PlayerStats/StatHover*`는 공용 스탯 설명 툴팁입니다. F5 디버그 패널은 그대로입니다.
- `StatsPanel/ExpandButton`: 상단 화살표. `SkillExplainUI.ToggleDetails()`가 Inspector의 OnClick에 연결되어 있습니다. 다시 누르면 접히고 C를 닫으면 접힘 상태로 초기화됩니다.
- `LoadoutPanel/DetailsPanel`: 펼쳤을 때만 보이는 장비·가드(왼쪽), 미니언·Space·마무리·대시(오른쪽). V보다 앞에서 그려지고 클릭을 막습니다. 큰 부모 `LoadoutPanel` 자체에는 배경/클릭 차단이 없어서 접힌 C가 가방을 가리지 않습니다.
- 각 창의 `Frame1006`: 적갈색 테두리 4개 + 청록색 모서리 4개. Image의 Color와 RectTransform으로 조절합니다. 예전 `PixelFrame`은 삭제하지 않고 비활성 보관했습니다.
- `PouchUI.prefab / PouchWindow/InventoryPanel`: 중앙 3×3 슬롯. 오른쪽 `SetEffects_NotImplemented`도 V와 함께 표시합니다. 세트 판정 기능은 아직 없으므로 준비 중임을 표시합니다. `Inset1006`은 슬롯 안쪽 배경이며 등급색·잠금·아이콘·드래그 기능은 유지합니다.
- `Hand Slot Selection/HandSlotSelectionUI.prefab`: 미니언 카드/성장 구조는 변경하지 않았습니다. `Replace`, `Skip`만 새 프레임이며 홀드 시간은 각각 1초입니다.
- `Player State/PlayerStateUI.prefab`: HP는 체리색, 가드는 청록빛 파랑(파괴 시 회색). `Track1006` 아래 막대는 스프라이트 없이도 실제 너비가 변합니다. 이후 스프라이트를 넣으면 Image Type을 **Filled / Horizontal**로 설정하세요.
- 같은 프리팹의 `WorldDashPips`: 발밑 대시 횟수/충전 표시. 프리팹에 4칸을 미리 배치하고 실제 최대 횟수만 켭니다. 위치 오프셋은 기존 `DashCooldownUI`의 `World Offset`에서 변경합니다. HUD 아이콘의 쿨타임은 그대로입니다.
- `MiniMap.prefab / Image_MiniMap`: 120×100(화면 240×200), 반투명 배경과 3.5(화면 7) 테두리. 전투 중 방 전체를 비율 유지해 안쪽에 맞춥니다. 전체 지도 기능은 유지합니다.

`Tools > UI > 1006`의 Validate / Check dynamic HUD and text / Render previews로 확인할 수 있습니다. **Apply placeholder layout은 배치를 초기화하는 이행 도구**이므로 수동 튜닝 후에는 재실행하지 마세요. 보통은 위 프리팹을 직접 편집하면 됩니다.

| 편집할 내용 | 프리팹 / 필드 |
| --- | --- |
| C 장착 상세 정보 | `SkillExplainUI.prefab` → `LoadoutPanel/DetailsPanel` 아래 Equipment / Guard / Minion / SpaceSkill / Finisher / Dash |
| V 주머니 9칸 | `PouchUI.prefab` → `PouchWindow/InventoryPanel` |
| 가방 밖 드래그의 붉은 선 | `PouchUI.prefab` → `DropOutline/Top, Bottom, Left, Right` (색/두께/화면 여백) |
| 세트 효과 확인란(판정 기능 준비 중) | `PouchUI.prefab` → `PouchWindow/SetEffects_NotImplemented` |
| 미니언 습득·교체·비교 | `Hand Slot Selection/HandSlotSelectionUI.prefab` → `MinionSelection` |
| 미니언 없음/있음 카드 위치 | 위 프리팹의 `SingleCardAnchor` / `CandidateAnchor` |
| 교체·스킵 홀드 시간 | 위 프리팹 루트의 `Confirm Hold Seconds` (교체 1초), `Skip Hold Seconds` (스킵 1초). 변경 시 버튼 문구도 함께 수정 |
| 아이템 버리기 거리 | `PouchUI.prefab` 루트의 `Drop Distance` (플레이어에서 1.25유닛) |
| 교체·스킵 채움 바 | `MinionSelection/Replace, Skip/HoldProgress` — 버튼 안쪽 전체를 왼쪽부터 채움. RectTransform의 가로 앵커만 실행 중 변경 |
| 월드 미니언 픽업 | `Assets/Prefabs/Resources/GroundMinion.prefab` |
| F 분해 시간 / 회복량 | 픽업 루트 `GroundItem`의 `Disassemble Hold Seconds` / `Minion Recycle Heal` (10) |
| 공용 마우스 툴팁 | `Hand Slot Selection/CommonTooltipUI.prefab` — 각 플레이 씬에 이미 배치된 1개를 공유 |

## 동작

- **C**: 왼쪽 스탯을 토글. 상단 화살표로 장비·가드·미니언 상세를 펼치거나 접습니다. **V**: 중앙 가방 + 오른쪽 세트 효과 확인란을 토글. B는 사용하지 않습니다.
- C/V는 전투 중에도 동시에 열 수 있고 시간을 멈추지 않습니다. 각 키는 자기 창만 닫습니다. 열기 순서와 무관하게 C 상세가 V 위에 놓이며, 둘 다 열었을 때 ESC는 C → V 순으로 닫습니다. 옵션/대화/보상은 기존 팝업 규칙을 유지합니다.
- C를 새로 열거나 화살표로 상세를 전환하면 진행 중 가방 드래그는 취소합니다. 스탯·상세·세트 효과 확인란 위에서 아이템을 놓아도 바닥에 버리지 않습니다.
- V에서 아이템을 쥐고 가방 밖으로 나가면 화면 가장자리에 붉은 선이 켜집니다. 놓는 즉시 마우스 방향의 플레이어 주변에 드랍하고 소지 효과를 제거합니다. 마우스를 멀리 둬도 드랍 거리는 동일하며, 별도 대기 칸은 없습니다.
- 놓으려던 주변 위치가 벽/물이면 가장 가까운 이동 가능 바닥으로 보정합니다. 벽 반대편이나 플레이어에게서 먼 지점은 제외하며, 주변이 막혔으면 플레이어 쪽 경계/발밑의 안전한 땅을 사용합니다. 화면 밖, 맵 미준비, 픽업 생성 실패 시에는 소지품을 유지합니다. 놓기 전에 V를 닫거나 사망하면 취소합니다.
- 가방·드래그·바닥 픽업은 같은 `ItemSO.icon`을 씁니다. 아이콘이 아직 없는 아이템은 모두 `GroundItem.prefab`의 기존 SpriteRenderer 그림으로 통일합니다.
- 미니언 카드/바닥 픽업은 `MinionDataSO.minionIcon` / `minionName`을 씁니다. Space 스킬 칸만 스킬 아이콘/이름을 표시합니다.
- 미니언이 없으면 후보 카드를 클릭해 바로 장착합니다. 이미 있으면 교체 버튼을 1초 누릅니다. 기존 미니언은 바닥에 떨어집니다. 스킵도 1초입니다.
- 후보/현재 카드에 마우스를 올리면 각 능력, 중앙 비교 영역에 올리면 양쪽 능력이 나옵니다.
- 스킵은 미습득이며 회복하지 않습니다. 후보는 바닥에 남고 다시 F로 확인하거나 F를 길게 눌러 분해할 수 있습니다. 미니언 분해만 체력 10을 회복합니다. 아이템 분해는 기존 골드 환급입니다.
- 상점 F 구매: 빈자리 있으면 즉시 습득, 없으면 무료 픽업으로 드랍. 진열품 마우스 호버는 이름·효과·가격 툴팁입니다.
- 미니언 데이터의 `finisher.uiDescription` / `dashModifier.uiDescription`을 채우면 그 문구를 표시합니다. 비워두면 타수·배율 등의 데이터로 설명합니다.

## 검사

`Tools > UI > 0928` 아래 Validate / Check 메뉴로 참조·정리·상점·미니언 교체를 검사할 수 있습니다.

기존 아트 파일과 미니언 Select 카드는 남겨두었습니다. 비활성 `PixelFrame`을 다시 쓰려면 `Frame1006`을 끄고 현재 창 치수에 맞게 위치/크기를 조정하세요. 슬롯의 Image Source Image도 에디터에서 교체할 수 있습니다.

빠른 플레이 확인: V에서 칸 교환 → 바깥 드래그 붉은 선 → 마우스 방향의 가까운 땅에 드랍 → 벽 옆/물가에서도 가까운 안전 지점인지 확인 → 다시 F로 습득. 미니언 1개 장착 후 다른 미니언을 F로 확인 → 교체·스킵 모두 1초 홀드(중간에 놓으면 취소).

**Apply authored inventory UI는 최초 이행/재생성 도구입니다. 다시 실행하면 프리팹 배치를 초기화하므로, 사람이 위치·크기를 튜닝한 뒤에는 실행하지 마세요.** 평소에는 위 프리팹을 직접 편집하면 됩니다.
