using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// MinionStateUI(Temp) - 보유 미니언 목록(Q/E/R 슬롯)과 연계스킬 쿨타임을 Fill로 표시합니다.
/// 변경이 있을 때만 UI를 갱신하여 매 프레임 GC / SetActive 비용을 최소화합니다.
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
        [Tooltip("연계스킬 쿨타임 Fill (0=방금 발동, 1=쿨 완료)")]
        public Image SkillCoolFill;
        [Tooltip("쿨타임 남은 초 텍스트 (선택)")]
        public TextMeshProUGUI CooldownText;

        // ─── dirty 비교용 캐시 (인스펙터 비노출) ───────────────────
        [System.NonSerialized] public bool LastHas;
        [System.NonSerialized] public MinionDataSO LastData;

        [System.NonSerialized] public float LastFill = -1f;
        [System.NonSerialized] public float LastRemaining = -1f;
        [System.NonSerialized] public bool TextEmpty = true;
    }

    [Header("미니언 슬롯 Q / E / R")]
    [SerializeField] private MinionSlotUI[] minionSlots = new MinionSlotUI[3];

    private PlayerSkillController _skillCtrl;

    private const float FILL_THRESHOLD = 0.004f;
    private const float TEXT_THRESHOLD = 0.1f;   // 텍스트는 0.1초 단위로만 갱신

    // ─────────────────────────────────────────────────────────────────
    public void Initialize(PlayerSkillController skillController)
    {
        _skillCtrl = skillController;
        RefreshIcons();
        Debug.Log("<color=cyan>[MinionStateUI]</color> Initialized.");
    }

    private void Update()
    {
        if (_skillCtrl == null) return;
        UpdateAllSlots();
    }

    // ── 아이콘 새로고침 (인벤토리 변경 시 외부 호출) ─────────────────
    public void RefreshIcons()
    {
        for (int i = 0; i < minionSlots.Length; i++)
        {
            var s = minionSlots[i];
            if (s == null || s.SlotRoot == null) continue;

            MinionDataSO data = _skillCtrl != null ? _skillCtrl.GetEquippedMinion(i) : null;
            bool has = data != null;

            s.SlotRoot.SetActive(has);
            s.LastHas = has;
            s.LastData = data;
            if (!has) continue;

            if (s.MinionIcon != null)
            {
                s.MinionIcon.sprite = data.minionIcon;

                var tooltip = s.MinionIcon.GetComponent<SkillTooltipTrigger>();
                if (tooltip == null) tooltip = s.MinionIcon.gameObject.AddComponent<SkillTooltipTrigger>();
                if (data.minionSkill != null)
                    tooltip.SetData(data.minionSkill.skillName, data.minionSkill.description);
                else
                    tooltip.Clear();
            }
            if (s.SkillCoolFill != null) { s.SkillCoolFill.fillAmount = 0f; s.SkillCoolFill.gameObject.SetActive(false); }
            if (s.CooldownText != null) { s.CooldownText.text = ""; s.TextEmpty = true; }
            s.LastFill = 0f;
            s.LastRemaining = -1f;
        }
    }

    // ── 매 프레임 슬롯 업데이트 ──────────────────────────────────────
    private void UpdateAllSlots()
    {
        for (int i = 0; i < minionSlots.Length; i++)
        {
            var s = minionSlots[i];
            if (s == null || s.SlotRoot == null) continue;

            MinionDataSO data = _skillCtrl.GetEquippedMinion(i);
            bool has = data != null;

            // ── 슬롯 활성화 ───────────────────────
            if (has != s.LastHas)
            {
                s.LastHas = has;
                s.SlotRoot.SetActive(has);
            }

            // 교체된 경우도 감지해서 갱신해야 함 (has가 안 변해도 data가 바뀔 수 있어서)
            if (has && s.MinionIcon != null && data != s.LastData)
            {
                s.LastData = data;
                s.MinionIcon.sprite = data.minionIcon;

                var tooltip = s.MinionIcon.GetComponent<SkillTooltipTrigger>();
                if (tooltip == null) tooltip = s.MinionIcon.gameObject.AddComponent<SkillTooltipTrigger>();
                if (data.minionSkill != null)
                    tooltip.SetData(data.minionSkill.skillName, data.minionSkill.description);
                else
                    tooltip.Clear();
            }
            else if (!has)
            {
                s.LastData = null;
            }
            if (!has) continue;

            // ── 연계스킬 쿨타임 Fill (1=방금 발동, 0=쿨 완료) ──────────────
            if (data.minionSkill == null || s.SkillCoolFill == null) continue;

            float maxCd = data.minionSkill.cooldownTime;
            // 액티브(스페이스바)는 메인 소환수만 갖는다. 서브는 패시브라 쿨타임이 없다.
            float remaining = (i == InventoryManager.SLOT_MAIN) ? _skillCtrl.GetMainSummonCooldownRemaining() : 0f;
            bool onCd = remaining > 0.05f;
            float fill = (maxCd > 0f && onCd) ? Mathf.Clamp01(remaining / maxCd) : 0f;

            // 쿨타임 완료 시 Fill Image 자신을 꺼줌
            bool wasActive = s.SkillCoolFill.gameObject.activeSelf;
            if (wasActive != onCd)
                s.SkillCoolFill.gameObject.SetActive(onCd);

            if (onCd && Mathf.Abs(fill - s.LastFill) > FILL_THRESHOLD)
            {
                s.LastFill = fill;
                s.SkillCoolFill.fillAmount = fill;
            }

            // ── 쿨타임 텍스트 (0.1초 단위 변동 시만 갱신) ────────────
            if (s.CooldownText != null)
            {
                if (onCd)
                {
                    // 0.1초 단위로만 텍스트 갱신 → GC 빈도 감소
                    if (Mathf.Abs(remaining - s.LastRemaining) >= TEXT_THRESHOLD)
                    {
                        s.LastRemaining = remaining;
                        s.CooldownText.SetText("{0:F1}", remaining);
                        s.TextEmpty = false;
                    }
                }
                else if (!s.TextEmpty)
                {
                    s.CooldownText.text = "";
                    s.TextEmpty = true;
                    s.LastRemaining = -1f;
                }
            }
        }
    }
}
