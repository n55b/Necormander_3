# 아이템 주머니 UI 저작

## C와 V 배치

- 기준 Canvas는 960×540. 접힌 C 통계 패널은 x=-460~-240, V는 x=-224~460이다.
- 사이 간격은 16, 두 창의 높이는 480이다. C 확장 패널만 V보다 위에 표시한다.
- 실제 배치는 `Assets/Prefabs/UI/PouchUI.prefab`의 `PouchWindow`에서 수정한다. 런타임에서 위치·크기를 다시 덮어쓰지 않는다.
- `Tools/UI/Inventory/Check stacked pouch and render`로 겹침·스크롤·선택·해상도 검사를 재실행할 수 있다.

## 나중에 스프라이트가 준비되면

생성형 이미지나 통짜 합성 UI는 사용하지 않는다. 현재 색 도형은 각각의 uGUI Image이며, 아래 Image의 Source Image를 바꾸면 된다. 새 그림의 비율이 다르면 RectTransform의 크기와 여백도 함께 조정한다.

| 프리팹 / 자식 | 교체 대상 |
| --- | --- |
| PouchUI / PouchWindow | 전체 주머니 배경·테두리 |
| PouchWindow / InventoryPanel | 목록 배경 |
| PouchWindow / ItemDetails | 상세정보 배경 |
| InventoryPanel / Sort0~2 / Face | 정렬 버튼 |
| ItemDetails / IconFrame, Backing | 상세 아이콘 테두리·바탕 |
| PouchItemSlot / 루트 Image, Backing | 개별 칸 테두리·바탕 |
| PouchItemSlot / Selection | 선택 표시. 현재 네 개의 얇은 Image로 된 테두리이며, 이 컨테이너 안에 선택용 아트를 배치할 수 있다. |

- `PouchItemSlot.prefab` 한 곳을 수정하면 목록의 모든 칸에 적용된다. 칸 크기·간격·열 수는 PouchUI의 `InventoryPanel/ItemScroll/Viewport/Content`에 있는 GridLayoutGroup에서 설정한다.
- 슬롯의 Backing과 Selection은 칸 크기에 맞춰 늘어나고, 수량은 오른쪽 아래에 고정된다. 아이콘과 텍스트는 배경 아트와 별도 오브젝트다.
- 테두리를 늘릴 그림은 Sprite Editor에서 Border를 잡고 해당 Image의 Type을 Sliced로 바꾼다. 원색 그대로 표시하려면 Image Color를 흰색으로 맞춘다.
- 슬롯의 frameImage는 등급 색(ItemSO.TierColor, 일반 등급은 PouchSlotUI.commonColor), 정렬 버튼의 Face는 선택 색(PouchUI의 Inspector)을 코드에서 갱신한다. 원색 아트와 등급 색을 모두 유지하려면 원색 장식 Image와 작은 등급 테두리 Image를 분리하고 frameImage에는 등급 테두리를 연결한다.
- 아이템 Icon과 상세 Icon은 ItemSO.icon을 표시하는 자리다. 공통 장식 그림을 넣는 곳이 아니다.
- PouchUI/PouchSlotUI의 연결 필드와 ScrollRect·Viewport·Content는 유지한다. 장식 Image는 Raycast Target을 끄고 버튼의 입력용 Image는 유지한다.
- `Tools/Items/Apply stacked pouch assets`는 최초 데이터·UI 구축용이다. 아트 교체에는 실행하지 않는다. 프리팹을 직접 편집한다.

아이템 추가·중첩·선택·정렬·스크롤 로직과 아트는 분리되어 있으므로, 배경과 칸 이미지를 교체해도 해당 로직을 다시 작성할 필요는 없다.
