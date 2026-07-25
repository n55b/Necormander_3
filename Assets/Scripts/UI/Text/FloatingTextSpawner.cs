using UnityEngine;
using UnityEngine.UI;

public class FloatingTextSpawner : MonoBehaviour
{

    private CharacterStatus status;
    private CharacterStat stats;

    [SerializeField] private Transform vec_float;

    [Header("색상 설정 (공용)")]
    [Tooltip("상태이상/데미지 텍스트 색을 모아둔 팔레트(StatusEffectPalette). 데미지 숫자는 비워두면 코드 내장 기본값으로 폴백하지만, 상태이상 텍스트는 비우면 아예 뜨지 않습니다.")]
    [SerializeField] private StatusEffectPalette colorConfig;

    [SerializeField] private bool isSubscribed = false;

    // CharacterStat에서 호출해줄 초기화 함수
    public void Initialize(CharacterStat characterStat)
    {
        if (isSubscribed) return;

        this.stats = characterStat;

        if (stats == null) Debug.Log("stats가 null임");
        if (stats.Health == null) Debug.Log("health가 null");

        if (stats != null && stats.Health != null)
        {
            stats.Health.TakeDamageEvent += ShowDamageText;
            stats.Health.TakeHealEvent += ShowHealText;
            isSubscribed = true;
            Debug.Log($"{gameObject.name} 데미지 텍스트 구독 성공!");
        }

        status = stats != null ? stats.GetComponent<CharacterStatus>() : null;
        if (status != null)
        {
            status.OnDebuffPopped -= ShowStatusText;
            status.OnDebuffPopped += ShowStatusText;
        }
    }

    private void OnEnable()
    {
        if (stats != null && stats.Health != null && !isSubscribed)
        {
            stats.Health.TakeDamageEvent += ShowDamageText;
            stats.Health.TakeHealEvent += ShowHealText;
            isSubscribed = true;
            Debug.Log($"{gameObject.name} 데미지 텍스트 재구독 성공!");
        }

        if (status != null)
        {
            status.OnDebuffPopped -= ShowStatusText; // avoid double subscription
            status.OnDebuffPopped += ShowStatusText;
        }
    }

    private void OnDisable()
    {
        // stats가 null인지 먼저 확인 (매우 중요!)
        if (stats != null)
        {
            // stats is set but Health may already be destroyed, so double-check
            if (stats.Health != null)
            {
                stats.Health.TakeDamageEvent -= ShowDamageText;
                stats.Health.TakeHealEvent -= ShowHealText;
                isSubscribed = false;
            }
        }

        if (status != null)
        {
            status.OnDebuffPopped -= ShowStatusText;
        }
    }

    // OnDestroy도 동일하게 방어 코드를 작성합니다.
    private void OnDestroy()
    {
        if (stats != null && stats.Health != null)
        {
            stats.Health.TakeDamageEvent -= ShowDamageText;
            stats.Health.TakeHealEvent -= ShowHealText;
        }

        if (status != null)
        {
            status.OnDebuffPopped -= ShowStatusText;
        }
    }

    private void ShowDamageText(int damage, DamageType dmgType, string typeStr, bool isCritical)
    {
        if (FloatingTextManager.Instance != null && !FloatingTextManager.Instance.ShowDamageNumbers) return; // [추가] 데미지 텍스트 켜기/끄기 스위치

        string text = typeStr == "MISS" ? "MISS" : damage.ToString();
        Color color;

        if (this.transform.gameObject.layer == Layers.Army)
            color = colorConfig != null ? colorConfig.allyHitColor : Color.red;
        else
            color = colorConfig != null ? colorConfig.GetDamageColor(dmgType) : GetDefaultDamageColor(dmgType);

        // 특수한 팝업 스트링이 있을 경우 강제 덮어쓰기
        if (typeStr == "Shield") color = colorConfig != null ? colorConfig.shieldColor : Color.grey;           // 쉴드
        else if (typeStr == "MISS") color = colorConfig != null ? colorConfig.missColor : Color.gray;             // 회피

        if (FloatingTextManager.Instance == null) return;
        TextFloating textObj = FloatingTextManager.Instance.GetFromPool();

        textObj.SetUp(text, color, vec_float, isCritical);
    }

    /// <summary>
    /// colorConfig가 할당되지 않았을 때 사용할 기본 색상 (기존 하드코딩 값과 동일)
    /// </summary>
    private Color GetDefaultDamageColor(DamageType dmgType)
    {
        switch (dmgType)
        {
            case DamageType.Physical: return Color.white;
            case DamageType.Magic: return new Color(0.6f, 0.4f, 1f);
            case DamageType.Fixed: return Color.cyan;
            case DamageType.Freeze: return new Color(0.4f, 0.85f, 1f);
            case DamageType.Poison: return Color.green;
            case DamageType.BloodPop: return Color.yellow;
            case DamageType.Bleed: return Color.red;
            default: return Color.white;
        }
    }

    private void ShowHealText(float amount)
    {
        if (amount <= 0.001f) return;
        string text = $"+{amount:F1}"; // 소수점 첫째자리까지 힐량 표시
        Color color = colorConfig != null ? colorConfig.healColor : Color.green;

        if (FloatingTextManager.Instance == null) return;
        TextFloating textObj = FloatingTextManager.Instance.GetFromPool();
        textObj.SetUp(text, color, vec_float, false);
    }

    private void ShowStatusText(StatusVisual visual)
    {
        if (FloatingTextManager.Instance == null) return;

        // 팔레트가 없으면 조용히 넘긴다. 여기에 색을 다시 하드코딩해두면 팔레트를 안 꽂은
        // 프리팹이 '그럭저럭 동작'해버려서 배선 누락을 아무도 눈치채지 못한다.
        if (colorConfig == null)
        {
            Debug.LogWarning($"{gameObject.name}: StatusEffectPalette 이 비어 있어 상태이상 텍스트를 띄우지 못했습니다.");
            return;
        }

        // 라벨이 비어 있으면 '이 상태는 텍스트를 안 띄운다'는 뜻이다(예: 경직).
        string label = colorConfig.GetLabel(visual);
        if (string.IsNullOrEmpty(label)) return;

        TextFloating textObj = FloatingTextManager.Instance.GetFromPool();
        textObj.SetUp(label, colorConfig.GetTextColor(visual), vec_float, false, colorConfig.GetPopScale(visual));
    }
}
