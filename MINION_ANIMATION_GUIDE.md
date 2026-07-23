# 소환수 애니메이션 설정 설명서

> 소환수(메인/서브)의 공격 애니메이션을 어떻게 연결하는지에 대한 문서.
> 코드는 이미 아래 모든 선택지를 받게 돼 있다. **결정과 에셋 값만 정하면 된다.**
>
> 적(enemy) 공격 애니메이션의 규약은 `BaseEntity.cs` 의 `[애니메이션 작업자 가이드라인]` 참조.
> 소환수도 **같은 이벤트 이름**(`OnHitEvent` / `OnAttackEndEvent`)을 쓴다.

---

## 0. 한눈에: 지금 상태

| 소환수 | 마무리(finisher) | 스페이스바(active) | 상태 |
|---|---|---|---|
| MeleeDoll | 태그 방식 (Slash 동안 판정) | 태그 방식 | ✅ 동작 |
| DashDoll | 이벤트 방식으로 지정됨, **아직 이벤트 미저작 → 태그로 폴백 중** | 동일 | ⚠️ 타이밍만 부정확 |
| Lion / Mask | 서브라 실체화 안 함 — 애니메이션 없음 | — | — |

**DashDoll 만 애니메이터 작업이 남았다.** 나머지는 그림 타이밍만 취향껏 조절하면 된다(→ 4장).

---

## 1. 결정해야 할 것 3가지

### (A) 타격 타이밍을 어떻게 알릴까 — 이벤트 vs 태그

두 방법이 있고 **이벤트가 있으면 이벤트가 이긴다.** 클립에 이벤트가 실제로 박혀 있는지
코드가 검사해서(`MinionSkillCaster.HasEvent`), 없으면 자동으로 태그 방식으로 폴백한다.

| | **이벤트 방식 (권장)** | **태그 방식 (폴백)** |
|---|---|---|
| 어떻게 | 타격 프레임에 `OnHitEvent`, 후딜 끝에 `OnAttackEndEvent` | "이 태그가 재생되는 동안" 판정 |
| 정확도 | 프레임 단위로 정확 | 태그 전체 구간 = 대략적 |
| 다단히트 | `OnHitEvent` 를 N번 박으면 N타 | hitCount 를 태그 구간에 균등 배분 |
| 적들 방식 | ✅ 이것 (Warrior 는 OnHitEvent ×2) | — |
| 에셋 설정 | `hitEvent: OnHitEvent` | `damageState: <태그이름>` |

**이벤트를 권장하는 이유:** 적 공격 클립이 전부 이 방식이고, 타격이 프레임에 직접 박혀 있어
그림을 다시 타이밍해도 판정이 알아서 따라온다. 2타 공격이면 `OnHitEvent` 를 2번 박으면 2타가 된다.

### (B) 애니메이션 구조를 일원화할까

지금은 소환수마다 태그 구조가 다르다:
- **MeleeDoll**: `Start`(준비) / `Slash`(타격, 이펙트 전용) / `End`(마무리) — 3태그
- **DashDoll**: `Attack`(준비+타격 한 덩어리) + `Effect`(별도, 동시 재생) — 2태그

일원화한다면 적들처럼 **`Attack` 태그 하나 + 이벤트**로 통일하는 게 자연스럽다:
```
Attack 태그 하나에 전체 프레임
  → 타격 프레임 셀에 event:OnHitEvent
  → (2타면 두 프레임에 각각)
  → 마지막 프레임 셀에 event:OnAttackEndEvent
```
그러면 `animSequence: [Attack]`, `damageState` 비움, `hitEvent: OnHitEvent` 로 4종이 똑같아진다.

**일원화 안 해도 됨.** 코드는 소환수마다 다른 구조를 다 받는다. 다만 통일하면 나중에
소환수를 추가할 때 규칙이 하나라 편하다.

### (C) 마무리 일격과 스페이스바가 같은 애니를 쓸까

지금은 **둘 다 같은 aseprite(`skillAnimVisual` = `visual`)를 공유**한다. 같은 그림을 쓰되
- 스페이스바(회전난타): 전체를 원본 속도로 (`skillAnimDuration: 2.075`)
- 마무리 일격: 조금 빠르게 (`castDuration: 1.4`)

**다른 애니를 쓰고 싶다면:** `MinionFinisher.visual` 에 다른 aseprite 프리팹을 넣으면 마무리만
그걸 쓴다. 스페이스바는 `MinionSkillSO.skillAnimVisual` 을 쓰므로 서로 독립이다.

---

## 2. Aseprite 에서 이벤트 박는 법

유니티 aseprite 임포터는 **셀(cel)의 user data** 가 `event:` 로 시작하면 그 프레임에
AnimationEvent 를 자동으로 심어준다. (`AsepriteImporter.ExtractEventStringFromCells`)

1. 타임라인에서 **타격 프레임의 셀을 클릭** (프레임 전체가 아니라 레이어가 있는 그 칸)
2. 우클릭 → **Cel Properties** (또는 프레임 우클릭 → Properties)
3. **User Data** 칸에 입력:
   - 타격 프레임: `event:OnHitEvent`
   - 후딜 끝 프레임: `event:OnAttackEndEvent`
4. 저장하면 유니티가 재임포트하면서 자동 반영. **그림을 고쳐 다시 저장해도 유지된다.**

> 셀이 여러 레이어에 걸쳐 있어도 그 프레임에 `event:` user data 가 **하나만** 있으면 된다.
> 중복으로 여러 셀에 적으면 임포터가 HashSet 으로 합쳐 한 번만 심는다.

**주의:** `event:` 로 시작하지 않는 user data 는 무시된다. 오타(`Event:`, `event :`)도 무시된다.

---

## 3. 에셋 필드 레퍼런스

이벤트/태그를 정한 뒤 인스펙터에서 채우는 값. 손으로 YAML 을 만질 필요 없이 인스펙터에서 하면 된다.

> **[26/07/23 이사]** 애니메이션 필드(비주얼/시퀀스/타이밍)는 이제 **미니언 에셋
> (`MainMinionDataSO`) 한 곳**에 모여 있다. finisher/스킬 에셋에는 더 이상 없다(게임플레이 수치만 남음).
> - 평타 마무리 연출 → 미니언의 **`basicAnim`** (`MinionAnimSet`)
> - 스페이스바 액티브 연출 → 미니언의 **`skillAnim`** (`MinionAnimSet`)
>
> 필드명 매핑: `castDuration`/`skillAnimDuration` → **`duration`**,
> `animSequence`(태그 리스트) + `movePhases`(위치) → **`sequence`** (한 리스트로 통합 — 항목마다
> 태그 + offset + snap. offset 0 이면 안 움직임). `damageState`/`hitEvent`/`effectState`/`hitWindowRatio` 는 이름 그대로.

### 평타 마무리 연출 — 미니언의 `basicAnim` (`MinionAnimSet`)
| 필드 | 뜻 |
|---|---|
| `duration` | **전체 재생 시간(초). 여기가 속도 손잡이.** 올리면 느려지고 내리면 빨라진다. 0 이면 1초로 침. |
| `sequence` | 순서대로 재생할 태그(+태그별 offset). 예: `[Start, Slash, End]` 또는 `[Attack]` |
| `damageState` | (태그 방식) 이 태그가 재생되는 동안 판정. 이벤트 쓰면 비워도 됨 |
| `hitEvent` | (이벤트 방식) 보통 `OnHitEvent`. 클립에 실제로 박혀 있어야 동작 |
| `effectState` | 동시에 겹쳐 재생할 이펙트 태그. 예: DashDoll 의 `Skill_Attack_Effect`. 없으면 비움 |

**게임플레이 수치는 여전히 `finisher` 섹션**: `hitCount`(타수), `damageMultiplier`, `element`,
`onHitStatus`, `hitBoxSize`(x=사거리 y=폭), `hitBoxPrefab`, `spawnOffset`(대각선 조준 보정), `knockbackForce` 등.

### 스페이스바 액티브 연출 — 미니언의 `skillAnim` (`MinionAnimSet`)
| 필드 | 뜻 |
|---|---|
| `duration` | **전체 재생 시간(초). 속도 손잡이.** 0 이면 1초 |
| `sequence` / `damageState` / `hitEvent` / `effectState` | basicAnim 과 동일 |

**게임플레이 수치는 스킬 에셋(`MinionActionSkillSO`, `Skills/Minion/*.asset`)**: `hitCount`,
`damageMultiplier`, `actionType`, `element`, `onHitStatus`, `useHitBox`, `hitBoxPrefab` 등.

---

## 4. 속도가 안 맞을 때 (지금 당장 만질 수 있는 것)

애니메이션이 너무 빠르거나 느리면 **`castDuration`(마무리) / `skillAnimDuration`(스페이스바)** 만
조절하면 된다. 그림 원본 길이는 이렇다:

| 소환수 | 그림 원본 길이 | 현재 마무리 castDuration | 현재 액티브 skillAnimDuration |
|---|---|---|---|
| MeleeDoll | Start+Slash+End = **2.075s** | 1.4 (약 1.5배속) | 2.075 (원본 속도) |
| DashDoll | Attack = **0.65s** | 0.65 (원본 속도) | 0.65 (원본 속도) |

애니메이션 전체가 이 값에 맞춰 자동 스케일되고, 타격 타이밍은 태그/이벤트가 정하므로
속도를 바꿔도 판정이 알아서 따라온다. **나중에 공속으로 시전이 빨라지는 로직을 넣을 때도
이 값 하나만 줄이면 애니와 타격이 같이 따라온다.**

---

## 5. 대각선 조준이 잘 안 맞을 때 (판정 위치 튜닝)

소환수 스프라이트는 좌우로만 뒤집히므로 판정 히트박스도 **수평**이다. 그래서 위쪽 대각선
(오른쪽/왼쪽 상단 끄트머리)을 조준하면 박스의 세로 위치가 적보다 낮아 빗나가기 쉽다.

두 개의 손잡이로 조절한다 (둘 다 `finisher` 섹션, 인스펙터에서 바로):

| 손잡이 | 효과 | 지금 값 (Melee / Dash) |
|---|---|---|
| `spawnOffset` | 소환수를 조준 방향으로 미는 거리 ↑ → 박스 세로 중심이 적 높이에 가까워짐 | 1.6 / 1.7 |
| `hitBoxSize.y` | 세로 폭 ↑ → 위아래로 관대해짐 | 2.8 / 3.2 |

`spawnOffset` 을 너무 키우면 코앞의 적(수평 조준)이 박스 근접 모서리 밖으로 빠질 수 있으니,
대각선이 안 맞으면 **`hitBoxSize.y` 를 먼저** 키우고 그래도 부족하면 `spawnOffset` 을 올린다.

---

## 6. DashDoll 만 남은 이유 (현재 ⚠️)

DashDoll 은 `hitEvent: OnHitEvent` 로 지정돼 있는데 **aseprite 에 아직 이벤트가 안 박혀 있다.**
코드가 이걸 감지해서 태그(`damageState: Attack`)로 폴백하고 콘솔에 경고를 찍는다. 그래서:
- **데미지는 들어간다** (폴백 덕분).
- 하지만 타이밍이 이르다 — `Attack` 태그 전체 = 준비 동작 포함이라 휘두르기 전에 맞는다.

**해결:** 2장대로 DashDoll 의 실제 타격 프레임 셀에 `event:OnHitEvent` 를 넣으면 끝. 코드는
자동으로 이벤트 방식으로 넘어간다 — 에셋도 코드도 더 손댈 것 없다.
