using System.Collections.Generic;
using UnityEngine;

/// <summary>한 캐릭터의 표정 하나.</summary>
[System.Serializable]
public class DialoguePortrait
{
    [Tooltip("표정 이름. CSV 에서 '키/표정' 으로 부른다 (예: bonemaster/angry).\n" +
             "비워두면 그 캐릭터의 기본 표정이 된다.")]
    public string expression;

    [Tooltip("초상화 스프라이트. 비어 있으면 플레이스홀더(이름만 뜨는 실루엣)로 대체된다.")]
    public Sprite sprite;
}

/// <summary>대사 CSV 가 부르는 캐릭터 키 하나의 정의.</summary>
[System.Serializable]
public class DialogueCastEntry
{
    [Tooltip("CSV 의 cast / speaker 칸에 적는 키. 영문 소문자를 권장한다 (예: bonemaster).")]
    public string key;

    [Tooltip("이름칸에 뜰 이름. CSV 의 name 칸이 비어 있을 때 이게 쓰인다.")]
    public string displayName;

    [Tooltip("이름칸 글자색.")]
    public Color nameColor = Color.white;

    [Tooltip("표정 목록. 첫 항목이 기본 표정이다.")]
    public List<DialoguePortrait> portraits = new List<DialoguePortrait>();
}

/// <summary>
/// 대사에 등장하는 캐릭터들의 명부.
///
/// CSV 에는 키(bonemaster)만 적고 실제 이름·초상화는 여기서 관리한다.
/// 초상화를 갈아끼울 때 CSV 를 건드리지 않아도 되고, 이름 표기를 한 번에 바꿀 수 있다.
///
/// 주의: 화자 이름을 몬스터 SO 의 minionName 이나 NPCBase.name 에서 끌어오지 않는다.
/// 그쪽은 절반이 'Enemy Skeleton Charger' 같은 내부 ID 라서 그대로 화면에 뜬다.
/// 이름의 단일 출처는 이 에셋(또는 CSV 의 name 칸)이다.
/// </summary>
[CreateAssetMenu(fileName = "DialogueCast", menuName = "Necromancer/Dialogue/Dialogue Cast")]
public class DialogueCastSO : ScriptableObject
{
    [Header("등장 인물")]
    [SerializeField] private List<DialogueCastEntry> entries = new List<DialogueCastEntry>();

    private Dictionary<string, DialogueCastEntry> _lookup;

    private void OnEnable()
    {
        _lookup = null;
    }

    /// <summary>"bonemaster/angry" 를 키와 표정으로 가른다.</summary>
    public static void SplitKey(string raw, out string key, out string expression)
    {
        key = raw;
        expression = null;
        if (string.IsNullOrEmpty(raw)) return;

        int slash = raw.IndexOf('/');
        if (slash < 0) return;

        key = raw.Substring(0, slash).Trim();
        expression = raw.Substring(slash + 1).Trim();
    }

    public DialogueCastEntry GetEntry(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        BuildLookup();
        return _lookup.TryGetValue(key, out var e) ? e : null;
    }

    /// <summary>표시 이름. 명부에 없는 키면 키를 그대로 돌려준다(오타를 화면에서 바로 보라고).</summary>
    public string GetDisplayName(string key)
    {
        var e = GetEntry(key);
        if (e == null) return key;
        return string.IsNullOrEmpty(e.displayName) ? key : e.displayName;
    }

    public Color GetNameColor(string key)
    {
        var e = GetEntry(key);
        return e != null ? e.nameColor : Color.white;
    }

    /// <summary>
    /// 초상화 스프라이트. 못 찾으면 null 을 돌려주고, 그 경우 DialogueUI 가
    /// 플레이스홀더(실루엣 + 이름)를 대신 그린다. 아트가 없어도 시스템이 돌게 하려는 것.
    /// </summary>
    public Sprite GetPortrait(string key, string expression)
    {
        var e = GetEntry(key);
        if (e == null || e.portraits == null || e.portraits.Count == 0) return null;

        if (!string.IsNullOrEmpty(expression))
        {
            foreach (var p in e.portraits)
            {
                if (string.Equals(p.expression, expression, System.StringComparison.OrdinalIgnoreCase))
                    return p.sprite;
            }
        }
        return e.portraits[0].sprite;   // 표정이 없거나 못 찾으면 기본 표정
    }

    private void BuildLookup()
    {
        if (_lookup != null) return;

        _lookup = new Dictionary<string, DialogueCastEntry>();
        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.key)) continue;
            _lookup[e.key] = e;
        }
    }
}
