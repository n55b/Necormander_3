using TMPro;
using UnityEngine;

/// <summary>
/// 프리팹/씬에 한국어로 적어 둔 고정 TMP 글자를 선택 언어로 바꿔 보여준다.
/// 한국어 원문은 TMP 텍스트 그대로 편집하고, key만 여기에 적는다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocTMP : MonoBehaviour
{
    [SerializeField] string key;

    TMP_Text text;
    string korean;

    void Awake()
    {
        text = GetComponent<TMP_Text>();
        korean = text.text;
    }

    void OnEnable()
    {
        Apply();
        Loc.Changed += Apply;
    }

    void OnDisable() => Loc.Changed -= Apply;

    void Apply() => text.text = Loc.T(key, korean);
}
