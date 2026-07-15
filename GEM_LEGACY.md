# GEM_LEGACY — 젬 효과 제거 기록

> **이 문서의 목적**: 2026-07-16 젬 **효과**를 전부 걷어내면서, 나중에 재건할 수 있도록 스펙을 박제한 기록.
> 제거 직전 커밋: `ebbc612` (Phase 2). 삭제는 Phase 3에서 수행.
> **젬 구조(통로)는 그대로 살아 있습니다** — 지운 것은 효과뿐입니다.

## 0. 왜 지웠나

미니언 구조를 메인/서브 소환수로 개편하면서, 젬 시너지 15그룹 중 7개가 **스켈레톤 직업에 묶여** 있어 직업 개념과 함께 고아가 됐습니다. 사용자 판단: *"젬 시너지 그룹도 레거시 잔재. 지금 안 쓰는 시스템이고 거기 있는 효과들은 하나도 안 쓸 거다. 다 없애도 된다. 젬 구조만 놔둘 수 있으면 좋고, 나중에 다시 만들기 편하게 기록도 해두면 좋고."*

## 1. 실제로 살아 있던 것 (중요 — 이건 무변화가 아니었다)

15개 그룹 중 **4개가 라이브**였고, 상점에서 살 수 있었습니다. 제거로 **실제 밸런스가 바뀝니다**:

| 그룹 | 에셋 | 잃는 것 |
|---|---|---|
| `Stamina` (4) | 10 | 스태미너 최대치/자연회복/투척 소모, 빈 스태미너 회복 가속(×2.0) |
| `Fastball` (5) | 6 | 차지 효율 |
| `Shotput` (600) | 6 | 포물선 투척 효율 (+25%/+50%) |
| `BigHand` (700) | 8 | 최대 보유 개수(+1/+3), 피격 시 드롭 면역, 투척 사거리, 융합(Golem/Twin) |

나머지 11개 그룹은 **에셋이 `SOData/Deprecated/Gems/`에 있고 레지스트리에 없어서 도달 불가**였습니다(원래 "에셋 0개"로 파악했으나 정정됨).

## 2. 남아 있는 통로 (재건 시 여기에 붙이면 됨)

**이게 이 문서의 핵심입니다.** 아래는 **삭제되지 않았습니다**:

- `GemSO.cs` 전체 — `synergyGroup` / `subSlots` / `eligibleJobs` / `effects` + `GemSynergyGroup` · `GemUniqueType` enum
- `GemEffect.cs`, `GemUniqueEffect.cs`, `GemStatEffect.cs` — **특히 `GemUniqueEffect`는 반드시 유지해야 함**. 지우면 31개 에셋의 `SerializeReference`가 null이 되어 로드 시 에러
- `GemTreeNode.cs`, `GemInstance.cs`, `GemTreeDebugger.cs`, `UI/GemTree/*`, `GemSlotSelection*`
- `InventoryManager`의 젬 영역 전부: `RecalculateGemTreeStats` → `CalculateSynergies` → `FindClusterSize`(부모/자식 + 좌우 시각 이웃 BFS, wrap-around), `SocketGem`/`UnsocketGem`, `OnGemTreeUpdated`
- **질의 API 3종 (재건의 진입점)**: `GetSynergyCount(group)` · `GetUniqueEffectCount(type)` · `HasUniqueEffect(type)`
- 31개 젬 에셋 (`SOData/Rewards/Gems/`) — 소켓은 되지만 효과는 없음

**재건 방법**: `InventoryManager.OnGemTreeUpdated`를 구독 → `GetSynergyCount` / `GetUniqueEffectCount` 읽기 → 효과 적용. **이음매는 그게 전부입니다.** 그 외에는 아무것도 필요 없었습니다.

## 3. 레벨 임계값 (`GemSynergyLogic.GetLevel`)

```
클러스터 크기 >=2 → Lv1 | >=4 → Lv2 | >=6 → Lv3 | >=8 → Lv4 | 그 외 0
```
클러스터 = 트리에서 **인접한** 같은 그룹 노드 수. 총 보유 개수가 아님.

> ⚠️ **알려진 불일치**: `GemSynergyDisplayUI`는 "Lv1~Lv4"로 표기하지만 코드는 같은 텍스트를 `level>=2`로 게이트한 경우가 있음(예: Poison 지속시간 — UI는 Lv1, 코드는 4개 필요). **재건 시 한쪽으로 통일할 것.**

## 3-1. ⚠️ 실제로 사라진 밸런스: 부식 적 피해 ×1.08

젬처럼 보였지만 **젬 없이도 항상 켜져 있던** 효과가 하나 있었고, 이번에 같이 사라졌습니다. 되살리고 싶으면 여기를 읽으세요.

- **무엇**: 부식(`DebuffBoolType.Corroded`) 상태인 **적이 받는 모든 피해 × 1.08**
- **어디**: `SynergyDamageAmplifier` (`Scripts/Deprecated/ActiveGemHandlers.cs`) → `DamageEventBus.OnBeforeDamageCalculated` 구독
- **왜 항상 켜져 있었나**: `GemSynergyLogic.GetSenilityDamageAmp(level)` = `level >= 3 ? 0.12f : 0.08f`. 젬 조회가 `level = 0`으로 스텁돼 있었으므로 **항상 0.08f 폴백**이 반환됐습니다. 즉 "Priest_Aging 시너지 효과"라는 껍데기를 쓰고 있었지만 실제로는 무조건 발동하는 상수였습니다.
- **왜 지웠나**: 이 핸들러는 젬 핸들러 레지스트리(`GemHandlerRegistry.InitializeAllHandlers()` → `SynergyDamageAmplifier.Initialize()`)를 통해서만 등록됐습니다. 레지스트리가 사라지면 구독 자체가 일어나지 않으므로, 파일을 남겨도 죽습니다.
- **덤 버그**: 이 클래스엔 `if (GetDebuffBool(Corroded))` 블록이 **두 개** 있었고 첫 번째(부식 증폭)는 배율이 항상 `0f`라 무효였습니다. 두 번째가 노쇠 증폭인데 `DebuffBoolType`에 `Senile`/`Aging` 항목이 아예 없어서 `Corroded`로 대체돼 있었습니다.

**되살리는 법 (젬과 무관하게)**: `DebuffRuleSystem`에 아래를 넣고 `DamageEventBus.OnBeforeDamageCalculated`에서 호출하면 됩니다.
```csharp
public static float GetSenilityDamageAmp(bool isEnemyTarget) => isEnemyTarget ? 0.08f : 0f;
```
제대로 하려면 `DebuffBoolType`에 `Senile` 항목을 먼저 추가하고 노쇠를 부여하는 경로를 만드세요.

## 4. 발견된 실제 버그 (재건 전 고칠 것)

1. **중복 유니크가 스택되지 않음** — `InventoryManager.cs:263-267`이 `UniqueEffectCounts`를 순회하며 **타입당 1개만** 리스트에 넣어서, `GemHandlerRegistry`의 "중복 장착 지원" 주석과 달리 핸들러형 유니크는 스택 안 됨. (`GetUniqueEffectCount`를 직접 읽는 `DemonHandPower`/`AllMine` 등은 스택 됨.)
2. **`ActiveGemHandlers.cs:21/27`이 같은 bool을 두 번 검사** — `DebuffBoolType`에 `Senile`/`Aging` 항목이 아예 없어서 노쇠 검사가 `Corroded`로 대체돼 있음. **실측 결과: 부식 상태 적이 받는 피해 ×1.08.** 재건 시 `DebuffBoolType.Senile` 추가가 선행돼야 함.
3. **상점에서 루트 젬(`Default_Root_Gem`)을 살 수 있었음** — `RefreshRegistry`가 `t:GemSO`를 무조건 다 긁어서 `gemPool`에 루트까지 들어감. `gemPool` 삭제로 함께 해소됨.

## 5. 살아남는 미사용 필드 (재건 시 다시 쓰기만 하면 됨)

`PlayerStamina`: `maxStaminaBonus`, `throwCostBonus`, `regenRateBonus`, `outOfCombatRegenBonus`, `deadMinionRegenBonus`, `negativeLimit`, `hasStaminaSynergyMax`
`PlayerController`: `bonusThrowChargeTime`, `chargeEfficiencyMultiplier`, `overchargeTimeLimit`, `bonusThrowEffectMultiplier`, `chargeMoveSpeedMultiplier`

이 필드들은 `PlayerStamina`/`ThrowController`가 **여전히 읽고 있습니다**. 재건 시 쓰기만 복원하면 동작합니다.

---

아래는 계열별 전수 기록입니다. 각 젬의 enum 값 · 에셋 경로 · 정확한 수치 · 쓰는 필드 · 읽는 곳 · 훅 지점이 담겨 있습니다.


---

# 【스태미너 Stamina (group 4) — 10 에셋, LIVE였음】

I have the complete picture. Here is the harvest.

---

# GEM_LEGACY — 스태미너 젬 계열 (Stamina family)

`synergyGroup: Stamina = 4` (`Assets/Scripts/Systems/Growth/Data/GemSO.cs:151`)
핸들러 원본: `Assets/Scripts/Systems/Growth/Rules/StaminaGemHandlers.cs` (296줄, 전체 삭제 대상)
에셋 원본: `Assets/SOData/Rewards/Gems/Stamina/` (10개 .asset)

**상태 요약: 이 계열은 LIVE였다.** `GemRuleSystem.cs`처럼 `int level = 0;`으로 죽어있는 계열과 달리, `StaminaSynergyHandler`는 `InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Stamina)`를 실제로 호출하고 있었다 (`StaminaGemHandlers.cs:32`). 예외는 HarvestOfDeath 1종뿐 (아래 참조).

---

## 0. 배선 (rebuild 시 반드시 다시 만들어야 하는 통로)

| 단계 | 위치 | 내용 |
|---|---|---|
| 팩토리 등록 | `Assets/Scripts/Systems/Growth/Rules/GemHandlerRegistry.cs:48-57` | 스태미너 10종을 `RegisterHandlerFactory(type, () => new XxxHandler())` |
| 시너지 초기화 | `GemHandlerRegistry.cs:44` | `StaminaSynergyHandler.Initialize();` |
| 초기화 진입점 | `Assets/Scripts/Systems/Growth/InventoryManager.cs:184` | `GemHandlerRegistry.InitializeAllHandlers();` (게임 시작 1회) |
| OnEquipped/OnUnequipped 구동 | `InventoryManager.cs:262-268` → `GemHandlerRegistry.RefreshActiveHandlers(activeUniqueGems)` (`GemHandlerRegistry.cs:88-111`) | 전체 해제 후 전체 재장착 방식 |
| 시너지 갱신 구동 | `InventoryManager.cs:77` `OnGemTreeUpdated` 이벤트 → `StaminaGemHandlers.cs:21`에서 구독 | Invoke 지점: `InventoryManager.cs:484`(SocketGem), `:506`(UnsocketGem), `:953`(세이브 로드 후) |
| 인터페이스 | `GemHandlerRegistry.cs:8-13` | `IGemEffectHandler { GemUniqueType HandledType; OnEquipped(); OnUnequipped(); }` |

### ⚠ 중복 장착 미스매치 (rebuild 시 결정 필요)
`InventoryManager.cs:263-267`:
```csharp
foreach (var kvp in _globalGemStats.UniqueEffectCounts)
    if (kvp.Value > 0) activeUniqueGems.Add(kvp.Key);   // ← 타입당 1번만 Add
```
`GemUniqueEffect.Apply` (`Assets/Scripts/Systems/Growth/Data/GemUniqueEffect.cs:37-47`)는 카운트를 정확히 증가시키지만, 위 루프는 **개수를 무시하고 타입당 1개만** 리스트에 넣는다. 즉 `GemHandlerRegistry.cs:21` 주석("중복 보석 장착 지원")과 달리 **스태미너 유니크는 실제로 중복 스택되지 않았다.** `BasicFitness`를 2개 박아도 `maxStaminaBonus`는 +20에서 멈춘다. (반면 `GetUniqueEffectCount`를 직접 읽는 `DemonHandPower`/`AllMine` 등은 중복이 먹힌다 — `PlayerController.cs:28-35`.)

---

## 1. StaminaSynergyHandler (시너지) — `StaminaGemHandlers.cs:6-45`

레벨 산출: `GemSynergyLogic.GetLevel(count)` (`Assets/Scripts/Systems/Growth/Rules/GemSynergyLogic.cs:9-16`)

| count | level |
|---|---|
| >= 8 | 4 |
| >= 6 | 3 |
| >= 4 | 2 |
| >= 2 | 1 |
| 그 외 | 0 |

효과 (`StaminaGemHandlers.cs:38-43`):

| 조건 | 쓰는 필드 | 값 |
|---|---|---|
| `level >= 1` (2세트) | `PlayerStamina.regenRateBonus` | **+1f** |
| `level >= 2` (4세트) | `PlayerStamina.maxStaminaBonus` | **+20f** |
| `level >= 3` (6세트) | `PlayerStamina.hasStaminaSynergyMax` | **true** |
| 8세트 | — | **없음** (level 4 전용 효과 미구현) |

**6세트(`hasStaminaSynergyMax`)의 실제 계산** — `Assets/Scripts/Player/PlayerStamina.cs:76-80`:
```csharp
float ratio = Mathf.Clamp01(_currentStamina / MaxStamina);
synergyMulti = Mathf.Lerp(2.0f, 1.0f, ratio);   // 스태미너 0%일 때 x2.0, 100%일 때 x1.0
```
→ 스태미너가 비어있을수록 회복이 빨라지는 곱연산 배율.

**해제 패턴 (중요):** 이 핸들러는 `_lastRegenBonus`/`_lastMaxBonus`에 직전 기여분을 캐싱해두고 재계산 시 먼저 빼고 다시 더한다 (`:35-36`, `:42-43`). 델타 누적을 막는 구조이므로 rebuild 시 이 패턴을 유지할 것. 트리거는 매 프레임이 아니라 `OnGemTreeUpdated` 이벤트뿐.

---

## 2. 젬 10종

모든 에셋 공통 authored 값: `rarity: 3`, `shopCost: 320`, `category: 0`, `synergyGroup: 4`, `eligibleJobs: -1`, `icon: {fileID: 0}` (**아이콘 전부 미할당**), 로컬라이즈 테이블 `GUID:fb33b23eb1ad3d64b9b02523ebac3189`.

---

### 200. CatchBreath — 숨 고르기
- **에셋**: `Assets/SOData/Rewards/Gems/Stamina/Gem_Stamina_CatchBreath.asset` (`uniqueType: 200`, `subSlots: 2`)
- **핸들러**: `StaminaGemHandlers.cs:51-68`
- **효과**: 비전투 상태 자연 회복 **+1f**
- **쓰는 필드**: `PlayerStamina.outOfCombatRegenBonus` (`StaminaGemHandlers.cs:59` / 해제 `:66`)
- **읽는 곳**: `PlayerStamina.cs:68-69` — `if (GameManager.Instance.PLAYERCONTROLLER.IsOutOfCombat) baseRegen += outOfCombatRegenBonus;`
- **비전투 판정**: `Assets/Scripts/Player/PlayerController.cs:81` — `IsOutOfCombat => (Time.time - lastCombatTime) > 5.0f` (5초). 갱신은 `PlayerController.RecordCombatAction()`
- **트리거**: `OnEquipped` / `OnUnequipped` (1회 가산)
- **에셋 설명문**: "Increases natural stamina regeneration when out of combat." (수치 미기재)

---

### 201. HarvestOfDeath — 죽음의 수확  ☠ **이미 죽어있음 (INERT)**
- **에셋**: `Assets/SOData/Rewards/Gems/Stamina/Gem_Stamina_HarvestOfDeath.asset` (`uniqueType: 201`, `subSlots: 2`)
- **핸들러**: **존재하지 않음.** `GemHandlerRegistry.cs:49`에서 `new EmptyGemHandler(GemUniqueType.HarvestOfDeath)` (= `Assets/Scripts/Systems/Growth/Rules/ShotputGemHandlers.cs:5-11`, 본문이 빈 더미)로 등록되어 있다.
- **원래 사양** (`StaminaGemHandlers.cs:71-75`의 주석만 남음): "죽은 미니언 수만큼 자연 회복 증가". 아군 미니언이 필드에 상주하지 않게 되면서 발동 조건(사망/부활)이 사라져 폐기됨.
- **쓰려던 필드**: `PlayerStamina.deadMinionRegenBonus` — **선언(`PlayerStamina.cs:19`)과 읽기(`PlayerStamina.cs:65`)는 살아있으나 쓰는 코드가 어디에도 없다. 항상 0.**
- **rebuild 노트**: 스택당 수치는 코드에 남아있지 않다. 되살리려면 새로 기획해야 함. 에셋 설명문만 있음: "Increases stamina regeneration based on the number of dead minions."

---

### 202. BasicFitness — 기초체력 강화
- **에셋**: `Gem_Stamina_BasicFitness.asset` (`uniqueType: 202`, `subSlots: 1`)
- **핸들러**: `StaminaGemHandlers.cs:81-98`
- **효과**: 최대 스태미너 **+20f**
- **쓰는 필드**: `PlayerStamina.maxStaminaBonus` (`:89` / 해제 `:96`)
- **읽는 곳**: `PlayerStamina.cs:28` — `MaxStamina => defaultMaxStamina + maxStaminaBonus` (기본 `defaultMaxStamina = 100f`, `PlayerStamina.cs:10`)
- **트리거**: `OnEquipped` / `OnUnequipped`

---

### 203. EndlessVitality — 끊임없는 활력
- **에셋**: `Gem_Stamina_EndlessVitality.asset` (`uniqueType: 203`, `subSlots: 1`)
- **핸들러**: `StaminaGemHandlers.cs:104-121`
- **효과**: 자연 회복 **+0.5f** (상시)
- **쓰는 필드**: `PlayerStamina.regenRateBonus` (`:112` / 해제 `:119`)
- **읽는 곳**: `PlayerStamina.cs:65` — `float baseRegen = defaultRegenRate + regenRateBonus + deadMinionRegenBonus;` (기본 `defaultRegenRate = 3f`, 초당, `PlayerStamina.cs:12`)
- **트리거**: `OnEquipped` / `OnUnequipped`

---

### 204. OverflowingThrow — 넘치는 투척
- **에셋**: `Gem_Stamina_OverflowingThrow.asset` (`uniqueType: 204`, `subSlots: 1`)
- **핸들러**: `StaminaGemHandlers.cs:127-152`
- **효과**: 투척 소모량 **+5f**, 투척 효과 **+25%**
- **쓰는 필드 2개**:
  - `PlayerStamina.throwCostBonus += 5f` (`:137`)
  - `PlayerController.bonusThrowEffectMultiplier += 0.25f` (`:138`)
- **읽는 곳**:
  - `throwCostBonus` → `PlayerStamina.cs:36` — `float finalCost = Mathf.Max(0f, dynamicCost + throwCostBonus);`
  - `bonusThrowEffectMultiplier` → `Assets/Scripts/Player/Throw related/ThrowController.cs:115` — `recipe.modifiers.gemPowerMultiplier += pc.bonusThrowEffectMultiplier;`
- **트리거**: `OnEquipped` / `OnUnequipped`

---

### 205. OrderedBreath — 정돈된 숨결
- **에셋**: `Gem_Stamina_OrderedBreath.asset` (`uniqueType: 205`, `subSlots: 1`)
- **핸들러**: `StaminaGemHandlers.cs:158-175`
- **효과**: 투척 소모량 **-3f**
- **쓰는 필드**: `PlayerStamina.throwCostBonus -= 3f` (`:166` / 해제 `:173`)
- **읽는 곳**: `PlayerStamina.cs:36`
- **트리거**: `OnEquipped` / `OnUnequipped`

---

### 206. ThrowOverload — 투척 과부화  ⚙ **유일한 동적 계산 젬**
- **에셋**: `Gem_Stamina_ThrowOverload.asset` (`uniqueType: 206`, `subSlots: 1`)
- **핸들러**: `StaminaGemHandlers.cs:182-211`
- **효과**: 투척 시 소모하는 스태미너 **1당 투척 효과 +2%**
- **트리거**: `OnEquipped`에서 `ThrowController.OnRecipeCreated` 이벤트 구독 (`:190-191`), `OnUnequipped`에서 해제 (`:197`). 매 투척마다 `ModifyRecipe(recipe)` 호출.
- **계산** (`:200-210`):
  ```csharp
  int count = throwCtrl != null ? throwCtrl.HeldObjectsCount : 1;
  float bonus = stamina.GetThrowCost(count) * 0.02f;
  recipe.modifiers.gemPowerMultiplier += bonus;
  ```
- **훅 체인**: `ThrowStrategy.cs:107` → `ThrowController.InvokeRecipeCreated(recipe)` (`ThrowController.cs:110-123`) → `OnRecipeCreated?.Invoke(recipe)` (`ThrowController.cs:122`)
- **`HeldObjectsCount`**: `ThrowController.cs:15` — `_heldObjects.Count`
- **`gemPowerMultiplier` 최종 소비처**: `Assets/Scripts/Define/ThrowRecipe.cs:96-101`
  ```csharp
  return baseValue * modifiers.chargeMultiplier * modifiers.treasurePowerMultiplier
       * modifiers.abilityMultiplier * modifiers.gemPowerMultiplier;
  ```
  (기본값 `gemPowerMultiplier = 1.0f`, `ThrowRecipe.cs:40`) 및 `ThrowStrategy.cs:194`
- **rebuild 주의**: `GetThrowCost`는 `throwCostBonus`가 반영된 **최종 비용**을 돌려주므로, 204/205/207과 곱해서 상호작용한다. 예: 기본 15 + OverflowingThrow(+5) = 20 → bonus 0.40 (+40%). 또한 `GameManager.Instance.testMode_InfiniteStamina`가 켜져 있으면 `GetThrowCost`가 0을 반환(`PlayerStamina.cs:32`)하므로 **이 젬은 무한 스태미너 테스트 모드에서 자동으로 무력화된다.**

---

### 207. MasterOfRapidFire — 속사의 대가
- **에셋**: `Gem_Stamina_MasterOfRapidFire.asset` (`uniqueType: 207`, `subSlots: 1`)
- **핸들러**: `StaminaGemHandlers.cs:217-242`
- **효과**: 투척 소모량 **-7f**, 투척 효과 **-30%** (트레이드오프)
- **쓰는 필드 2개**:
  - `PlayerStamina.throwCostBonus -= 7f` (`:227`)
  - `PlayerController.bonusThrowEffectMultiplier -= 0.30f` (`:228`)
- **읽는 곳**: 204와 동일 (`PlayerStamina.cs:36`, `ThrowController.cs:115`)
- **트리거**: `OnEquipped` / `OnUnequipped`

---

### 208. LimitBreak — 한계돌파  ⚠ **가산이 아니라 대입**
- **에셋**: `Gem_Stamina_LimitBreak.asset` (`uniqueType: 208`, `subSlots: 0` ← 리프 노드)
- **핸들러**: `StaminaGemHandlers.cs:248-265`
- **효과**: 스태미너가 음수까지 떨어질 수 있음 (최대 **-50**). 음수 상태(침식)에서 회복 **절반**.
- **쓰는 필드**: `PlayerStamina.negativeLimit = 50f` (`:256`) / 해제 시 `= 0f` (`:263`)
  - **주의**: `+=`가 아니라 **`=` 대입**이다. 다른 젬들과 패턴이 다르며, 중복/타 소스와 충돌 시 덮어쓴다.
- **읽는 곳**:
  - `PlayerStamina.cs:112` — `CanThrow`: `return _currentStamina - GetThrowCost(...) >= -negativeLimit;`
  - `Assets/Scripts/UI/State/StaminaUI.cs:101` — 음수 UI 게이지 분모. **UI 폴백이 `: 50f`로 하드코딩**되어 있어 (`_playerStamina`가 null일 때) 이 젬 없이도 -50 기준으로 그린다.
- **"회복 절반" 파트는 젬이 아니라 PlayerStamina에 상시 내장**: `PlayerStamina.cs:72` — `float erosionMultiplier = (_currentStamina < 0) ? 0.5f : 1.0f;` → 이 코드는 젬 핸들러 삭제와 무관하게 살아남는다. 단, `negativeLimit == 0`이면 스태미너가 음수가 될 수 없으므로 **실질적으로 도달 불가능한 죽은 분기가 된다.**
- **UI 색상 (참고)**: 정상 녹색 `(0.2, 0.8, 0.2)`, 침식 보라 `(0.6, 0.1, 0.8)` — `StaminaUI.cs:93,100`
- **트리거**: `OnEquipped` / `OnUnequipped`

---

### 209. EfficientThrow — 효율적인 투척
- **에셋**: `Gem_Stamina_EfficientThrow.asset` (`uniqueType: 209`, `subSlots: 0` ← 리프 노드)
- **핸들러**: `StaminaGemHandlers.cs:271-296`
- **효과**: 최대 스태미너 **-40f**, 투척 효과 **+60%**
- **쓰는 필드 2개**:
  - `PlayerStamina.maxStaminaBonus -= 40f` (`:281`)
  - `PlayerController.bonusThrowEffectMultiplier += 0.60f` (`:282`)
- **읽는 곳**: `PlayerStamina.cs:28` (MaxStamina), `ThrowController.cs:115`
- **트리거**: `OnEquipped` / `OnUnequipped`

---

## 3. 삭제 후 살아남지만 아무도 쓰지 않게 되는 필드 (rebuild = 이 필드에 다시 쓰기만 하면 됨)

`Assets/Scripts/Player/PlayerStamina.cs` — 전부 `[HideInInspector] public`, 읽는 쪽은 그대로 유지됨:

| 필드 | 선언 | 읽는 곳 | 삭제 후 값 |
|---|---|---|---|
| `float maxStaminaBonus` | `:15` | `:28` (`MaxStamina`) | 항상 0 |
| `float throwCostBonus` | `:16` | `:36` (`GetThrowCost`) | 항상 0 |
| `float regenRateBonus` | `:17` | `:65` (`RegenRate`) | 항상 0 |
| `float outOfCombatRegenBonus` | `:18` | `:69` (`RegenRate`) | 항상 0 |
| `float deadMinionRegenBonus` | `:19` | `:65` (`RegenRate`) | **이미 항상 0** (201 폐기로 쓰는 코드 없음) |
| `float negativeLimit` | `:22` | `:112` (`CanThrow`), `StaminaUI.cs:101` | 항상 0 → 스태미너 음수 불가 |
| `bool hasStaminaSynergyMax` | `:23` | `:76-80` (`RegenRate` 시너지 배율) | 항상 false |

`Assets/Scripts/Player/PlayerController.cs`:

| 필드 | 선언 | 읽는 곳 | 삭제 후 값 |
|---|---|---|---|
| `float bonusThrowEffectMultiplier` | `:76` (`[Header("Overcharge System (Closer Gem)")]` 아래에 있으나 인스펙터 노출 public) | `ThrowController.cs:115` | 항상 0 (강속구 계열도 이 필드는 안 씀 — 유일한 writer가 스태미너 젬 3종이었다) |

**살아남아 계속 동작하는 것 (젬과 무관):**
- `PlayerStamina.cs:32` — `testMode_InfiniteStamina` 게이트
- `PlayerStamina.cs:35` — 동적 비용 `defaultThrowCost + ((count - 1) * 5f)` (미니언 1마리당 +5)
- `PlayerStamina.cs:48-54` — `InventoryManager.ActiveAbilities`의 `ModifyStaminaCost` 체인 (Ability 시스템, 젬 아님)
- `PlayerStamina.cs:72` — 음수 침식 x0.5 (단, `negativeLimit`이 0이면 도달 불가)
- `PlayerController.cs:81` — `IsOutOfCombat` (5초)

## 4. rebuild 체크리스트

1. `StaminaGemHandlers.cs` 재작성 (10 핸들러 + 시너지 핸들러)
2. `GemHandlerRegistry.InitializeAllHandlers()`에 `RegisterHandlerFactory` 10줄 + `StaminaSynergyHandler.Initialize()` 재등록
3. `.asset` 10개는 `uniqueType` 숫자만 맞으면 그대로 재사용 가능 (구조 보존이므로 삭제 불필요). 단 **아이콘 전부 미할당** 상태.
4. 중복 스택을 지원하려면 `InventoryManager.cs:263-267`의 `activeUniqueGems` 수집 루프를 `for (i < kvp.Value)`로 고칠 것
5. 208은 `=` 대입 → 다른 소스와 공존시키려면 `+=`/max 방식으로 재설계
6. 8세트(level 4) 시너지 효과는 **원래부터 미구현** — 새로 기획 필요
7. 201 HarvestOfDeath는 발동 조건 자체가 소멸 — 수치가 코드에 남아있지 않으므로 전면 재기획 필요

---

# 【속구 Fastball (group 5) / 투포환 Shotput (group 600) — 12 에셋, LIVE였음】

# 젬 효과 하베스트 기록 — Fastball (group 5) / Shotput (group 600)

> 삭제 전 스펙 보존용. 모든 수치/필드/훅 사이트는 삭제 시점 코드 기준. 경로는 `Assets/` 하위.

---

## 0. 공용 배관 (삭제 대상 아님 — 재구축 시 그대로 재사용)

| 항목 | 위치 | 내용 |
|---|---|---|
| `StatType` enum | `Scripts/Systems/Growth/Data/GemSO.cs:5-14` | `0 Attack, 1 Health, 2 AttackSpeed, 3 RespawnTime, 4 ThrowEffect, 5 ParabolicEffectMultiplier, 6 ParabolicFlightTimeMultiplier` |
| `GemSynergyGroup` | `GemSO.cs:152` / `GemSO.cs:172` | `Fastball = 5`, `Shotput = 600` |
| `GemUniqueType` | `GemSO.cs:39-44`, `GemSO.cs:118-121` | Fastball 210~215, Shotput 250~253 |
| `GetSynergyCount` | `Scripts/Systems/Growth/InventoryManager.cs:391-394` | **보유 개수가 아니라 젬 트리 상 "같은 그룹끼리 상하좌우 인접(BFS)한 최대 클러스터 크기"**. 계산: `CalculateSynergies` `InventoryManager.cs:271-289` → `FindClusterSize` `:291-322`. 좌우 인접은 같은 depth의 시각적 인덱스 기준이며 **wrap-around 포함** (`GetVisualNeighbor` `:328-346`). |
| `GemSynergyLogic.GetLevel` | `Scripts/Systems/Growth/Rules/GemSynergyLogic.cs:9-16` | `count>=8→4, >=6→3, >=4→2, >=2→1, else 0` |
| 효과 합산 루프 | `InventoryManager.cs:244-251` | `node.Gem.BaseData.effects` 각각 `.Apply(targetStats)` + `.Apply(_globalGemStats)`. `isEffectActive = true` 하드코딩(`:235`) → **시너지 미달이어도 젬 자체 효과는 장착 즉시 발동** |
| 핸들러 갱신 | `InventoryManager.cs:262-268` → `GemHandlerRegistry.RefreshActiveHandlers` (`Scripts/Systems/Growth/Rules/GemHandlerRegistry.cs:88-111`) | |
| 젬 획득 경로 | `Scripts/SOData/Define/Registry/ShopRegistrySO.cs:20-31` | 폴더 스캔으로 `gemPool` 자동 채움 → `Scripts/Systems/Growth/Logic/RewardProcessor.cs:180-182`. 두 패밀리 12종 모두 **획득 가능했음** |

### ⚠ 재구축 시 반드시 알아야 할 공용 배관 결함 2개

**(A) `GemStatEffect.Apply`가 statType 5/6을 통째로 버림**
`Scripts/Systems/Growth/Data/GemStatEffect.cs:17-26`:
```csharp
public override void Apply(InventoryManager.GemAggregatedStats targetStats)
{
    switch (statType)
    {
        case StatType.Attack: targetStats.AttackBonus += value; break;
        case StatType.Health: targetStats.HealthBonus += value; break;
        case StatType.AttackSpeed: targetStats.AttackSpeedBonus += value; break;
        case StatType.RespawnTime: targetStats.RespawnTimeBonus += value; break;
    }   // ← ParabolicEffectMultiplier(5) / ParabolicFlightTimeMultiplier(6) case 없음. default도 없음.
}
```
5/6을 처리하는 `switch`는 `InventoryManager.ApplyStatModifier` (`InventoryManager.cs:396-408`) 쪽에만 있는데, 이건 **`node.Gem.RandomModifiers` 전용 경로**(`InventoryManager.cs:253-257`)다. 그리고 `RandomModifiers`는 어떤 생성기에서도 채워지지 않는다 — `GemInstance` 생성자는 항상 빈 리스트를 만들고(`Scripts/Systems/Growth/GemTree/GemInstance.cs:31`), 세이브 로드(`:50`)만 복원한다. 즉 **authored `GemStatEffect`로 statType 5/6을 주는 젬은 아무 것도 쓰지 못한다.**

**(B) 중복 젬은 핸들러가 1번만 켜짐**
`InventoryManager.cs:263-267`이 `UniqueEffectCounts`의 **키**(타입당 1개)만 모아 `RefreshActiveHandlers`에 넘긴다. 따라서 `IGemEffectHandler` 기반 효과(= Fastball 6종 전부)는 **같은 젬을 2개 박아도 1개분만 적용**된다. 반대로 `GetUniqueEffectCount`를 직접 읽는 효과(= Shotput의 Ballistics, JustThrowIt)는 **개수만큼 중첩**된다. 이 비대칭은 의도된 설계가 아니라 사고로 보인다.

---

## 1. Fastball (강속구) — `synergyGroup: 5` — 전원 **LIVE**

핸들러 파일: `Scripts/Systems/Growth/Rules/FastballGemHandlers.cs` (241줄, 전체 확인)
등록: `GemHandlerRegistry.cs:45` (시너지), `:60-65` (유니크 6종)
에셋 공통: `rarity: 3`, `shopCost: 320`, `category: 0`, `eligibleJobs: -1`, `icon: {fileID: 0}` (아이콘 미지정)

### 1-1. 시너지 — `FastballSynergyHandler` (`FastballGemHandlers.cs:5-39`)

- 초기화: `GemHandlerRegistry.InitializeAllHandlers` → `FastballSynergyHandler.Initialize()` (`:45`). 싱글턴, `InventoryManager.OnGemTreeUpdated`에 구독 (`FastballGemHandlers.cs:17`)
- 계산 (`:28-37`):
```csharp
int count = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Fastball);
int level = GemSynergyLogic.GetLevel(count);
pc.chargeEfficiencyMultiplier -= _lastEfficiency;   // 이전 값 회수 (idempotent)
if (level >= 2) _lastEfficiency = 0.75f;
else if (level >= 1) _lastEfficiency = 0.50f;
else _lastEfficiency = 0f;
pc.chargeEfficiencyMultiplier += _lastEfficiency;
```
- **최종 임계값**: 클러스터 **2개 이상 → +0.50**, **4개 이상 → +0.75**. 6/8개(level 3/4)도 `level >= 2`에 걸려 **+0.75 그대로** — 상한 없음이 아니라 **4개에서 포화**. 에셋이 6종뿐이라 실질 최대 클러스터 6 → 어차피 +0.75.
- 쓰는 필드: `PlayerController.chargeEfficiencyMultiplier` (`Scripts/Player/PlayerController.cs:72`, 기본 0)
- 소비 지점: `Scripts/Player/Throw related/ThrowController.cs:110-123`
```csharp
public void InvokeRecipeCreated(ThrowRecipe recipe)
{
    recipe.modifiers.gemPowerMultiplier += pc.bonusThrowEffectMultiplier;
    if (recipe.info.isDirect)                                    // ← 직구 전용 게이트
        recipe.modifiers.gemPowerMultiplier += pc.chargeEfficiencyMultiplier;
    OnRecipeCreated?.Invoke(recipe);                             // ← 개별 젬 훅 발화
}
```
호출부: `Scripts/Player/Throw related/ThrowStrategy.cs:106-107`. `isDirect`는 `ThrowStrategy.cs:90`에서 `chargeRatio >= 0.98f`로 결정.
- UI 문구: `Scripts/UI/GemTree/GemSynergyDisplayUI.cs:159-162` (Lv1 "+50%", Lv2 "+75%"), 그룹 색 Amber `(1.0, 0.5, 0.0)` — `GemSO.cs:217` / `GemSynergyDisplayUI.cs:302`

### 1-2. 유니크 6종

| enum | 에셋 경로 | itemName | subSlots | 효과 (정확 수치) | 쓰는 필드 | 훅 사이트 |
|---|---|---|---|---|---|---|
| `SetPosition = 210` | `SOData/Rewards/Gems/Fastball/Gem_Fastball_SetPosition.asset` | `Set Position` (셋 포지션) | 2 | 차징 시간 **-0.1s**, 직구 효율 **-0.02** | `bonusThrowChargeTime`, `chargeEfficiencyMultiplier` | `FastballGemHandlers.cs:55-56` (OnEquipped) / `:66-67` (OnUnequipped) |
| `Windup = 211` | `.../Gem_Fastball_Windup.asset` | `Windup` (와인드업) | 2 | 차징 시간 **+0.5s**, 직구 효율 **+0.02** | 동일 | `:86-87` / `:97-98` |
| `MagicPitchFireball = 212` | `.../Gem_Fastball_MagicPitchFireball.asset` | `Magic Pitch: Fireball` (마구: 파이어볼) | 1 | 직구 시 **`ChargeTime * 0.10`** 만큼 `gemPowerMultiplier` 가산 (**필요 차징 1초당 +10%**) | `recipe.modifiers.gemPowerMultiplier` | `ThrowController.OnRecipeCreated` 구독 (`:117` 등록, `:126-134` 콜백) |
| `MagicPitchArirangBall = 213` | `.../Gem_Fastball_MagicPitchArirangBall.asset` | `Magic Pitch: Arirang Ball` (마구: 아리랑볼) | 1 | 차징 시간 **-0.5s**, 직구 효율 **-0.40** | `bonusThrowChargeTime`, `chargeEfficiencyMultiplier` | `:151-152` / `:162-163` |
| `Closer = 214` | `.../Gem_Fastball_Closer.asset` | `Closer` (클로저) | 0 | 오버차지 허용 **+4.0s** + 오버차지 비율 선형 **최대 +0.50** | `overchargeTimeLimit`, `gemPowerMultiplier` | `:185` / `:198` + `OnRecipeCreated` (`:189`, 콜백 `:204-211`) |
| `ExperiencedPitcher = 215` | `.../Gem_Fastball_ExperiencedPitcher.asset` | `Experienced Pitcher` (숙련된 투수) | 1 | 차징 중 이동속도 배율 **+0.25** (0.5 → 0.75, 즉 감속 50%→25%) | `chargeMoveSpeedMultiplier` | `:228` / `:238` |

**세부 사항**

- **`bonusThrowChargeTime` 소비**: `ThrowController.cs:50-58`
  ```csharp
  float time = pc.ThrowChargeTime + pc.bonusThrowChargeTime;
  return Mathf.Max(0.1f, time);   // 하한 0.1초
  ```
  `pc.ThrowChargeTime` 기본값 **1.0** (`PlayerController.cs:44`, `[SerializeField] throwChargeTime = 1.0f`). → SetPosition 1개 = 0.9s, ArirangBall 1개 = 0.5s, Windup 1개 = 1.5s.

- **`MagicPitchFireball`은 "실제로 누른 시간"이 아니라 "필요 차징 시간"에 비례**. `ChargeTime`은 `ThrowChargeTime + bonusThrowChargeTime`이므로 기본 1.0s → **+10%**. Windup과 조합 시 1.5s → **+15%**. ArirangBall과 조합 시 0.5s → **+5%**. 즉 **Windup/ArirangBall과 직접 곱해지는 설계**였음.

- **`Closer`**: `overchargeTimeLimit`은 `MaxChargeTime`으로 소비 (`ThrowController.cs:61-68`, `ChargeTime + pc.overchargeTimeLimit`). 기본 1.0 + 4.0 = **5.0s** → UI 문구 "Max charge time extended to 5s" (`GemSynergyDisplayUI.cs:218`)와 일치. 차징 타이머 상한은 `ThrowInputHandler.cs:55` (`Mathf.Min(_chargeTimer + Time.unscaledDeltaTime, _controller.MaxChargeTime)`).
  `OverchargeRatio` 정의 (`ThrowInputHandler.cs:28-36`):
  ```csharp
  if (_controller.MaxChargeTime <= _controller.ChargeTime) return 0f;   // Closer 없으면 항상 0
  float overchargeAmount = _chargeTimer - _controller.ChargeTime;
  return Mathf.Clamp01(overchargeAmount / (_controller.MaxChargeTime - _controller.ChargeTime));
  ```
  ModifyRecipe (`FastballGemHandlers.cs:204-211`)는 `isDirect`를 **검사하지 않고** `OverchargeRatio > 0f`만 본다. 다만 오버차지 중이면 `_chargeTimer > ChargeTime` → `ChargeRatio == 1` → `isDirect == true`가 강제되므로 **실질 직구 전용**. 재구축 시 이 암묵 의존을 명시할 것.
  가산식: `recipe.modifiers.gemPowerMultiplier += 0.50f * OverchargeRatio;` (선형, 최대 +0.50)

- **`ExperiencedPitcher`**: `chargeMoveSpeedMultiplier` 기본 **0.5** (`PlayerController.cs:77`). 소비: `PlayerController.cs:452` `SetSpeedModifier(SpeedModifierSource.ThrowCharge, chargeMoveSpeedMultiplier)`. +0.25 → 0.75 배율 = 25% 감속. **2개 이상 박아도 중첩 안 됨** (위 결함 B). 3개면 1.0 초과 = 차징 중 가속이 되지만 결함 B 때문에 발생 불가.

- **부호 주의**: 모든 Fastball 핸들러가 `OnEquipped`에서 `+=`/`-=`로 전역 필드를 직접 갈긴다. `OnUnequipped`가 정확히 반대 연산을 한다는 전제에 의존 — 상태 저장 없음. `FastballSynergyHandler`만 `_lastEfficiency`로 회수 값을 기억한다.

---

## 2. Shotput (투포환) — `synergyGroup: 600` — 6종 중 **3종 LIVE, 3종 사실상 죽음**

**`Scripts/Systems/Growth/Rules/ShotputGemHandlers.cs`는 실질 내용이 없다.** 파일 전체(12줄)가 `EmptyGemHandler` 정의뿐:
```csharp
public class EmptyGemHandler : IGemEffectHandler
{
    public GemUniqueType HandledType { get; }
    public EmptyGemHandler(GemUniqueType type) { HandledType = type; }
    public void OnEquipped() { }
    public void OnUnequipped() { }
}
```
Shotput 4종은 전부 `EmptyGemHandler`로 등록되고 (`GemHandlerRegistry.cs:68-71`), **실제 로직은 `ThrowStrategy.cs`와 `PlayerUniqueEffectManager.cs`에 인라인**되어 있다. 즉 이 패밀리는 핸들러 패턴을 안 쓴다.

에셋 공통: `rarity: 3`, `shopCost: 320`, `category: 0`, `eligibleJobs: -1`, `icon: {fileID: 0}`

### 2-1. 시너지 — **RAW COUNT 사용, `GetLevel` 미경유 (검증 완료)**

`Scripts/Player/Throw related/ThrowStrategy.cs:165-168`:
```csharp
// [시너지] 투포환 (2) / (4) 세트 효과 - 포물선 투척 효율 25% / 50% 증가
int shotputGems = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Shotput);
if (shotputGems >= 4) gemEffectBonus += 0.50f;
else if (shotputGems >= 2) gemEffectBonus += 0.25f;
```

**검증 결과 — 확인함. `GemSynergyLogic.GetLevel` 호출 없음.** 단 정확히 기록해 둘 점:

1. `GetSynergyCount`가 반환하는 값은 여전히 **트리 인접 클러스터 크기**지 보유 개수가 아니다. "raw count"란 **`GetLevel`을 안 거친다**는 뜻이지 카운트 의미가 다르다는 뜻이 아니다.
2. **임계값 2 / 4는 `GetLevel`의 level 1 / level 2 경계와 정확히 일치한다.** 따라서 이 코드는 `GetLevel`을 쓴 것과 **동작이 동일**하다 (`GetLevel(2..3)==1`, `GetLevel(4..7)==2`). Fastball은 같은 2/4 경계에서 +0.50/+0.75, Shotput은 +0.25/+0.50. 즉 **버그가 아니라 스타일 불일치**이며, 6/8개 확장 여지를 스스로 닫아둔 것.
3. Shotput 에셋이 6종이므로 최대 클러스터 6 → **+0.50에서 포화**.
4. 이 시너지는 **`!isDirect` 블록 안**에 있다 (`ThrowStrategy.cs:139`) → **포물선 전용**. Fastball 시너지가 직구 전용인 것과 정확히 대칭.

### 2-2. 포물선 투척 효율(`gemEffectBonus`)이 실제로 바꾸는 것

`ThrowStrategy.cs:136-194`. 이 블록 전체가 `if (!isDirect && InventoryManager.Instance != null)` 게이트 안(`:139`).

```csharp
float gemEffectBonus = 0f;
float gemDamageBonus  = 0f;

// :141  Protractor 경로 (아래 2-4 참조 — 항상 0)
gemEffectBonus += InventoryManager.Instance.GetAggregatedGemBonus(CommandData.None, StatType.ParabolicEffectMultiplier);

float dist = Vector2.Distance(playerPos, targetPos);

// :145-149  [기본 룰, 젬 아님] 거리 비례 데미지 감소
float distancePenalty = -0.05f * Mathf.Max(0f, Mathf.Floor(dist));
distancePenalty = Mathf.Max(-0.40f, distancePenalty);   // 1칸당 -5%, 최대 -40%
gemDamageBonus += distancePenalty;

// :152-156  탄도학
// :159-163  단안경
// :166-168  Shotput 시너지
// :171-177  인해전술 (BigHand 그룹, 본 문서 범위 밖)

recipe.modifiers.gemPowerMultiplier += gemEffectBonus;    // :180
recipe.modifiers.gemDamageMultiplier += gemDamageBonus;   // :181
totalMultiplier *= recipe.modifiers.gemPowerMultiplier;   // :194
```

**즉 "포물선 투척 효율"의 정체 = `recipe.modifiers.gemPowerMultiplier`에 대한 가산치.** `ThrowStrategy.cs:194`에서 `totalMultiplier`(= `chargeMultiplier * treasurePowerMultiplier * abilityMultiplier`)에 **곱해져** 이후 `:196`~ 유닛별 루프의 스택/효과 산정에 들어간다. 데미지 배율(`gemDamageMultiplier`)과는 **별개 채널**이다 — 거리 페널티만 데미지 쪽으로 간다.

`ThrowStrategy.cs:141`이 `gemEffectBonus`의 **유일한 `ParabolicEffectMultiplier` 소비처**다.

### 2-3. 유니크 4종

| enum | 에셋 경로 | itemName | subSlots | 상태 | 효과 (정확 수치) | 훅 사이트 |
|---|---|---|---|---|---|---|
| `JustThrowIt = 250` | `SOData/Rewards/Gems/Shotput/Gem_Shotput_JustThrowIt.asset` | 일단 던지고 보자 | 1 | **LIVE** | 포물선 투척마다 **8초**간 스택 +1 (**최대 5스택**), 스택당 체공시간 **-8% × 보유 개수** | 아래 참조 |
| `Ballistics = 251` | `.../Gem_Shotput_Ballistics.asset` | 탄도학 | 1 | **LIVE** | `gemEffectBonus += dist * 0.10f * 보유개수` (**거리 1칸당 +10%, 상한 없음**) | `ThrowStrategy.cs:152-156` |
| `SiegeMode = 252` | `.../Gem_Shotput_SiegeMode.asset` | 시즈 모드 | 0 | **💀 완전히 죽음** | 아래 참조 | 없음 |
| `Monocle = 253` | `.../Gem_Shotput_Monocle.asset` | 단안경 | 0 | **LIVE** | `gemEffectBonus += Mathf.Max(0f, 5f - dist) * 0.10f` (**5칸 기준 역순, 발밑 최대 +50%, 5칸 이상 0**) | `ThrowStrategy.cs:159-163` |

**`Ballistics`** (`ThrowStrategy.cs:152-156`) — `GetUniqueEffectCount`를 쓰므로 **개수만큼 선형 중첩**. `dist` 상한 없음 → 원거리에서 폭주 가능 (`dist=10`, 2개 → +2.00). 밸런스 상한 부재를 그대로 기록해 둠.

**`Monocle`** (`ThrowStrategy.cs:159-163`) — `HasUniqueEffect`(bool)를 쓰므로 **여러 개 박아도 1개분**. 주석에 "(On/Off형 효과)"로 명시됨. `Ballistics`(개수 비례)와 의도적으로 다른 처리.

**`JustThrowIt` — LIVE지만 위치가 함정**
로직은 `Scripts/Deprecated/PlayerUniqueEffectManager.cs`에 있다. **`Deprecated/` 폴더에 있지만 `.cs`라 컴파일되고, `Player.prefab` / `Player Melee.prefab` 양쪽에 컴포넌트가 실제로 붙어 있다** (스크립트 GUID `8fecc708d442b824b9a5abc8bc6dbf99`로 검증). → **폴더 이름만 보고 죽었다고 판단하면 안 됨.**
```csharp
// PlayerUniqueEffectManager.cs:9-20
private const float JustThrowItDuration = 8f;
private const int   MaxJustThrowItStacks = 5;
private const float SpeedBonusPerStack = 0.08f;   // 8%
public float JustThrowItSpeedBonus =>
    _justThrowItStacks * SpeedBonusPerStack * InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.JustThrowIt);
```
- 스택 적립: `OnParabolicThrow()` (`:46-59`) — `Mathf.Min(_justThrowItStacks + 1, 5)`, 타이머 8초로 **리셋(갱신)**. 스택은 개별 만료가 아니라 **타이머 만료 시 0으로 전멸** (`:30-38`).
- 호출: `ThrowController.cs:419` (`FireDamageCluster` 경로) / `ThrowController.cs:542` (`ThrowAll` 경로) — 둘 다 `!isDirect`일 때만.
- 소비: `ThrowController.cs:418` / `ThrowController.cs:505`
  ```csharp
  float flightTimeBonus = InventoryManager.Instance.GetAggregatedGemBonus(CommandData.None, StatType.ParabolicFlightTimeMultiplier);
  flightTimeBonus += uem.JustThrowItSpeedBonus;
  duration *= (1f - Mathf.Clamp(flightTimeBonus, 0f, 0.9f));   // 최대 90% 단축
  ```
  → **`duration`(체공 시간) 단축**. 데미지가 아님. 5스택 × 8% × 1개 = **-40%**. 2개 보유 시 -80%. 3개면 클램프 0.9에 걸림.
- `ThrowController.cs:395-400`: 포물선 기본 `speed = 12f`, `jumpH = 3.5f`, `duration = dist / speed`.
- ⚠ **경로 불일치**: `FireDamageCluster`(`:416-420`)는 `JustThrowItSpeedBonus`를 **읽고 나서 같은 자리에서 `OnParabolicThrow()`를 호출**하지만, `ThrowAll`은 **읽기(`:503-506`)와 적립(`:540-543`)이 분리**되어 있다. 두 경로 모두 "이번 투척은 이전 스택으로 계산 → 그 다음 적립" 순서라 결과는 같으나, 코드 형태가 다르다.

**`SiegeMode = 252` — 💀 구현 자체가 존재하지 않음**
전체 코드베이스에서 참조가 **딱 2곳**:
- `GemSO.cs:120` — enum 선언
- `GemHandlerRegistry.cs:68` — `new EmptyGemHandler(GemUniqueType.SiegeMode)` (빈 껍데기)

`GetUniqueEffectCount(SiegeMode)` / `HasUniqueEffect(SiegeMode)`를 **읽는 코드가 단 한 줄도 없다.** 에셋 설명("플레이어가 해당 위치에 고정되며, 카메라 위치가 넓게 고정됩니다.\n보유한 소환수의 수 만큼 탄약으로 변경되어 고각도 포격을 실시합니다.")은 **기획 문구일 뿐 한 번도 구현된 적 없음**. 상점/보상에는 정상적으로 등장했으므로 **플레이어는 돈 주고 아무 효과 없는 젬을 살 수 있었다.** 재구축 시 이건 "복원"이 아니라 **신규 구현**임.

### 2-4. `GemStatEffect` 2종 — 지시대로 확인함, **둘 다 💀 INERT**

| 에셋 | itemName | subSlots | `effects` RefId 페이로드 |
|---|---|---|---|
| `SOData/Rewards/Gems/Shotput/Gem_Shotput_Protractor.asset` | 각도기 | 2 | `type: {class: GemStatEffect}` / `statType: 5` / `value: 0.2` |
| `SOData/Rewards/Gems/Shotput/Gem_Shotput_EfficientCurve.asset` | 효율적인 곡선 | 2 | `type: {class: GemStatEffect}` / `statType: 6` / `value: 0.2` |

**확인 완료 — 지시 내용대로 맞음:**
- `Protractor` → `statType: 5` = `StatType.ParabolicEffectMultiplier`, `value: 0.2` (**+0.2**). 설명 "포물선 던지기 시 투척 효율이 증가합니다."
- `EfficientCurve` → `statType: 6` = `StatType.ParabolicFlightTimeMultiplier`, `value: 0.2` (**+0.2**). 설명 "포물선 던지기의 투척 속도가 빨라집니다. (20% 더 빨리 떨어집니다)"
- 둘 다 `synergyGroup: 600`, `subSlots: 2` — 즉 **Shotput 시너지 카운트에는 정상 기여**한다. 클러스터 6종 구성에 필요한 부품이었음.

**❗ 그러나 두 젬의 스탯 효과 자체는 게임에 전혀 반영되지 않았다.**
경로: `InventoryManager.cs:244-251` → `effect.Apply(...)` → `GemStatEffect.Apply` (`GemStatEffect.cs:17-26`) → **`switch`에 case 5/6이 없어 조용히 no-op**. (위 §0 결함 A)

따라서:
- `ThrowStrategy.cs:141` `GetAggregatedGemBonus(None, ParabolicEffectMultiplier)` → **항상 0.0**
- `ThrowController.cs:415`, `ThrowController.cs:500` `GetAggregatedGemBonus(None, ParabolicFlightTimeMultiplier)` → **항상 0.0**
- `InventoryManager.cs:404-405`(5/6을 처리하는 유일한 코드)는 `RandomModifiers` 전용이고, `RandomModifiers`는 **어디서도 생성되지 않음** (`GemInstance.cs:31` 항상 빈 리스트) → 이 경로도 동반 사망.

즉 **`Protractor`와 `EfficientCurve`는 "시너지 카운트 채우는 부품" 이상의 기능이 없었다.** 재구축 시 두 젬을 되살리려면 젬 자체가 아니라 **`GemStatEffect.Apply`의 switch에 case 2개를 추가**해야 한다. 이걸 모르고 젬만 복원하면 또 조용히 죽는다.

**부수 효과**: `EfficientCurve`가 죽어 있던 덕분에 `ThrowController`의 `flightTimeBonus`는 사실상 **`JustThrowIt` 단독 입력**이었다. 즉 그 자리의 `Mathf.Clamp(flightTimeBonus, 0f, 0.9f)` 클램프는 실전에서 5스택 -40%(1개)/-80%(2개)로만 도달했다.

---

## 3. 로컬라이제이션

두 패밀리 전원이 테이블 `GUID:fb33b23eb1ad3d64b9b02523ebac3189`를 참조 (`localizedItemName` / `localizedDescription`).
- Fastball KeyId: `47607894216704~5` (Closer), `47607898411008~17` (나머지 5종)
- Shotput KeyId: `71717584080896`, `71717613441024~34`

`GemUniqueEffect.GetDescription` (`Scripts/Systems/Growth/Data/GemUniqueEffect.cs:13-36`)은 별도로 `Reward Text Table`에서 `UniqueEffect_{uniqueType}` 키를, `UI Text Table`에서 `UI_Unique` 접두사를 찾는다 (없으면 `displayDescription` 폴백). `Settings/Localization/Reward Text Table Shared Data.asset`에 두 패밀리 항목이 실제로 존재.

폴백 문구 하드코딩: `GemSynergyDisplayUI.cs:214-218` (Fastball 5종). **`ExperiencedPitcher`(215)와 Shotput 4종(250~253)은 `GetUniqueDescription`의 `switch`에 case가 없어 `"Unique ability active"`로 폴백**하고, `GetSynergyGroupOfUnique` (`:167-194`)에도 없어 **`GemSynergyGroup.Base`로 오분류**된다. 로컬라이즈 테이블이 채워져 있으면 문구는 뜨지만, 그룹 분류는 UI에서 틀린다. (`GemSynergyDisplayUI.cs:159-162`의 시너지 문구 switch에도 `Shotput` case 없음 → **Shotput 시너지는 UI에 설명이 안 뜬다.**)

---

## 4. 재구축용 요약 체크리스트

**되살릴 때 그대로 쓸 수 있는 것 (실제로 동작했음)**
1. Fastball 시너지 2/4 → +0.50/+0.75 (`chargeEfficiencyMultiplier`, 직구 전용)
2. Fastball 유니크 6종 전부 (210~215) — 수치는 위 표 그대로
3. Shotput 시너지 2/4 → +0.25/+0.50 (`gemPowerMultiplier`, 포물선 전용)
4. `Ballistics`(251), `Monocle`(253), `JustThrowIt`(250)

**되살리기 전에 배관부터 고쳐야 하는 것**
5. `Protractor`(5, +0.2) / `EfficientCurve`(6, +0.2) → **`GemStatEffect.Apply`에 case 5/6 추가가 선행되어야 함**. 안 하면 또 no-op.
6. 중복 젬 핸들러 1회만 등록 (`InventoryManager.cs:263-267`) → Fastball 계열 스택 불가

**처음부터 만들어야 하는 것**
7. `SiegeMode`(252) — 구현 이력 전무. 에셋 설명만 존재.

**옮기거나 정리해야 하는 것**
8. `PlayerUniqueEffectManager`가 `Deprecated/`에 있으면서 Player 프리팹 2개에 붙어 실제 동작 중 — 위치 정리 필요
9. `GemSynergyDisplayUI`의 `GetUniqueDescription` / `GetSynergyGroupOfUnique` / 시너지 문구 switch에 `ExperiencedPitcher` + Shotput 5종 누락

---

# 【큰손 BigHand (group 700) — 8 에셋, LIVE였음】

Harvest complete. All files read in full; every claim below is traced to a call site.

---

# BigHand (큰손) 보석군 — group 700 스펙 하베스트

> 삭제 전 최종 기록. 8종 전부 `GemSynergyGroup.BigHand = 700` (`GemSO.cs:175`), `GemUniqueType` 260–267 (`GemSO.cs:123-131`).

## 0. 핵심 구조 — 전용 핸들러는 존재하지 않는다

**BigHand 전용 핸들러 파일은 없다.** 8종 전부 `EmptyGemHandler`로 등록된다 (`Scripts/Systems/Growth/Rules/GemHandlerRegistry.cs:73-81`):

```csharp
// 큰손 보석
RegisterHandlerFactory(GemUniqueType.DemonHandPower, () => new EmptyGemHandler(GemUniqueType.DemonHandPower));
... (8종 전부 EmptyGemHandler)
```

즉 **효과 로직은 전부 소비처(consumer)에 인라인**되어 있다. 재구축 시 핸들러를 찾지 말고 아래 훅 사이트를 직접 봐야 한다.

젬이 켜지는 유일한 경로는 카운터다 (`Scripts/Systems/Growth/Data/GemUniqueEffect.cs:37-47`):
```csharp
public override void Apply(InventoryManager.GemAggregatedStats targetStats)
{
    if (uniqueType != GemUniqueType.None) targetStats.UniqueEffectCounts[uniqueType]++;
}
```
`InventoryManager.cs:235` `bool isEffectActive = true;` — **유니크 효과는 시너지 활성화와 무관하게 장착 즉시 발동**한다(게이팅 해제됨).

## 1. 시너지 — RAW COUNT다 (중요)

`GetSynergyCount(group)`는 트리 인접 클러스터(상/하/좌우, wrap-around 포함)의 **최대 크기**를 반환한다 (`InventoryManager.cs:271-322`, `391-394`).

다른 계열은 전부 `GemSynergyLogic.GetLevel(count)`를 거친다 — 2/4/6/8 → 레벨 1/2/3/4 (`Rules/GemSynergyLogic.cs:9-16`).
**BigHand만 GetLevel을 거치지 않고 raw count를 3/5로 직접 비교한다.** 재구축 시 이걸 놓치면 임계값이 전부 틀어진다.

| 훅 | 조건 (raw count) | 효과 |
|---|---|---|
| `ThrowController.cs:23-30` | `>= 5` | `MaxHoldCount` **+3** |
| `ThrowController.cs:23-30` | `>= 3` (else if) | `MaxHoldCount` **+1** |
| `PlayerController.cs:252-262` | `>= 3` | 피격 시 **드롭 면역** |

```csharp
// ThrowController.cs:18-33 — 배타적 분기. 5 이상이면 +3만 (+1과 합산 아님)
int count = InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.BigHand);
if (count >= 5) bonus += 3;
else if (count >= 3) bonus += 1;
return maxHoldCount + bonus;   // maxHoldCount 기본값 = 2 (ThrowController.cs:11)
```
→ 인스펙터 기본 2 기준: 3세트=3개, 5세트=5개까지 집기.

```csharp
// PlayerController.cs:252-262 — HandleDamageTaken 내부. damage > 0 일 때만
bool preventDrop = false;
if (InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.BigHand) >= 3) preventDrop = true;
if (!preventDrop) throwController.DropAll();
```
→ 무적(i-frame) 처리는 별개로 그대로 진행됨 (`PlayerController.cs:265-270`). 드롭만 막는다.

## 2. 8종 개별 스펙

전 8종 공통 authored 값: `rarity: 3`, `shopCost: 320`, `category: 0`(Common), `eligibleJobs: -1`(All).
경로는 전부 `Assets/SOData/Rewards/Gems/BigHand/`.

---

### 260 — DemonHandPower / 귀수의 힘 — **LIVE**
- **에셋**: `Gem_BigHand_DemonHandPower.asset` / `subSlots: 2`
- **효과**: 집기 범위 **+0.5칸 / 개수 비례 스택**
- **쓰는 필드**: `PlayerController.THROWRANGE` (getter, 매 호출 재계산)
- **훅**: `PlayerController.cs:28`
```csharp
range += InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.DemonHandPower) * 0.5f;
```
- **비고**: `GetUniqueEffectCount` = raw 보유 개수. 2개면 +1.0칸.

---

### 261 — HumanWaveTactics / 인해전술 — **LIVE (단, 포물선 전용 + 개수 비례 없음)**
- **에셋**: `Gem_BigHand_HumanWaveTactics.asset` / `subSlots: 2`
- **효과**: 3명 이상 투척 시 **1명당 투척 효율 +7%**
- **쓰는 필드**: `recipe.modifiers.gemPowerMultiplier` (기본값 `1.0f`, `ThrowRecipe.cs:40`)
- **훅**: `ThrowStrategy.cs:170-177`
```csharp
if (InventoryManager.Instance.HasUniqueEffect(GemUniqueType.HumanWaveTactics))
    if (heldObjects.Count >= 3) gemEffectBonus += (heldObjects.Count * 0.07f);
```
- **함정 2개 (재구축 시 의도 확인 필요)**:
  1. **`if (!isDirect …)` 블록 안 (ThrowStrategy.cs:139)** → **직구(chargeRatio ≥ 0.98)에는 전혀 안 붙는다. 포물선 전용.**
  2. `(count - 3)`이 아니라 **전체 count에 7%를 곱한다** → 3명 = +21%, 5명 = +35%.
  3. `HasUniqueEffect`(on/off)라서 **보석 2개 겹쳐도 효과 동일**(개수 비례 없음). 형제 젬들과 불일치.

---

### 262 — TwinFusion / 쌍둥이 연성 — **LIVE**
- **에셋**: `Gem_BigHand_TwinFusion.asset` / `subSlots: 1`
- **효과**: 조합 투척 착탄 시 앞 **2명**을 **10초**간 합체. 합체체 능력치 = 2명 합산. 만료/파괴 시 반피 비율로 복귀.
- **훅 (발동)**: `ThrowCluster.cs:370-427`, `_activeRecipe.state.isMaster`인 착탄에서만
```csharp
else if (_units.Count >= 2 && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.TwinFusion))
    performTwinFusion = true;
int fusionCount = performGolemFusion ? 5 : (performTwinFusion ? 2 : 0);
...
GameObject fusionObj = Instantiate(firstUnit.gameObject, transform.position, Quaternion.identity);
var fusionController = fusionObj.AddComponent<FusionMinionController>();
float scaleMult = 1.5f; Color fusionColor = Color.blue; string popupName = "Twin!";
fusionController.Setup(fusedUnits, 10f, 1f, scaleMult, fusionColor, popupName);
```
- **Golemizing과 배타(else if)** — 둘 다 있고 5명 이상이면 **Golemizing이 이긴다**.
- **합체 몸통 = 앞 1번 유닛의 gameObject 복제본.**
- **스탯 합산**: `FusionMinionController.cs:19-49` — `totalBaseMaxHP`/`totalBaseAtk` 합산 → `_stat.OverrideBaseStats(...)`, `_stat.IsFusion = true`. 재료는 `SetActive(false)`.
- **복귀 공식**: `FusionMinionController.cs:111-114`
```csharp
float curHPRatio = _stat.Health.CurHP / _stat.MAXHP;
float returnHPRatio = 0.5f + (curHPRatio * 0.5f);   // 최소 50% 보장
```
- **집기 방지**: `IsFused` 플래그 → `ThrowController.cs:205, 258, 306`.

---

### 263 — MobMentality / 군중심리 — **LIVE (단 Deprecated 폴더에 있음)**
- **에셋**: `Gem_BigHand_MobMentality.asset` / `subSlots: 1`
- **효과**: 집기 범위 내 소환수 **1마리당 플레이어 이동속도 +0.1** (개수 비례)
- **구현 위치**: **`Scripts/Deprecated/PlayerUniqueEffectManager.cs:61-94`** — 폴더 이름이 Deprecated지만 **asmdef가 프로젝트에 하나도 없어서 Assembly-CSharp에 그대로 컴파일된다. 살아있다.** `PlayerController.cs:194-195`에서 보험으로 `AddComponent`까지 한다.
- **쓰는 필드**: `PlayerUniqueEffectManager.MobMentalitySpeedBonus` (public get / private set)
- **훅 (생산)**: `PlayerUniqueEffectManager.cs:66-94`, `Update()`에서 **0.2초 간격** 폴링
```csharp
private const float MobMentalityCheckInterval = 0.2f;
float radius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;   // ← THROWRANGE에 의존 = AllMine/DemonHandPower와 연쇄
Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius, Layers.PlayerArmy);
int armyLayer = Layers.Army;
foreach (var col in colls) if (col.gameObject.layer == armyLayer) minionCount++;
MobMentalitySpeedBonus = minionCount * 0.1f * gemCount;
```
- **훅 (소비)**: `CharacterStat.cs:202-206` — `MOVESPEED` getter 안, **가산(additive)**
```csharp
if (_isPlayer) { var uem = GetComponentInParent<PlayerUniqueEffectManager>();
                 if (uem != null) finalSpeed += uem.MobMentalitySpeedBonus; }
```
- **비고**: `Layers.PlayerArmy`는 마스크(`Layers.cs:42`), `Layers.Army`는 인덱스(`Layers.cs:21`) — 필터 조합은 **정상**. 다만 **유닛이 아니라 콜라이더를 센다** — Army 레이어 콜라이더가 유닛당 2개면 2배로 센다. 그리고 `reductionMult` 곱연산 **뒤에** 더해지므로 **둔화/한기의 영향을 받지 않는다**.

---

### 264 — SwiftRelocation / 신속한 재배치 — ⚠️ **이미 죽어 있음 (INERT). 재구축 노력 절약할 것.**
- **에셋**: `Gem_BigHand_SwiftRelocation.asset` / `subSlots: 1`
- **의도한 효과**: 3명 이상 투척 시 사용된 소환수들 **이동속도 +50%, 5초간**
- **훅**: `ThrowController.cs:550-560`
```csharp
if (_heldObjects.Count >= 3 && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.SwiftRelocation))
    foreach (var t in _heldObjects)
        if (t is MonoBehaviour mb && mb.TryGetComponent<CharacterStatus>(out var status))
            status.ApplySpeedBuff("SwiftRelocation", 0.5f, 5.0f);
```
- **왜 죽었나 (검증 완료)**: `IThrowable` 구현체는 `AllyController`이고, `AllyController`는 프리팹 **루트** GameObject `"AllyBase"`에 붙어 있다. 반면 `CharacterStatus`는 **자식** GameObject `"CharacterStatStuff"`에 붙어 있다 (`Assets/Prefabs/Ally/AllyBase.prefab` YAML 직접 파싱으로 확인. `CharacterStat`도 같은 자식에 있음 — `CharacterStat.cs:8` `[RequireComponent(typeof(CharacterStatus), ...)]`).
  `TryGetComponent<T>`는 **같은 GameObject만** 본다 → **항상 false → 버프가 단 한 번도 적용된 적 없다.**
  같은 파일 다른 코드는 이미 이 구조를 알고 있다: `BaseEntity.cs:143` `_stats = GetComponentInChildren<CharacterStat>()`, `FusionMinionController.cs:31` `mb.GetComponentInChildren<CharacterStat>()`.
- **재구축 시 고칠 점**: `mb.GetComponentInChildren<CharacterStatus>()`를 쓸 것. 단, 그러면 **처음으로 실제 발동**하므로 밸런스 미검증 상태임을 감안할 것.
- **부수 정보 (고쳐도 남는 이슈)**: 이 블록은 `_activeCluster.Launch()`(546행) **뒤**에 있다. `_heldObjects`는 562행에서야 비워지므로 카운트 자체는 유효하지만, 버프 5초가 **비행 중에 이미 소모되기 시작**한다. 착지 후 5초를 의도했다면 `OnLanded` 쪽으로 옮겨야 한다.
- **참고**: `CharacterStatus.ApplySpeedBuff(string id, float increase, float duration)`는 정상 존재 (`CharacterStatus.cs:228-233`, 동일 id는 갱신/최댓값). API는 멀쩡하고 호출 지점만 틀렸다.

---

### 265 — Afterimage / 잔상 — **LIVE**
- **에셋**: `Gem_BigHand_Afterimage.asset` / `subSlots: 1`
- **효과**: 직전 소환수 조합(타입/개수/**순서**가 완전 동일)을 연속으로 던지면 해당 조합의 **투척 효율 ×1.5**
- **쓰는 필드**: `recipe.modifiers.gemPowerMultiplier` — **곱연산**
- **훅**: `ThrowStrategy.cs:183-191`
```csharp
if (InventoryManager.Instance.HasUniqueEffect(GemUniqueType.Afterimage))
    if (CheckAfterimageCombo(heldObjects)) recipe.modifiers.gemPowerMultiplier *= 1.5f;
SaveComboForAfterimage(heldObjects);
```
- **상태 저장**: `ThrowStrategy._lastThrowCombo` (`List<CommandData>`, `ThrowStrategy.cs:307`) — **런타임 전용, 순서 민감 비교** (`cs:309-317`). 방/층 전환 시 초기화 로직 **없음**.
- **중요 세부**:
  1. `!isDirect` 블록 **바깥** → **직구/포물선 둘 다 적용**. (HumanWaveTactics와 반대)
  2. `+= gemEffectBonus`(180행) **뒤에** 곱해지므로 **누적 보너스 전체를 1.5배** 한다 (인해전술/탄도학/투포환 시너지까지 증폭).
  3. `heldObjects.Count == 0`이면 116행에서 early return → **빈손 투척은 콤보를 초기화하지 않는다.**
  4. `HasUniqueEffect`(on/off) → **개수 비례 없음.**

---

### 266 — AllMine / 다 내꺼야 — **LIVE**
- **에셋**: `Gem_BigHand_AllMine.asset` / `subSlots: 1`
- **효과**: **집어든 소환수 1마리당** 집기 범위 증가. 기본 **1칸**, 추가 노드당 **+0.4칸**
- **쓰는 필드**: `PlayerController.THROWRANGE`
- **훅**: `PlayerController.cs:29-36`
```csharp
int allMineLevel = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.AllMine);
if (allMineLevel > 0)
{
    float multiplierPerHeld = 1.0f + (allMineLevel - 1) * 0.4f;
    int heldCount = (throwController != null) ? throwController.HeldObjectsCount : 0;
    range += heldCount * multiplierPerHeld;
}
```
- **수치 표**: 1개 = 마리당 +1.0칸 / 2개 = +1.4칸 / 3개 = +1.8칸.
- **주의**: `allMineLevel`은 이름과 달리 **GetLevel이 아니라 raw 보유 개수**다.
- **연쇄**: `THROWRANGE`는 MobMentality 탐지 반경(`PlayerUniqueEffectManager.cs:80`), 자동 집기 반경(`ThrowController.cs:243, 291`), 집기 거리 판정(`ThrowController.cs:209`)에 전부 쓰인다 → AllMine/DemonHandPower는 **군중심리까지 간접 강화**한다.

---

### 267 — Golemizing / 골레마이징 — **LIVE**
- **에셋**: `Gem_BigHand_Golemizing.asset` / **`subSlots: 0`** ← 8종 중 유일. 트리에서 **하위 슬롯을 제공하지 않는 말단 노드**. 클러스터 확장을 못 시키므로 시너지 3/5 달성에 불리하다. 의도된 트레이드오프로 보임.
- **효과**: 조합 투척 착탄 시 앞 **5명**을 **10초**간 골렘으로 합체. 능력치 = 5명 합산, 크기 2.5배.
- **훅**: `ThrowCluster.cs:378-382` (TwinFusion보다 **우선**)
```csharp
if (_units.Count >= 5 && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.Golemizing))
    performGolemFusion = true;
...
float scaleMult = 2.5f; Color fusionColor = Color.red; string popupName = "Golem!";
fusionController.Setup(fusedUnits, 10f, 1f, scaleMult, fusionColor, popupName);
```
- **나머지 합체/해체 로직은 TwinFusion과 100% 공유** (`FusionMinionController`).
- **도달 조건 주의**: 5명을 들려면 `MaxHoldCount >= 5` 필요 → 기본 2 + BigHand 5세트(+3) = 5. 즉 **Golemizing은 사실상 BigHand 5세트 시너지가 전제**다.

---

## 3. 부분적으로 죽은 코드 — `FusionMinionController.Setup`의 미사용 파라미터

`FusionMinionController.cs:11` 시그니처:
```csharp
public void Setup(List<IThrowable> units, float duration, float hpRatio, float scaleMultiplier, Color color, string popupName)
```
**`hpRatio`와 `popupName`은 함수 본문(11-84행)에서 단 한 번도 읽히지 않는다** (grep 확인: 11행 시그니처에만 등장).
→ **`"Golem!"` / `"Twin!"` 팝업은 화면에 뜬 적이 없다.** 호출부가 넘기는 `1f`도 무의미.
재구축 시 팝업 연출을 원한다면 **새로 만들어야 한다** (기존 코드 복원으로는 안 나옴).

## 4. 획득 경로

8종 전부 **`Assets/SOData/Registry/Shop Registry.asset`에서만** 참조된다 (전 GUID 역참조 스캔 완료 — 다른 어떤 .asset/.prefab에도 등장하지 않음).
→ **상점 전용, 320골드.** 일반 보상 풀/GrowthRegistry에는 없다.

## 5. 로컬라이제이션 (같이 지울 대상)

`Assets/Settings/Localization/Reward Text Table Shared Data.asset` — 키 8개 (783~811행):
`UniqueEffect_DemonHandPower`, `UniqueEffect_HumanWaveTactics`, `UniqueEffect_TwinFusion`, `UniqueEffect_MobMentality`, `UniqueEffect_SwiftRelocation`, `UniqueEffect_Afterimage`, `UniqueEffect_AllMine`, `UniqueEffect_Golemizing`
조회 규칙: `GemUniqueEffect.cs:26` `GetLocalizedStringAsync("Reward Text Table", $"UniqueEffect_{uniqueType}")`.
각 에셋의 `localizedItemName`/`localizedDescription`은 테이블 GUID `fb33b23eb1ad3d64b9b02523ebac3189`, KeyId 98372570550272 / 98372578938880~98372578938894.

## 6. 삭제 시 플레이어가 잃는 것 (요약)

| 잃는 것 | 출처 | 체감 |
|---|---|---|
| **집기 슬롯 2 → 최대 5** | 시너지 3세트/5세트 | **가장 큼.** Golemizing(5명) 자체가 도달 불가능해짐 |
| **피격 시 드롭 면역** | 시너지 3세트 | 큼. 이게 없으면 다수 홀드 빌드가 피격 1회에 붕괴 |
| **집기 범위 확장 (2계통)** | DemonHandPower(+0.5/개), AllMine(마리당 +1.0~1.8) | 큼. **군중심리·자동집기 반경까지 동반 축소** |
| **플레이어 이속 (마리당 +0.1)** | MobMentality | 중간. 둔화 무시 가산이라 체감 큼 |
| **포물선 조합 효율 +21~35%** | HumanWaveTactics | 중간 (포물선 한정) |
| **동일 조합 반복 시 효율 ×1.5** | Afterimage | 중간~큼 (누적 보너스 전체를 증폭, 직구도 적용) |
| **소환수 합체 (2명/10초, 5명/10초)** | TwinFusion, Golemizing | 큼. `FusionMinionController` 전체가 BigHand **전용** — 다른 소비처 없음, 통째로 삭제 가능 |
| ~~투척 후 소환수 이속 +50%~~ | SwiftRelocation | **없음 — 원래 작동한 적 없음** |

## 7. 재구축 시 반드시 기억할 함정 5개

1. **시너지는 raw count 3 / 5** — `GemSynergyLogic.GetLevel`(2/4/6/8)을 쓰면 안 된다. BigHand만 예외.
2. **SwiftRelocation은 한 번도 작동한 적 없다** — `TryGetComponent` → `GetComponentInChildren`. 밸런스 미검증.
3. **HumanWaveTactics는 포물선 전용, Afterimage는 직구+포물선 양쪽** — 비대칭이 의도인지 확인.
4. **개수 비례 스케일링이 제각각** — 카운트 비례: DemonHandPower, AllMine, MobMentality / on-off: HumanWaveTactics, Afterimage, TwinFusion, Golemizing. 통일할지 결정할 것.
5. **`Scripts/Deprecated/`는 살아있다** — asmdef가 프로젝트에 하나도 없어서 전부 Assembly-CSharp로 컴파일된다. 폴더 이름만 믿고 MobMentality를 죽은 코드로 넘기지 말 것.

---

# 【도달 불가였던 11개 그룹 + 미구현 유니크】

I have everything. Two of the brief's premises turned out to be wrong — I've documented the correction.

---

# 젬 시너지/유니크 스펙 하베스트 — 죽은 11개 그룹

> 수집 시각 기준 브랜치: `074720d`. 모든 인용은 `path:line`.

## 0. 브리핑 정정 (중요 — 읽고 시작할 것)

의뢰문의 전제 2개가 **사실과 다릅니다**. 재구축 비용 추정이 달라지므로 먼저 정정합니다.

**정정 1 — "ZERO assets"는 틀림. 에셋은 전부 존재합니다.**
11개 그룹 모두 `.asset`이 **존재**하며, 다만 전부 `Assets/SOData/Deprecated/Gems/` 아래에 있고 **`Growth Reward Registry.asset`에서 단 하나도 참조되지 않습니다**. (전체 젬 에셋 97개 중 66개가 Deprecated. 레지스트리의 38개 guid 참조와 Deprecated 젬 guid를 교차 검증한 결과 **교집합 0**.)

즉 정확한 상태는 **"dead-by-data"가 아니라 "dead-by-registry"** 입니다. 드랍 풀에 안 들어가서 획득이 불가능할 뿐, 오브젝트·이름·설명·유니크 타입·효과 리스트는 YAML로 살아있습니다. **재구축 시 처음부터 만들 필요 없이 Deprecated 폴더에서 되살리면 됩니다.** 삭제 전 이 폴더를 반드시 보존하십시오.

| 그룹 | enum | Deprecated 에셋 수 | 레지스트리 등록 |
|---|---|---|---|
| Base | 0 | 1개 (**LIVE**, `SOData/Rewards/Gems/Base/Default_Root_Gem.asset`) | O |
| Poison | 1 | 11 | X |
| BloodPop | 2 | 10 | X |
| Execution | 3 | 2 | X |
| Priest_Chill | 100 | 7 | X |
| Priest_Aging | 101 | 4 | X |
| Priest_Corrosion | 102 | 4 | X |
| Archer_ArcheryPrinciples | 202 | 8 | X |
| Warrior_Executioner | 300 | 7 | X |
| Shield_Guardian | 400 | 9 | X |
| Spearman_Vanguard | 500 | 4 | X |

살아있는 그룹은 `Stamina(4)`, `Fastball(5)`, `Shotput(600)`, `BigHand(700)` 뿐이며 에셋은 `SOData/Rewards/Gems/` 아래에 있습니다.

**Base(0)은 죽지 않았습니다.** `Default_Root_Gem.asset`은 LIVE + 등록됨이며, `effects: []` / `subSlots: 2` / `eligibleJobs: -1(All)` — 트리 루트 전용 **구조물**입니다. 시너지 수치가 애초에 없고, `GemSynergyDisplayUI.cs:30`에서 `if (group == GemSynergyGroup.Base) continue;`로 표시에서도 제외됩니다. **효과 삭제 대상이 아니라 트리 구조의 일부이므로 반드시 남겨야 합니다.**

**정정 2 — "NO asset AND NO handler인 GemUniqueType"은 공집합입니다.**
enum 89개 값 전체를 `SOData/**/*.asset`의 `uniqueType:` 필드와 대조한 결과, **`None=0`을 제외한 모든 값이 에셋을 가집니다.** 미구현 enum 값은 없습니다. 대신 실제로 존재하는 문제는 §5의 두 부류(설명 누락 / UI 미표시)입니다.

---

## 1. `GemSynergyLogic.GetLevel` 임계값 (정확값)

`Assets/Scripts/Systems/Growth/Rules/GemSynergyLogic.cs:9-16`

```csharp
public static int GetLevel(int count)
{
    if (count >= 8) return 4;
    if (count >= 6) return 3;
    if (count >= 4) return 2;
    if (count >= 2) return 1;
    return 0;
}
```

| 연결 젬 수 | level |
|---|---|
| 0–1 | 0 |
| 2–3 | 1 |
| 4–5 | 2 |
| 6–7 | 3 |
| 8+ | 4 |

주석(`:8`)은 "기획표에 따라 2, 4, 6, 8 시너지를 레벨로 매핑". **이 함수는 살아있는 Stamina/Fastball도 사용하므로 삭제 금지** (`StaminaGemHandlers.cs:33`, `FastballGemHandlers.cs:29`).

---

## 2. 오프바이원 불일치 (요청 항목 — 확인됨, 단 "일관된 off-by-one"이 아님)

UI는 `Lv1..Lv4` 라벨을 붙이지만 코드 게이트는 `level >= N`입니다. **중요: 이 어긋남은 그룹마다 다릅니다.** 단일 규칙으로 보정하면 틀립니다.

| 그룹 | UI 라벨 | 코드 게이트 | 실제 필요 젬 수 | 어긋남 |
|---|---|---|---|---|
| Poison | Lv1 | `level >= 2` | **4개** | +1 |
| Poison | Lv2 | `level >= 3` | **6개** | +1 |
| Poison | Lv3 | `level >= 4` | **8개** | +1 |
| Priest_Chill | Lv1 / Lv2 / Lv3 | `>= 2` / `>= 3` / `>= 4` | 4 / 6 / 8 | +1 |
| BloodPop | Lv1 / Lv2 | `>= 2` / `>= 3` | 4 / 6 | +1 |
| Priest_Aging | Lv1 | `>= 2` | 4 | +1 |
| Priest_Aging | **Lv2** | `>= 3` | 6 | +1 |
| Priest_Aging | **Lv3** | `>= 3` | **6** | **충돌** |
| Priest_Corrosion | Lv1 / Lv2 | `>= 1` / `>= 2` | **2 / 4** | **0 (정상)** |

**함정 2개:**

1. **`Priest_Corrosion`만 어긋나지 않습니다.** `GemSynergyLogic.cs:41-46`의 `GetCorrosionDamageAmp`는 `level>=1 → 0.25`, `level>=2 → 0.40`으로 UI의 Lv1/Lv2와 정확히 일치합니다. 다른 그룹에 일괄로 `-1` 보정을 적용하면 부식만 깨집니다.

2. **`Priest_Aging` Lv2와 Lv3이 같은 게이트를 공유합니다.** `GetAgingMaxStack`(`:37`)과 `GetSenilityDamageAmp`(`:38`) 둘 다 `level >= 3`입니다. UI는 서로 다른 두 단계(Lv2=최대스택 120, Lv3=피해 12%)로 광고하지만 **실제로는 6개에서 동시에 켜지고 8개에서는 아무 일도 없습니다.** UI가 Lv3 항목을 렌더하려면 `maxLevel>=3`(=6개)이어야 하므로 표시 자체는 나오지만, "Lv3을 위해 8개를 모은다"는 기획 의도는 코드에 없습니다. 재구축 시 `GetSenilityDamageAmp`를 `level >= 4`로 올릴지 결정 필요.

또 `PriestAction.cs:31,36,41`은 `>= 1`(2개)을 게이트로 쓰므로 **또 다른 3번째 규칙**입니다.

---

## 3. 코드 수치가 존재하는 5개 그룹 (Poison / Chill / BloodPop / Aging / Corrosion)

이 5개만 `GemSynergyLogic`에 실제 수치 함수가 있습니다. 나머지 6개(Execution, Archer, Warrior, Shield, Spearman, Base)는 **로직 함수가 아예 없습니다** — §4 참조.

### 3-1. Poison (1)

UI 문자열 — `GemSynergyDisplayUI.cs:108-112`:
- Lv1: `"Poison duration extended to +5s."`
- Lv2: `"Basic attacks apply +1 extra Poison stack."`
- Lv3: `"Poison tick interval reduced to 3s (0.6x)."`

| 함수 | 정의 | 게이트 | 값 |
|---|---|---|---|
| `GetPoisonDurationBonus` | `GemSynergyLogic.cs:19` | `level>=2` | `+5.0f` (else `0f`) |
| `GetPoisonExtraStack` | `GemSynergyLogic.cs:20` | `level>=3` | `+1.0f` (else `0f`) |
| `GetPoisonIntervalMultiplier` | `GemSynergyLogic.cs:21` | `level>=4` | `0.6f` (else `1.0f`) — 기본 5초 × 0.6 = **3초** |

소비 지점 — **전부 무력화됨**:
- `GemRuleSystem.cs:12-24` `GetPoisonInterval` — `int level = 0;` **하드코딩**, 원래 호출은 주석 처리(`:17`). `hasLethalDose = false` 하드코딩(`:18`). baseInterval `5.0f`(`:14`).
- `GemRuleSystem.cs:26-33` `GetPoisonDuration` — `level = 0` 하드코딩(`:31`). baseDuration `10.0f`(`:28`).
- `GemRuleSystem.cs:35-40` `ModifyIncomingPoisonStack` — `level = 0` 하드코딩(`:38`).
- `GemRuleSystem.cs:42-47` `GetLethalPoisonBonus` — `hasLethalPoison = false` 하드코딩(`:45`).

> **이미 사문(死文)입니다.** `level`이 리터럴 `0`이라 `GetPoisonDurationBonus(0)`=0, `GetPoisonExtraStack(0)`=0, `GetPoisonIntervalMultiplier(0)`=1.0. 시너지 개수와 무관하게 항상 기본값. 에셋이 등록돼 있었어도 효과가 없었을 것입니다.

### 3-2. Priest_Chill (100)

UI 문자열 — `GemSynergyDisplayUI.cs:113-117`:
- Lv1: `"Slow effect per stack increased by 5%."`
- Lv2: `"Refund 25 Chill stacks upon freezing."`
- Lv3: `"Freezing deals true damage based on max HP."`

| 함수 | 정의 | 게이트 | 값 |
|---|---|---|---|
| `GetChillSlowBonus` | `GemSynergyLogic.cs:25` | `level>=2` | `0.05f` — 주석 "4세트: 각 구역마다 감속량 5% 추가" |
| `GetChillRefundAmount` | `GemSynergyLogic.cs:26` | `level>=3` | `25.0f` — 주석 "6세트: 동결 후 스택 25 반환" |
| `HasChillFreezeDamage` | `GemSynergyLogic.cs:27` | `level>=4` | `true` — 주석 "8세트: 동결 시 체력 비례 고정 피해" |

**한기 스택 구간별 기본 감속** (시너지와 별개, `GemRuleSystem.cs:62-68`) — 재구축 시 필요:
| 스택 | 감속 |
|---|---|
| 76+ | `0.25f` |
| 51–75 | `0.20f` |
| 26–50 | `0.10f` |
| 1–25 | `0.05f` |

시너지 보너스는 이 값에 **가산**(`return baseReduction + bonus;` `:68`).

기타 수치:
- `GetMaxChillStacks` (`GemRuleSystem.cs:71-78`): base `15.0f`(적) / `5.0f`(아군). `flowerCount = 0` 하드코딩(`:76`).
- `GetChillFreezeDamagePercentage` (`GemRuleSystem.cs:80-83`): **보스 `0.04f` / 일반 `0.08f`**. 이 함수만 인벤토리를 안 보므로 **유일하게 살아있는 값**이지만, 호출자인 `HasFreezeFixedDamage`가 항상 false라 도달 불가.

소비 지점 — **전부 무력화됨**: `GemRuleSystem.cs:59`(`level=0`), `:89`(`return false;` — `ShouldBlockChillStack` 호출 주석 처리), `:95`(`level=0`), `:102`(`level=0`).

### 3-3. BloodPop (2)

UI 문자열 — `GemSynergyDisplayUI.cs:118-121`:
- Lv1: `"BloodPop damage multiplier increased to 0.5."`
- Lv2: `"BloodPop explosion radius increased by 1.5x."`

| 함수 | 정의 | 게이트 | 값 |
|---|---|---|---|
| `GetBloodPopDamageRatio` | `GemSynergyLogic.cs:31` | `level>=2` | `0.5f` / **기본 `0.4f`** (0이 아님 — 기본값 주의) |
| `GetBloodPopRadiusMultiplier` | `GemSynergyLogic.cs:32` | `level>=3` | `1.5f` (else `1.0f`) |

소비 지점 — **무력화됨**: `GemRuleSystem.cs:114`(`level=0` → ratio 항상 `0.4f`), `:122`(`level=0` → 항상 `1.0f`), `:129`(`return 0f;` — `GetExplodingFleshStackRatio` 주석 처리).

### 3-4. Priest_Aging (101)

UI 문자열 — `GemSynergyDisplayUI.cs:122-126`:
- Lv1: `"Slow effect per stack increased by 5%."`
- Lv2: `"Maximum Aging stacks increased to 120."`
- Lv3: `"Senile enemies take 12% extra damage."`

| 함수 | 정의 | 게이트 | 값 |
|---|---|---|---|
| `GetAgingSlowBonus` | `GemSynergyLogic.cs:36` | `level>=2` | `0.05f` |
| `GetAgingMaxStack` | `GemSynergyLogic.cs:37` | `level>=3` | `120.0f` / 기본 `100.0f` |
| `GetSenilityDamageAmp` | `GemSynergyLogic.cs:38` | `level>=3` | `0.12f` / **기본 `0.08f`** (0이 아님) |

**노화 스택 구간별 기본 감속** (`GemRuleSystem.cs:144-152`):
| 스택 | 감속 |
|---|---|
| 101+ | `0.25f` |
| 81–100 | `0.20f` |
| 61–80 | `0.16f` |
| 41–60 | `0.12f` |
| 21–40 | `0.08f` |
| 1–20 | `0.04f` |

기타:
- `GetMaxAgingStacks` (`GemRuleSystem.cs:155-162`): 아군 `10f`, 인벤 없으면 `25f`, 그 외 `GetNoCountryMaxStack(0)` → `25f`. **`GetAgingMaxStack`의 100/120은 이 경로에서 호출되지 않습니다** — 두 최대스택 개념이 서로 연결돼 있지 않음(별개 버그).
- `ShouldAgingInstaKill` (`GemRuleSystem.cs:171-174`): **본문이 `return false;` 뿐.** 파라미터 미사용, `GemUniqueLogic.ShouldAgingInstaKill`은 호출되지 않음.
- `GetGoryeojangSlowReduction` (`GemRuleSystem.cs:176-179`): `0.20f` 상수 반환. 인벤 의존 없음.
- **`GemUniqueLogic.cs:25`에 미해결 혼란 주석이 남아있음**:
  ```csharp
  public static float GetNoCountryMaxStack(int count) => count > 0 ? (25f + count * 100f) : 25f; // Wait, wait. "Aging max stacks +100".
  ```
  작성자 스스로 의도를 확신하지 못한 상태로 커밋됨. 재구축 시 **25 기준 + 개당 100 가산**이 맞는지 기획 재확인 필요.

### 3-5. Priest_Corrosion (102)

UI 문자열 — `GemSynergyDisplayUI.cs:127-130`:
- Lv1: `"Corrosion damage amplification increased to 25%."`
- Lv2: `"Corrosion damage amplification increased to 40%."`

`GemSynergyLogic.cs:41-46` — **유일하게 UI 라벨과 게이트가 일치**:
```csharp
public static float GetCorrosionDamageAmp(int level)
{
    if (level >= 2) return 0.40f;
    if (level >= 1) return 0.25f;
    return 0f;
}
```

소비 지점 — `GemRuleSystem.cs:185-200` `GetCorrosionDamageAmp`: `level = 0` 하드코딩(`:188`) → 항상 `0f`. `doubleCorrosionCount = 0` 하드코딩(`:193`). 유니크 `DoubleCorrosion` 가산 공식은 보존됨: `amp += 0.10f * doubleCorrosionCount;` (`:196`, **개수 비례 스택**, `amp > 0f`일 때만).

---

## 4. 로직 함수가 아예 없는 6개 그룹 (Execution / Archer / Warrior / Shield / Spearman / Base)

`GemSynergyLogic.cs`에는 이 그룹들의 함수가 **하나도 없습니다**. 수치가 호출부에 인라인 하드코딩돼 있거나, `Scripts/Deprecated/*.txt`(**컴파일 대상 아님 — `.txt` 확장자**)에 주석으로만 남아있습니다.

### 4-1. Execution (3)

UI — `GemSynergyDisplayUI.cs:131-133`:
- Lv1: `"Basic attacks and throws apply 1 Execute stack."`

**Lv1만 존재. 구현 코드 0줄.** `DebuffStackType`(`Scripts/Define/CommandData.cs:23-31`)에 **`Execute` 항목 자체가 없습니다.** 처형 스택을 담을 자료구조가 부재 → 완전 미구현. 유니크 2종(`Fear=18`, `Guillotine=19`)만 에셋으로 존재.

### 4-2. Warrior_Executioner (300)

UI — `GemSynergyDisplayUI.cs:134-138`:
- Lv1: `"Warrior throw damage +20% to enemies below 50% HP."`
- Lv2: `"Warrior throw HP cost reduced by 30%."`
- Lv3: `"Throw damage amplified up to +50% based on enemy missing HP."`

**Lv2만 살아있는 코드 — `AllyController.cs:178-186`** (이 그룹의 유일한 실제 구현):
```csharp
if (minionData.minionType == CommandData.SkeletonWarrior && InventoryManager.Instance != null)
{
    int execLevel = GemSynergyLogic.GetLevel(InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Warrior_Executioner));
    if (execLevel >= 2)
    {
        damageAmount *= 0.7f;
    }
}
```
- 진입: `ApplyThrowCost()` (`AllyController.cs:173`), 기준값 `damageAmount = _stats.MAXHP * minionData.hpCostRatioPerThrow` (`:176`).
- **주의: UI/주석은 "스테미너 소모 30% 감소"라 하지만 실제로는 HP 소모입니다.** `_stats.Health.GetDamage(new DamageInfo(damageAmount, DamageType.Fixed, null));` (`:188`).
- `GetSynergyCount`가 항상 0 → `execLevel=0` → **도달 불가**. 코드는 살아있으나 데이터가 없어 inert.

**Lv1/Lv3 — `Scripts/Deprecated/DeprecatedThrowGems.txt:46-62` (주석, 미컴파일):**
```csharp
int execLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Warrior_Executioner));
float hpRatio = entity.Stats.Health.MaxHP > 0 ? entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP : 1f;

if (execLevel >= 1 && hpRatio < 0.5f) // (2) 스택: 50% 미만 적 20% 증가
{
    finalDamage *= 1.2f;
}

if (execLevel >= 3 && hpRatio <= 0.3f) // (6) 스택: 30% 이하 시 최대 50% 비례 증폭
{
    float extraAmp = 0.5f * ((0.3f - hpRatio) / 0.3f);
    finalDamage *= (1f + extraAmp);
}
```
> **Lv3 문턱 불일치:** UI는 "missing HP 기반"이라고만 하지만 코드는 **`hpRatio <= 0.3f`에서만** 발동하며, 증폭은 `0.3 → 0.0` 구간을 `0 → +50%`로 선형 보간. Lv1과 곱연산으로 중첩(최대 `1.2 * 1.5 = 1.8배`).

### 4-3. Archer_ArcheryPrinciples (202)

UI — `GemSynergyDisplayUI.cs:139-141`:
- Lv1: `"Fires a piercing arrow after every 5 missed attacks."`

**UI에는 Lv1 하나뿐이지만, Deprecated 코드에는 UI가 설명하지 않는 Lv3/Lv4가 있습니다.** UI 문자열은 이 그룹의 신뢰할 수 있는 소스가 아닙니다.

- **Lv3** — `DeprecatedThrowGems.txt:97-107`: 중앙(반경 30% 이내) 적 대상 `archerSynLevel >= 3` → `finalDamage *= 1.50f;`
  ```csharp
  float dist = Vector2.Distance(target.transform.position, impactPos);
  if (dist <= radius * 0.3f)
  {
      if (inven.HasUniqueEffect(GemUniqueType.AimedStrike)) finalDamage *= 1.20f;
      int archerSynLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Archer_ArcheryPrinciples));
      if (archerSynLevel >= 3) // (6) 스택
      {
          finalDamage *= 1.50f;
      }
  }
  ```
- **Lv1 / Lv4 (범위 증가)** — `DeprecatedThrowGems.txt:171-176`:
  ```csharp
  int archerSynLevel = GemSynergyLogic.GetLevel(InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Archer_ArcheryPrinciples));
  if (archerSynLevel >= 1) radiusMult += 0.20f; // (2) 스택
  if (archerSynLevel >= 4) radiusMult += 0.10f; // (8) 스택
  ```
  **가산(`+=`)이지 곱이 아님.** UI의 "5회 빗나감 → 관통 화살"과 전혀 다른 설계 → **UI 문자열과 Deprecated 코드가 서로 다른 세대의 기획입니다.** 재구축 시 어느 쪽을 채택할지 결정 필요.

### 4-4. Shield_Guardian (400)

UI — `GemSynergyDisplayUI.cs:142-147`:
- Lv1: `"Throwing Shieldbearer deals 20% of shield as AoE damage."`
- Lv2: `"Shield expiration deals true damage to nearby enemies."`
- Lv3: `"Converts 15% of excess healing into Shield."`
- Lv4: `"All stats +15% while shielded."`

**4단계 전부 설계된 유일한 그룹.** Lv3/Lv4는 **살아있는 코드**, Lv1/Lv2는 Deprecated.

**Lv3 — `CharacterHealth.cs:292-315` (LIVE):**
```csharp
int guardianLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
if (guardianLevel >= 3 && _status != null) // (6) 스택
{
    float shieldToAdd = excessHeal * 0.15f;
    float maxShieldLimit = _stat.MAXHP * 0.15f;
    if (_status.TotalShield < maxShieldLimit)
    {
        float allowedToAdd = Mathf.Min(shieldToAdd, maxShieldLimit - _status.TotalShield);
        if (allowedToAdd > 0) _status.AddShield(allowedToAdd, 10.0f); // 임시 10초
    }
}
```
- 진입: `Heal()` 내부, `excessHeal = (curHP + healAmount) - _stat.MAXHP` (`:288`), `if (excessHeal > 0 ...)` (`:293`).
- 초과 회복분의 **15%**, 상한 **MAXHP의 15%**, 지속 **10.0초**(코드 주석에 "임시"로 명시 — 확정값 아님).
- **UI가 언급하지 않은 상한/지속 규칙이 코드에만 있습니다.**

**Lv4 — `CharacterStat.cs:35-56` (LIVE):**
```csharp
private float ShieldbearerSelfMult
{
    get
    {
        if (jobType != CommandData.SkeletonShieldbearer) return 1f;
        float mult = 1f;
        if (Status != null && Status.TotalShield > 0)
        {
            ...
            int guardianLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
            if (guardianLevel >= 4) // (8) 스택
            {
                mult += 0.15f;
            }
        }
        return mult;
    }
}
```
> **UI는 "All stats +15% while shielded"라고 하지만 실제로는 `jobType != CommandData.SkeletonShieldbearer` 조기 반환(`:39`) 때문에 방패병 자신에게만 적용됩니다.** 이름 `ShieldbearerSelfMult`가 실제 동작. 재구축 시 UI 문구 수정 또는 범위 확대 결정 필요.

**Lv1 — `DeprecatedThrowGems.txt:127-150`:**
```csharp
int guardianLevel = GemSynergyLogic.GetLevel(inven.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
bool hasGuardianAoE = guardianLevel >= 1; // (2) 스택
if (hasTwistedGround || hasGuardianAoE)
{
    float aoeDamage = finalShield * 0.20f;
    if (hasTwistedGround && hasGuardianAoE) aoeDamage = finalShield * 0.40f; // 둘 다 있으면 40%
    float radius = 2.5f;
    ... DamageType.Physical ...
}
```
`TwistedGround` 유니크와 **중첩 시 20%→40%** 특수 규칙. 반경 `2.5f`, `DamageType.Physical`.

**Lv2 — `DeprecatedStatusGems.txt:134-152`:**
```csharp
int guardianLevel = GemSynergyLogic.GetLevel(InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.Shield_Guardian));
if (guardianLevel >= 2) // (4) 스택: 보호막이 사라지면 주변 피해
{
    float radius = 3.0f;
    ... new DamageInfo(amount, DamageType.Fixed, this.gameObject) ...
}
```
반경 `3.0f`, `DamageType.Fixed`, 피해량 = **소멸한 보호막 수치(`amount`) 전액**.

### 4-5. Spearman_Vanguard (500)

UI — `GemSynergyDisplayUI.cs:148-153`:
- Lv1: `"Dash distance +30%, dash speed +20%."`
- Lv2: `"Dash deals 150% physical damage to collided enemies."`
- Lv3: `"Allies touched by dash gain +15% move/evasion speed."`
- Lv4: `"Player becomes invincible during dash."`

**4단계 전부 UI에만 존재. 구현 코드 0줄** — 살아있는 코드에도, Deprecated `.txt`에도 이 그룹의 `GetSynergyCount(GemSynergyGroup.Spearman_Vanguard)` 호출이 없습니다. `GemSynergyLogic`에도 함수 없음. 유니크 4종(`Vanguard=130`, `SpearSwiftness=131`, `IronMountain=132`, `ThousandStabs=133`)만 에셋 존재.

> **재구축 관점: 11개 그룹 중 가장 비싼 그룹입니다.** 4단계 전부 백지에서 시작해야 하며, 대시 시스템(`SkillCombatUtil.GetSafeDestination` 등)과의 연동도 새로 설계해야 합니다.

### 4-6. Base (0)

**시너지 개념 자체가 없습니다.** UI 표시 제외(`GemSynergyDisplayUI.cs:30`), `GetSingleLevelDescription`에 `case` 없음(→ fallback `"New power unlocked."`), `GemSynergyLogic`에 함수 없음. 트리 루트 구조물. **삭제 대상 아님** (§0 참조).

---

## 5. GemUniqueType — 미구현/미표시 목록

### 5-1. "NO asset AND NO handler" = **공집합**

enum 89개 값 전수 대조 결과 `None=0` 외 **모든 값이 에셋을 보유**. 요청하신 목록은 존재하지 않습니다.

### 5-2. 대신 실재하는 문제 ①: `GetUniqueDescription`에 `case`가 없어 기본 문자열로 빠지는 17개

`GemSynergyDisplayUI.cs:286` `default: fallback = "Unique ability active"; break;`

| 값 | 이름 | 에셋 | 비고 |
|---|---|---|---|
| 9 | `PoisonFlask` | DEPRECATED | 주석(`GemSO.cs:49`): 중독 플라스크 (던지기 적용 스택 증가) |
| 14 | `BitingWind` | DEPRECATED | 주석(`GemSO.cs:56`): 칼바람 (투척 범위 내 5초마다 한기 스택 부여) |
| 16 | `AbsoluteZero` | DEPRECATED | 주석(`GemSO.cs:58`): 절대영도 (동결 진입 시 주변 50스택 광역, 방 1회) |
| 21 | `GoreParty` | DEPRECATED | 주석(`GemSO.cs:67`): 내장 파티 (반경 내 아군 회복) |
| 215 | `ExperiencedPitcher` | **LIVE** | 숙련된 투수 |
| 250–253 | `JustThrowIt`, `Ballistics`, `SiegeMode`, `Monocle` | **LIVE** | 투포환 4종 |
| 260–267 | `DemonHandPower`, `HumanWaveTactics`, `TwinFusion`, `MobMentality`, `SwiftRelocation`, `Afterimage`, `AllMine`, `Golemizing` | **LIVE** | 큰손 8종 |

> **주의: 215/250–253/260–267(13개)은 LIVE + 등록 + 핸들러 보유입니다.** UI 설명만 누락된 것이고 게임에서 정상 작동합니다. **이번 삭제 범위 밖 — 건드리지 마십시오.** `GetUniqueDescription`을 통째로 지우면 이 13개의 설명이 함께 사라집니다.
> 반대로 9/14/16/21(4개)은 설명도 없고 등록도 안 됨 → 의도를 아는 유일한 단서가 위 표의 `GemSO.cs` 주석뿐입니다. **이 4개는 이 문서가 유일한 기록입니다.**

### 5-3. 대신 실재하는 문제 ②: `GetSynergyGroupOfUnique`가 `Base`로 흘려 **영구 미표시**되는 유니크 (68개)

`GemSynergyDisplayUI.cs:167-194`가 명시적으로 매핑하는 값은 **21개뿐**:

| 반환 그룹 | 매핑된 값 | 소스 |
|---|---|---|
| `Poison` | `LethalPoison`, `LethalDose` | `:171-172` |
| `Priest_Chill` | `AchingBones`, `SlowlyFreezingFlower` | `:173-174` |
| `BloodPop` | `ExplodingFlesh` | `:175` |
| `Priest_Aging` | `NoCountryForOldMen` | `:176` |
| `Stamina` | `CatchBreath` ~ `EfficientThrow` (200–209, 10종) | `:177-186` |
| `Fastball` | `SetPosition`, `Windup`, `MagicPitchFireball`, `MagicPitchArirangBall`, `Closer` (210–214) | `:187-191` |
| **`Base`** | **그 외 전부** (`default:`) | `:192` |

**결정적 결함:** 표시 루프(`:28-58`)가 **`Base`를 최상단에서 건너뜁니다**:
```csharp
foreach (GemSynergyGroup group in System.Enum.GetValues(typeof(GemSynergyGroup)))
{
    if (group == GemSynergyGroup.Base) continue;   // :30
    ...
    foreach (var uniqueKvp in globalStats.UniqueEffectCounts)   // :48
    {
        ...
        if (GetSynergyGroupOfUnique(unique) == group && uniqueCount > 0)   // :52
```
→ `default: return Base`로 떨어지는 **68개 유니크는 장착해도 시너지 패널에 절대 렌더되지 않습니다.**

여기 해당하는 것들: Execution 2종(`Fear`, `Guillotine`), Corrosion 4종(29–32), Warrior 7종(33–36, 111–113), Archer 8종(40–47), Shield 9종(120–128), Spearman 4종(130–133), Poison 나머지 6종(7–12), Chill 나머지 5종(13–17), BloodPop 나머지 5종(20–25), Aging 나머지 3종(26–28), **그리고 LIVE인 `ExperiencedPitcher`(215) + Shotput 4종(250–253) + BigHand 8종(260–267)**.

> **즉 현재 살아있는 Shotput/BigHand/ExperiencedPitcher 13종도 시너지 패널에 표시되지 않고 있습니다** — `GemSynergyGroup.Shotput(600)`, `BigHand(700)`이 `GetSynergyGroupOfUnique`에 없기 때문. 이건 **젬 효과 삭제와 무관한 현존 버그**이며, UI를 재사용할 계획이면 지금 고쳐야 합니다. `GemSO.GetSynergyColor`(`GemSO.cs:208-225`)에도 `Shotput`/`BigHand`/`Spearman_Vanguard` 색상이 없어 `Color.white`로 빠집니다(`:223`).

---

## 6. 삭제 전 반드시 알아야 할 부수 사실

1. **`ImpactAction.ApplyCommonSynergyDebuffs`는 빈 껍데기입니다** — `Scripts/Player/Throw related/ImpactActions/ImpactAction.cs:11-14`:
   ```csharp
   protected void ApplyCommonSynergyDebuffs(GameObject target, ThrowRecipe recipe)
   {
       // 이제 이 로직은 ThrowEventBus.OnThrowImpactAfterDamage 핸들러로 위임될 예정이므로 공란으로 비워둡니다.
   }
   ```
   호출처 `PriestAction.cs:49`, `SpearmanAction.cs:44`. **주석이 약속한 `ThrowEventBus.OnThrowImpactAfterDamage` 위임은 실제로 구현되지 않았습니다.** 따라서 던지기를 통한 Poison/BloodPop/Execution 스택 부여는 **경로 자체가 존재하지 않습니다.** 재구축 시 이 이관을 완료하거나 메서드를 되살려야 합니다.

2. **`PriestAction`이 잘못된 디버프 타입에 씁니다** — `PriestAction.cs:31-45`:
   - `Priest_Chill` 시너지 → `AddDebuffStack(DebuffStackType.Fracture, ...)` (**골절**, `:33`)
   - `Priest_Aging` 시너지 → `AddDebuffStack(DebuffStackType.Corrosion, ...)` (**부식**, `:38`)
   - `Priest_Corrosion` 시너지 → `SetDebuffBool(DebuffBoolType.Corroded, 5.0f)` (`:44`)

   `DebuffStackType`(`Scripts/Define/CommandData.cs:23-31`)에는 **`Chill`도 `Aging`도 `Poison`도 `Execute`도 없습니다** (있는 것: `Vulnerability, BloodPop, Bleed, Wound, Corrosion, Fracture`). 즉 한기/노화 스택 타입이 이미 enum에서 제거되었고, `PriestAction`은 남은 타입으로 **임시 대체**해 둔 상태입니다. `GemRuleSystem`의 한기/노화 구간표(§3-2, §3-4)는 **담을 그릇이 없습니다.** 재구축 시 `DebuffStackType`에 `Chill`/`Aging`/`Poison`/`Execute` 추가가 선행되어야 합니다.
   (참고: `DeprecatedStatGems.txt:66`은 `GetDebuffStack(DebuffStackType.Aging)`을 호출 — enum이 트리밍되기 전 세대의 코드라는 증거.)

   부여량은 `float scaledStacks = recipe.GetScaledEffectValue(1.0f);` (`:28`) — 기본 1스택에 풀차지/조합 배율 적용. 게이트는 전부 `>= 1`(2개).

3. **삭제 시 보존해야 할 것** (의뢰 범위대로 구조는 유지):
   - `GemSynergyLogic.GetLevel` — Stamina/Fastball이 사용 중.
   - `SOData/Deprecated/Gems/` 66개 에셋 — 이 문서보다 정확한 1차 사료.
   - `Scripts/Deprecated/*.txt` 4개 — Warrior Lv1/Lv3, Archer Lv1/Lv3/Lv4, Shield Lv1/Lv2의 유일한 구현 기록.
   - `GetUniqueDescription`의 LIVE 13종(§5-2) 설명.
   - `Default_Root_Gem.asset`.

4. **재구축 난이도 요약** (비싼 순):
   | 그룹 | 남은 것 |
   |---|---|
   | `Spearman_Vanguard` | UI 문자열 4줄뿐. 코드 0줄. **백지** |
   | `Execution` | UI 문자열 1줄 + `DebuffStackType.Execute` 부재. **백지** |
   | `Base` | 해당 없음 (구조물, 유지) |
   | `Archer_ArcheryPrinciples` | Deprecated 코드 존재하나 **UI와 기획 세대 불일치** — 결정 필요 |
   | `Warrior_Executioner` | Lv2 LIVE(inert) + Lv1/Lv3 Deprecated 코드 |
   | `Shield_Guardian` | Lv3/Lv4 LIVE(inert) + Lv1/Lv2 Deprecated 코드. **가장 온전** |
   | `Poison`/`Chill`/`BloodPop`/`Aging`/`Corrosion` | `GemSynergyLogic` 수치 함수 전부 보존. `GemRuleSystem`의 `int level = 0;` 하드코딩만 되돌리면 부활 — 단 `DebuffStackType` 선행 복구 필요 |

---

# 【Deprecated 유니크 매니저 6종】

Everything traced. Final answer below.

---

# GEM_LEGACY.md — Deprecated Unique-Effect Managers (하베스트 기록)

대상: `Assets/Scripts/Deprecated/` 의 6개 파일. 삭제 전 스펙 원본 기록.

---

## 0. 요약 표

| 파일 | 구현한 효과 | 현재 상태 | 삭제 시 컴파일 파손 |
|---|---|---|---|
| `PoisonUniqueManager.cs` | PoisonHost (숙주 독 전염) | **완전 inert** (`\|\| true` 조기 return) | `InventoryManager.cs:144-145` |
| `ChillUniqueManager.cs` | BitingWind, AbsoluteZero | **완전 inert** (`false` / `\|\| true`) | `InventoryManager.cs:148-149` |
| `AgingUniqueManager.cs` | Goryeojang (최고 노화 적 장판 표시) | **완전 inert** (`\|\| true`) | `InventoryManager.cs:152-153` |
| `ShieldbearerUniqueManager.cs` | SturdyShield, AuraOfPatience/Overwhelming, ShieldsWill×3 | **완전 inert** (모든 `has*` 하드코딩 `false`) | `InventoryManager.cs:156-157`, `CharacterStat.cs:80/132/209` |
| `PlayerUniqueEffectManager.cs` | JustThrowIt, MobMentality | **살아있음 (LIVE)** — 에셋 존재 + 게이트 실재 | `PlayerController.cs:194-195`, `CharacterStat.cs:204-205`, `ThrowController.cs:416/503/540` |
| `ActiveGemHandlers.cs` (`SynergyDamageAmplifier`) | Corroded 대상 피해 증폭 | **부분 LIVE** — 부식 적에게 **×1.08** 실제 적용 중 | `GemHandlerRegistry.cs:41` |
| `PoisonPuddle` (PlayerUniqueEffectManager.cs 내 동거 클래스) | 독 장판 3초 틱 | **도달 불가 사망 코드** (파일명 불일치 → 컴포넌트 부착 불가, 코드 AddComponent 없음) | 없음 |

---

## 1. PoisonUniqueManager.cs — `GemUniqueType.PoisonHost` (=7)

**상태: 완전 inert.** `PoisonUniqueManager.cs:22` — `if (inven == null || true /* !inven.HasUniqueEffect(GemUniqueType.PoisonHost) */) return;` → `Update()` 는 매 프레임 즉시 return. 그 아래 로직 전부 도달 불가.

**원래 스펙 (재구축용):**
- 상수: `SPREAD_INTERVAL = 3.0f` (`:10`), `SPREAD_RADIUS = 5.0f` (`:11`)
- 숙주 지정 (`AssignNewHost`, `:49-85`): `FindObjectsByType<BaseEntity>` 전수 스캔 → `ent.team == Team.Enemy && !ent.Stats.Health.IsDead` 인 것 중 **랜덤 1체** 선택. 숙주 사망/null 이면 재지정.
- 숙주 시각 표시: `CharacterVisualFeedback.SetBaseColor(new Color(0.7f, 1f, 0.7f))`, 없으면 자식 `SpriteRenderer.color` 직접 변경 (`:69-79`).
- 숙주 UI 아이콘 유지: 매 프레임 `Status.SetDebuffBool(DebuffBoolType.Bleeding, 0.5f)` (`:37`).
- 전염 (`SpreadPoisonFromHost`, `:87-113`): 3초마다, `hostStack = GetDebuffStack(DebuffStackType.BloodPop)`; `hostStack > 0` 이면 `passAmount = Mathf.Max(1f, hostStack * 0.1f)` (**최소 1스택 보장**). `Physics2D.OverlapCircleAll(host.pos, 5.0f, Layers.EnemyMask)` 의 숙주 제외 전원에게 `AddDebuffStack(DebuffStackType.BloodPop, passAmount)`.
- **주의**: 이름은 Poison 인데 실제로 쓰는 스택은 `BloodPop` 임 (기획 의도와 불일치 가능).
- 에셋: `SOData/Deprecated/Gems/Poison/Gem_Poison_Unique_PoisonHost.asset` — `uniqueType: 7`, `rarity: 3`, `shopCost: 100`, `synergyGroup: 1`, desc "Spreads poison stacks in an area every 3 seconds."

**외부 호출부:** `InventoryManager.cs:144-145` (AddComponent) 뿐. `static Instance` (`:6`) 를 읽는 live 코드 **없음**.

---

## 2. ChillUniqueManager.cs — `BitingWind` (=14), `AbsoluteZero` (=16)

**상태: 완전 inert.**
- `ChillUniqueManager.cs:31` — `if (InventoryManager.Instance != null && false /* ...HasUniqueEffect(BitingWind) */)` → 항상 else 분기 (`_bitingWindTimer = 0f`). BitingWind 발동 불가.
- `ChillUniqueManager.cs:86` — `if (InventoryManager.Instance == null || true /* !...HasUniqueEffect(AbsoluteZero) */) return;` → `TriggerAbsoluteZero` 는 호출돼도 즉시 return. **게다가 `TriggerAbsoluteZero` 를 부르는 live 코드가 아예 없음** (grep 결과 정의부 `:84` 외 0건) — 이중으로 죽어 있음.

**원래 스펙:**
- **BitingWind (칼바람)** (`:9-12`, `:46-66`): 인스펙터 필드 `bitingWindInterval = 5f`, `bitingWindChillStacks = 1f`. 5초마다 플레이어 기준 반경 = `PlayerController.THROWRANGE` 의 `Layers.EnemyMask` 내 **살아있는** 적 전원에게 `AddDebuffStack(DebuffStackType.Fracture, 1f)`.
  - 에셋: `Gem_Priest_Chill_BitingWind.asset` — `uniqueType: 14`, rarity 3, shopCost 100, synergyGroup 100, "Applies 1 chill stack to nearby enemies."
- **AbsoluteZero (절대영도)** (`:84-109`): 방당 1회 (`_hasTriggeredAbsoluteZeroThisRoom`, `RoomInstance.OnPlayerEnteredRoom` 에서 리셋 — `:68-82`). 발동 시 `centerPosition` 기준 **반경 5f** (`:93`) 의 살아있는 적 전원에게 `AddDebuffStack(DebuffStackType.Fracture, 50f)` (`:105`).
  - 에셋: `Gem_Priest_Chill_AbsoluteZero.asset` — `uniqueType: 16`, rarity 3, shopCost 100, synergyGroup 100, "Applies 50 chill stacks in an area upon freezing (Once per room)."
  - **재구축 시 유의**: 원래 트리거는 "빙결(freeze) 시점" 이어야 하는데 그 호출부가 이미 소실됨. `TriggerAbsoluteZero(Vector3)` 를 부를 곳을 새로 정해야 함.
- 잠재 버그 기록: `:28` 은 `GameManager.Instance` 를 null 체크 없이 `.PLAYERCONTROLLER` 로 역참조 함.

**외부 호출부:** `InventoryManager.cs:148-149` (AddComponent) 뿐.

---

## 3. AgingUniqueManager.cs — `Goryeojang` (=26)

**상태: 완전 inert.** `AgingUniqueManager.cs:30` — `if (inven == null || true /* !inven.HasUniqueEffect(GemUniqueType.Goryeojang) */)` → 항상 진입해서 `HighestAgingEnemy = null` 로 놓고 return. `Update` 는 0.5초 타이머(`_updateInterval = 0.5f`, `:11`)만 헛돌림.

**원래 스펙:**
- 0.5초마다 `CharacterStatus.ActiveEnemies` 순회 → `GetDebuffStack(DebuffStackType.Corrosion)` 이 **최대**인 1체를 `HighestAgingEnemy` 로 지정 (`:37-51`). 동률/0스택이면 null (`maxStack` 초기 0, `>` 비교).
- 그 적 위치에 장판 VFX: `GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY.goryeojangAuraPrefab` 사용, 없으면 런타임 생성 원 스프라이트(64×64, `Color(0,0,0,0.5f)`) fallback (`:55-69`, `:83-104`). `localScale = Vector3.one * 4.0f` (반경 2.0 가정, `:75`).
- 실제 둔화 수치는 이 파일이 아니라 **`GemRuleSystem.cs:176-179` `GetGoryeojangSlowReduction() => 0.20f`** (고려장 둔화 20%) 에 있음 — 그 함수는 남아있고 호출부가 없음.
- 에셋: `Gem_Priest_Aging_Goryeojang.asset` — `uniqueType: 26`, rarity 3, shopCost 100, synergyGroup 101, "Creates a 20% slow and aging area around the enemy with the highest (aging)."

**외부 호출부:** `InventoryManager.cs:152-153` (AddComponent) 뿐. `public static CharacterStatus HighestAgingEnemy` (`:9`) 를 읽는 live 코드 **0건**.
**남는 잔재:** `ThrowEffectRegistrySO.cs:42 public GameObject goryeojangAuraPrefab;` — 유일한 소비자가 이 파일이므로 삭제 후 고아 필드가 됨 (컴파일은 됨).

---

## 4. ShieldbearerUniqueManager.cs — 방패병 유니크 6종

**상태: 완전 inert.** 게이트가 전부 리터럴 `false`:
- `:33-34` `hasPatience = false`, `hasOverwhelm = false` → `:36` `if (!hasPatience && !hasOverwhelm) return;` → `Update()` 매 프레임 즉시 return. 오오라/VFX 전부 도달 불가.
- `:154-157` `hasSturdy/hasCourage/hasWind/hasClash = false` → `:159` `if (!hasSturdy && !hasCourage && !hasWind && !hasClash) return;` → `HandleRoomEnter` 는 방 진입 이벤트를 받아도 아무것도 안 함.

### 4-1. ⚠️ `IsWill*Active` 3종은 **영구 false** — 증명

`CharacterStat.cs:80/132/209` 가 읽는 `ShieldbearerUniqueManager.IsWillClashActive / IsWillCourageActive / IsWillWindActive` 는 `{ get; private set; }` 자동 프로퍼티 (`:145-147`).

**세터가 존재하는 위치는 전 코드베이스 통틀어 `RoomBuffCoroutine` 내부 6줄뿐** (`:195-197` 대입, `:201-203` false 복귀). grep(`Assets/**/*.cs`) 결과 이 파일 밖에서의 대입 **0건**, `private set` 이라 외부 대입 불가능.

그리고 `RoomBuffCoroutine` 을 시작하는 유일한 지점은 `:186-190`:
```csharp
if (hasCourage || hasWind || hasClash)   // :186
{
    StopCoroutine("RoomBuffCoroutine");
    StartCoroutine(RoomBuffCoroutine(hasCourage, hasWind, hasClash));  // :189
}
```
`hasCourage/hasWind/hasClash` 는 `:155-157` 에서 **리터럴 `false` 로 대입되고 그 사이에 재대입이 없음**. 게다가 `:159` 에서 이미 return 되므로 `:186` 에는 도달조차 못 함.

→ **코루틴 실행 불가 → 세터 호출 0회 → 세 프로퍼티는 bool 기본값 `false` 로 영구 고정.**
→ `CharacterStat.cs:80/132/209` 의 `if (...)` 는 **항상 거짓**, 곱수는 항상 `1f` / 나눗수는 항상 `1f`.
→ **이 3개 곱수 제거는 순수 no-op. 밸런스 영향 0.** (Q.E.D.)

### 4-2. 원래 스펙 (재구축용)
- **SturdyShield (든든한 방패, =120)** `:178-182`: 방 진입 시 아군 방패병에게 `AddShield(maxHP * 0.5f, 9999f)` — 지속시간 무한이 없어서 9999초로 대체. 에셋 desc "Grants the Shieldbearer a shield equal to 50% of Max HP upon entering (a room)."
- **AuraOfPatience (인내의 오오라, =126)** `:96-115`: 5초 주기(`_updateInterval = 5.0f`, `:11`), 반경 **2.5f** (`:90`). 방패병 주변 아군 중 `TotalShield < shieldMaxHP * 0.18f` 인 대상에게 차액만큼 `AddShield(amountToAdd, 5.0f)` (5초 지속). "보호막은 방패병 체력의 18%를 초과 불가" 를 차액 보정으로 단순 구현.
- **AuraOfOverwhelming (압도, =127)** `:118-141`: 5초 주기, 반경 2.5f. 주변 적에게 `damageAmount = shieldMaxHP * 0.04f` 를 `DamageType.Fixed` 로 `health.GetDamage(...)`, 총 가한 피해의 **120%** (`totalDamageDealt * 1.2f`) 를 방패병이 `Heal`.
- **ShieldsWill 3종** (방 진입 후 **10초** 지속, `:199` `WaitForSeconds(10f)`) — 실제 수치는 이 파일이 아니라 `CharacterStat` 에 박혀 있음:
  - **Clash (격돌, =123)** → `CharacterStat.cs:79-80`: 아군 ATK `allyWillClashMult += 0.08f` → `ATK` 최종식 `... * allyWillClashMult` (`:88`). 에셋 desc: "Increases all Shieldbearer stats by 10%. Increases ally attack **damage by 8%** for 10 seconds upon entering a room."
  - **Courage (용기, =121)** → `CharacterStat.cs:131-132`: 아군 `allyWillCourageDivisor = 1f / 1.12f` → `ATKSPD`(공격 주기) 식 `baseAtkSpd * allyWillCourageDivisor * ...` (`:145`) — 주기가 짧아지므로 **공속 +12%**. 에셋 desc: "...ally attack **speed by 12%** for 10 seconds..."
  - **Wind (바람, =122)** → `CharacterStat.cs:208-209`: 아군 `allyWillWindMult += 0.14f` → `MOVESPEED` 식 `... * allyWillWindMult` (`:217`) — **이속 +14%**.
  - 에셋 3종 공통 앞문장 "Increases all Shieldbearer stats by 10%" 는 **어디에도 구현 안 됨** (`DeprecatedStatGems.txt:7-9` 에 `mult += 0.1f` 로 백업만 남음).
- 방패병 식별: `CharacterStatus.ActiveAllies` 순회 + `stat.jobType == CommandData.SkeletonShieldbearer` (`:46`, `:165`).
- VFX: `AuraVFXPrefab` (인스펙터, 미할당), fallback 은 스프라이트 없는 `SpriteRenderer` + `Color(1f, 0.8f, 0.2f, 0.3f)` → 실제로 안 보임 (`:68-72`). `localScale = Vector3.one * 5.0f` (`:78`).
- 함께 살아있는 관련 로직(**이 파일 아님, 삭제 대상 아님**): `CharacterStat.cs:35-56 ShieldbearerSelfMult` — `jobType == SkeletonShieldbearer && Status.TotalShield > 0 && GemSynergyLogic.GetLevel(GetSynergyCount(GemSynergyGroup.Shield_Guardian)) >= 4` 이면 `mult += 0.15f`. 이건 시너지 경로라 별도 판단 필요.

**외부 호출부:** `InventoryManager.cs:156-157` (AddComponent), `CharacterStat.cs:80`, `:132`, `:209` (전부 영구 false 분기).

---

## 5. PlayerUniqueEffectManager.cs — **⚠️ 유일하게 살아있는 파일**

게이트가 진짜 `HasUniqueEffect` 호출이고 (`:49`, `:68`), 해당 유니크의 **live 에셋이 `SOData/Rewards/` 아래 실존**하며 `GemHandlerRegistry.cs:69/77` 에 `EmptyGemHandler` 로 등록까지 되어 있음. → **삭제하면 실제 밸런스가 바뀜.**

### 5-1. JustThrowIt (일단 던지고 보자, =250)
- 상수 (`:9-11`): `JustThrowItDuration = 8f`, `MaxJustThrowItStacks = 5`, `SpeedBonusPerStack = 0.08f` (8%).
- 획득 (`OnParabolicThrow`, `:46-59`): 포물선 투척 시마다 `_justThrowItStacks = Mathf.Min(stacks + 1, 5)`, 타이머 `8f` 로 **리프레시**. 5스택 상한.
- 만료 (`:30-38`): 타이머 0 이하 → 스택 **전체 0으로 리셋** (감소 아님).
- 소비 (`JustThrowItSpeedBonus`, `:13-20`): `_justThrowItStacks * 0.08f * gemCount` — 보유 젬 **개수만큼 배수 증폭**.
- 적용 지점: `ThrowController.cs:418`, `:505` — `flightTimeBonus += uem.JustThrowItSpeedBonus;` 이후 `duration *= (1f - Mathf.Clamp(flightTimeBonus, 0f, 0.9f))` (`:421`, `:509`). 즉 **비행시간 단축** (최대 90% 컷). 젬 1개·5스택 = 40% 단축.
- 에셋: `SOData/Rewards/Gems/Shotput/Gem_Shotput_JustThrowIt.asset` — `uniqueType: 250`, `rarity: 3`, `shopCost: 320`, `category: 0`, `synergyGroup: 600`, `subSlots: 1`, `eligibleJobs: -1`. desc(ko): "이번 방에서 포물선 던질 때마다 8초동안 투척 속도가 8% 빨라지며 해당 효과는 5번까지 중첩됩니다."
  - **기록**: desc 의 "이번 방에서" 는 **미구현** (방 단위 리셋 로직 없음).

### 5-2. MobMentality (군중심리, =263)
- 상수 (`:62-63`): `MobMentalityCheckInterval = 0.2f`.
- 계산 (`UpdateMobMentality`, `:66-94`): 0.2초마다 `Physics2D.OverlapCircleAll(transform.position, PlayerController.THROWRANGE, Layers.PlayerArmy)` → `col.gameObject.layer == Layers.Army` 인 것만 카운트 → **`MobMentalitySpeedBonus = minionCount * 0.1f * gemCount`** (`:92`). 젬 미보유 시 0으로 초기화 (`:70`).
- 적용 지점: `CharacterStat.cs:202-206` — `if (_isPlayer) { var uem = GetComponentInParent<PlayerUniqueEffectManager>(); if (uem != null) finalSpeed += uem.MobMentalitySpeedBonus; }` → **가산(additive) 이동속도**. 이후 `ShieldbearerSelfMult * allyWillWindMult * moveSpdMult` 곱해짐 (`:217`).
- 에셋: `SOData/Rewards/Gems/BigHand/Gem_BigHand_MobMentality.asset` — `uniqueType: 263`, `rarity: 3`, `shopCost: 320`, `synergyGroup: 700`, `subSlots: 1`, `eligibleJobs: -1`. desc(ko): "소환수 집기 범위 내 소환수가 많을 경우 1명당 플레이어 이동속도 0.1증가".

### 5-3. PoisonPuddle (같은 파일 `:97-153`) — **도달 불가 사망 코드**
- 파일명이 `PlayerUniqueEffectManager.cs` 라 Unity 규칙상 이 클래스는 **컴포넌트로 부착 불가**, 코드상 `AddComponent<PoisonPuddle>()` 도 **0건**. 프리팹/씬 참조도 없음. 이미 완전히 죽어 있음.
- 스펙(기록만): `TICK_INTERVAL = 3.0f`. `OnTriggerEnter2D` 로 `stat.IsEnemy` 인 대상 등록 + 즉시 `AddDebuffStack(DebuffStackType.BloodPop, 1f)`; 3초마다 장판 내 잔류 대상 전원에게 `BloodPop 1f` 추가; `OnTriggerExit2D` 로 해제.

### 5-4. 프리팹 부착
스크립트 GUID `8fecc708d442b824b9a5abc8bc6dbf99` 가 **`Assets/Prefabs/Player.prefab`, `Assets/Prefabs/Player Melee.prefab`** 에 부착돼 있음. (`PlayerController.cs:194-195` 는 "인스펙터에서 안 달아뒀을 경우 보험" 이라 주석까지 달려 있음.) → 파일 삭제 시 두 프리팹에 **Missing Script** 컴포넌트가 남음. 반드시 수동 제거 필요.

---

## 6. ActiveGemHandlers.cs — `SynergyDamageAmplifier`

**상태: inert 아님. 실제로 적에게 ×1.08 을 걸고 있음.** (삭제 = 밸런스 변경)

- 등록: `GemHandlerRegistry.cs:41` `SynergyDamageAmplifier.Initialize();` — "항상 켜져 있음" 주석. `Initialize()` 가 `DamageEventBus.OnBeforeDamageCalculated += HandleDamageAmplification` (`ActiveGemHandlers.cs:9`) — **해제(-=) 코드 없음** (핸들러가 `IGemEffectHandler` 를 구현하지 않음).
- 로직 (`:12-34`): 대상의 `CharacterStat`/`CharacterStatus` 필요. `isEnemyTarget = stat.IsEnemy`.
  1. `if (status.GetDebuffBool(DebuffBoolType.Corroded))` → `remainingDamage *= (1.0f + GemRuleSystem.GetCorrosionDamageAmp(isEnemyTarget))`
  2. `if (status.GetDebuffBool(DebuffBoolType.Corroded))` **(동일 조건 — 버그, 원래는 노쇠/Senility 조건이어야 함)** → `remainingDamage *= (1.0f + GemRuleSystem.GetSenilityDamageAmp(isEnemyTarget))`
  3. `info.amount = remainingDamage;`
- **현재 실효 수치 계산**:
  - `GemRuleSystem.cs:185-200 GetCorrosionDamageAmp` → `level` 이 `:188` 에서 **리터럴 0** 으로 고정 → `GemSynergyLogic.cs:41-46 GetCorrosionDamageAmp(0) == 0f`, `doubleCorrosionCount` 도 `:193` 리터럴 0 → **amp = 0f → ×1.0 (무효)**
  - `GemRuleSystem.cs:164-169 GetSenilityDamageAmp` → `level` 리터럴 0 → `GemSynergyLogic.cs:38 GetSenilityDamageAmp(int level) => (level >= 3) ? 0.12f : 0.08f` → **0.08f 반환 → ×1.08 실적용**
  - 두 함수 모두 `isEnemyTarget == false` 면 0 반환 → **아군/플레이어에겐 무효**.
  - **결론: 현재 살아있는 유일한 효과 = "Corroded(부식) 상태의 적은 받는 피해 +8%".** 이건 시너지 레벨이 0으로 박혀서 나온 우발적 상수이며, 원래 의도는 부식 시너지 레벨 1/2 → +25%/+40%, 노쇠 시너지 레벨 3+ → +12% 였음.

**외부 호출부:** `GemHandlerRegistry.cs:41` 단 1곳.

---

# (b) 삭제 순서 — 컴파일 유지 편집 리스트

호출부를 먼저 전부 걷어낸 뒤 파일을 지운다. 그래야 중간 상태에서도 컴파일이 깨지지 않는다.

### STEP 1 — `Assets/Scripts/Entities/CharacterStat.cs` (4곳)

1. **`:79-80`** 삭제:
   ```csharp
   float allyWillClashMult = 1f;
   if (_isAlly && ShieldbearerUniqueManager.IsWillClashActive) allyWillClashMult += 0.08f;
   ```
   **`:88`** 에서 `* allyWillClashMult` 제거 →
   `return (baseAtk * (1f + bonusMult) * agingMult * corrosionMult * atkMult) * ShieldbearerSelfMult;`
   *(근거: `IsWillClashActive` 영구 false → 곱수는 항상 1f. **순수 no-op**.)*

2. **`:131-132`** 삭제:
   ```csharp
   float allyWillCourageDivisor = 1f;
   if (_isAlly && ShieldbearerUniqueManager.IsWillCourageActive) allyWillCourageDivisor = 1f / 1.12f;
   ```
   **`:145`** 에서 `allyWillCourageDivisor *` 제거 →
   `return (baseAtkSpd * selfMultDivisor * speedDivisor / (1f + bonusMult)) / chillMult;`
   *(근거: 영구 false → 나눗수 항상 1f. **순수 no-op**.)*

3. **`:202-206`** 블록 통째 삭제:
   ```csharp
   if (_isPlayer)
   {
       var uem = GetComponentInParent<PlayerUniqueEffectManager>();
       if (uem != null) finalSpeed += uem.MobMentalitySpeedBonus;
   }
   ```
   *(대체값 없음 — 가산항이므로 그냥 제거. **주의: 이건 실효 효과 제거임** (MobMentality 젬 삭제와 세트).)*

4. **`:208-209`** 삭제:
   ```csharp
   float allyWillWindMult = 1f;
   if (_isAlly && ShieldbearerUniqueManager.IsWillWindActive) allyWillWindMult += 0.14f;
   ```
   **`:217`** 에서 `* allyWillWindMult` 제거 →
   `return finalSpeed * ShieldbearerSelfMult * moveSpdMult;`
   *(근거: 영구 false → 항상 1f. **순수 no-op**.)*

> 부수 확인: `_isAlly` 는 다른 곳에서도 쓰이므로 필드 유지. `ShieldbearerSelfMult` 는 시너지 경로라 이번 편집 대상 아님.

### STEP 2 — `Assets/Scripts/Player/Throw related/ThrowController.cs` (3곳, 뒤에서부터 지우면 라인 밀림 없음)

3-1. **`:538-544`** 블록 통째 삭제:
```csharp
if (!isDirect)
{
    if (GameManager.Instance.PLAYERCONTROLLER.TryGetComponent<PlayerUniqueEffectManager>(out var uem))
    {
        uem.OnParabolicThrow();
    }
}
```

3-2. **`:502-506`** 삭제 (주석 포함), `flightTimeBonus` 는 `:500` 의 젬 보너스만 남김:
```csharp
// [일단 던지고 보자] 버프 스택 적용 (PlayerUniqueEffectManager에서 받아옴)
if (GameManager.Instance.PLAYERCONTROLLER.TryGetComponent<PlayerUniqueEffectManager>(out var uem))
{
    flightTimeBonus += uem.JustThrowItSpeedBonus;
}
```
→ `:509` `duration *= (1f - Mathf.Clamp(flightTimeBonus, 0f, 0.9f));` 는 **그대로 유지** (`GetAggregatedGemBonus(...ParabolicFlightTimeMultiplier)` 경로는 살아있음).

3-3. **`:416-420`** 삭제:
```csharp
if (GameManager.Instance.PLAYERCONTROLLER.TryGetComponent<PlayerUniqueEffectManager>(out var uem))
{
    flightTimeBonus += uem.JustThrowItSpeedBonus;
    uem.OnParabolicThrow();
}
```
→ `:415` 의 `float flightTimeBonus = InventoryManager.Instance.GetAggregatedGemBonus(...)` 와 `:421` 의 `duration *= ...` 는 유지.

> `ThrowController.cs:551` 의 `SwiftRelocation` 은 **다른 유니크**라 이번 삭제와 무관 (건드리지 말 것).

### STEP 3 — `Assets/Scripts/Player/PlayerController.cs`

**`:193-195`** 삭제:
```csharp
// [유니크] 유니크 효과 전담 매니저 추가 (만약 인스펙터에서 안 달아뒀을 경우를 대비한 보험)
if (GetComponent<PlayerUniqueEffectManager>() == null)
    gameObject.AddComponent<PlayerUniqueEffectManager>();
```
*(`:197-199` `PlayerParryController` 블록은 유지.)*

### STEP 4 — `Assets/Scripts/Systems/Growth/InventoryManager.cs`

**`:143-157`** (주석 포함 4블록 전부) 삭제:
```csharp
// [유니크] 중독 전역 유니크(PoisonHost 등) 매니저 부착
if (GetComponent<PoisonUniqueManager>() == null) gameObject.AddComponent<PoisonUniqueManager>();
// [유니크] 한기 광역 유니크 (AbsoluteZero, BitingWind 등) 매니저 부착
if (GetComponent<ChillUniqueManager>() == null) gameObject.AddComponent<ChillUniqueManager>();
// [유니크] 기력/노화 광역 매니저 (Goryeojang) 매니저 부착
if (GetComponent<AgingUniqueManager>() == null) gameObject.AddComponent<AgingUniqueManager>();
// [유니크] 방패병 고유 매니저 (ShieldbearerUniqueManager) 부착
if (GetComponent<ShieldbearerUniqueManager>() == null) gameObject.AddComponent<ShieldbearerUniqueManager>();
```
→ `Initialize()` 는 `:141 InitializeGemTree();` 다음 바로 `:159 Debug.Log(...)` 로 이어짐. **AddComponent 4개는 전부 inert 매니저 부착이므로 제거는 no-op** (Poison/Chill/Aging/Shieldbearer 모두 게이트가 하드 false).

### STEP 5 — `Assets/Scripts/Systems/Growth/Rules/GemHandlerRegistry.cs`

**`:40-41`** 삭제:
```csharp
// 공통 시너지 증폭 핸들러 초기화 (항상 켜져 있음)
SynergyDamageAmplifier.Initialize();
```
→ **⚠️ 순수 no-op 아님**: "Corroded 적 피해 ×1.08" 이 사라짐. 의도된 삭제라면 OK. 유지하고 싶으면 이 2줄만 남기고 `ActiveGemHandlers.cs` 삭제를 보류할 것.
*(`:44-45 StaminaSynergyHandler/FastballSynergyHandler.Initialize()` 및 `:48-81` 팩토리 등록은 그대로 유지.)*

### STEP 6 — 파일 삭제 (`.cs` + `.cs.meta` 쌍으로)

```
Assets/Scripts/Deprecated/PoisonUniqueManager.cs(.meta)
Assets/Scripts/Deprecated/ChillUniqueManager.cs(.meta)
Assets/Scripts/Deprecated/AgingUniqueManager.cs(.meta)
Assets/Scripts/Deprecated/ShieldbearerUniqueManager.cs(.meta)
Assets/Scripts/Deprecated/PlayerUniqueEffectManager.cs(.meta)   ← PoisonPuddle 동반 삭제
Assets/Scripts/Deprecated/ActiveGemHandlers.cs(.meta)           ← STEP 5 결정에 따름
```

### STEP 7 — 프리팹 정리 (컴파일은 안 깨지지만 필수)

`Assets/Prefabs/Player.prefab` 및 `Assets/Prefabs/Player Melee.prefab` 에서 `PlayerUniqueEffectManager` 컴포넌트(script guid `8fecc708d442b824b9a5abc8bc6dbf99`) 제거. 안 하면 두 프리팹에 Missing Script 경고가 영구히 남음.

### STEP 8 — 선택적 잔재 (컴파일 무관, 나중에 치워도 됨)
- `ThrowEffectRegistrySO.cs:42 public GameObject goryeojangAuraPrefab;` — 소비자 소멸 → 고아 필드.
- `GemRuleSystem.cs:176-179 GetGoryeojangSlowReduction()` — 호출부 없음.
- `GemHandlerRegistry.cs:69 (JustThrowIt)`, `:77 (MobMentality)` 의 `EmptyGemHandler` 등록 — 효과 실체가 사라져도 등록 자체는 무해 (`RefreshActiveHandlers` 에서 미등록 타입이면 경고 로그가 뜨므로 **에셋을 남길 거면 등록도 남기는 게 맞음**).
- 유니크 에셋 자체 (`Gem_Shotput_JustThrowIt.asset`, `Gem_BigHand_MobMentality.asset`) — 남기면 "효과 없는 젬" 이 되고, 지우면 보상 풀에서 빠짐. 젬 구조 보존 방침이므로 **에셋은 남기고 효과만 제거** 하는 쪽이 일관됨.

---

## 부록 — 검증한 grep 범위

`Assets/**/*.cs` 전수에 대해 `PoisonUniqueManager|ChillUniqueManager|AgingUniqueManager|ShieldbearerUniqueManager|PlayerUniqueEffectManager|PoisonPuddle|SynergyDamageAmplifier|HighestAgingEnemy|IsWillCourageActive|IsWillWindActive|IsWillClashActive|JustThrowItSpeedBonus|MobMentalitySpeedBonus|OnParabolicThrow|TriggerAbsoluteZero` 검색. `Scripts/Deprecated/` 밖의 히트는 위 STEP 1~5 의 **12개 지점이 전부**이며, 질문에 주어진 알려진 목록(InventoryManager 144-157 / PlayerController 199 / CharacterStat 80·132·204·209 / ThrowController 416·503·540) 외 **추가로 발견된 것은 `GemHandlerRegistry.cs:41` 1곳**. (`PlayerController.cs:199` 는 실제로는 `:194-195`, `:199` 는 `PlayerParryController` 로 무관.) `.prefab/.unity/.asset` 에 대한 script-GUID 검색 히트는 `PlayerUniqueEffectManager` → `Player.prefab`, `Player Melee.prefab` 2건뿐.

---

# 【GemRuleSystem 분해 — 라이브 디버프 테이블 보존】

# GemRuleSystem.cs 하베스트 기록 (삭제 전 스펙 보존)

대상: `Assets/Scripts/Systems/Growth/Rules/GemRuleSystem.cs` (203줄, 전체 정독 완료)

결론 요약: 이름과 달리 **보석 규칙은 이 파일에 하나도 살아있지 않습니다.** 모든 보석 질의는 `int level = 0;` / `bool x = false;` / `int count = 0;` 로 스텁 처리되어 있고, 실제로 살아있는 것은 **한기/노화 구간별 감속 테이블 + 노쇠 피해 증폭 상수** 뿐입니다. 17개 public 메서드 중 **호출자가 있는 것은 5개**, 그중 **값이 실제로 0이 아닌 것은 3개**입니다.

**`.asset` 의존성 없음.** 이 파일은 `InventoryManager.Instance`(런타임)와 하드코딩 상수만 읽습니다. 저작된(authored) YAML 값이 이 파일의 수치에 들어오는 경로는 존재하지 않습니다 — 아래 수치가 전부 컴파일타임 상수입니다.

---

## 1. 메서드별 판정표

| # | 메서드 | 호출자 | 스텁된 보석 질의 | 스텁 하 실제 반환 | 판정 |
|---|---|---|---|---|---|
| 1 | `GetPoisonInterval` (`:12`) | 없음 | `int level = 0; // GemSynergyLogic.GetLevel(Inven.GetSynergyCount(GemSynergyGroup.Poison))` (`:17`)<br>`bool hasLethalDose = false; // Inven.HasUniqueEffect(GemUniqueType.LethalDose)` (`:18`) | `5.0f * 1.0f * 1.0f` = **5.0f 고정** | **DEAD** (호출자 0) |
| 2 | `GetPoisonDuration` (`:26`) | 없음 | `int level = 0; // ...GemSynergyGroup.Poison` (`:31`) | `10.0f + 0f` = **10.0f 고정** | **DEAD** |
| 3 | `ModifyIncomingPoisonStack` (`:35`) | 없음 | `int level = 0; // ...GemSynergyGroup.Poison` (`:38`) | `amount + 0f` = **항등함수** | **DEAD** |
| 4 | `GetLethalPoisonBonus` (`:42`) | `ThrowImpactManager.cs:140` | `bool hasLethalPoison = false; // Inven.HasUniqueEffect(GemUniqueType.LethalPoison)` (`:45`) | `GetLethalPoisonBonus(false, n)` = **항상 0f** | **LIVE 호출 / 값은 INERT** |
| 5 | `GetChillSlowReduction` (`:54`) | `CharacterStat.cs:118`, `CharacterStat.cs:189` | `int level = 0; // ...GemSynergyGroup.Priest_Chill` (`:59`) → `bonus = GetChillSlowBonus(0)` = **0f** | 구간 테이블 그대로 | **LIVE — 반드시 보존** |
| 6 | `GetMaxChillStacks` (`:71`) | 없음 | `int flowerCount = 0; // Inven.GetUniqueEffectCount(GemUniqueType.SlowlyFreezingFlower)` (`:76`) | `(적15/아군5) + 0f` | **DEAD** |
| 7 | `GetChillFreezeDamagePercentage` (`:80`) | 없음 | 없음 (순수 기본 테이블) | 보스 `0.04f` / 일반 `0.08f` | **DEAD** (기본 테이블이지만 호출자 0) |
| 8 | `ShouldBlockChill` (`:86`) | 없음 | `return false; // GemUniqueLogic.ShouldBlockChillStack(Inven.HasUniqueEffect(GemUniqueType.AchingBones), isFrozen)` (`:89`) | **항상 false** | **DEAD** |
| 9 | `GetFreezeRefundStacks` (`:92`) | 없음 | `int level = 0; // ...Priest_Chill` (`:95`) | `GetChillRefundAmount(0)` = **0f** | **DEAD** |
| 10 | `HasFreezeFixedDamage` (`:99`) | 없음 | `int level = 0; // ...Priest_Chill` (`:102`) | `HasChillFreezeDamage(0)` = **false** | **DEAD** |
| 11 | `GetBloodPopDamage` (`:110`) | 없음 | `int level = 0; // ...GemSynergyGroup.BloodPop` (`:114`) | `currentStacks * 0.4f` | **DEAD** |
| 12 | `GetBloodPopRadiusMultiplier` (`:119`) | 없음 | `int level = 0; // ...BloodPop` (`:122`) | **1.0f 고정** | **DEAD** |
| 13 | `GetBloodPopChainRatio` (`:126`) | 없음 | `return 0f; // GemUniqueLogic.GetExplodingFleshStackRatio(Inven.HasUniqueEffect(GemUniqueType.ExplodingFlesh))` (`:129`) | **항상 0f** | **DEAD** |
| 14 | `GetAgingSlowReduction` (`:137`) | `CharacterStat.cs:70`, `CharacterStat.cs:190` | `int level = 0; // ...GemSynergyGroup.Priest_Aging` (`:141`) → `bonus = GetAgingSlowBonus(0)` = **0f** | 구간 테이블 그대로 | **LIVE — 반드시 보존** |
| 15 | `GetMaxAgingStacks` (`:155`) | 없음 | `int noCountryCount = 0; // Inven.GetUniqueEffectCount(GemUniqueType.NoCountryForOldMen)` (`:160`) | 아군 `10f` / 적 `GetNoCountryMaxStack(0)` = **25f** | **DEAD** |
| 16 | `GetSenilityDamageAmp` (`:164`) | `ActiveGemHandlers.cs:29` | `int level = 0; // ...Priest_Aging` (`:167`) | `GetSenilityDamageAmp(0)` = **0.08f** ⚠️ **0이 아님** | **LIVE — 값 살아있음** |
| 17 | `ShouldAgingInstaKill` (`:171`) | 없음 | 질의조차 없음, 본문이 `return false;` (`:173`) | **항상 false** | **DEAD (완전 스텁)** |
| 18 | `GetGoryeojangSlowReduction` (`:176`) | 없음 | 없음 (순수 상수) | **0.20f** | **DEAD** |
| 19 | `GetCorrosionDamageAmp` (`:185`) | `ActiveGemHandlers.cs:23` | `int level = 0; // ...GemSynergyGroup.Priest_Corrosion` (`:188`)<br>`int doubleCorrosionCount = 0; // Inven.GetUniqueEffectCount(GemUniqueType.DoubleCorrosion)` (`:193`) | `GetCorrosionDamageAmp(0)` = **0f**, `count=0`이라 `+0.10f*n` 분기 미진입 → **항상 0f** | **LIVE 호출 / 값은 INERT** |

---

## 2. 반드시 살아남아야 하는 기본 테이블 (원문 그대로)

### 2-1. 한기(Chill) 구간별 감속 — `GemRuleSystem.cs:62-67`

| 스택 | 감속 |
|---|---|
| `>= 76` | `0.25f` |
| `>= 51` | `0.20f` |
| `>= 26` | `0.10f` |
| `>= 1` | `0.05f` |
| `<= 0` | `0f` |

- 보석 보너스(제거됨): `GemSynergyLogic.GetChillSlowBonus(level)` = `(level >= 2) ? 0.05f : 0f` — **4세트: 각 구간마다 감속량 +5%**. `level=0`이므로 현재 기여 `0f`.
- **주의:** 호출부는 `DebuffStackType.Chill`이 아니라 **`DebuffStackType.Fracture`(골절) 스택**을 넘깁니다 (`CharacterStat.cs:118`, `:189`). 이름/enum이 레거시로 어긋나 있음.

### 2-2. 노화(Aging) 구간별 감속 — `GemRuleSystem.cs:144-150`

| 스택 | 감속 |
|---|---|
| `>= 101` | `0.25f` |
| `>= 81` | `0.20f` |
| `>= 61` | `0.16f` |
| `>= 41` | `0.12f` |
| `>= 21` | `0.08f` |
| `>= 1` | `0.04f` |
| `<= 0` | `0f` |

- 보석 보너스(제거됨): `GemSynergyLogic.GetAgingSlowBonus(level)` = `(level >= 2) ? 0.05f : 0f`. `level=0` → `0f`.
- **주의:** 호출부는 **`DebuffStackType.Corrosion`(부식) 스택**을 넘깁니다 (`CharacterStat.cs:70`, `:190`). 역시 레거시 미스매치.

### 2-3. 노쇠(Senility) 피해 증폭 — `GemRuleSystem.cs:164-169`

- `GemSynergyLogic.GetSenilityDamageAmp(level)` = `(level >= 3) ? 0.12f : 0.08f` (`GemSynergyLogic.cs:38`)
- `level=0` → **`0.08f` 반환. 이것만 유일하게 값이 살아있는 보석 경유 수치입니다.**
- 인라인 상수: **`0.08f`** (적 대상일 때만, 아니면 `0f`)

### 2-4. 호출자가 없어 파일에서는 빠지지만 기록해두는 기본값 (재구축용)

| 항목 | 값 | 출처 |
|---|---|---|
| 독 기본 틱 주기 | `5.0f` (기획 반영) | `:14` |
| 독 기본 지속시간 | `10.0f` | `:28` |
| 한기 최대 스택 | 적 `15.0f` / 아군 `5.0f` | `:73` |
| 동결 고정피해 비율 | 보스 `0.04f` / 일반 `0.08f` | `:82` |
| 비폭 기본 데미지 배율 | `0.4f` | `GemSynergyLogic.cs:31` |
| 노화 최대 스택 | 아군 `10f` / 적 `25f` | `:157-161` |
| 고려장 둔화 | `0.20f` | `:178` |

---

## 3. 삭제로 인해 함께 죽는 것 (중요)

- **`GemUniqueLogic.cs` 는 `GemRuleSystem.cs` 에서만 호출됩니다.** (grep 전수: `GemUniqueLogic.` 히트가 `GemRuleSystem.cs:21/46/77/89/129/161` 6곳 전부) → **GemRuleSystem 삭제 시 `GemUniqueLogic.cs`도 통째로 고아가 되므로 같이 삭제 가능.**
  - 참고로 `GemUniqueLogic.cs:25` 에는 작성 중 남은 흔적 주석(`// Wait, wait. "Aging max stacks +100".`)이 그대로 있습니다. 어차피 죽는 코드.
- **`GemSynergyLogic.cs` 는 살아남습니다.** 외부 라이브 호출자 존재: `AllyController.cs:181`, `CharacterHealth.cs:298`, `CharacterStat.cs:47`, `PriestAction.cs:31/36/41`, `FastballGemHandlers.cs:29`, `StaminaGemHandlers.cs:33`, `GemSynergyDisplayUI.cs:34`. (본 작업 범위 밖)

---

## 4. ⚠️ 인라인 시 드러나는 실제 버그 2건 (기록 필수)

### (a) `ActiveGemHandlers.cs:21-31` — 같은 bool을 두 번 검사
```csharp
if (status.GetDebuffBool(DebuffBoolType.Corroded))   // :21  → 부식 증폭, amp = 0f (무효)
if (status.GetDebuffBool(DebuffBoolType.Corroded))   // :27  → 노쇠 증폭, amp = 0.08f (유효!)
```
`DebuffBoolType` enum에는 **`Senile`/`Aging` 항목이 아예 없습니다** (`CommandData.cs:33-42`: `Stunned, Bleeding, Wounded, Corroded, Fractured, Feared, Hitstunned`). 즉 두 번째 분기는 노쇠 상태를 검사할 방법이 없어 `Corroded`로 대체된 잔재입니다.

**현재 실측 동작: 부식(Corroded) 상태의 적은 받는 피해가 `× 1.08` 됩니다.** (부식 증폭 0% + 노쇠 증폭 8%)

이 경로가 실제로 켜지는지 확인 완료: `InventoryManager.Initialize()` (`InventoryManager.cs:135` `Instance = this`) → `InitializeGemTree()` (`:141`) → `GemHandlerRegistry.InitializeAllHandlers()` (`:184`) → `SynergyDamageAmplifier.Initialize()` (`GemHandlerRegistry.cs:41`) → `DamageEventBus.OnBeforeDamageCalculated` 구독 (`ActiveGemHandlers.cs:9`). `InventoryManager`는 `Prefabs/GameManager.prefab`에 상주.

### (b) `ThrowImpactManager.cs:138-144` — 영구 무효 블록
```csharp
int current = status.GetDebuffStack(DebuffStackType.BloodPop);
float bonus = GemRuleSystem.GetLethalPoisonBonus(current);  // 항상 0f
if (bonus > 0) { ... }                                      // 절대 진입 안 함
```
`hasLethalPoison = false` 하드코딩(`GemRuleSystem.cs:45`) 때문에 **완전 무효**. 덤으로 변수명은 "독(Poison)"인데 실제로는 `DebuffStackType.BloodPop`(비폭) 스택을 읽고 있음 — 이중 레거시.

---

## 5. 인라인 상수 결정 (동작 보존 근거)

| 메서드 | 제거되는 보석 항 | 인라인 결과 |
|---|---|---|
| `GetChillSlowReduction` | `bonus = GetChillSlowBonus(0)` = `0f` | `baseReduction + 0f` → 구간 테이블 그대로 |
| `GetAgingSlowReduction` | `bonus = GetAgingSlowBonus(0)` = `0f` | `baseReduction + 0f` → 구간 테이블 그대로 |
| `GetSenilityDamageAmp` | `GetSenilityDamageAmp(0)` = `0.08f` | **상수 `0.08f`** |
| `GetCorrosionDamageAmp` | `GetCorrosionDamageAmp(0)` = `0f`, `doubleCorrosionCount = 0` | **상수 `0f`** → 메서드+호출부 분기 삭제 |
| `GetLethalPoisonBonus` | `GetLethalPoisonBonus(false, n)` = `0f` | **상수 `0f`** → 메서드+호출부 블록 삭제 |

**`Inven == null` 가드에 대하여:** 원본은 `InventoryManager.Instance == null`이면 한기/노화 감속을 `0f`로 반환합니다. 이 가드는 오직 보석 질의를 위해 존재했으므로 제거합니다. **유일한 동작 델타:** `GameManager` 프리팹이 없는 씬(테스트 씬 등)에서 이제 한기/노화 감속이 **무효(0) 대신 정상 적용**됩니다. 정규 게임플레이 씬에서는 `Instance`가 항상 채워지므로 델타 없음. 사실상 버그 수정이지만, 원치 않으면 각 메서드 첫 줄에 `if (InventoryManager.Instance == null) return 0f;`를 되살리면 100% 동일해집니다.

---

## 6. 최종 파일: `Assets/Scripts/Systems/Growth/Rules/DebuffRuleSystem.cs`

```csharp
/// <summary>
/// 스택형 디버프(한기/노화)의 기본 수치 테이블을 제공합니다.
/// (구 GemRuleSystem: 보석 질의는 전부 스텁 상태였고 제거됨. 기본 테이블만 남았습니다.)
/// </summary>
public static class DebuffRuleSystem
{
    #region Chill Rules

    /// <summary>
    /// 한기 스택 구간별 감속 비율.
    /// [주의] 호출부는 DebuffStackType.Fracture(골절) 스택을 넘깁니다. (레거시 네이밍 미스매치)
    /// </summary>
    public static float GetChillSlowReduction(int currentStacks, bool isEnemyTarget)
    {
        if (!isEnemyTarget || currentStacks <= 0) return 0f;

        if (currentStacks >= 76) return 0.25f;
        if (currentStacks >= 51) return 0.20f;
        if (currentStacks >= 26) return 0.10f;
        return 0.05f; // 1 ~ 25
    }

    #endregion

    #region Aging Rules

    /// <summary>
    /// 노화 스택 구간별 감속 비율.
    /// [주의] 호출부는 DebuffStackType.Corrosion(부식) 스택을 넘깁니다. (레거시 네이밍 미스매치)
    /// </summary>
    public static float GetAgingSlowReduction(int currentStacks, bool isEnemyTarget)
    {
        if (!isEnemyTarget || currentStacks <= 0) return 0f;

        if (currentStacks >= 101) return 0.25f;
        if (currentStacks >= 81) return 0.20f;
        if (currentStacks >= 61) return 0.16f;
        if (currentStacks >= 41) return 0.12f;
        if (currentStacks >= 21) return 0.08f;
        return 0.04f; // 1 ~ 20
    }

    /// <summary>
    /// 노쇠 상태일 때 받는 피해 증폭량. (구 Priest_Aging 시너지 미적용 시의 기본값 0.08f)
    /// </summary>
    public static float GetSenilityDamageAmp(bool isEnemyTarget) => isEnemyTarget ? 0.08f : 0f;

    #endregion
}
```

`using UnityEngine;` 제거됨 — `Mathf`/`Debug` 미사용.

---

## 7. 리네임에 따른 호출부 편집 목록

### 7-1. 단순 치환 (`GemRuleSystem.` → `DebuffRuleSystem.`)

| 파일:줄 | 변경 |
|---|---|
| `Assets/Scripts/Entities/CharacterStat.cs:70` | `GemRuleSystem.GetAgingSlowReduction(...)` → `DebuffRuleSystem.GetAgingSlowReduction(...)` |
| `Assets/Scripts/Entities/CharacterStat.cs:118` | `GemRuleSystem.GetChillSlowReduction(...)` → `DebuffRuleSystem.GetChillSlowReduction(...)` |
| `Assets/Scripts/Entities/CharacterStat.cs:189` | `GemRuleSystem.GetChillSlowReduction(...)` → `DebuffRuleSystem.GetChillSlowReduction(...)` |
| `Assets/Scripts/Entities/CharacterStat.cs:190` | `GemRuleSystem.GetAgingSlowReduction(...)` → `DebuffRuleSystem.GetAgingSlowReduction(...)` |
| `Assets/Scripts/Deprecated/ActiveGemHandlers.cs:29` | `GemRuleSystem.GetSenilityDamageAmp(isEnemyTarget)` → `DebuffRuleSystem.GetSenilityDamageAmp(isEnemyTarget)` |

### 7-2. 블록 삭제 (INERT 확정)

**`Assets/Scripts/Deprecated/ActiveGemHandlers.cs:21-25`** — 통째로 삭제 (`amp`가 항상 `0f` → `remainingDamage *= 1.0f`, 무의미):
```csharp
        if (status.GetDebuffBool(DebuffBoolType.Corroded))
        {
            float corrosionAmp = GemRuleSystem.GetCorrosionDamageAmp(isEnemyTarget);
            remainingDamage *= (1.0f + corrosionAmp);
        }
```

**`Assets/Scripts/Managers/ThrowImpactManager.cs:138-144`** — 통째로 삭제 (`bonus`가 항상 `0f` → `if (bonus > 0)` 진입 불가):
```csharp
                    // [특수] 치명적인 독: 현재 부여된 독 스택을 배로 올려줌 (GemRuleSystem에서 보너스량 계산)
                    int current = status.GetDebuffStack(DebuffStackType.BloodPop);
                    float bonus = GemRuleSystem.GetLethalPoisonBonus(current);
                    if (bonus > 0)
                    {
                        status.AddDebuffStack(DebuffStackType.BloodPop, bonus);
                    }
```

### 7-3. 파일 삭제

| 경로 | 사유 |
|---|---|
| `Assets/Scripts/Systems/Growth/Rules/GemRuleSystem.cs` (+ `.meta`) | `DebuffRuleSystem.cs`로 대체 |
| `Assets/Scripts/Systems/Growth/Rules/GemUniqueLogic.cs` (+ `.meta`) | GemRuleSystem 삭제 후 호출자 0 (전수 확인 완료) |

`Assets/Scripts/Systems/Growth/Rules/GemSynergyLogic.cs` 는 **삭제 금지** — 외부 라이브 호출자 8곳 잔존 (§3 참조).

---

## 8. 남는 판단 사항 (부모 에이전트 결정용)

`GetSenilityDamageAmp`는 "살아있는 유일한 보석 경유 수치(0.08f)"이지만, 그 호출부(`ActiveGemHandlers.cs:27`)가 **노쇠가 아닌 부식(Corroded)을 검사하는 잔재**입니다. 두 선택지:

- **(A) 위 파일대로 유지** — 부식 적 `× 1.08` 현행 밸런스 그대로 보존. 안전. (본 문서의 기본 제안)
- **(B) `GetSenilityDamageAmp` + `ActiveGemHandlers.cs:27-31` 도 함께 삭제** — 그러면 `SynergyDamageAmplifier` 전체가 no-op이 되어 `ActiveGemHandlers.cs` / `GemHandlerRegistry.cs:41` 까지 정리 가능. 단 **부식 적 피해가 8% 감소하는 실제 밸런스 변경**이며, 노쇠를 나중에 재구축할 때 `DebuffBoolType`에 `Senile` 항목 추가가 선행되어야 함.

skipped: 커브 검증 테스트 — Unity 테스트 인프라가 이 폴더에 없고, 구간 테이블은 §2에 상수로 박제됨. 추가할 때: `DebuffBoolType.Senile`를 넣어 (B)를 실행하는 시점.

---

# 【상점 젬 풀 / 융합(Fusion) 시스템】

# 젬 효과 제거 — SHOP / FUSION 하베스트 기록

## 1. SHOP: 젬 풀 제거

### 1-1. 현재 구조

**`Assets/Scripts/SOData/Define/Registry/ShopRegistrySO.cs`** (전체 49줄)
- `:11` — `public List<GemSO> gemPool = new List<GemSO>();` (헤더: "상점에 등장할 보석 목록")
- `:14` — `public List<MinionDataSO> minionPool = new List<MinionDataSO>();`
- `:17-47` — `#if UNITY_EDITOR` 컨텍스트 메뉴 `RefreshRegistry()`. `:24-32`에서 `t:GemSO`를 전부 긁어와 `gemPool`에 채움 (`/Deprecated/` 경로만 제외). `:35-41`에서 `t:MinionDataSO`를 `minionPool`에 채움.

**`Assets/Scripts/Systems/Growth/Logic/RewardProcessor.cs` `GenerateShopRoom` (:153-210)**
- `:156` — `var shopRegistry = data.SHOP_REGISTRY;` (`DataManager.cs:28`)
- `:163-177` — 미니언 풀 → `RewardCandidate { displayData = BuildMinionDisplayData(minion), rawData = minion, techIndex = 0, category = RewardCategory.Minion, goldAmount = minion.shopCost }`
- `:179-194` — **젬 풀** → `RewardCandidate { displayData = gem.GetDynamicDisplayData(CommandData.SkeletonWarrior), rawData = gem, category = RewardCategory.Gem, targetJob = CommandData.SkeletonWarrior, goldAmount = gem.shopCost }`
- `:197-207` — 5개 랜덤 추출. **`:205`의 `// combinedPool.RemoveAt(idx);`가 주석 처리되어 있어 현재 복원추출(중복 허용)** — 같은 젬/미니언이 한 상점에 여러 번 뜰 수 있음. 젬을 빼면 풀이 미니언 15개로 줄어들 뿐, 중복 동작 자체는 변하지 않음.

**소비 경로**: `ShopNPC.Initialize()` (`Assets/Scripts/NPC/Shop/InDungeon/ShopNPC.cs:38`) → `prizes[i]`를 `SellItem.item`에 주입 (`:40-47`) → `SellItem.Interact()` (`Assets/Scripts/NPC/Shop/SellItem.cs:31-46`)가 `SpendGold(item.goldAmount)` 후 `RewardManager.Instance.ApplyReward(item)` → `RewardManager.cs:173-176`의 `case RewardCategory.Gem: inven.AddGemToAvailable((GemSO)candidate.rawData, candidate.targetJob);`

### 1-2. 정확한 수정 (미니언 풀 유지)

**`RewardProcessor.cs`: `:179-194` 블록 통째 삭제.** 이 블록만 지우면 `combinedPool`은 미니언만 남고 `:197-207`의 추출 루프는 그대로 동작. 그 외 `GenerateShopRoom` 손댈 곳 없음.

**`ShopRegistrySO.cs`**: `:11`의 `gemPool` 필드와 `:20`의 `gemPool.Clear()`, `:23-32`의 젬 검색 루프, `:46` Debug.Log의 `상점 보석({gemPool.Count})` 부분 삭제. (필드를 남겨두면 `.asset`의 31개 참조가 계속 살아있어 젬 에셋이 "사용 중"으로 보임 — 실제로 끊으려면 필드째 삭제.)

> 주의: `RewardProcessor.cs`의 젬 경로는 상점 말고도 `:114`(`GenerateCandidatesByCategory`의 `case RewardCategory.Gem`), `:295-338`(`GetValidGems`), `:363`(`GenerateMixedCandidates`)에 있음. 다만 이들은 **`registry.gems`를 읽는데, `SOData/Registry/Growth Reward Registry.asset`의 `gems: []`가 비어 있어 현재 이미 무효(inert)** — 보상방/엘리트 상자(`EliteRewardBox.cs:25`가 `RewardCategory.Gem` 포함)에서 젬은 하나도 안 나옴. **즉 젬을 얻는 유일한 살아있는 경로는 상점의 `gemPool` 뿐.**

### 1-3. 젬 에셋 31개의 참조처 — 확인 완료

`SOData/Rewards/Gems/**` 의 모든 `.asset` GUID를 `.asset`/`.prefab`/`.unity`/`.cs` 전체에 대해 역참조 검색한 결과:

| 에셋 | 참조하는 곳 |
|---|---|
| `Default_Root_Gem` | `Prefabs/GameManager.prefab`, `SOData/Registry/Shop Registry.asset` |
| 나머지 30개 (`Gem_BigHand_*` 8, `Gem_Fastball_*` 6, `Gem_Shotput_*` 6, `Gem_Stamina_*` 10) | **`SOData/Registry/Shop Registry.asset` 단 하나** |

**답변: 예. `Shop Registry.asset`의 `gemPool`이 30개 젬 에셋의 유일한 참조처입니다.** `gemPool`을 제거하면 이 30개는 어떤 코드/씬/프리팹에서도 도달 불가능해집니다 (에셋 자체는 그대로 디스크에 남음 — 의도된 결과).

**단, `Default_Root_Gem`은 예외 — 삭제하면 안 됨.** `Prefabs/GameManager.prefab`이 이걸 젬 트리 루트로 참조합니다 (`InventoryManager.cs:180-188`, `GemTreeRoot = new GemTreeNode(rootInstance)`). 그리고 현재 `gemPool` 31개 항목 안에 `Default_Root_Gem`도 들어있음 (`RefreshRegistry`가 `t:GemSO`를 무조건 다 긁기 때문) — **즉 지금 상점에서 루트 젬을 살 수 있는 상태**. `gemPool` 삭제로 이 버그도 같이 사라짐.

---

## 2. FUSION: ThrowCluster 융합 블록

### 2-1. 게이트 (현재 살아있음)

`Assets/Scripts/Player/Throw related/ThrowCluster.cs` `OnLanded()` 내부:
- `:379` — `if (_units.Count >= 5 && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.Golemizing))` → `performGolemFusion = true`
- `:384` — `else if (_units.Count >= 2 && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.TwinFusion))` → `performTwinFusion = true`
- `:390` — `int fusionCount = performGolemFusion ? 5 : (performTwinFusion ? 2 : 0);`

`HasUniqueEffect` → `InventoryManager.cs:442-445` → `GetUniqueEffectCount(type) > 0` → `_globalGemStats.UniqueEffectCounts`, 이는 `RecalculateGemTreeStats()` (`:196-269`)가 `node.Gem.BaseData.effects`의 `effect.Apply(_globalGemStats)` (`:248-249`)로 채움.

**현재 상태: inert 아님, 살아있음.** 관련 젬 에셋이 정상 authoring 되어 있음:

| 에셋 | uniqueType | rarity | shopCost | synergyGroup | subSlots | eligibleJobs |
|---|---|---|---|---|---|---|
| `SOData/Rewards/Gems/BigHand/Gem_BigHand_Golemizing.asset` | `267` (`Golemizing`) | 3 | 320 | 700 (`BigHand`) | 0 | -1 (`All`) |
| `SOData/Rewards/Gems/BigHand/Gem_BigHand_TwinFusion.asset` | `262` (`TwinFusion`) | 3 | 320 | 700 (`BigHand`) | 1 | -1 (`All`) |

enum 정의: `Scripts/Systems/Growth/Data/GemSO.cs:126` (`TwinFusion = 262`), `:131` (`Golemizing = 267`).
`GemHandlerRegistry.cs:76`/`:81`에 `EmptyGemHandler`로 등록되어 있으나 이는 no-op — **실제 융합 로직은 ThrowCluster에 인라인**되어 있으므로 핸들러는 무관.

두 젬 모두 `Shop Registry.asset`의 `gemPool`에만 존재 → **gemPool 제거 = 융합 영구 도달 불가.**

### 2-2. 삭제할 정확한 라인 범위 (`ThrowCluster.cs`)

**주의: `:370-428`의 `if (_activeRecipe != null && _activeRecipe.state.isMaster)` 블록 전체를 지우면 안 됨.** 이 블록은 `unit.transform.SetParent(null)` / `ApplyThrowCost()` / `SetImpacted()` / `OnLanded()` 같은 유닛 착지 필수 처리를 담당함 (`isMaster`는 잔상/에코 레시피 구분용 — `ThrowRecipe.cs:71`, `ThrowBouncingAbilitySO.cs:68`, `ThrowRepeatAbilitySO.cs:48`에서 `false`로 세팅).

삭제할 조각 (아래→위 순으로 지울 것):

| 범위 | 내용 |
|---|---|
| `:412-427` | `// 융합 실행` 주석 + `if (fusionCount > 0 && fusedUnits.Count == fusionCount) { ... }` 전체 (`Instantiate` → `AddComponent<FusionMinionController>()` → `Setup(...)`) |
| `:404-408` | `if (i < fusionCount) { fusedUnits.Add(unit); }` (앞의 빈 줄 포함) |
| `:372-390` | `List<IThrowable> fusedUnits`, `bool performTwinFusion`, `bool performGolemFusion` 선언 + `if (InventoryManager.Instance != null) { ...379... ...384... }` + `int fusionCount = ...` |

삭제 후 `:370-431`은 다음만 남음:
```csharp
if (_activeRecipe != null && _activeRecipe.state.isMaster)
{
    for (int i = 0; i < _units.Count; i++)
    {
        var unit = _units[i];
        if (unit == null || (unit is MonoBehaviour mb && mb == null)) continue;

        unit.transform.SetParent(null);
        unit.ApplyThrowCost();

        if (unit != null && (unit is MonoBehaviour aliveMb && aliveMb != null))
        {
            unit.SetImpacted(isImpactSuccess);
            unit.OnLanded();
        }
    }
}
_units.Clear();
Destroy(gameObject);
```

### 2-3. 무의미해지는 IsFused 가드 3곳 (`ThrowController.cs`)

`FusionMinionController`가 붙는 유일한 지점이 `ThrowCluster.cs:419`이므로, 융합 블록 삭제 시 씬에 `FusionMinionController` 컴포넌트가 존재할 수 없음 → 아래 3개는 항상 false, 완전 vacuous:

- `:205` — `if (hovered.TryGetComponent<FusionMinionController>(out var fusion) && fusion.IsFused) return;` (`TryPickUpWithMouse`)
- `:258` — `if (throwable is MonoBehaviour mb && mb.TryGetComponent<FusionMinionController>(out var fusion) && fusion.IsFused) continue;` (`TryAutoPickUpNearbyThrowable`)
- `:306` — 같은 형태 (`TryPickUpByType`)

`:258`/`:306`은 `if` 한 줄 + 위 주석(`// 융합체 등 집을 수 없는 조건 확인` / `// [융합 방지] 융합체는 집을 수 없음`)만 지우면 되고, 감싸는 루프는 유지.

### 2-4. `FusionMinionController.cs` 삭제 가능 여부

**가능. 통째로 삭제해도 됨.** 전체 참조처 (Assets 전역 grep):
- `ThrowCluster.cs:419` — `AddComponent<FusionMinionController>()` (2-2에서 삭제)
- `ThrowController.cs:205 / :258 / :306` — `IsFused` 가드 (2-3에서 삭제)
- **프리팹/씬에 붙어있는 인스턴스 없음** (런타임 `AddComponent`만 사용). `.prefab`/`.unity` 검색 결과 0건.

`Assets/Scripts/Entities/FusionMinionController.cs`(+`.meta`) 삭제 시 함께 죽는 코드 (`FusionMinionController`가 유일한 사용처):
- `Assets/Scripts/Entities/CharacterStat.cs:332` — `public bool IsFusion { get; set; } = false;` (읽는 곳은 `:338`의 `if (data != null && !IsFusion)` 하나뿐 → 삭제하면 `if (data != null)`로 단순화)
- `Assets/Scripts/Entities/CharacterStat.cs:374-382` — `public void OverrideBaseStats(float newMaxHP, float newAtk)` (주석에 "융합체용"이라 명시)

---

## 3. 재구축용 스펙 기록 (지워지기 전 숫자 원본)

### 3-1. 융합 파라미터 (`ThrowCluster.cs:390`, `:421-425`)

| | Golemizing | TwinFusion |
|---|---|---|
| 발동 조건 | `_units.Count >= 5` + `HasUniqueEffect(GemUniqueType.Golemizing)` | `_units.Count >= 2` + `HasUniqueEffect(GemUniqueType.TwinFusion)` (Golem이 우선, `else if`) |
| `fusionCount` (앞에서부터 합칠 인원) | `5` | `2` |
| `scaleMult` (`:421`) | `2.5f` | `1.5f` |
| `fusionColor` (`:422`) | `Color.red` | `Color.blue` |
| `popupName` (`:423`) | `"Golem!"` | `"Twin!"` |

호출: `fusionController.Setup(fusedUnits, 10f, 1f, scaleMult, fusionColor, popupName);` (`:425`) — **duration = `10f`초, hpRatio = `1f`, 둘 다 하드코딩** (골렘/쌍둥이 공통). `popupName`은 `Setup` 시그니처에 있지만 **`FusionMinionController.Setup` 본문에서 한 번도 쓰이지 않음 — 죽은 파라미터**. `hpRatio` 파라미터도 마찬가지로 **본문 미사용 — 죽은 파라미터** (`:11`의 시그니처에만 존재).

융합체 생성 방식 (`:418`): `Instantiate(fusedUnits[0]의 gameObject, transform.position, Quaternion.identity)` — **첫 번째 유닛을 복제**해서 그 복제본에 `FusionMinionController`를 `AddComponent`. 원본 재료들은 `Setup` 안에서 `SetActive(false)`.

### 3-2. `FusionMinionController.Setup` 스탯 합산 (`FusionMinionController.cs:11-84`)

- `:19-22, :34-37` — 재료 전원 순회하며 합산: `totalBaseMaxHP += stat.BaseMaxHP`, `totalBaseAtk += stat.BaseAtk`, `totalMaxHP += stat.MAXHP`, `totalCurHP += (stat.Health != null && !stat.Health.IsDead) ? stat.Health.CurHP : stat.MAXHP`
- `:38` — `_materials.Add(stat.Status)` (`CharacterStatus` 보관)
- `:41` — `mb.gameObject.SetActive(false)` — 재료 비활성화
- `:46` — `_stat.IsFusion = true` → 이후 `CharacterStat.InitializeStats`가 SO 수치로 덮어쓰는 것을 차단 (`CharacterStat.cs:338`)
- `:47` — `_stat.OverrideBaseStats(totalBaseMaxHP, totalBaseAtk)` → **쓰는 필드: `CharacterStat.baseMaxHP`, `CharacterStat.baseAtk`**, 그리고 `Health.ResetHP()` (`CharacterStat.cs:376-381`)
- `:52-60` — **체력 비율 보존**: `targetCurHP = _stat.MAXHP * (totalCurHP / totalMaxHP)`; 부족분은 `_stat.Health.GetDamage(new DamageInfo(_stat.MAXHP - targetCurHP, DamageType.Fixed, null))`로 깎음
- `:63-71` — 색상: `_stat.Visual.SetBaseColor(color)`, Visual 없으면 `GetComponentsInChildren<SpriteRenderer>()` 전부 `r.color = color`
- `:74` — `transform.localScale = Vector3.one * scaleMultiplier`
- `:79-83` — `_stat.Health.OnDeath += HandleDeath` (중복 방지 위해 `-=` 선행)

**주의(재구축 시 함정): 합산된 `totalBaseMaxHP`/`totalBaseAtk`에는 복제 원본(`fusedUnits[0]`)의 스탯도 포함됨.** 즉 복제된 골렘 본체 = 재료 5명 스탯 합 (본인 포함). 의도된 동작.

### 3-3. 해제(Defuse) 로직 (`:86-170`)

**트리거 2가지**:
1. `Update()` `:96-102` — `_timer -= Time.deltaTime`, `_timer <= 0f` → `Defuse()` (타이머 = `Setup`의 duration = `10f`)
2. `HandleDeath()` `:86-90` — `Health.OnDeath` → `Defuse()`
3. (fallback) `OnDestroy()` `:158-170` — `_isFused`가 아직 true면 `Defuse()`

**반환 체력 공식 (`:111-114`)** — 이게 핵심 숫자:
```
curHPRatio    = _stat.Health.CurHP / _stat.MAXHP   // MAXHP<=0 이면 0
returnHPRatio = 0.5f + (curHPRatio * 0.5f)          // 범위 [0.5, 1.0]
```
즉 **골렘이 만신창이여도 재료는 최소 50% 체력으로 복귀, 무피해면 100%.**

**재료 복귀 (`:118-147`)**:
- `rootObj = material.transform.root.gameObject`
- `rootObj.transform.position = transform.position + (Vector3)Random.insideUnitCircle * 0.5f` — **`SetActive(true)` 전에 위치 이동** (`:124-125`의 주석: NavMeshAgent가 활성 상태면 position 변경이 무시되므로)
- `rootObj.SetActive(true)`
- 체력 설정: `targetHP = Mathf.Max(1f, stat.MAXHP * returnHPRatio)`; `stat.Health.CurHP > targetHP`일 때만 `GetDamage(new DamageInfo(stat.Health.CurHP - targetHP, DamageType.Fixed, null))` — **깎기만 함, 회복은 안 함**
- `:149` `_materials.Clear()`
- `:152-155` — `!_stat.Health.IsDead`일 때만 `Destroy(gameObject)` (이미 죽어서 Destroy 예약된 경우 중복 파괴 방지)

**집기 방지**: 레이어/태그는 그대로 두고 (`:76` 주석: 아군 인식 유지 목적) `public bool IsFused => _isFused` (`:173`) 플래그로만 차단 → `ThrowController.cs:205/:258/:306`.

### 3-4. 참고: 같은 파일에서 죽는 게 아닌 것 (혼동 주의)

`ThrowController.cs:551-560`의 `SwiftRelocation` (3명 이상 투척 시 `status.ApplySpeedBuff("SwiftRelocation", 0.5f, 5.0f)` — 이동속도 +50%, 5초)와 `:25-30`의 `GetSynergyCount(GemSynergyGroup.BigHand)` 기반 `MaxHoldCount` 보너스 (5개 이상 → +3, 3개 이상 → +1)는 **융합과 별개의 젬 효과**. 이번 융합 블록 삭제 범위 밖이지만 젬 효과 일괄 제거 대상이므로 별도 기록 필요.