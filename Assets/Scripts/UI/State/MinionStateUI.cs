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
        [Tooltip("사망 오버레이 이미지 (선택)")]
        public Image DeadOverlay;

        // ─── dirty 비교용 캐시 (인스펙터 비노출) ───────────────────
        [System.NonSerialized] public bool   LastHas;
        [System.NonSerialized] public bool   LastDead;
        [System.NonSerialized] public float  LastFill   = -1f;
        [System.NonSerialized] public float  LastRemaining = -1f;
        [System.NonSerialized] public bool   TextEmpty  = true;
    }

    [Header("미니언 슬롯 Q / E / R")]
    [SerializeField] private MinionSlotUI[] minionSlots = new MinionSlotUI[3];

    private AllyManager           _allyManager;
    private PlayerSkillController _skillCtrl;

    private static readonly Color COLOR_ALIVE = Color.white;
    private static readonly Color COLOR_DEAD  = new Color(1f, 1f, 1f, 0.4f);
    private const float FILL_THRESHOLD = 0.004f;
    private const float TEXT_THRESHOLD = 0.1f;   // 텍스트는 0.1초 단위로만 갱신

    // ─────────────────────────────────────────────────────────────────
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
            s.LastHas  = has;
            s.LastDead = false;

            if (!has) continue;

            if (s.MinionIcon    != null) s.MinionIcon.sprite       = data.minionIcon;
            if (s.MinionIcon    != null) s.MinionIcon.color        = COLOR_ALIVE;
            if (s.SkillCoolFill != null) s.SkillCoolFill.fillAmount = 0f;
            if (s.CooldownText  != null) { s.CooldownText.text = ""; s.TextEmpty = true; }
            s.LastFill      = 0f;
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

            // ── 슬롯 활성화 ─────────────────────────────────────────
            if (has != s.LastHas)
            {
                s.LastHas = has;
                s.SlotRoot.SetActive(has);
                if (has && s.MinionIcon != null)
                    s.MinionIcon.sprite = data.minionIcon;
            }
            if (!has) continue;

            // ── 사망 상태 ───────────────────────────────────────────
            bool isDead = IsInfoDead(data);
            if (isDead != s.LastDead)
            {
                s.LastDead = isDead;
                if (s.DeadOverlay != null) s.DeadOverlay.gameObject.SetActive(isDead);
                if (s.MinionIcon  != null) s.MinionIcon.color = isDead ? COLOR_DEAD : COLOR_ALIVE;
            }

            // ── 연계스킬 쿨타임 Fill ─────────────────────────────────
            if (data.minionSkill == null || s.SkillCoolFill == null) continue;

            float maxCd     = data.minionSkill.cooldownTime;
            float remaining = _skillCtrl.GetMinionSkillCooldownRemaining((PlayerSkillController.SkillSlot)i);
            float fill      = (maxCd > 0f && remaining > 0f)
                ? 1f - Mathf.Clamp01(remaining / maxCd)
                : 1f;

            if (Mathf.Abs(fill - s.LastFill) > FILL_THRESHOLD)
            {
                s.LastFill = fill;
                s.SkillCoolFill.fillAmount = fill;
            }

            // ── 쿨타임 텍스트 (0.1초 단위 변동 시만 갱신) ────────────
            if (s.CooldownText != null)
            {
                bool onCd = remaining > 0.05f;
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
                    s.TextEmpty         = true;
                    s.LastRemaining     = -1f;
                }
            }
        }
    }

    // ── AllyManager에서 사망 여부 조회 ──────────────────────────────
    private bool IsInfoDead(MinionDataSO data)
    {
        if (_allyManager == null || data == null) return false;
        var infos = _allyManager.ActiveMinionInfos;
        for (int i = 0; i < infos.Count; i++)
            if (infos[i].Data == data) return infos[i].IsDead;
        return false;
    }
}
