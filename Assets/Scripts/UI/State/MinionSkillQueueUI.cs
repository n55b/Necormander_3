using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 미니언 스킬 사용 가능 큐 UI — 화면 중앙 하단에 표시.
///
/// 동작:
///  - PlayerSkillController.OnQueueChanged 이벤트를 받아 큐 전체를 다시 그림
///  - 가장 먼저 사용 가능해진 스킬이 왼쪽, 이후 추가된 것이 오른쪽으로 쌓임
///  - 각 아이콘 아래에 남은 사용 가능 시간(timeRemaining)이 표시됨
///  - 타임아웃 또는 스킬 사용 시 해당 아이콘이 제거됨
///
/// GameManager에서 Initialize(skillController) 호출 필요.
/// </summary>
public class MinionSkillQueueUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    // 내부 클래스: 아이콘 슬롯 1개
    // ─────────────────────────────────────────────────────────────────────
    private class QueueSlot
    {
        public GameObject Root;
        public Image      MinionIcon;
        public Image      TimerFill;       // 남은 시간 게이지 (1→0 줄어듦)
        public TextMeshProUGUI TimerText;  // 남은 초 텍스트
        public PendingMinionSkill Bound;   // 연결된 큐 항목
    }

    // ─────────────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────────────
    [Header("아이콘 프리팹")]
    [Tooltip("미니언 아이콘 1개에 해당하는 프리팹. MinionIcon(Image), TimerFill(Image), TimerText(TMP) 자식을 포함해야 함")]
    [SerializeField] private GameObject slotPrefab;

    [Header("아이콘 컨테이너")]
    [Tooltip("아이콘들이 가로로 쌓일 부모 Transform (HorizontalLayoutGroup 권장)")]
    [SerializeField] private Transform iconContainer;
    [SerializeField] private GameObject spaceBarObj;

    // ─────────────────────────────────────────────────────────────────────
    // 런타임
    // ─────────────────────────────────────────────────────────────────────
    private PlayerSkillController _skillCtrl;
    private readonly List<QueueSlot> _slots = new List<QueueSlot>();

    // ─────────────────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────────────────
    public void Initialize(PlayerSkillController skillController)
    {
        _skillCtrl = skillController;
        if (_skillCtrl != null)
            _skillCtrl.OnQueueChanged += RebuildSlots;

        RebuildSlots();
        Debug.Log("<color=yellow>[MinionSkillQueueUI]</color> Initialized.");
    }

    private void OnDestroy()
    {
        if (_skillCtrl != null)
            _skillCtrl.OnQueueChanged -= RebuildSlots;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Update: 타이머 텍스트/Fill 매 프레임 갱신
    // ─────────────────────────────────────────────────────────────────────
    private void Update()
    {
        foreach (var slot in _slots)
        {
            if (slot.Bound == null) continue;
            float t   = slot.Bound.timeRemaining;
            float max = _skillCtrl != null ? _skillCtrl.skillTimeout : 1f;

            if (slot.TimerFill != null)
                slot.TimerFill.fillAmount = Mathf.Clamp01(t / max);

            if (slot.TimerText != null)
                slot.TimerText.text = t > 0.05f ? t.ToString("F1") : "";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 큐 변경 시 전체 재구성
    // ─────────────────────────────────────────────────────────────────────
    private void RebuildSlots()
    {
        // 기존 슬롯 전부 제거
        foreach (var s in _slots)
            if (s.Root != null) Destroy(s.Root);
        _slots.Clear();

        if (_skillCtrl == null) return;

        var pending = _skillCtrl.GetAllPendingSkills();
        foreach (var p in pending)
            AddSlot(p);
        
        // 큐가 비어있으면 SpaceBar 아이콘 숨김
        if (spaceBarObj != null)
        {
            if(pending.Count == 0)
                spaceBarObj.SetActive(false);
            else
                spaceBarObj.SetActive(true);
        }
    }

    private void AddSlot(PendingMinionSkill pending)
    {
        GameObject root;

        if (slotPrefab != null && iconContainer != null)
        {
            root = Instantiate(slotPrefab, iconContainer);
        }
        else
        {
            Debug.LogWarning("<color=orange>[MinionSkillQueueUI]</color> Slot prefab or container not set! Cannot create slot.");
            return; // 프리팹 또는 컨테이너가 없으면 슬롯 생성 불가
        }
        {
            spaceBarObj.SetActive(true);
        }

        var slot = new QueueSlot { Root = root, Bound = pending };

        // 자식 탐색
        var tf = root.transform;
        var iconT  = tf.Find("Icon_SkillQueue");
        var fillT  = tf.Find("Outline_SkillQueue");
        var textT  = tf.Find("TimerText");

        slot.MinionIcon = iconT  != null ? iconT.GetComponent<Image>()             : root.GetComponent<Image>();
        slot.TimerFill  = fillT  != null ? fillT.GetComponent<Image>()              : root.GetComponentInChildren<Image>();
        slot.TimerText  = textT  != null ? textT.GetComponent<TextMeshProUGUI>()   : root.GetComponentInChildren<TextMeshProUGUI>();

        // 아이콘 세팅
        if (slot.MinionIcon != null && pending.minionData != null)
            slot.MinionIcon.sprite = pending.minionData.minionIcon;

        // Fill 초기값
        if (slot.TimerFill != null)
        {
            slot.TimerFill.type        = Image.Type.Filled;
            slot.TimerFill.fillMethod  = Image.FillMethod.Radial360;
            slot.TimerFill.fillClockwise = false;
            slot.TimerFill.fillAmount  = 1f;
        }

        _slots.Add(slot);
    }
}
