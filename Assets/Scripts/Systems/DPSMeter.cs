using UnityEngine;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;

public class DPSMeter : MonoBehaviour
{
    [Header("UI Settings")]
    [Tooltip("플레이어 스탯과 DPS 미터를 함께 표시합니다. F5: 함께 토글 / F4: DPS 기록만 초기화")]
    [SerializeField] private bool _showUI = false;

    private float _totalDamage = 0f;
    private Dictionary<string, float> _damageBySource = new Dictionary<string, float>();
    private Dictionary<string, Dictionary<string, float>> _damageBySkill = new Dictionary<string, Dictionary<string, float>>();
    private float _startTime = 0f;
    private bool _isRecording = false;

    private GUIStyle _panelStyle;
    private GUIStyle _labelStyle;
    private Vector2 _damageScroll;
    private Vector2 _statsScroll;
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

        // 닫혀 있을 때는 스탯 조회/문자열 생성을 하지 않는다. 시간 정지 중에도 갱신한다.
        if (_showUI && Time.unscaledTime >= _nextStatsRefresh)
        {
            _statsText = BuildStatsText(GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null);
            _nextStatsRefresh = Time.unscaledTime + 0.1f;
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

    private void OnGUI()
    {
        if (!_showUI) return;

        if (_panelStyle == null)
        {
            _panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };
            _labelStyle.normal.textColor = Color.white;
        }

        float top = Mathf.Min(150f, Screen.height * 0.15f);
        float width = Mathf.Min(380f, (Screen.width - 30f) * 0.5f);
        float height = Mathf.Max(100f, Screen.height - top - 10f);
        DrawDamagePanel(new Rect(10f, top, width, height));
        GUILayout.BeginArea(new Rect(Screen.width - width - 10f, top, width, height), _panelStyle);
        GUILayout.Label("<b><color=cyan>[플레이어 스탯]</color></b> F5: 함께 토글", _labelStyle);
        GUILayout.Label("현재 계산값 · 0.1초 갱신 · 스크롤 가능", _labelStyle);
        _statsScroll = GUILayout.BeginScrollView(_statsScroll);
        GUILayout.Label(_statsText, _labelStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawDamagePanel(Rect rect)
    {

        float duration = _isRecording ? (Time.time - _startTime) : 0f;
        float dps = duration > 0f ? _totalDamage / duration : 0f;

        GUILayout.BeginArea(rect, _panelStyle);
        GUILayout.Label("<b><color=yellow>[DPS Meter]</color></b> F5: 함께 토글 / F4: 리셋", _labelStyle);
        GUILayout.Label($"Total Damage: {_totalDamage:F1}", _labelStyle);
        GUILayout.Label($"Duration: {duration:F1}s", _labelStyle);
        GUILayout.Label($"<b>Total DPS: <color=cyan>{dps:F1}</color></b>", _labelStyle);
        GUILayout.Space(10);
        
        _damageScroll = GUILayout.BeginScrollView(_damageScroll);
        GUILayout.Label("<b><color=orange>[Damage by Source]</color></b>", _labelStyle);
        foreach (var kvp in _damageBySource)
        {
            float percentage = _totalDamage > 0 ? (kvp.Value / _totalDamage) * 100f : 0f;
            GUILayout.Label($"- <b>{kvp.Key}</b>: {kvp.Value:F1} ({percentage:F1}%)", _labelStyle);

            if (_damageBySkill.ContainsKey(kvp.Key))
            {
                foreach (var skillKvp in _damageBySkill[kvp.Key])
                {
                    float skillPercentage = kvp.Value > 0 ? (skillKvp.Value / kvp.Value) * 100f : 0f;
                    GUILayout.Label($"   └ <color=#cccccc>{skillKvp.Key}</color>: {skillKvp.Value:F1} ({skillPercentage:F1}%)", _labelStyle);
                }
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
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
            text.AppendLine($"미니언 스킬 남은 쿨타임: {skill.GetMainSummonCooldownRemaining():F2}초 / 사용 중: {skill.IsMainSummonBusy}");
        text.AppendLine("\n<b>[가드 / 자원]</b>");
        var guard = player.GetComponent<PlayerParryController>();
        if (guard != null)
        {
            text.AppendLine($"가드 게이지: {guard.GuardAmount:F1} / {guard.GuardCapacity:F1}");
            text.AppendLine($"방어 중: {guard.IsParrying} / 소진 잠금: {guard.GuardBroken}");
            text.AppendLine($"최근 퍼펙트 가드: {guard.LastBlockWasPerfect}");
        }
        if (player.STAMINA != null)
            text.AppendLine($"스태미나: {player.STAMINA.CurrentStamina:F1} / {player.STAMINA.MaxStamina:F1}");
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
}
