using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보스방 전용 상단 체력바. BossRoomEvent 가 소환/클리어 시점에 Show/Hide 를 호출한다.
/// </summary>
public class BossHPBarUI : MonoBehaviour
{
    public static BossHPBarUI Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject root;             // 켜고 끌 패널
    [SerializeField] private Image fillImage;             // Filled / Horizontal
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private float delayCatchUpSpeed = 0.6f;

    private CharacterHealth _health;

    /// <summary>
    /// 체력바 아래에 뭔가를 달아야 할 때 쓰는 부모(= 켜고 끌 패널 자체).
    /// 여기 붙은 것은 체력바가 숨을 때 같이 숨는다. <see cref="BossCounterPipsUI"/> 가 쓴다.
    /// </summary>
    public RectTransform PipAnchor => root != null ? root.transform as RectTransform : transform as RectTransform;

    private void Awake()
    {
        Instance = this;
        if (root != null) root.SetActive(false);
    }

    private void OnDestroy()
    {
        Unbind();
        if (Instance == this) Instance = null;
    }

    public void Show(CharacterHealth health, string bossName)
    {
        Unbind();
        _health = health;
        if (_health == null) return;

        _health.UpdateHPBar += Refresh;
        _health.OnDeath     += Hide;

        if (nameText != null) nameText.text = bossName;
        if (root != null) root.SetActive(true);

        Refresh();
    }

    public void Hide()
    {
        Unbind();
        if (root != null) root.SetActive(false);
    }

    private void Unbind()
    {
        if (_health == null) return;
        _health.UpdateHPBar -= Refresh;
        _health.OnDeath     -= Hide;
        _health = null;
    }

    private void Refresh()
    {
        if (_health == null || fillImage == null) return;

        float max   = _health.MaxHP;
        float ratio = max > 0f ? Mathf.Clamp01(_health.CurHP / max) : 0f;

        fillImage.fillAmount = ratio;
        if (hpText != null)
            hpText.text = $"{Mathf.CeilToInt(Mathf.Max(_health.CurHP, 0f))} / {Mathf.CeilToInt(max)}";
    }
}