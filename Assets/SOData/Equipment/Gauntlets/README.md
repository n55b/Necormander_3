# 건틀릿 장비 설정

새 런은 MK0으로 시작합니다. 각 상점의 **EnhanceShopNPC**에게 F → 기존 보상 카드 창에서 분기를 선택합니다.
취소는 무료이며, 구매한 상점에서는 다시 강화할 수 없습니다. 다음 상점에서 2단계로 강화할 수 있습니다.
장비와 그림자 중첩은 층 이동 저장에 포함됩니다. 새 런은 초기화됩니다.

## 강화 경로

```
MK0
├─ MK1 ── MK2 / MK02
├─ FrostWind ── FrostBurst / FrostCore
└─ Shadow ── ShadowAbsorption
```

이 폴더의 각 `.asset`을 선택하고 Inspector에서 수치를 수정하세요.

| 항목 | 설정 |
|---|---|
| 기본 공격 | `combo`: 3~5단계. `multiplier`는 1타당 배율, `hits`는 타수 |
| 미니언 마무리 | 마지막 단계의 `multiplier × hits`만 곱합니다. 미니언 SO의 배율·타수·속성·애니메이션은 유지합니다 |
| 공격 속도 | `attackSpeed`, `speedMultipliers`: 0.7 / 0.85 / 1 / 1.15 / 1.3 |
| 경직 | `hitstun`, `hitstunSeconds`: 0 / 0.1 / **0.2** / 0.3 / 0.4초 |
| 다단히트 간격 | `multiHitInterval`: 플레이어 그림자 주먹은 0.08초 간격 2타 |
| 다음 강화 | `upgradeTier`, `upgrades`의 실제 에셋 참조 |
| 열정 | `passion*`: 한 공격 동작이 여러 적을 맞혀도 중첩은 1개. 5초 미명중 시 전체 해제 |
| 빙결 | `frostHits`, `frostStackDuration`, `freezeDuration`, `frostMaxHpRatios`(일반/엘리트/보스) |
| 서리 폭발 | `frostExplosionRatio`, `frostExplosionRadius`(월드 유닛) |
| 서리 흉갑 | `frostAuraRadius`=1.75 유닛, `frostAuraReduction`=0.15 |
| 그림자 | `shadowAttackPerStack`, `shadowLossRatio`, `shadowKillStacks` |
| 카드 문구/아이콘 | `description`, `icon` — 수치 변경 시 설명도 맞춰 주세요. 기존 아트만 재사용합니다 |
| 강화 카드 크기 | `Assets/Prefabs/UI/Reward Selection/RewardCard 1.prefab`의 `RewardCard.weaponCardSize`. 일반 보상에는 기존 크기를 복원합니다 |
| 카드 이름/설명 배치 | 같은 프리팹의 `Name`과 `Description Viewport` RectTransform에서 조절합니다. 카드 아트와 함께 늘어나는 비율 앵커를 사용하며 코드가 위치를 덮어쓰지 않습니다 |
| 카드 글자/긴 설명 | `Name` 및 `Description Viewport/Description`의 TMP에서 조절합니다. 긴 설명은 아래 설명칸 안에서 휠/드래그로 읽습니다. ScrollRect·RectMask2D·ContentSizeFitter가 실제 프리팹에 저장되어 있습니다 |
| 강화 가격 | `Assets/SOData/Registry/Shop Registry.asset`의 `enhanceCost` + 현재 단계 × `enhanceCostPerLevel` |

## 피해와 효과 적용 범위

- 플레이어 기본 공격: 플레이어 ATK × 현재 단계의 1타당 배율 × 기존 평타 보정.
- 미니언 마무리: **플레이어 ATK × 미니언 고유 1타 배율 × 장비 마지막 단계 총배율**, 미니언 고유 타수 유지.
  - MK0 + DashDoll: ATK 10이면 30 × 1타.
  - MK0 + MeleeDoll: ATK 10이면 7.5 × 2타.
  - 그림자 + DashDoll: ATK 10이면 32 × 1타. 미니언을 2번 공격시키지 않습니다.
- 열정·빙결 누적은 기본 공격과 미니언 **콤보 마무리까지** 적용됩니다. Space 스킬에는 적용하지 않습니다.
- 미니언이 없거나 Space 스킬로 사용 중이면 마지막 단계를 빼고 플레이어 공격만 반복합니다.
- 4~5단계 설정 시 플레이어의 기존 두 주먹 모션을 재사용합니다. 새 애니메이션은 생성하지 않습니다.
- 열정은 성공한 공격 동작당 1중첩입니다. 최대 중첩의 추가 피해/경직은 다음 공격부터 적용합니다.
- 그림자 처치 보상은 아군 전체 공유. 실제 체력 피해에만 중첩을 잃습니다. 잃는 중첩 수를 내림합니다.

## 빙결

5번째 타격은 빙결을 걸고, **다음 직접 타격**에 해제 추가 피해가 발생합니다.
DoT와 빙결 폭발은 빙결을 깨지 않으며 연쇄 폭발하지 않습니다. 해제 후 재빙결 내성은 기존 1초입니다.
보스·엘리트도 실제로 얼어붙지만, 영구 슈퍼아머의 경직·스턴·밀침 면역은 유지됩니다.
진행 중인 행동/패턴/예고는 멈췄다가 이어집니다. 이미 발사된 투사체와 활성화된 바닥 공격은 계속됩니다.

## 배선과 검증

- `Growth Reward Registry.asset`의 `equipments`에는 현행 건틀릿 9종만 등록됩니다.
- 예전 장비 파일과 아트는 삭제하지 않았지만, 진열/랜덤 교체용 풀에서는 제외했습니다.
- 강화 NPC는 실제 방 프리팹에 존재합니다. 현재 일반 상점과 튜토리얼 상점 `Tutorial_05` 모두 연결되어 있습니다.
- 새 상점 프리팹을 만들면 기존 `EnhanceShopNPC.prefab`을 원하는 바닥 위치에 배치하세요.
- `Tools/Equipment/1. Create Weapons And Wire Shops`: 없는 에셋과 강화 NPC만 생성. 기존 수치/위치는 덮어쓰지 않습니다.
- `Tools/Equipment/2. Verify Weapons`: 분기, 배율, 실제 명중 시 열정, 그림자 처치·피격·저장, 강화 비용·상점당 1회 제한, 빙결·폭발·내성, 오라 회수, 패턴 정지/재개, 프리팹 배선을 검사합니다. Play 모드 밖에서 실행하세요.
- `Tools/Equipment/3. Preview Reward Cards`: 기존 프리팹을 임시 씬에서 렌더해 `Logs/RewardCards-0.png`(설명 처음), `-1.png`(끝까지 스크롤), `-2.png`(일반 보상)를 저장합니다. 이름판/설명칸의 아트 영역, 긴 설명의 높이 및 일반 보상 크기 복원도 검사합니다. 실제 게임 씬은 변경하지 않습니다.
