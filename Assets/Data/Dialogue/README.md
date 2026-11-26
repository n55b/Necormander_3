# 게임 대화 제작

Unity 플레이 모드에서 **레벨 제작실 → 게임 콘텐츠 → 대사**을 연다. 대화 ID와 언어를 선택하고, 화자·표시 이름·등장인물·본문을 입력한다. 새 줄을 추가하거나 순서를 바꾼 뒤 **프로젝트에 저장** 또는 **Ctrl+S**를 누른다.

같은 ID를 사용하는 트리거나 NPC가 다음 대화를 시작할 때 변경분을 읽는다. 한국어 대화만 있는 경우 영어에서도 한국어 본문을 표시한다. 제작실의 English 탭에서 번역을 저장하면 영어 본문을 사용한다. 등장인물 입력을 비워두면 직전 줄의 배치를 유지하고, 화자를 비우면 나레이션으로 표시한다.

언어·줄별 파일은 [DATA 안내](../../../DATA/README.md)에서 확인할 수 있다.

## 표정과 자리

캐릭터 한 칸의 문법은 **`키/표정@자리`**. 표정과 자리는 둘 다 생략할 수 있다.

```
bonemaster                 키만
bonemaster/angry           표정
bonemaster@오른쪽          자리
bonemaster/angry@오른쪽    둘 다
```

### 표정

`cast` 와 `speaker` 양쪽에서 쓸 수 있다. 같은 캐릭터면 `speaker` 쪽이 이긴다 —
무대는 평소 얼굴로 적어두고 그 줄에서만 표정을 바꾸는 게 편하다.

```text
bm_intro,player|bonemaster,bonemaster/angry,,<shake>네놈의 뼈도 내 것이 된다.</shake>
```

표정 이름은 `DialogueCast.asset` 의 `portraits` 목록에서 정한다. 없는 표정을 부르면
그 캐릭터의 **첫 번째 표정**(기본)으로 떨어진다. 초상화가 아예 없으면 이름표 실루엣이 뜬다.

### 자리

`cast` 에서만 쓴다 (`speaker` 에 적으면 조용히 무시된다 — 자리는 무대가 정한다).

| 적는 법 | 칸 |
|---|---|
| `왼쪽` `왼` `left` `l` | 왼쪽 |
| `가운데` `중앙` `center` `c` | 가운데 |
| `오른쪽` `오른` `right` `r` | 오른쪽 |

```text
duel,player@왼쪽|bonemaster@오른쪽,bonemaster,,"자리를 찍으면 순서와 상관없이 거기 선다."
duel,player@가운데|bonemaster|merchant,merchant,,"찍은 놈이 먼저 칸을 잡고, 나머지가 빈 칸을 줍는다."
```

**섞어 써도 된다.** 찍은 캐릭터가 먼저 자기 칸을 예약하고, 안 찍은 캐릭터는
남은 칸을 왼쪽 → 오른쪽 → 가운데 순으로 주워 간다.

자리 이름을 틀리거나 둘이 같은 칸을 찍으면 자동 배치로 떨어지고 콘솔에 경고가 뜬다.
**검증 메뉴가 미리 잡아준다** — 눈으로는 못 잡는 종류라 꼭 돌려라.

---

## 대사 안에 쓸 수 있는 태그

TMP 순정 태그는 그대로 통과한다: `<color=#ff5544>`, `<b>`, `<i>`, `<size=24>` …

여기에 더해 글자 단위 연출 태그 5종:

| 태그 | 효과 | 지속 |
|---|---|---|
| `<shake>불안하다</shake>` | 부들부들 떨림 | 1회 |
| `<punch>쾅</punch>` | 튕기듯 확대 | 1회 |
| `<wave>흔들흔들</wave>` | 위아래 물결 | 계속 |
| `<rainbow>무지개</rainbow>` | 색이 순환 | 계속 |
| `<jitter>지직</jitter>` | 지직거림 | 계속 |

세기/속도는 `=배수` 로: `<shake=1.5>`, `<rainbow=2>`

**주의**
- 닫는 태그가 반드시 있어야 한다. `<shake>글자</shake>`
- **중첩 불가.** `<shake><wave>글자</wave></shake>` 는 안 된다. 한 구간에 하나만.
- `<wave>` `<rainbow>` `<jitter>` 는 그 대사가 끝날 때까지 매 프레임 계산한다. 남발하지 말 것.

---

## 캐릭터 추가하기

`Assets/SOData/Dialogue/DialogueCast.asset`

| 필드 | 설명 |
|---|---|
| `key` | 등장 인물·화자 필드에 적을 키. 영문 소문자 권장 (예: `skeleton`) |
| `displayName` | 이름칸에 뜰 글자 (예: `해골 병사`) |
| `nameColor` | 이름칸 글자색 |
| `portraits` | 표정 목록. `expression` + `sprite`. **첫 항목이 기본 표정** |

`portraits` 는 **비워둬도 된다.** 초상화가 없으면 이름표 실루엣으로 뜬다.
아트가 나오면 스프라이트만 꽂으면 되고 대사 줄에는 같은 키를 유지한다.

지금 들어 있는 키: `player` `bonemaster` `merchant` `enhancer` `ally`

> 화자 이름을 몬스터 SO 의 `minionName` 이나 `NPCBase.name` 에서 끌어오지 않는다.
> 그쪽은 절반이 `Enemy Skeleton Charger` 같은 내부 ID 라서 그대로 화면에 뜬다.
> **이름의 출처는 이 에셋(또는 줄의 표시 이름)뿐이다.**

---

## 게임에 붙이기

### 코드에서

```csharp
DialogueUI.Instance.Play("bm_intro");                     // 그냥 재생
DialogueUI.Instance.Play("bm_intro", () => 보스스폰());    // 끝나면 콜백

if (DialogueUI.Instance.IsPlaying) { ... }                 // 대화 중인지
DialogueUI.Instance.StopDialogue();                        // 강제 종료

// 코루틴에서 대화가 끝날 때까지 기다리기
yield return new WaitUntil(() => !DialogueUI.Instance.IsPlaying);
```

### 인스펙터에서 (드래그로)

`DialogueTrigger` 컴포넌트를 아무 오브젝트에 붙인다.

| 필드 | 설명 |
|---|---|
| `dialogueId` | 재생할 대화 id |
| `playOnce` | 한 번만 재생 (기본 켜짐). 방을 다시 들어와도 안 뜨게 |
| `playOnEnable` | 오브젝트가 켜질 때 자동 재생 |
| `onDialogueComplete` | 대화가 끝나면 실행할 것들 (보스 스폰, 문 열기 등) |

`Play()` 가 인자를 안 받으므로 **UnityEvent 슬롯에 그대로 잡힌다.**
`BossRoomEvent` 의 `OnBossCombatStart` / `OnBossCombatClear`, 문·포탈의 `FadeAction` 등
이미 있는 UnityEvent 어디에든 끌어다 놓으면 된다.

### 대화하는 NPC 만들기

```csharp
public class DialogueNPC : NPCBase
{
    [SerializeField] private string dialogueId;

    public override string InteractionPrompt => "F : 대화";

    public override bool Interact(GameObject interactor)
    {
        DialogueUI.Instance.Play(dialogueId);
        return true;
    }
}
```

주의할 것 셋:

- 콜라이더는 **`IInteractable` 과 같은 GameObject** 에 있어야 한다. 자식에 두면 영영 안 잡힌다
  (`PlayerController.CheckForInteractable` 이 `col.TryGetComponent<IInteractable>()` 를 쓴다)
- 레이어는 `Interactable`
- 머리 위 "F" 아이콘을 띄우려면 `PopupSystem` 을 인스펙터에 연결해야 한다.
  전역 상호작용 프롬프트 UI 가 없어서, 안 걸면 아무 표시도 없이 F 만 먹는다

> 포커스는 **항상 가장 가까운 하나뿐이다.** NPC 옆에 아이템이나 진열대가 떨어져 있으면
> 그쪽이 더 가까울 때 F 가 그리로 간다.

---

## 진행 키

**스페이스 / 엔터 / 좌클릭**

타이핑 중에 누르면 먼저 전체가 표시되고, 한 번 더 눌러야 다음 줄로 간다.

> `E` 는 이미 스킬이고 `F` 는 대화 중 입력차단에 같이 죽기 때문에, 대화창이 자기 입력을
> 직접 읽는다. 액션 에셋(`PlayerInputSystem.inputactions`)은 안 건드렸다.

---

## 조정할 수 있는 값

전부 `DialogueUI` 인스펙터에 있다.

| 항목 | 기본값 | 설명 |
|---|---|---|
| `charsPerSecond` | 40 | 타이핑 속도. 0 이하면 통째로 즉시 표시 |
| `clickSkipsTyping` | 켬 | 타이핑 중 클릭 시 전체 표시 |
| `speakerScale` | 1.0 | 말하는 캐릭터 크기 |
| `idleScale` | 0.88 | 안 말하는 캐릭터 크기 |
| `speakerTint` | 흰색 | 말하는 캐릭터 색 (원본 그대로) |
| `idleTint` | 어두운 청회색 | 안 말하는 캐릭터 색 |
| `idleDropPixels` | 10 | 안 말하는 캐릭터를 아래로 내리는 양 |
| `highlightSpeed` | 14 | 강조 전환 속도 |
| `stopTime` | 켬 | 대화 중 `Time.timeScale = 0` |
| `blockPlayerInput` | 켬 | 대화 중 플레이어 조작 차단 |
| `typeSfx` | 없음 | 타이핑 효과음 |
| `typeSfxEveryNChars` | 3 | 몇 글자마다 소리 낼지 |

> 타이핑 소리를 **글자마다** 내면 안 된다. SFX 풀이 8채널 라운드로빈이라 전투 효과음이 씹힌다.
> 2~3 이상으로 유지할 것.

---

## 슬롯 배치 규칙

칸은 **왼쪽 / 가운데 / 오른쪽** 셋뿐이고, `cast` 에 적은 순서대로
**왼쪽 → 오른쪽 → 가운데** 로 채워진다. 둘이면 양 끝에 서고, 셋째가 그 사이에 낀다.

```
        왼      가운데     오른
1명   [ A ]  [    ]  [    ]
2명   [ A ]  [    ]  [ B  ]
3명   [ A ]  [ C  ]  [ B  ]
```

4명 이상은 뒤가 잘린다. 검증에서 잡아준다.

칸의 가로 위치는 `Assets/Editor/Localization/DialogueSetupTools.cs` 의 `SLOT_X`
(기본 `0.22 / 0.5 / 0.78` = 화면 폭 비율). 양 끝을 더 벌리거나 좁히려면 그 값을 고치고
`Tools/Dialogue/1. 대화 UI 프리팹 만들기` 를 다시 돌린다.

---
