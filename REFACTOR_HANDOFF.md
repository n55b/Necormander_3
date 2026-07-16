# 미니언 리팩토링 인수인계 (2026-07-16 새벽)

브랜치 `미니언스킬바꾸기`, 커밋 **12개** (`979ad35` ~ `e0306ba`).
**Phase 0~2는 이미 origin에 있고, Phase 3~11은 로컬입니다.**

## 먼저 읽을 것

- `MINION_ARCHITECTURE.md` — 리팩토링 **전** 구조 맵 (이제 히스토리 문서)
- `GEM_LEGACY.md` — 지운 젬 효과 전수 스펙 (재건용)

## 상태 한 줄

C# 컴파일 **오류 0** (Assembly-CSharp / -Editor 양쪽, `dotnet build`로 검증).
**Unity를 한 번도 열지 못했습니다** — 실제 플레이 검증은 안 됐습니다.

---

## ⚠️ Unity 열면 바로 해야 할 것

### 1. 새 에셋 6개가 아직 임포트 안 됨
`SOData/Minion/MinionData/{MeleeDoll,DashDoll,Lion,Mask} Minion.asset`
`SOData/Skills/Minion/{MeleeDoll,DashDoll}_Active.asset`

전부 **손으로 쓴 YAML**입니다. 필드명은 C#과 대조해 오타 0을 확인했지만, Unity가 파싱한 적은 없습니다.
→ 열고 인스펙터에서 값이 제대로 들어갔는지 눈으로 확인해주세요. 특히 `skillAnimVisual` /
`finisher.visual`의 오브젝트 참조가 **None이 아닌지** — 여기가 null이면 소환수가 안 보입니다.

### 2. 새 스크립트 5개의 `.meta`가 없음
`MinionSkillCaster` / `SubSummonPassiveController` / `MinionDashModifier` / `MinionFinisher` /
`MinionSubPassive`. Unity가 열리면서 자동 생성합니다. **생성된 뒤 커밋해주세요** —
안 그러면 다른 PC에서 클론할 때 guid가 달라집니다.

### 3. 소환수 아이콘 4개 미할당 (의도적으로 안 함)
4종 전부 `minionIcon: {fileID: 0}`이라 보상 카드/슬롯/상점이 **빈 아이콘**으로 뜹니다.
스프라이트는 `Resources/Sprites/Minion/*.aseprite`에 있지만, Unity가 임포트 전이라
sub-asset fileID가 존재하지 않아서 손으로 넣으면 잘못된 참조가 됩니다.
→ 인스펙터에서 드래그로 넣어주세요.

### 4. `SkillExplainUI`의 유령 3번째 슬롯
`linkedSkillSlots` 배열이 3칸인데 소환수는 2마리입니다. 3번째는 빈 박스로 남습니다.
프리팹에서 배열 크기를 2로 줄이면 됩니다. (`MinionStateUI`는 자동으로 꺼지므로 문제없음)

---

## 설계 대비 구현 현황

| 설계 | 상태 |
|---|---|
| 영혼 대기 → Space → 실체화 → 시전 → 소멸 → 쿨타임 | ✅ (`MinionSkillCaster`) |
| 소환수별 개별 쿨타임 | ✅ |
| 메인 1 + 서브 1 슬롯 | ✅ (`SLOT_MAIN`/`SLOT_SUB`) |
| 메인/서브 풀 분리 | ✅ (`MinionRole` + 카운트 기반 유동성) |
| 메인이 대쉬 변화 | ✅ (`MinionDashModifier`) |
| 메인이 평타 변화 (2타 + 소환수 마무리) | ✅ (`MinionFinisher`) |
| 서브 상시 스탯 + 패시브 | ✅ (`MinionSubPassive`) |
| 보상 3종 (플스킬/메인/서브) | ✅ |
| 서브 상점 등장 | ✅ |
| 플레이어 외형 변화 | ❌ 설계가 "예정"이라 안 함 |
| 유물 | ❌ 설계가 "제외해도 무관"이라 안 함 |

---

## 알려진 남은 문제 (검증에서 나왔으나 안 고친 것)

### 소환수 보상 방이 카드 1장 + "None" 2장
`GenerateSummonRewards`는 항상 3장을 뽑는데 메인 2종/서브 2종뿐이고 장착 중인 건 제외됩니다.
**코드 결함이 아니라 콘텐츠 부족**입니다. 소환수를 더 만들거나
`MapGenerationData.asset`의 `mainSummonRewardRoomCount` / `subSummonRewardRoomCount`를
낮추면 됩니다 (그게 "풀 유동성" 스위치입니다).

### `AddMinionOrIncreaseQuantity`의 minionType 붕괴 (도달 불가지만 살아있음)
MeleeDoll/DashDoll이 둘 다 `minionType: 0`, Lion/Mask가 둘 다 `100`입니다.
이 함수는 `minionType`으로 찾아서 DashDoll/Mask를 못 줍니다.
**현재 도달 불가** — 실제 경로(`HandSlotSelectionUI` → `EquipMinion(asset)`)는 에셋을 직접
넘기고, BattleScene/MapDesign 모두 `handSlotUI`가 연결돼 있습니다. `RewardManager.cs:148`의
`handSlotUI == null` 폴백에서만 터집니다. 지우거나 이름 기반으로 고치는 게 안전합니다.

### 엘리트/보스 방이 "None" 카드만 줌 (리팩토링 이전부터)
`specialAbilities: []` / `gems: []`가 원래 비어 있었고 `Metamorphosis`는 no-op입니다.
소프트락은 없습니다(`rawData == null` 가드). **이번 리팩토링이 만든 게 아닙니다.**

### 투척 시스템이 사실상 빈 껍데기 (일부는 이전부터)
`AllyController`가 사라지며 남은 `IThrowable` 구현체 4개가 전부 `CommandData.None`을
반환합니다 → `ThrowStrategy`의 직업별 액션(`ArcherAction`/`PriestAction`/...)이 전부 도달 불가.
다만 `TryPickUpByType`/`CanPickUpType`은 **리팩토링 전부터** 죽은 코드(`/* [OLD LOGIC] */`
블록 안에만 호출부가 있음)였습니다. 투척 시스템을 어떻게 할지는 별도 결정이 필요합니다.

### 기존 dangling guid (리팩토링과 무관, `979ad35~1`에서도 동일)
`Renderer2D.asset`, `SOData/State/*.asset` 4개, `Player.prefab:367/1058`,
`Charger Elite.prefab:173`, `DebuffIcon.prefab:121`, `MapDesign.unity:1103`,
`VillageScene.unity:4051`. **오늘 것으로 오해하지 마세요.**

---

## 밸런스가 실제로 바뀐 것

1. **젬 4계열 30개 효과 제거** — 단 젬은 소켓에 꽂아야 발동했고 새 런은 젬 0개로
   시작하므로 **바닥은 그대로**입니다. 없어진 건 골드로 강해지던 천장입니다.
2. **부식 적 피해 ×1.08 사라짐** — 이건 젬 없이도 항상 켜져 있던 상수였습니다.
   되살리려면 `GEM_LEGACY.md` 3-1 참조. (사라진 채로 두기로 결정)
3. **평타 3타(`Attack_Medium`, 1.5배)가 소환수 마무리로 교체** — 메인 소환수가 없으면 2타만 반복.
4. **투척 관련 젬 보너스** (보유 개수/사거리/차지 효율/드롭 면역/융합) 전부 제거.

## 세이브

포맷이 바뀌었습니다 (`equippedLineageJob` → `equippedMinionName`).
`%LOCALAPPDATA%Low/DefaultCompany/Necromander_3/save_data.json`의 **기존 세이브는
안전하게 degrade**됩니다(크래시 없이 미니언 슬롯만 비고 디버그 로드아웃이 들어감) —
로드 경로를 추적해 확인했습니다.

## 디버그 시작 로드아웃

`GameManager.prefab`의 `debugStartingMinions`를 **MeleeDoll(메인) + Lion(서브)**로 바꿔놨습니다.
켜자마자 새 구조를 테스트할 수 있습니다.

---

## 문제가 생기면

커밋이 Phase별로 하나씩이라 이분탐색이 됩니다:

```
979ad35 Phase 0  죽은 코드 삭제
9cf075a Phase 1  Layers 정리          (동작 변화 0)
ebbc612 Phase 2  연계 폐기 → Space 무조건 발동
c6e3c7d Phase 3  젬 효과 제거
2b9621c Phase 4  MinionRole + 세이브 정체성 + 슬롯 2칸
59dab50 Phase 5  퍼펫 → MinionSkillCaster
3bfb6bd Phase 6  대쉬 개조
abe6cad Phase 7  평타 2타 + 마무리
2db397a Phase 8  서브 패시브
07d9866 Phase 9  보상 3종
e1166d3 Phase 10 구 에셋 삭제 + 새 소환수 4종
e0306ba Phase 11 검증 결함 수정
```

`git checkout <커밋>` 후 Unity를 열어보면 어디서 깨졌는지 좁힐 수 있습니다.
