# C / V / 미니언 습득 UI

모든 화면 구조는 프리팹에 저장되어 있습니다. 게임 중에는 데이터와 표시 상태만 바꿉니다. 새 아트는 만들지 않았고 기존 보상 카드·폰트·아이콘을 사용합니다.

| 편집할 내용 | 프리팹 / 필드 |
| --- | --- |
| C 장착 정보 | `SkillExplainUI.prefab` → `LoadoutPanel` 아래 Equipment / Guard / Minion / SpaceSkill / Finisher / Dash |
| V 주머니 9칸 | `PouchUI.prefab` → `PouchWindow/InventoryPanel` |
| 가방 밖 드래그의 붉은 선 | `PouchUI.prefab` → `DropOutline/Top, Bottom, Left, Right` (색/두께/화면 여백) |
| 세트 효과 자리(미구현·비활성) | `PouchUI.prefab` → `SetEffects_NotImplemented` |
| 미니언 습득·교체·비교 | `Hand Slot Selection/HandSlotSelectionUI.prefab` → `MinionSelection` |
| 미니언 없음/있음 카드 위치 | 위 프리팹의 `SingleCardAnchor` / `CandidateAnchor` |
| 교체·스킵 홀드 시간 | 위 프리팹 루트의 `Confirm Hold Seconds` (교체 1초), `Skip Hold Seconds` (스킵 3초). 변경 시 버튼 문구도 함께 수정 |
| 아이템 버리기 거리 | `PouchUI.prefab` 루트의 `Drop Distance` (플레이어에서 1.25유닛) |
| 교체·스킵 채움 바 | `MinionSelection/Replace, Skip/HoldProgress` — 버튼 안쪽 전체를 왼쪽부터 채움. RectTransform의 가로 앵커만 실행 중 변경 |
| 월드 미니언 픽업 | `Assets/Prefabs/Resources/GroundMinion.prefab` |
| F 분해 시간 / 회복량 | 픽업 루트 `GroundItem`의 `Disassemble Hold Seconds` / `Minion Recycle Heal` (10) |
| 공용 마우스 툴팁 | `Hand Slot Selection/CommonTooltipUI.prefab` — 각 플레이 씬에 이미 배치된 1개를 공유 |

## 동작

- **C**: 장비·가드·현재 미니언 능력 조회. **V**: 아이템 정리. 다시 누르거나 ESC로 닫습니다. B는 사용하지 않습니다.
- C/V는 전투 중에도 열리며 시간을 멈추지 않습니다. 둘 중 하나만 열립니다.
- V에서 아이템을 쥐고 가방 밖으로 나가면 화면 가장자리에 붉은 선이 켜집니다. 놓는 즉시 마우스 방향의 플레이어 주변에 드랍하고 소지 효과를 제거합니다. 마우스를 멀리 둬도 드랍 거리는 동일하며, 별도 대기 칸은 없습니다.
- 놓으려던 주변 위치가 벽/물이면 가장 가까운 이동 가능 바닥으로 보정합니다. 벽 반대편이나 플레이어에게서 먼 지점은 제외하며, 주변이 막혔으면 플레이어 쪽 경계/발밑의 안전한 땅을 사용합니다. 화면 밖, 맵 미준비, 픽업 생성 실패 시에는 소지품을 유지합니다. 놓기 전에 V/ESC/C로 창을 닫거나 사망하면 취소합니다.
- 가방·드래그·바닥 픽업은 같은 `ItemSO.icon`을 씁니다. 아이콘이 아직 없는 아이템은 모두 `GroundItem.prefab`의 기존 SpriteRenderer 그림으로 통일합니다.
- 미니언 카드/바닥 픽업은 `MinionDataSO.minionIcon` / `minionName`을 씁니다. Space 스킬 칸만 스킬 아이콘/이름을 표시합니다.
- 미니언이 없으면 후보 카드를 클릭해 바로 장착합니다. 이미 있으면 교체 버튼을 1초 누릅니다. 기존 미니언은 바닥에 떨어집니다. 스킵은 기존대로 3초입니다.
- 후보/현재 카드에 마우스를 올리면 각 능력, 중앙 비교 영역에 올리면 양쪽 능력이 나옵니다.
- 스킵은 미습득이며 회복하지 않습니다. 후보는 바닥에 남고 다시 F로 확인하거나 F를 길게 눌러 분해할 수 있습니다. 미니언 분해만 체력 10을 회복합니다. 아이템 분해는 기존 골드 환급입니다.
- 상점 F 구매: 빈자리 있으면 즉시 습득, 없으면 무료 픽업으로 드랍. 진열품 마우스 호버는 이름·효과·가격 툴팁입니다.
- 미니언 데이터의 `finisher.uiDescription` / `dashModifier.uiDescription`을 채우면 그 문구를 표시합니다. 비워두면 타수·배율 등의 데이터로 설명합니다.

## 검사

`Tools > UI > 0928` 아래 Validate / Check 메뉴로 참조·정리·상점·미니언 교체를 검사할 수 있습니다.

기존 아트: `UI_New.png`의 left/middle/right 프레임과 Select 카드, `UI.png`의 UI_SkillFrame을 재사용합니다. 새 이미지를 생성하지 않았습니다. 프리팹의 PixelFrame 자식 및 Image의 Source Image를 에디터에서 교체할 수 있습니다.

빠른 플레이 확인: V에서 칸 교환 → 바깥 드래그 붉은 선 → 마우스 방향의 가까운 땅에 드랍 → 벽 옆/물가에서도 가까운 안전 지점인지 확인 → 다시 F로 습득. 미니언 1개 장착 후 다른 미니언을 F로 확인 → 교체 1초 / 스킵 3초 홀드(중간에 놓으면 취소).

**Apply authored inventory UI는 최초 이행/재생성 도구입니다. 다시 실행하면 프리팹 배치를 초기화하므로, 사람이 위치·크기를 튜닝한 뒤에는 실행하지 마세요.** 평소에는 위 프리팹을 직접 편집하면 됩니다.
