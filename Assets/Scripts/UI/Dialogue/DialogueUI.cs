using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 화면 하단 대사창 + 상단 초상화 5칸을 굴리는 대화 UI.
///
/// <b>부르는 법</b> — 이거 하나면 된다.
/// <code>
/// DialogueUI.Instance.Play("bm_intro");                 // 그냥 재생
/// DialogueUI.Instance.Play("bm_intro", () => 보스스폰()); // 끝나면 콜백
/// yield return new WaitUntil(() =&gt; !DialogueUI.Instance.IsPlaying); // 코루틴에서 대기
/// </code>
/// 인스펙터에서 배선하려면 <see cref="DialogueTrigger"/> 를 UnityEvent 슬롯에 끌어다 놓는다.
///
/// <b>왜 UIPopUpManager 를 안 쓰나</b> — 셋 다 대화에는 치명적이라서다.
///   · PopUpUI 는 전투 중(_isOnBattle)이면 조용히 무시된다 → 보스 등장 대사가 아예 안 뜬다.
///   · 팝업 스택이 1개뿐이라 상점창 위에 겹치면 서로를 지운다.
///   · ForcePopUpUI 는 SetActive(true) 를 하지 않는다(빈 화면이 뜬다).
/// 그래서 대화는 timeStop / 입력차단을 직접 걸고 직접 푼다.
///
/// <b>timeScale=0 규약</b> — 대화 중에는 시간이 멈춘다. 여기 있는 타이핑·트윈이
/// 전부 unscaled 로 도는 이유고, 새로 뭘 추가할 때도 Time.deltaTime 을 쓰면 안 된다.
/// </summary>
public class DialogueUI : Singleton<DialogueUI>
{
    [Header("데이터")]
    [Tooltip("대사 CSV 를 물고 있는 테이블 에셋.")]
    [SerializeField] private DialogueTableSO table;

    [Tooltip("캐릭터 키 → 이름/초상화 명부.")]
    [SerializeField] private DialogueCastSO cast;

    [Header("패널")]
    [Tooltip("대화 중에만 켜지는 루트. 이 오브젝트 하나만 SetActive 로 토글한다.")]
    [SerializeField] private GameObject panel;

    [Tooltip("화자 이름칸 전체. 나레이션일 때 통째로 숨긴다.")]
    [SerializeField] private GameObject nameBox;

    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Tooltip("선택. 붙어 있으면 대사의 <shake>/<wave>/<rainbow> 태그가 살아난다.\n" +
             "비워두면 태그 없는 평문으로만 나온다.")]
    [SerializeField] private TMPTextEffectPlayer bodyEffect;

    [Tooltip("선택. 타이핑이 끝나면 켜지는 '다음' 화살표.")]
    [SerializeField] private GameObject nextArrow;

    [Header("초상화 슬롯 (왼쪽부터 5칸)")]
    [Tooltip("5칸을 왼쪽부터 순서대로 넣는다. 인원이 5명보다 적으면 가운데로 모아서 배치한다.")]
    [SerializeField] private PortraitSlot[] slots = new PortraitSlot[SLOT_COUNT];

    [Header("타이핑")]
    [Tooltip("초당 글자 수. 0 이하면 타이핑 없이 전체가 한 번에 뜬다.")]
    [SerializeField] private float charsPerSecond = 40f;

    [Tooltip("타이핑 중에 넘기기 키를 누르면 먼저 전체를 표시하고, 한 번 더 눌러야 다음 줄로 간다.")]
    [SerializeField] private bool clickSkipsTyping = true;

    [Header("화자 강조")]
    [Tooltip("말하는 캐릭터의 크기 배율.")]
    [SerializeField] private float speakerScale = 1f;

    [Tooltip("말하지 않는 캐릭터의 크기 배율.")]
    [SerializeField] private float idleScale = 0.88f;

    [Tooltip("말하는 캐릭터의 색. 보통 흰색(원본 그대로).")]
    [SerializeField] private Color speakerTint = Color.white;

    [Tooltip("말하지 않는 캐릭터의 색. 어둡게 해서 뒤로 물러난 느낌을 준다.")]
    [SerializeField] private Color idleTint = new Color(0.42f, 0.42f, 0.52f, 1f);

    [Tooltip("말하지 않는 캐릭터를 아래로 내리는 픽셀 수(960x540 기준). 0 이면 안 내린다.")]
    [SerializeField] private float idleDropPixels = 10f;

    [Tooltip("강조 전환 속도. 클수록 빠르게 붙는다.")]
    [SerializeField] private float highlightSpeed = 14f;

    [Header("게임 정지")]
    [Tooltip("대화 중 Time.timeScale 을 0 으로. 기존 상점/보상창과 같은 관행이다.")]
    [SerializeField] private bool stopTime = true;

    [Tooltip("대화 중 플레이어 입력을 막는다. timeScale=0 만으로는 평타/스킬이 그대로 들어온다.")]
    [SerializeField] private bool blockPlayerInput = true;

    [Header("사운드")]
    [Tooltip("타이핑 효과음. 비워두면 소리 없음.")]
    [SerializeField] private AudioClip typeSfx;

    [Tooltip("몇 글자마다 한 번 소리를 낼지. SFX 풀이 8채널뿐이라 매 글자마다 내면 전투 효과음이 씹힌다.")]
    [SerializeField] private int typeSfxEveryNChars = 3;

    [Range(0f, 1f)]
    [SerializeField] private float typeSfxVolume = 0.5f;

    [Header("디버그")]
    [Tooltip("플레이 중에 이 키를 누르면 아래 id 의 대화를 띄운다.\n" +
             "아직 대화를 붙일 NPC/트리거가 없을 때 확인용. None 으로 두면 꺼진다.")]
    [SerializeField] private Key debugKey = Key.F9;

    [Tooltip("디버그 키로 재생할 대화 id.")]
    [SerializeField] private string debugDialogueId = "layout_test";

    /// <summary>초상화 슬롯 하나의 배선.</summary>
    [Serializable]
    public class PortraitSlot
    {
        [Tooltip("슬롯 루트. 크기/위치 연출이 여기 걸린다.")]
        public RectTransform root;

        [Tooltip("초상화 이미지.")]
        public Image image;

        [Tooltip("초상화가 없을 때 대신 뜨는 이름표(플레이스홀더). 아트 없이 검수하려고 둔다.")]
        public TextMeshProUGUI placeholderLabel;
    }

    public const int SLOT_COUNT = 5;

    /// <summary>
    /// 인원수별 슬롯 배치. 5칸 중 가운데로 모은다.
    /// 1명=[가운데], 2명=[2,4]칸, 3명=[2,3,4]칸, 4명=[1,2,4,5]칸, 5명=전부.
    /// </summary>
    private static readonly int[][] CENTERED_LAYOUT =
    {
        new int[0],
        new[] { 2 },
        new[] { 1, 3 },
        new[] { 1, 2, 3 },
        new[] { 0, 1, 3, 4 },
        new[] { 0, 1, 2, 3, 4 },
    };

    public bool IsPlaying { get; private set; }

    private List<DialogueLine> _lines;
    private int _index;
    private Action _onComplete;

    private Coroutine _typing;
    private bool _typingDone;
    private int _openedFrame = -1;

    // 무대에 서 있는 캐릭터들. cast 칸이 빈 줄은 이걸 그대로 물려받는다.
    private readonly List<string> _stage = new List<string>();

    // 슬롯별 현재/목표 연출값. 코루틴 여러 개 굴리는 대신 Update 에서 한꺼번에 민다.
    private float[] _scaleNow, _scaleTarget, _offsetNow, _offsetTarget;
    private Color[] _tintNow, _tintTarget;
    private Vector2[] _slotHomePos;

    protected override void OnAwake()
    {
        _scaleNow     = new float[SLOT_COUNT];
        _scaleTarget  = new float[SLOT_COUNT];
        _offsetNow    = new float[SLOT_COUNT];
        _offsetTarget = new float[SLOT_COUNT];
        _tintNow      = new Color[SLOT_COUNT];
        _tintTarget   = new Color[SLOT_COUNT];
        _slotHomePos  = new Vector2[SLOT_COUNT];

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            _scaleNow[i] = _scaleTarget[i] = idleScale;
            _tintNow[i]  = _tintTarget[i]  = idleTint;
            if (slots != null && i < slots.Length && slots[i] != null && slots[i].root != null)
            {
                _slotHomePos[i] = slots[i].root.anchoredPosition;
            }
        }

        if (panel != null) panel.SetActive(false);
    }

    // ─── 진입점 ──────────────────────────────────────────────────────
    /// <summary>
    /// id 에 해당하는 대화를 재생한다. 이미 대화 중이면 무시한다(중첩 금지).
    /// </summary>
    /// <param name="id">CSV 의 id 칸 값.</param>
    /// <param name="onComplete">마지막 줄까지 넘긴 뒤 호출. 대화가 없어서 못 틀었을 때도 호출된다.</param>
    public void Play(string id, Action onComplete = null)
    {
        if (IsPlaying)
        {
            Debug.LogWarning($"<color=orange>[DialogueUI]</color> 이미 대화 중이라 '{id}' 를 건너뛴다.");
            onComplete?.Invoke();
            return;
        }

        if (table == null)
        {
            Debug.LogError("<color=orange>[DialogueUI]</color> DialogueTable 이 비어 있다.");
            onComplete?.Invoke();
            return;
        }

        _lines = table.Get(id);
        if (_lines == null || _lines.Count == 0)
        {
            Debug.LogWarning($"<color=orange>[DialogueUI]</color> '{id}' 대화를 테이블에서 못 찾았다.");
            onComplete?.Invoke();
            return;
        }

        _onComplete  = onComplete;
        _index       = 0;
        _openedFrame = Time.frameCount;
        IsPlaying    = true;
        _stage.Clear();

        if (panel != null) panel.SetActive(true);
        if (stopTime && GameManager.Instance != null) GameManager.Instance.SetTimeStop(true);
        if (blockPlayerInput) SetPlayerInputBlocked(true);
        UIEventBus.NotifyOpen("Dialogue");

        ShowLine(_lines[0]);
        SnapHighlight();   // 첫 줄은 트윈 없이 제자리에서 시작한다(이전 대화의 잔상 방지)
    }

    /// <summary>대화를 즉시 끊는다. 씬 전환이나 예외 상황용.</summary>
    public void StopDialogue()
    {
        if (!IsPlaying) return;
        Finish();
    }

    // ─── 진행 ────────────────────────────────────────────────────────
    private void Update()
    {
        UpdateHighlight();

        if (!IsPlaying)
        {
            // 디버그 키. 대화를 걸어둔 NPC 나 트리거가 아직 없어도 여기서 바로 확인할 수 있다.
            if (debugKey != Key.None)
            {
                var debugKb = Keyboard.current;
                if (debugKb != null && debugKb[debugKey].wasPressedThisFrame) Play(debugDialogueId);
            }
            return;
        }

        if (Time.frameCount == _openedFrame) return;   // 대화를 연 그 입력으로 첫 줄이 넘어가지 않게
        if (!AdvancePressed()) return;

        if (!_typingDone && clickSkipsTyping)
        {
            CompleteTyping();
            return;
        }

        _index++;
        if (_index >= _lines.Count) Finish();
        else ShowLine(_lines[_index]);
    }

    /// <summary>
    /// 넘기기 입력. PlayerController 를 거치지 않고 직접 읽는다 —
    /// E 는 이미 스킬에 물려 있고, SetInputBlocked(true) 를 걸면 F(상호작용)도 같이 죽기 때문이다.
    /// </summary>
    private bool AdvancePressed()
    {
        var kb = Keyboard.current;
        if (kb != null &&
            (kb.spaceKey.wasPressedThisFrame ||
             kb.enterKey.wasPressedThisFrame ||
             kb.numpadEnterKey.wasPressedThisFrame))
        {
            return true;
        }

        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void ShowLine(DialogueLine line)
    {
        // cast 가 비어 있으면 이전 무대를 그대로 쓴다.
        if (line.cast != null)
        {
            _stage.Clear();
            foreach (var c in line.cast)
            {
                if (!string.IsNullOrEmpty(c)) _stage.Add(c);
            }
        }

        DialogueCastSO.SplitKey(line.speaker, out string speakerKey, out string speakerExpr);
        ApplyStage(speakerKey, speakerExpr);
        ApplyName(line, speakerKey);
        StartTyping(line.text);
    }

    private void ApplyName(DialogueLine line, string speakerKey)
    {
        bool narration = string.IsNullOrEmpty(speakerKey);
        if (nameBox != null) nameBox.SetActive(!narration);
        if (nameText == null || narration) return;

        nameText.text = !string.IsNullOrEmpty(line.displayName)
            ? line.displayName
            : (cast != null ? cast.GetDisplayName(speakerKey) : speakerKey);

        if (cast != null) nameText.color = cast.GetNameColor(speakerKey);
    }

    // ─── 초상화 ──────────────────────────────────────────────────────
    private void ApplyStage(string speakerKey, string speakerExpr)
    {
        int count = Mathf.Min(_stage.Count, SLOT_COUNT);
        int[] layout = CENTERED_LAYOUT[count];

        bool[] used = new bool[SLOT_COUNT];

        for (int i = 0; i < count; i++)
        {
            int slotIndex = layout[i];
            used[slotIndex] = true;

            DialogueCastSO.SplitKey(_stage[i], out string key, out string expr);

            // 이 줄에서 화자가 다른 표정을 지정했으면 그걸 우선한다.
            // (cast 에는 bonemaster 로만 적어두고 speaker 에 bonemaster/angry 로 쓰는 흐름)
            if (key == speakerKey && !string.IsNullOrEmpty(speakerExpr)) expr = speakerExpr;

            bool isSpeaker = !string.IsNullOrEmpty(speakerKey) && key == speakerKey;
            FillSlot(slotIndex, key, expr, isSpeaker);
        }

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (!used[i]) ClearSlot(i);
        }
    }

    private void FillSlot(int slotIndex, string key, string expression, bool isSpeaker)
    {
        var slot = GetSlot(slotIndex);
        if (slot == null) return;

        if (slot.root != null) slot.root.gameObject.SetActive(true);

        Sprite sprite = cast != null ? cast.GetPortrait(key, expression) : null;

        // 스프라이트가 없으면 슬롯을 숨기는 게 아니라 플레이스홀더로 채운다.
        // 숨기면 인원수에 따라 레이아웃이 매번 흔들려서 연출 검수가 안 된다.
        if (slot.image != null)
        {
            slot.image.sprite  = sprite;
            slot.image.enabled = true;
            slot.image.preserveAspect = true;
        }

        if (slot.placeholderLabel != null)
        {
            bool needPlaceholder = sprite == null;
            slot.placeholderLabel.gameObject.SetActive(needPlaceholder);
            if (needPlaceholder)
            {
                slot.placeholderLabel.text = cast != null ? cast.GetDisplayName(key) : key;
            }
        }

        _scaleTarget[slotIndex]  = isSpeaker ? speakerScale : idleScale;
        _tintTarget[slotIndex]   = isSpeaker ? speakerTint : idleTint;
        _offsetTarget[slotIndex] = isSpeaker ? 0f : -idleDropPixels;
    }

    private void ClearSlot(int slotIndex)
    {
        var slot = GetSlot(slotIndex);
        if (slot == null) return;

        if (slot.root != null) slot.root.gameObject.SetActive(false);
        if (slot.placeholderLabel != null) slot.placeholderLabel.gameObject.SetActive(false);

        _scaleTarget[slotIndex]  = idleScale;
        _tintTarget[slotIndex]   = idleTint;
        _offsetTarget[slotIndex] = -idleDropPixels;
    }

    private PortraitSlot GetSlot(int i)
    {
        if (slots == null || i < 0 || i >= slots.Length) return null;
        return slots[i];
    }

    /// <summary>
    /// 현재값을 목표값에 즉시 붙인다. 대화를 새로 열 때 이전 대화의 강조 상태가
    /// 한 프레임 비쳤다가 사라지는 걸 막는다.
    /// </summary>
    private void SnapHighlight()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            _scaleNow[i]  = _scaleTarget[i];
            _offsetNow[i] = _offsetTarget[i];
            _tintNow[i]   = _tintTarget[i];
        }
        UpdateHighlight();
    }

    /// <summary>
    /// 크기·색·높이를 목표값으로 민다. timeScale=0 이라 unscaledDeltaTime 을 써야 한다.
    /// 지수 감쇠라 프레임레이트가 흔들려도 도착 시간이 같다.
    /// </summary>
    private void UpdateHighlight()
    {
        if (_scaleNow == null) return;

        float k = 1f - Mathf.Exp(-highlightSpeed * Time.unscaledDeltaTime);

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var slot = GetSlot(i);
            if (slot == null || slot.root == null) continue;
            if (!slot.root.gameObject.activeSelf) continue;

            _scaleNow[i]  = Mathf.Lerp(_scaleNow[i], _scaleTarget[i], k);
            _offsetNow[i] = Mathf.Lerp(_offsetNow[i], _offsetTarget[i], k);
            _tintNow[i]   = Color.Lerp(_tintNow[i], _tintTarget[i], k);

            slot.root.localScale     = Vector3.one * _scaleNow[i];
            slot.root.anchoredPosition = _slotHomePos[i] + new Vector2(0f, _offsetNow[i]);
            if (slot.image != null) slot.image.color = _tintNow[i];
        }
    }

    // ─── 타이핑 ──────────────────────────────────────────────────────
    private void StartTyping(string raw)
    {
        if (_typing != null) StopCoroutine(_typing);

        SetBodyText(raw);
        if (nextArrow != null) nextArrow.SetActive(false);

        if (charsPerSecond <= 0f || bodyText == null)
        {
            CompleteTyping();
            return;
        }

        _typingDone = false;
        _typing = StartCoroutine(TypeRoutine());
    }

    /// <summary>
    /// 태그를 살리려면 TMPTextEffectPlayer 를 거쳐야 한다.
    /// tmp.text 에 직접 넣으면 TMP 가 모르는 태그를 화면에 그대로 찍는다.
    /// </summary>
    private void SetBodyText(string raw)
    {
        if (bodyEffect != null) bodyEffect.SetText(raw);
        else if (bodyText != null) bodyText.text = raw;
    }

    /// <summary>
    /// maxVisibleCharacters 를 늘려서 글자를 드러낸다.
    /// Substring 으로 자르면 안 된다 — 리치 태그가 반토막 나서 화면에 그대로 찍힌다.
    /// </summary>
    private IEnumerator TypeRoutine()
    {
        bodyText.ForceMeshUpdate();
        int total = bodyText.textInfo.characterCount;
        bodyText.maxVisibleCharacters = 0;

        float shown = 0f;
        int lastSfxAt = 0;

        while (shown < total)
        {
            shown += charsPerSecond * Time.unscaledDeltaTime;
            int visible = Mathf.Min(total, Mathf.FloorToInt(shown));
            bodyText.maxVisibleCharacters = visible;

            int step = Mathf.Max(1, typeSfxEveryNChars);
            if (typeSfx != null && visible - lastSfxAt >= step)
            {
                lastSfxAt = visible;
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(typeSfx, typeSfxVolume);
            }

            yield return null;
        }

        CompleteTyping();
    }

    private void CompleteTyping()
    {
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }

        if (bodyText != null)
        {
            bodyText.ForceMeshUpdate();
            bodyText.maxVisibleCharacters = int.MaxValue;
        }

        _typingDone = true;
        if (nextArrow != null) nextArrow.SetActive(true);
    }

    // ─── 종료 ────────────────────────────────────────────────────────
    private void Finish()
    {
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }

        IsPlaying = false;
        _lines = null;
        _stage.Clear();

        if (panel != null) panel.SetActive(false);
        if (blockPlayerInput) SetPlayerInputBlocked(false);
        if (stopTime && GameManager.Instance != null) GameManager.Instance.SetTimeStop(false);
        UIEventBus.NotifyClose("Dialogue");

        // 콜백이 또 대화를 열 수 있으므로 비운 뒤에 부른다.
        var cb = _onComplete;
        _onComplete = null;
        cb?.Invoke();
    }

    private void SetPlayerInputBlocked(bool blocked)
    {
        var pc = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (pc != null) pc.SetInputBlocked(blocked);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터 전용. 플레이 모드에 들어가지 않고 Game 뷰에서 레이아웃만 보려고
    /// 5칸을 전부 채우고 대사창에 견본 문장을 넣는다.
    /// 대사창 비율·글자 크기·확대 배율은 실제로 눈으로 봐야 감이 오는 값들이라,
    /// 던전을 로딩해서 확인하는 것보다 이게 빠르다.
    /// </summary>
    /// <returns>토글한 뒤의 상태(켜졌으면 true).</returns>
    public bool EditorPreviewToggle()
    {
        bool on = panel == null || !panel.activeSelf;
        if (panel != null) panel.SetActive(on);
        if (!on) return false;

        if (nameBox != null) nameBox.SetActive(true);
        if (nameText != null) nameText.text = "본 마스터";
        if (bodyText != null)
        {
            bodyText.text = "여기에 대사가 들어간다. 줄이 길어지면 이렇게 넘어가니까, " +
                            "대사창 높이와 좌우 여백을 이 문장 기준으로 맞추면 된다.";
            bodyText.maxVisibleCharacters = int.MaxValue;
        }
        if (nextArrow != null) nextArrow.SetActive(true);

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var slot = GetSlot(i);
            if (slot == null || slot.root == null) continue;

            bool speaking = i == 2;   // 가운데 칸을 화자로 놓고 강조 대비를 본다
            slot.root.gameObject.SetActive(true);
            slot.root.localScale = Vector3.one * (speaking ? speakerScale : idleScale);

            if (slot.image != null)
            {
                slot.image.enabled = true;
                slot.image.color = speaking ? speakerTint : idleTint;
            }
            if (slot.placeholderLabel != null)
            {
                slot.placeholderLabel.gameObject.SetActive(true);
                slot.placeholderLabel.text = speaking ? "화자" : $"슬롯 {i}";
            }
        }
        return true;
    }
#endif

    /// <summary>
    /// 대화 중에 패널이 꺼지거나 씬이 바뀌면 timeScale 이 0 에 묶인 채로 남는다.
    /// SetTimeStop 은 참조 카운트가 없어서 아무도 대신 풀어주지 않으므로 여기서 확실히 푼다.
    /// </summary>
    private void OnDisable()
    {
        if (IsPlaying) Finish();
    }
}
