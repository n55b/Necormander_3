using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 체력바 바로 아래에 뜨는 <b>카운터 성공 표시</b>(구슬 3개).
/// 평소엔 아예 없고, 카운터를 여러 번 성공해야 하는 패턴(집행)이 시작될 때만 나타난다.
/// 검은 구슬로 켜졌다가 한 번 성공할 때마다 왼쪽부터 하나씩 파랗게 칠해진다.
///
/// <para>
/// [씬에 배선하지 않는 이유] 보스 체력바(<see cref="BossHPBarUI"/>)는 프리팹이 아니라
/// 씬 3개(BattleScene / BossTestScene / EliteTestScene)에 각각 직접 박혀 있다. 씬마다 손으로
/// 끼우면 하나만 빠져도 그 씬에서 조용히 안 뜨므로, Resources 에서 프리팹을 읽어 체력바 밑에
/// 런타임으로 붙인다. 모양·색·간격을 바꾸고 싶으면 프리팹만 열면 된다.
/// </para>
/// </summary>
public class BossCounterPipsUI : MonoBehaviour
{
    /// <summary>Assets/Prefabs/Resources/UI/Boss Counter Pips.prefab</summary>
    private const string ResourcePath = "UI/Boss Counter Pips";

    [Tooltip("왼쪽부터 순서대로. 배열 길이가 곧 표시할 수 있는 최대 카운터 횟수다.")]
    [SerializeField] private Image[] pips;
    [Tooltip("아직 성공하지 못한 구슬 색.")]
    [SerializeField] private Color emptyColor = new Color(0.09f, 0.09f, 0.11f, 1f);
    [Tooltip("카운터에 성공한 구슬 색.")]
    [SerializeField] private Color filledColor = new Color(0.25f, 0.62f, 1f, 1f);

    private static BossCounterPipsUI _instance;
    private int _total;

    /// <summary>구슬 <paramref name="total"/>개를 전부 빈 상태로 띄운다.</summary>
    public static void Show(int total)
    {
        BossCounterPipsUI ui = Ensure();
        if (ui == null) return;

        ui.gameObject.SetActive(true);
        ui.Render(total, 0);
    }

    /// <summary>왼쪽부터 <paramref name="filled"/>개를 성공 색으로 칠한다.</summary>
    public static void SetFilled(int filled)
    {
        if (_instance == null) return;
        _instance.Render(_instance._total, filled);
    }

    /// <summary>
    /// 표시를 끈다. 패턴이 <b>어떤 이유로 끝나든</b>(성공/중단/사망) 반드시 불러야 한다 —
    /// 안 부르면 전투가 끝난 뒤에도 구슬이 화면에 남는다.
    /// </summary>
    public static void Hide()
    {
        if (_instance != null) _instance.gameObject.SetActive(false);
    }

    private static BossCounterPipsUI Ensure()
    {
        if (_instance != null) return _instance;

        BossHPBarUI bar = BossHPBarUI.Instance;
        if (bar == null || bar.PipAnchor == null) return null;

        var prefab = Resources.Load<BossCounterPipsUI>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[BossCounterPips] Resources/{ResourcePath} 를 못 찾았다. 카운터 구슬 없이 진행한다.");
            return null;
        }

        _instance = Instantiate(prefab, bar.PipAnchor);
        _instance.transform.localScale = Vector3.one;
        return _instance;
    }

    private void Render(int total, int filled)
    {
        if (pips == null) return;

        _total = Mathf.Clamp(total, 0, pips.Length);
        for (int i = 0; i < pips.Length; i++)
        {
            if (pips[i] == null) continue;
            // 요구 개수가 구슬 수보다 적으면 남는 것은 아예 숨긴다(빈 구슬로 남겨두면 거짓말이 된다).
            pips[i].gameObject.SetActive(i < _total);
            pips[i].color = i < filled ? filledColor : emptyColor;
        }
    }
}
