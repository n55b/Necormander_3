# THROW_LEGACY — 투척 시스템 제거 기록

> **이 문서의 목적**: 2026-07-17 투척 시스템을 **통째로** 걷어내면서, 나중에 어떤 형태로든
> 되살릴 수 있도록 스펙을 박제한 기록.
> 제거 직전 커밋: `97d8341` (Phase 5). 삭제는 Phase 7에서 수행.
> **젬과 달리 이번엔 통로도 남기지 않았습니다** — 구조째 삭제입니다. 되살리려면 이 문서와
> 위 커밋의 소스를 같이 봐야 합니다.

## 0. 왜 지웠나

기획의 조작 레이아웃에 **투척이 없습니다**. 현재 확정 레이아웃은
`WASD 이동 / 좌클릭 평타 / Shift 대쉬 / Q·E 플레이어 스킬 / R 메인 소환수`뿐입니다.

사용자 판단: *"투척을 이번에 통째로 들어내고 주석으로 남긴다 — 헷갈리지 않도록 이렇게 하는 게
좋을 거 같은데? (…) 어차피 던지기 기믹 관련된 젬과 보스 로직은 전부 다 수정 예정이거든?
걍 다 배제해버려도 괜찮아 컴파일 오류만 안 나게끔."*

## 0-1. ⚠️ 브리핑 정정 — "이미 죽어 있었나?"

작업 초기에 저(AI)는 **"투척은 비활성이 아니라 살아 있다"**고 잘못 보고했습니다. 근거는
`ThrowInputHandler` 의 `[NEW LOGIC]` 이 `FireDamageCluster()` → `CreateRecipe()` 를 여전히
부르고 있다는 호출 그래프였습니다.

**틀렸습니다.** 프리팹 배선을 안 봤기 때문입니다. `Player Melee.prefab:1401,1412` 에서:

```yaml
m_ActionName: 'Player/Throw[/Mouse/leftButton]'
m_MethodName: OnAttackInput      # ← MeleeCombatController.OnAttackInput (평타)
```

즉 **`Throw` 액션(좌클릭)은 평타로 배선돼 있었고**, `PlayerController.OnThrow` /
`ThrowController.OnThrow` 는 **프리팹에 아예 안 붙어 있어 도달 불가**였습니다.
코드 경로는 멀쩡한데 입력이 영원히 안 들어오는 상태 — 죽은 코드가 맞았습니다.

> **교훈**: Unity 에서 "이 코드가 살아 있나"는 호출 그래프만으로 판정할 수 없습니다.
> UnityEvent 배선(프리팹 YAML)까지 봐야 합니다.

액션 이름이 `Throw` 인데 평타를 하던 상태였으므로, 철거하면서 **`Throw` → `Attack`** 으로
리네임하고 프리팹 배선도 갱신했습니다.

## 1. 삭제한 것 — 스크립트 (50 파일)

**폴더째:**
- `Assets/Scripts/Player/Throw related/` — `ThrowController`, `ThrowInputHandler`,
  `ThrowStrategy`, `ThrowPhysics`, `ThrowCluster`, `PoisonPotionThrowable`,
  `ImpactActions/` (`ImpactAction`, `BaseDamageAction`, `ArcherAction`, `MagicianAction`,
  `PriestAction`, `ShieldBearerAction`, `SpearmanAction`, `WarriorAction`)
- `Assets/Scripts/UI/ThrowInformation/` — `Panel_ThrowInformation`, `ImageThrown`

**개별:**
| 파일 | 역할 |
|---|---|
| `Interfaces/IThrowable.cs` | 던질 수 있는 것의 계약 |
| `Define/ThrowRecipe.cs`, `ThrowParams.cs` | 투척 1회의 레시피(모디파이어 묶음) |
| `Managers/ThrowImpactManager.cs` | 착탄 처리 진입점 |
| `Managers/ThrowEffectPoolManager.cs` | 투척 VFX 풀 |
| `Entities/ThrowableUnit.cs` | 미니언을 집어 던지는 래퍼 |
| `Entities/Objects/ThrowableBoneSpear.cs` | 본 마스터 레이드 전용 뼈창 |
| `Object/ThrowableBox.cs` | 궁수 보스가 떨구던 상자 |
| `Systems/Growth/Rules/ThrowEventBus.cs` | ☠ **구독자 0** — 죽은 버스였음 |
| `Systems/Growth/Data/ThrowAbilitySO.cs` + `Data/Abilities/Throw*AbilitySO.cs` ×7 | 투척 능력(Bouncing/HeavyStrike/Juggling/Pierce/Pinball/Repeat/TimingCharge) |
| `Systems/Growth/Logic/ThrowAbilityStateManager.cs` | 투척 능력 상태 |
| `SOData/Define/Registry/ThrowEffectRegistrySO.cs` | 투척 VFX 레지스트리 |
| `SOData/Define/MouseCursorSO/ThrowCursorSO.cs` | 투척 커서 |
| `UI/ThrowChargeBarUI.cs` | 차징 바 |
| `TrajectoryPredictor.cs` | ⚙ 궤적 미리보기 — `ThrowController` 차징 상태를 읽는 게 존재 이유의 전부였음 |
| `Deprecated/DeprecatedThrowGems.txt` | 이미 죽어 있던 텍스트 |

## 2. 삭제한 것 — 에셋/프리팹 (28개)

- `SOData/Rewards/Throw Ability/` (능력 7종)
- `SOData/Rewards/Treasure/Throw Effect Treasure.asset`
- `SOData/Registry/Throw Effect Registry.asset`
- `SOData/MouseCursor/ThrowCursorSO.asset`
- `SOData/State/ThrownState.asset`
- `Prefabs/Throwable Objects/`, `Prefabs/Skill Visual Effects/Throw Stuff/`
- `Prefabs/UI/Player State/Panel_ThrowInformation.prefab`
- **투척 젬 6종**: `Gem_Stamina_{EfficientThrow, OverflowingThrow, ThrowOverload}`,
  `Gem_Shotput_{JustThrowIt, Protractor, EfficientCurve}`

> `Protractor`(`statType: 5`)와 `EfficientCurve`(`statType: 6`)는 투척 스탯을 직렬화하던
> 유이한 에셋이라 그 스탯과 함께 삭제했습니다. 자세한 사연은 4장.

## 3. 딸려 죽은 것 — 투척이 아닌데 투척에 얹혀 있던 것들

이번 철거에서 제일 성가셨던 부분입니다.

| 대상 | 무슨 일이 있었나 | 처리 |
|---|---|---|
| **`ShieldCollectible`** | 투척과 무관한 기능인데 획득 VFX 를 `THROW_EFFECT_REGISTRY.shieldAttachVFX` 에서 빌려 씀 | `[SerializeField] pickupVFX` 자체 참조로 전환. **프리팹에서 VFX 를 다시 꽂아줘야 함** ⚠ |
| **`PlayerStamina`** | 투척이 **유일한 소비자**였음 | 클래스는 남김(Q/E 가 쓸 수 있게). `GetThrowCost`/`CanThrow`/`ConsumeStamina` 삭제, `ConsumeRawStamina` 는 남김. 💀 **지금은 소비처가 없는 자원** |
| **`AIState.Caught` / `Thrown`** | '유닛을 집어 들고 던진다'는 투척 전용 상태. AI 파일 7곳이 조기 반환 가드로 참조 | enum 값째 삭제. 가드도 전부 제거. `OutCaught` 훅도 삭제 |
| **`ThrowableBoneSpear`** | 본 마스터 페이즈2 의 '창 훔치기' 패턴이 `FindObjectsByType<ThrowableBoneSpear>` 로 창을 찾음 | 💀 **패턴 삭제**. 플레이어가 창을 던질 수 없으니 맵에 창이 안 생겨 조건이 영원히 불성립. `pattern1Cooldown` 필드는 남겨둠 — 새 패턴을 그 자리에 넣으면 됨 |
| **궁수 보스 상자 기믹** | `throwHitCount >= throwHitsRequired` 로 페이즈 전환. `isThrowDamage` 로 카운트 | 💀 **삭제**. 페이즈 전환은 `loopDuration >= attackSpeedRampTime` 조건만으로 계속 동작 |
| **`RewardCategory.Ability`** | 투척 능력 전용 보상 카테고리였음 | enum 값째 삭제. `GetValidAbilities`, 보상 UI 분기, 엘리트 보상 상자 풀에서 제거 |
| **`SaveData.equippedThrowAbilityName`** | 세이브 포맷에 박혀 있음 | ⚠ **필드만 남김**. 지우면 기존 세이브가 깨짐. 읽는 쪽도 쓰는 쪽도 없는 죽은 필드 |
| **`InventoryManager.CoreSlot.EquippedThrowAbility`** | 슬롯이 '소환수 or 투척능력' 둘 중 하나를 담는 구조였음 | 삭제. 슬롯은 이제 **소환수 전용** |
| **`Layers.ThrowableObject`** | 레이어 + 마스크. `MouseManager` 가 마우스 탐색에 씀 | 삭제. `CursorType.Throw` 도 함께 |

## 4. ⚠️ 이 작업 중에 발견하고 고친 실제 버그

### StatType 인덱스 밀림 (Phase 1 회귀)

Phase 1 에서 `StatType.RespawnTime`(암묵적 인덱스 3)을 지웠더니 **뒤 항목이 전부 하나씩
밀렸습니다.** enum 이 암묵적 번호였기 때문입니다:

```
[변경 전] Attack=0 Health=1 AttackSpeed=2 RespawnTime=3 ThrowEffect=4 Parabolic…=5 ParabolicFlight…=6
[변경 후] Attack=0 Health=1 AttackSpeed=2 ThrowEffect=3 Parabolic…=4 ParabolicFlight…=5
```

에셋은 이 숫자를 그대로 직렬화합니다. `Gem_Shotput_Protractor: statType: 5` 와
`Gem_Shotput_EfficientCurve: statType: 6` 이 **조용히 다른 스탯을 가리키게 됐습니다.**
컴파일도 되고 에러도 없이 틀린 값이 되는 부류입니다.

**조치**: Phase 2 에서 `StatType` 에 **명시적 숫자**를 박아 원래 매핑을 복원했습니다.
3/4/5/6 은 **영구 결번**입니다:

```csharp
public enum StatType
{
    Attack = 0, Health = 1, AttackSpeed = 2,
    // 3 = 구 RespawnTime (삭제됨 — 재사용 금지)
    // 4, 5, 6 = 구 투척 스탯 (삭제됨 — 재사용 금지)
    Magic = 10, Defense = 11, /* ... */
}
```

> **재건 시 주의**: 투척 스탯을 되살릴 때 4/5/6 을 재사용하지 마세요. 새 번호를 쓰세요.

### DamageInfo 생성자 인자 자리 (지뢰 회피)

`DamageInfo.isThrowDamage` 를 지우면서 **생성자 인자를 빼지 않고 이름만 바꿨습니다**:

```csharp
public DamageInfo(float amount, DamageType type = ..., GameObject attacker = null,
                  bool _unusedWasThrow = false,   // ← 자리만 유지
                  float debuffMultiplier = 1f, bool isBasicAttack = false, ...)
```

호출부가 50곳이 넘고 **전부 위치 인자**로 넘기고 있어서, 인자를 빼면 뒤 값들이 통째로 한 칸씩
밀립니다(예: `debuffMultiplier` 자리에 `isBasicAttack` 이 들어감). 컴파일은 통과하고 값만
틀어지는 최악의 부류라 자리를 남겼습니다.

> **정리하려면**: 모든 호출부를 named argument 로 바꾼 뒤 인자를 빼세요. 한 번에 해야 합니다.

## 5. 살아남은 죽은 잔재 (의도적)

지우면 다른 게 깨지거나, 지울 가치보다 위험이 큰 것들:

| 대상 | 왜 남겼나 |
|---|---|
| `SaveData.equippedThrowAbilityName` | 지우면 기존 세이브 깨짐. 세이브 포맷 갈아엎을 때 같이 |
| `GemUniqueType` 의 투척 유니크 (`OverflowingThrow=204`, `ThrowOverload=206`, `EfficientThrow=209`, `JustThrowIt=250` 등) | 젬 효과는 이미 전부 죽어 있음(GEM_LEGACY.md). 값째 지우면 또 인덱스 사고. 죽은 시스템의 죽은 라벨 |
| `GemSynergyDisplayUI` 의 영문 fallback 문자열 | 위와 같음. 젬 재작업 때 통째로 갈릴 자리 |
| `PlayerStamina` | 소비처가 없지만 Q/E 가 쓸 수 있음 (사용자 판단) |
| `Player.prefab` 의 `SummonButton` 배선 4개 | 레거시 프리팹(사용자: *"이 Player Prefab은 무시해"*). `Player Melee` 가 현역 |

## 6. 입력 정리

| 액션 | 변경 |
|---|---|
| `Throw` → **`Attack`** | 좌클릭. 이름만 Throw 였고 실제로는 평타를 부르고 있었음 |
| `SummonButton1~4` | 💀 삭제. `m_MethodName` 이 없어 **받는 쪽이 0개**인 죽은 액션이었음 (숫자 1~4 가 비어 있었기에 상태이상 디버그 키를 여기 붙일 수 있었음) |

최종 레이아웃 — 기획서와 일치:

```
WASD    이동          leftButton  평타          rightButton  패리
Shift   대쉬          Q / E       플레이어 스킬   R            메인 소환수
```

## 7. 재건 체크리스트

되살린다면:

1. **입력**: `Attack` 액션과 충돌하지 않는 키를 고를 것. 좌클릭은 이제 평타 전용
2. **`IThrowable`** 계약부터 복구 (`MinionType`/`MaxSpeed`/`OnPickedUp`/`OnThrown`/`OnLanded`)
3. **`ThrowRecipe`** — 모디파이어 묶음. `debuffStacks` 딕셔너리는 구 디버프 타입에 물려 있었으므로 신규 `StatusType` 으로 다시 설계할 것
4. **`AIState`** 에 `Caught`/`Thrown` 재추가 + AI 7곳 가드 복구
5. **스태미나**: `ConsumeRawStamina` 는 남아 있음. 투척 비용 계산만 다시 얹으면 됨
6. **`StatType`**: 새 번호 사용 (4/5/6 결번)
7. **`ShieldCollectible.pickupVFX`**: 레지스트리를 되살린다면 여기 참조도 되돌릴지 판단

## 부록 — 검증한 grep 범위

```
ThrowController ThrowStrategy ThrowRecipe IThrowable ThrowAbilitySO
ThrowEffectRegistrySO ThrowImpactManager ThrowEventBus ThrowableBoneSpear
ThrowableUnit ThrowableBox ThrowCursorSO TrajectoryPredictor ThrowChargeBarUI
Panel_ThrowInformation ImpactAction ThrowParams ThrowAbilityStateManager
ThrowEffectPoolManager ImageThrown isThrowDamage AIState.Thrown AIState.Caught
RewardCategory.Ability EquippedThrowAbility ActiveAbilities baseThrowDamage
ThrowableObject CursorType.Throw GlobalThrowEffect ParabolicEffectMultiplier
```

범위: `Assets/Scripts`, `Assets/Editor`, `Assets/SOData`, `Assets/Prefabs`,
`Assets/PlayerInputSystem.inputactions`. **최종 CS 에러 0.**
