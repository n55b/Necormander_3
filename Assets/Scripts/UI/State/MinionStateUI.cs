using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// MinionStateUI(Temp) - 보유 미니언 목록(Q/E/R 슬롯)과 연계스킬 쿨타임을 Fill로 표시합니다.
/// GameManager에서 Initialize(allyManager, skillController)를 호출해야 합니다.
/// </summary>
public class MinionStateUI : MonoBehaviour
{
    [System.Serializable]
    public class MinionSlotUI
    {
        [Tooltip("슬롯 루트 오브젝트 (비활성화 = 비어있음)")]
        public GameObject SlotRoot;
        [Tooltip("미니언 아이콘 이미지")]
        public Image MinionIcon;
        [Tooltip("연계스킬 쿨타임 Fill (1=방금 발동, 0=쿨 완료)")]
        public Image SkillCoolFill;
        [Tooltip("쿨타임 남은 초 텍스트 (선택)")]
        public TextMeshProUGUI CooldownText;
        [Tooltip("사망 오버레이 이미지 (선택)")]
        public Image DeadOverlay;
    }

    [Header("미니언 슬롯 Q / E / R")]
    [SerializeField] private MinionSlotUI[] minionSlots = new MinionSlotUI[3];

    private AllyManager           _allyManager;
    private PlayerSkillController _skillCtrl;

    // ─────────────────────────────────────────────────────
    public void Initialize(AllyManager allyManager, PlayerSkillController skillController)
    {
        _allyManager = allyManager;
        _skillCtrl   = skillController;
        RefreshIcons();
        Debug.Log("<color=cyan>[MinionStateUI]</color> Initialized.");
    }

    private void Update()
    {
        if (_skillCtrl == null) return;
        UpdateAllSlots();
    }

    // ── 아이콘 새로고침 (인벤토리 변경 시 외부 호출 가능) ────────────
    public void RefreshIcons()
    {
        for (int i = 0; i < minionSlots.Length; i++)
        {
            var s = minionSlots[i];
            if (s == null || s.SlotRoot == null) continue;

            MinionDataSO data = _skillCtrl != null ? _skillCtrl.GetEquippedMinion(i) : null;
            bool has = data != null;
            s.SlotRoot.SetActive(has);
            if (!has) continue;

            if (s.MinionIcon    != null) s.MinionIcon.sprite       = data.minionIcon;
            if (s.SkillCoolFill != null) s.SkillCoolFill.fillAmount = 0f;
            if (s.CooldownText  != null) s.CooldownText.text        = "";
        }
    }

    // ── 매 프레임 슬롯 업데이트 ─────────────────────────────────
    private void UpdateAllSlots()
    {
        for (int i = 0; i < minionSlots.Length; i++)
        {
            var s = minionSlots[i];
            if (s == null || s.SlotRoot == null) continue;

            MinionDataSO data = _skillCtrl.GetEquippedMinion(i);
            bool has = data != null;

            if (s.SlotRoot.activeSelf != has) s.SlotRoot.SetActive(has);
            if (!has) continue;

            bool isDead = IsInfoDead(data);
            if (s.DeadOverlay != null) s.DeadOverlay.gameObject.SetActive(isDead);
            if (s.MinionIcon  != null) s.MinionIcon.color = isDead ? new Color(1f,1f,1f,0.4f) : Color.white;

            if (data.minionSkill == null || s.SkillCoolFill == null) continue;

            float maxCd     = data.minionSkill.cooldownTime;
            float remaining = _skillCtrl.GetMinionSkillCooldownRemaining((PlayerSkillController.SkillSlot)i);

            // 쿨타임이 지나면서 게이지가 채워진다 (0 → 1)
            float fill = (maxCd > 0f && remaining > 0f)
                ? 1f - Mathf.Clamp01(remaining / maxCd)
                : 1f;

            s.SkillCoolFill.fillAmount = fill;

            if (s.CooldownText != null)
                s.CooldownText.text = remaining > 0.05f ? remaining.ToString("F1") : "";
        }
    }

    private bool IsInfoDead(MinionDataSO data)
    {
        if (_allyManager == null || data == null) return false;
        foreach (var info in _allyManager.ActiveMinionInfos)
            if (info.Data == data) return info.IsDead;
        return false;
    }
}
