# 대화(다이얼로그) 시스템 사용법

대사는 이 폴더의 **CSV** 로 관리한다. 화면은 하단 1/3 이 대사창, 상단 2/3 가 초상화 3칸(왼쪽 / 가운데 / 오른쪽).

---

## 1. 빠른 시작

1. `dialogue_sample.csv` 를 연다 (구글 시트에 붙여넣거나 텍스트 에디터로)
2. 대사를 쓴다
3. Unity 로 돌아와서 아무 씬이나 플레이 → **F9**

끝. NPC 도 트리거도 필요 없다.

---

## 2. CSV 칸

```csv
id,cast,speaker,name,text_ko
bm_intro,player|bonemaster,bonemaster,,"드디어 왔군, 애송이."
bm_intro,,player,,…준비됐어.
bm_intro,,bonemaster/angry,,<shake>네놈의 뼈도 내 것이 된다.</shake>
bm_intro,,,,뼈들이 일제히 일어섰다.
```

| 칸 | 뜻 | 비우면 |
|---|---|---|
| `id` | 대화 묶음 이름. 같은 값이 **연속으로** 오면 한 대화 | 그 줄은 통째로 무시된다 |
| `cast` | 무대에 세울 캐릭터 키, `\|` 로 구분 (최대 3). 한 칸 문법은 `키/표정@자리` | **이전 줄의 무대를 그대로 유지** |
| `speaker` | 말하는 캐릭터 키 | **나레이션** — 이름칸이 사라지고 전원 어두워진다 |
| `name` | 이름칸에 띄울 글자 (덮어쓰기) | `DialogueCast.asset` 의 이름을 쓴다 → **보통 비워둔다** |
| `text_ko` | 대사 본문 | 검증에서 경고 |

칸 **순서는 상관없다**. 헤더 이름으로 찾는다.

### cast 를 매 줄 안 적어도 되는 이유

무대가 안 바뀌면 비워두면 된다. 위 예시에서 2·3·4번째 줄은 `cast` 가 비어 있지만
`player|bonemaster` 두 명이 계속 서 있다. 무대를 바꾸고 싶은 줄에서만 다시 적으면 된다.

### 화자는 반드시 무대에 있어야 한다

```csv
bm_intro,player|bonemaster,merchant,,"..."      ← 잘못됨
```
`merchant` 가 `cast` 에 없다. 이러면 **아무도 강조되지 않는다.** 게임에선 조용히 넘어가서
알아채기 어려우니, `Tools/Dialogue/2. 대사 테이블 검증` 이 이걸 잡아준다. 제일 흔한 실수다.

---

## 3. 표정과 자리

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

```csv
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

```csv
duel,player@왼쪽|bonemaster@오른쪽,bonemaster,,"자리를 찍으면 순서와 상관없이 거기 선다."
duel,player@가운데|bonemaster|merchant,merchant,,"찍은 놈이 먼저 칸을 잡고, 나머지가 빈 칸을 줍는다."
```

**섞어 써도 된다.** 찍은 캐릭터가 먼저 자기 칸을 예약하고, 안 찍은 캐릭터는
남은 칸을 왼쪽 → 오른쪽 → 가운데 순으로 주워 간다.

자리 이름을 틀리거나 둘이 같은 칸을 찍으면 자동 배치로 떨어지고 콘솔에 경고가 뜬다.
**검증 메뉴가 미리 잡아준다** — 눈으로는 못 잡는 종류라 꼭 돌려라.

---

## 4. 작성 규칙

### 쉼표

대사에 쉼표가 들어가면 **큰따옴표로 감싼다.**

```csv
bm_intro,,player,,"안녕, 반가워."     ← 이렇게
bm_intro,,player,,안녕, 반가워.        ← 이러면 칸이 밀린다
```

구글 시트나 엑셀에서 셀에 그냥 치면 **저장할 때 알아서 감싸준다.** 신경 안 써도 된다.
직접 텍스트 에디터로 쓸 때만 챙기면 된다.

따옴표 자체를 대사에 쓰려면 두 번 친다: `"그가 ""안녕"" 이라고 했다"`

### 주석

`#` 으로 시작하는 줄은 무시된다. 챕터 구분이나 메모에 쓰면 된다.

```csv
# ─── 4층 보스 ───
bm_intro,player|bonemaster,bonemaster,,"드디어 왔군."
```

### 인코딩 (중요)

**반드시 UTF-8 로 저장.**

- **구글 시트** — 기본이 UTF-8 이라 그냥 다운로드하면 된다. 권장.
- **엑셀** — 저장할 때 `CSV UTF-8 (쉼표로 분리)` 를 골라야 한다.
  그냥 `CSV` 로 저장하면 CP949 라서 **한글이 전부 깨진다.** 이게 제일 흔한 사고다.

---

## 5. 대사 안에 쓸 수 있는 태그

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

## 6. 캐릭터 추가하기

`Assets/SOData/Dialogue/DialogueCast.asset`

| 필드 | 설명 |
|---|---|
| `key` | CSV 에 적을 이름. 영문 소문자 권장 (예: `skeleton`) |
| `displayName` | 이름칸에 뜰 글자 (예: `해골 병사`) |
| `nameColor` | 이름칸 글자색 |
| `portraits` | 표정 목록. `expression` + `sprite`. **첫 항목이 기본 표정** |

`portraits` 는 **비워둬도 된다.** 초상화가 없으면 이름표 실루엣으로 뜬다.
아트가 나오면 스프라이트만 꽂으면 되고 CSV 는 안 건드려도 된다.

지금 들어 있는 키: `player` `bonemaster` `merchant` `enhancer` `ally`

> 화자 이름을 몬스터 SO 의 `minionName` 이나 `NPCBase.name` 에서 끌어오지 않는다.
> 그쪽은 절반이 `Enemy Skeleton Charger` 같은 내부 ID 라서 그대로 화면에 뜬다.
> **이름의 출처는 이 에셋(또는 CSV 의 `name` 칸)뿐이다.**

---

## 7. 새 CSV 파일 추가하기

대사가 늘어나면 파일을 쪼개도 된다. `id` 만 안 겹치면 몇 장이든 상관없다.

1. CSV 를 `Assets/Resources/Dialogue/` 에 넣는다
2. `Assets/SOData/Dialogue/DialogueTable.asset` 의 **`대사 CSV`** 리스트에 드래그
3. `Tools/Dialogue/2. 대사 테이블 검증` 으로 확인

---

## 8. 게임에 붙이기

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

## 9. 테스트 / 확인

### 플레이 중 — F9

`DialogueUI` 인스펙터의 `디버그` 항목:
- `debugKey` — 기본 `F9`. `None` 으로 두면 꺼진다
- `debugDialogueId` — 기본 `layout_test`

`layout_test` 는 스페이스를 눌러 넘기면 **1명 → 2명 → 3명 배치**가 차례로 나온다.

### 플레이 안 켜고 — `Tools/Dialogue/4. 레이아웃 미리보기 토글`

씬을 연 채로 메뉴를 누르면 대사창이 켜지고 3칸이 견본으로 채워진다.
**Game 뷰에서 바로 보이고, 인스펙터 값을 만지면 실시간 반영된다.** 한 번 더 누르면 꺼진다.

패널 비율·글자 크기·확대 배율·어둡게 하는 색은 숫자로는 감이 안 오니 이걸로 맞출 것.
가운데 칸이 화자로 세팅돼 있어서 강조 대비가 바로 보인다.

### 검증 — `Tools/Dialogue/2. 대사 테이블 검증`

```
대화 3 개
[bm_intro]  4 줄   | 드디어 왔군, 애송이.
주의  bm_intro 3번째 줄: 'bonemaster' 의 표정 'angry' 초상화가 없다 — 플레이스홀더로 뜬다.
문제 없음
```

잡아주는 것: 무대에 없는 화자, 명부에 없는 키, 빈 대사, 6명 이상, 첫 줄에 cast 가 없음.
리포트는 `Temp/Dialogue_Validate.txt` 에도 남는다.

---

## 10. 진행 키

**스페이스 / 엔터 / 좌클릭**

타이핑 중에 누르면 먼저 전체가 표시되고, 한 번 더 눌러야 다음 줄로 간다.

> `E` 는 이미 스킬이고 `F` 는 대화 중 입력차단에 같이 죽기 때문에, 대화창이 자기 입력을
> 직접 읽는다. 액션 에셋(`PlayerInputSystem.inputactions`)은 안 건드렸다.

---

## 11. 조정할 수 있는 값

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

## 12. 슬롯 배치 규칙

칸은 **왼쪽 / 가운데 / 오른쪽** 셋뿐이고, `cast` 에 적은 순서대로
**왼쪽 → 오른쪽 → 가운데** 로 채워진다. 둘이면 양 끝에 서고, 셋째가 그 사이에 낀다.

```
        왼      가운데     오른
1명   [ A ]  [    ]  [    ]
2명   [ A ]  [    ]  [ B  ]
3명   [ A ]  [ C  ]  [ B  ]
```

4명 이상은 뒤가 잘린다. 검증에서 잡아준다.

칸의 가로 위치는 `Assets/Editor/DialogueSetupTools.cs` 의 `SLOT_X`
(기본 `0.22 / 0.5 / 0.78` = 화면 폭 비율). 양 끝을 더 벌리거나 좁히려면 그 값을 고치고
`Tools/Dialogue/1. 대화 UI 프리팹 만들기` 를 다시 돌린다.

---

## 13. 다국어

**언어별로 파일을 나누지 말고 칸을 늘린다.**

```csv
id,cast,speaker,name,text_ko,text_en
bm_intro,player|bonemaster,bonemaster,,"드디어 왔군, 애송이.",So you came.
```

칸 이름에 로케일 코드를 붙이면(`text_ko` / `text_en`) 현재 언어에 맞는 칸을 알아서 고른다.
없으면 `text_ko` 로 떨어지고, 접미 없는 `text` 칸만 있어도 그냥 돈다.

파일을 나누면 안 되는 이유 — 번역가가 줄 하나 지우는 순간 그 아래 대사가 전부 밀려서
화자와 대사가 뒤섞인다. 한 줄에 원문과 번역이 나란히 있으면 빈칸이 그냥 보인다.

`name` 칸도 같은 규칙이 먹는다 (`name_ko` / `name_en`).

---

## 14. 함정 모음

- **엑셀 CP949** — `CSV UTF-8` 로 저장. 안 그러면 한글이 다 깨진다
- **화자가 cast 에 없음** — 아무도 강조 안 됨. 검증으로 잡을 것
- **대화 중엔 시간이 멈춘다** (`Time.timeScale = 0`) — 대화 연출에 새 코드를 붙일 땐
  `Time.deltaTime` 대신 `Time.unscaledDeltaTime`, `WaitForSeconds` 대신 `WaitForSecondsRealtime`
- **대화를 겹치지 말 것** — 이미 대화 중이면 새 `Play()` 는 무시되고 경고가 뜬다.
  상점창·보상창과 동시에 뜨지 않게 호출부에서 막아야 한다
- **Bold 폰트 주의** — `Galmuri11-Bold SDF` 는 아틀라스 1024 에 multi-atlas 가 꺼져 있다.
  대사창에 Bold 를 상시 쓰면 고유 한글이 늘었을 때 글자가 **조용히 사라진다.**
  쓸 거면 아틀라스를 4096 으로 올리거나 multi-atlas 를 켤 것. Regular(4096)는 여유 있다
- **씬에 `DialogueUI` 가 있어야 한다** — 없으면 `DialogueUI.Instance` 가 null 이다.
  지금 배치된 씬: `BattleScene` `VillageScene` `BossTestScene` `EliteTestScene`.
  다른 씬에도 필요하면 그 씬을 열고 `Tools/Dialogue/3. 씬에 배치`

---

## 15. 파일 위치

| 무엇 | 어디 |
|---|---|
| 대사 CSV | `Assets/Resources/Dialogue/` |
| 테이블 에셋 (CSV 등록) | `Assets/SOData/Dialogue/DialogueTable.asset` |
| 캐릭터 명부 | `Assets/SOData/Dialogue/DialogueCast.asset` |
| UI 프리팹 | `Assets/Prefabs/UI/Dialogue/DialogueUI.prefab` |
| 스크립트 | `Assets/Scripts/UI/Dialogue/` |
| 에디터 툴 | `Assets/Editor/DialogueSetupTools.cs` |

### `Tools/Dialogue/` 메뉴

| 메뉴 | 언제 |
|---|---|
| `1. 대화 UI 프리팹 만들기` | 프리팹을 처음부터 다시 만들 때 (**기존 프리팹을 덮어쓴다**) |
| `2. 대사 테이블 검증` | CSV 를 고칠 때마다 |
| `3. 씬에 배치` | 새 씬에 대화 UI 를 넣을 때 |
| `4. 레이아웃 미리보기 토글` | 화면 비율·색·크기를 눈으로 맞출 때 |
