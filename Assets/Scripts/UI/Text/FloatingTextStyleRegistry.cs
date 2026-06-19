using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FloatingTextStyleSO 목록을 typeKey로 빠르게 찾을 수 있게 관리합니다.
/// FloatingTextManager 같은 오브젝트에 붙이고 styles 배열에 SO들을 등록하세요.
/// </summary>
public class FloatingTextStyleRegistry : MonoBehaviour
{
    public static FloatingTextStyleRegistry Instance { get; private set; }

    [Header("등록된 스타일들")]
    [SerializeField] private FloatingTextStyleSO[] styles;

    [Header("매칭되는 스타일이 없을 때 사용할 기본 스타일")]
    [SerializeField] private FloatingTextStyleSO defaultStyle;

    private Dictionary<string, FloatingTextStyleSO> _map;

    private void Awake()
    {
        Instance = this;

        _map = new Dictionary<string, FloatingTextStyleSO>(styles.Length);
        foreach (var s in styles)
            if (s != null && !string.IsNullOrEmpty(s.typeKey))
                _map[s.typeKey] = s;
    }

    /// <summary>typeKey에 맞는 스타일을 반환합니다. 없으면 defaultStyle, 그것도 없으면 null.</summary>
    public FloatingTextStyleSO GetStyle(string typeKey)
    {
        if (!string.IsNullOrEmpty(typeKey) && _map.TryGetValue(typeKey, out var style))
            return style;
        return defaultStyle;
    }
}
