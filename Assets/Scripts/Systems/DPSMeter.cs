using UnityEngine;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine.UI;
#endif

public class DPSMeter : MonoBehaviour
{
    [Header("UI 설정")]
    [Tooltip("플레이어 스탯과 DPS 미터를 함께 표시합니다. F5: 함께 토글 / F4: DPS 기록만 초기화")]
    [SerializeField] private bool _showUI = false;

    // 릴리스 빌드(Development Build 체크 해제)에서는 아래 코드 전체가 컴파일되지 않는다.
    // F4/F5 입력, 패널 표시, 피해 기록이 모두 빠지고 에디터와 Development Build에서만 동작한다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private float _totalDamage = 0f;
    private Dictionary<string, float> _damageBySource = new Dictionary<string, float>();
    private Dictionary<string, Dictionary<string, float>> _damageBySkill = new Dictionary<string, Dictionary<string, float>>();
    private float _startTime = 0f;
    private bool _isRecording = false;

    private GameObject _debugCanvas;
    private TMP_Text _damageLabel;
    private TMP_Text _statsLabel;
    private string _statsText = "플레이어를 기다리는 중...";
    private float _nextStatsRefresh;
    private static readonly StatusType[] StatusTypes = (StatusType[])System.Enum.GetValues(typeof(StatusType));

    // GameManager/Data Systems에 이미 배치된 컴포넌트다. GameManager와 함께 교체된다.
    // 자식에서 DontDestroyOnLoad를 호출하면 경고만 발생하므로 별도 영속화를 하지 않는다.

    private void OnEnable()
    {
        DamageEventBus.OnDamageReceived += HandleDamage;
    }

    private void OnDisable()
    {
        DamageEventBus.OnDamageReceived -= HandleDamage;
        if (_debugCanvas != null) _debugCanvas.SetActive(false);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
        {
            _showUI = !_showUI;
            _nextStatsRefresh = 0f;
        }
        if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
        {
            ResetMeter();
        }

        if (Application.isPlaying)
        {
            if (_showUI && _debugCanvas == null) CreateOverlay();
            if (_debugCanvas != null && _debugCanvas.activeSelf != _showUI)
                _debugCanvas.SetActive(_showUI);
        }

        // 닫혀 있을 때는 스탯 조회/문자열 생성을 하지 않는다. 시간 정지 중에도 갱신한다.
        if (_showUI && Time.unscaledTime >= _nextStatsRefresh)
        {
            _statsText = BuildStatsText(GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null);
            _nextStatsRefresh = Time.unscaledTime + 0.1f;
            if (_statsLabel != null) _statsLabel.text = _statsText;
            if (_damageLabel != null) _damageLabel.text = BuildDamageText();
        }
    }

    private void HandleDamage(CharacterHealth target, DamageInfo info)
    {
        if (target == null) return;
        
        var stat = target.GetComponent<CharacterStat>();
        // 적이 입은 데미지만 카운트 (플레이어/미니언이 때린 것)
        if (stat == null || !stat.IsEnemy) return; 

        if (!_isRecording)
        {
            _isRecording = true;
            _startTime = Time.time;
        }

        float dmg = info.amount;
        _totalDamage += dmg;

        string sourceName = "Unknown";
        if (info.attacker != null)
        {
            sourceName = info.attacker.name;
        }
        else if (!string.IsNullOrEmpty(info.popupText))
        {
            sourceName = info.popupText; // e.g. "Poison", "Bleed Pop"
        }
        else
        {
            sourceName = info.type.ToString() + " Damage";
        }

        string skillName = string.IsNullOrEmpty(info.popupText) ? (info.category == DamageCategory.BasicAttack ? "Basic Attack" : "Unknown Skill") : info.popupText;

        if (!_damageBySource.ContainsKey(sourceName))
        {
            _damageBySource[sourceName] = 0f;
            _damageBySkill[sourceName] = new Dictionary<string, float>();
        }
        _damageBySource[sourceName] += dmg;

        if (!_damageBySkill[sourceName].ContainsKey(skillName))
        {
            _damageBySkill[sourceName][skillName] = 0f;
        }
        _damageBySkill[sourceName][skillName] += dmg;
    }

    private void ResetMeter()
    {
        _totalDamage = 0f;
        _damageBySource.Clear();
        _damageBySkill.Clear();
        _isRecording = false;
        _startTime = Time.time;
    }

    private void CreateOverlay()
    {
        _debugCanvas = new GameObject("DPS and Player Stats", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        _debugCanvas.transform.SetParent(transform, false);
        var canvas = _debugCanvas.GetComponent<Canvas>();
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = 30000;
        _debugCanvas.AddComponent<CanvasCameraBinder>();

        var damage = CreateColumn("Damage", 0f, "<color=#DBBD9E>DPS Meter</color>   F5 / F4");
        _damageLabel = damage;
        _statsLabel = CreateColumn("Stats", .5f, "<color=#B1DFCE>플레이어 스탯</color>   F5");
    }

    private TMP_Text CreateColumn(string title, float leftAnchor, string heading)
    {
        var panel = new GameObject(title, typeof(RectTransform), typeof(ScrollRect));
        var bounds = (RectTransform)panel.transform;
        bounds.SetParent(_debugCanvas.transform, false);
        bounds.anchorMin = new Vector2(leftAnchor, 0f);
        bounds.anchorMax = new Vector2(leftAnchor + .5f, 1f);
        bounds.offsetMin = new Vector2(8f, 12f);
        bounds.offsetMax = new Vector2(-8f, -28f);
        UIChrome.Panel(bounds);

        var header = CreateLabel("Heading", bounds);
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = Vector2.one;
        header.rectTransform.offsetMin = new Vector2(8f, -20f);
        header.rectTransform.offsetMax = new Vector2(-8f, -5f);
        header.text = heading;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        viewport.SetParent(bounds, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8f, 6f);
        viewport.offsetMax = new Vector2(-8f, -23f);

        var label = CreateLabel("Content", viewport);
        var content = label.rectTransform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var fitter = label.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = panel.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;
        return label;
    }

    private static TMP_Text CreateLabel(string objectName, RectTransform parent)
    {
        var label = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false);
        PixelText.Configure(label, 12);
        label.raycastTarget = false;
        return label;
    }

    private string BuildDamageText()
    {
        float duration = _isRecording ? Time.time - _startTime : 0f;
        float dps = duration > 0f ? _totalDamage / duration : 0f;
        var text = new StringBuilder(640);
        text.AppendLine($"Total Damage: {_totalDamage:F1}");
        text.AppendLine($"Duration: {duration:F1}s");
        text.AppendLine($"<b>Total DPS: <color=cyan>{dps:F1}</color></b>");
        text.AppendLine("\n<b><color=orange>[Damage by Source]</color></b>");
        foreach (var source in _damageBySource)
        {
            float percentage = _totalDamage > 0f ? source.Value / _totalDamage * 100f : 0f;
            text.AppendLine($"- <b>{source.Key}</b>: {source.Value:F1} ({percentage:F1}%)");
            foreach (var skill in _damageBySkill[source.Key])
            {
                float skillPercentage = source.Value > 0f ? skill.Value / source.Value * 100f : 0f;
                text.AppendLine($"    <color=#cccccc>{skill.Key}</color>: {skill.Value:F1} ({skillPercentage:F1}%)");
            }
        }
        return text.ToString();
    }

    private static string BuildStatsText(PlayerController player)
    {
        if (player == null || player.Stat == null) return "플레이어를 기다리는 중...";
        CharacterStat stat = player.Stat;
        var text = new StringBuilder(1400);
        text.AppendLine("<b>[체력 / 방어]</b>");
        text.AppendLine($"체력: {stat.CURHP:F1} / {stat.MAXHP:F1}");
        text.AppendLine($"방어력 (피해 감소): {stat.DEF:F1}%");
        text.AppendLine($"회피율: {stat.EVASION * 100f:F1}%");
        text.AppendLine($"보호막: {(stat.Status != null ? stat.Status.TotalShield : 0f):F1}");
        text.AppendLine($"무적: {(stat.Health != null && stat.Health.Invincible ? "ON" : "OFF")} / 사망: {stat.IsDead}");
        text.AppendLine("\n<b>[공격]</b>");
        text.AppendLine($"물리 공격력: {stat.ATK:F2}");
        text.AppendLine($"마법 공격력: {stat.MAGIC:F2}");
        text.AppendLine($"물리 피해 증폭: {stat.PHYS_AMP * 100f:F1}%");
        text.AppendLine($"마법 피해 증폭: {stat.MAGIC_AMP * 100f:F1}%");
        text.AppendLine($"공격 속도: {stat.ATKSPD:F2}회/초");
        text.AppendLine($"스탯 기준 공격 간격: {stat.AttackInterval:F3}초");
        text.AppendLine($"치명타 확률: {stat.CRIT_CHANCE:F1}%");
        text.AppendLine($"행운 (Luck): {stat.LUCK:F1}%");
        text.AppendLine($"치명타 피해: {stat.CRIT_DAMAGE:F1}%");
        text.AppendLine($"적중률: {stat.ACCURACY * 100f:F1}%");
        text.AppendLine($"기본 공격 스탯 배율: x{stat.BASIC_ATK_MULT:F2}");
        text.AppendLine("장비/미니언의 타격별 배율·모션 시간은 별도 (C: 장착 정보)");
        text.AppendLine("\n<b>[이동 / 재사용]</b>");
        text.AppendLine($"이동 속도: {stat.MOVESPEED:F2}");
        text.AppendLine($"스킬 쿨타임 감소: {stat.SKILL_CDR * 100f:F1}%");
        text.AppendLine($"대쉬 쿨타임 감소: {stat.DASH_CDR * 100f:F1}%");
        var dodge = player.GetComponent<MeleeDodgeController>();
        if (dodge != null)
        {
            text.AppendLine($"대쉬 횟수: {dodge.CurrentCharges} / {dodge.MaxCharges}");
            text.AppendLine($"대쉬 충전 시간: {stat.ApplyDashCooldown(dodge.RechargeTime):F2}초");
            text.AppendLine($"대쉬 충전 진행: {dodge.RechargeProgress * 100f:F1}%");
        }
        var skill = player.GetComponent<PlayerSkillController>();
        if (skill != null)
            text.AppendLine($"소환수 스킬 남은 쿨타임: {skill.GetMainSummonCooldownRemaining():F2}초 / 사용 중: {skill.IsMainSummonBusy} / 스왑 쿨타임: {skill.SwapCooldownRemaining:F2}초");
        text.AppendLine("\n<b>[상태 / 원본값]</b>");
        text.AppendLine($"플레이어 상태: {player.GetPlayerState()}");
        text.AppendLine($"입력 차단: {player.IsInputBlocked} / 행동 불가: {player.IsCCed}");
        if (stat.Status != null)
        {
            text.AppendLine($"슈퍼아머: {stat.Status.HasSuperArmor} ({stat.Status.SuperArmorGauge:F1} / {stat.Status.MaxSuperArmorGauge:F1})");
            text.Append("상태이상:");
            bool any = false;
            foreach (StatusType type in StatusTypes)
            {
                if (type == StatusType.None || !stat.Status.HasStatus(type)) continue;
                text.Append(' ').Append(type);
                any = true;
            }
            text.AppendLine(any ? "" : " 없음");
        }
        text.AppendLine($"기본 최대 체력: {stat.BaseMaxHP:F1}");
        text.AppendLine($"기본 공격력: {stat.BaseAtk:F2}");
        text.AppendLine($"기본 이동 속도: {stat.BaseMoveSpeed:F2}");
        return text.ToString();
    }
#endif
}
