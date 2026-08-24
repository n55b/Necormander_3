using UnityEngine;
using System.Collections.Generic;

public class DPSMeter : MonoBehaviour
{
    [Header("UI Settings")]
    [Tooltip("체크 시 게임 화면에 DPS 미터기 IMGUI가 표시됩니다. (인게임 F3 키로도 토글 가능)")]
    [SerializeField] private bool _showUI = false;

    private float _totalDamage = 0f;
    private Dictionary<string, float> _damageBySource = new Dictionary<string, float>();
    private Dictionary<string, Dictionary<string, float>> _damageBySkill = new Dictionary<string, Dictionary<string, float>>();
    private float _startTime = 0f;
    private bool _isRecording = false;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

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
        if (Input.GetKeyDown(KeyCode.F3))
        {
            _showUI = !_showUI;
        }
        if (Input.GetKeyDown(KeyCode.F4))
        {
            ResetMeter();
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

        float duration = _isRecording ? (Time.time - _startTime) : 0f;
        float dps = duration > 0f ? _totalDamage / duration : 0f;

        // 스타일 설정
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = 14;
        style.normal.textColor = Color.white;

        GUILayout.BeginArea(new Rect(10, 150, 300, 500), style);
        GUILayout.Label($"<b><color=yellow>[DPS Meter]</color></b> (F3: 끄기/켜기, F4: 리셋)");
        GUILayout.Label($"Total Damage: {_totalDamage:F1}");
        GUILayout.Label($"Duration: {duration:F1}s");
        GUILayout.Label($"<b>Total DPS: <color=cyan>{dps:F1}</color></b>");
        GUILayout.Space(10);
        
        GUILayout.Label("<b><color=orange>[Damage by Source]</color></b>");
        foreach (var kvp in _damageBySource)
        {
            float percentage = _totalDamage > 0 ? (kvp.Value / _totalDamage) * 100f : 0f;
            GUILayout.Label($"- <b>{kvp.Key}</b>: {kvp.Value:F1} ({percentage:F1}%)");

            if (_damageBySkill.ContainsKey(kvp.Key))
            {
                foreach (var skillKvp in _damageBySkill[kvp.Key])
                {
                    float skillPercentage = kvp.Value > 0 ? (skillKvp.Value / kvp.Value) * 100f : 0f;
                    GUILayout.Label($"   └ <color=#cccccc>{skillKvp.Key}</color>: {skillKvp.Value:F1} ({skillPercentage:F1}%)");
                }
            }
        }
        GUILayout.EndArea();
    }
}
