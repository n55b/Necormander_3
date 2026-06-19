using UnityEngine;
using UnityEngine.UI;

public class FloatingTextSpawner : MonoBehaviour
{
    private CharacterStat stats;

    [SerializeField] private Transform vec_float;

    [SerializeField] private bool isSubscribed = false;

    // CharacterStat에서 호출해줄 초기화 함수
    public void Initialize(CharacterStat characterStat)
    {
        if (isSubscribed) return;

        this.stats = characterStat;

        if (stats != null && stats.Health != null)
        {
            stats.Health.TakeDamageEvent += ShowDamageText;
            stats.Health.TakeHealEvent += ShowHealText;
            isSubscribed = true;
        }

        if (stats != null && stats.Status != null)
            stats.Status.OnDebuffNewlyApplied += ShowStatusText;
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
    }

    private void OnDisable()
    {
        if (stats != null)
        {
            if (stats.Health != null)
            {
                stats.Health.TakeDamageEvent -= ShowDamageText;
                stats.Health.TakeHealEvent -= ShowHealText;
                isSubscribed = false;
            }
            if (stats.Status != null)
                stats.Status.OnDebuffNewlyApplied -= ShowStatusText;
        }
    }

    // OnDestroy도 동일하게 방어 코드를 작성합니다.
    private void OnDestroy()
    {
        if (stats != null)
        {
            if (stats.Health != null)
            {
                stats.Health.TakeDamageEvent -= ShowDamageText;
                stats.Health.TakeHealEvent -= ShowHealText;
            }
            if (stats.Status != null)
                stats.Status.OnDebuffNewlyApplied -= ShowStatusText;
        }
    }

    private void ShowDamageText(int damage, string type, bool isCritical)
    {
        string text = type == "MISS" ? "MISS" : damage.ToString();

        // 스타일 키 계산
        // type이 특수(Poison, Shield, Execution 따위)이르이자 그 이르을 우선에 쓸다
        // type이 일반(Normal)이고 isCritical이 쭕이육이자 Critical로 대시
        // 그 외에는 Normal
        string styleKey;
        if (!string.IsNullOrEmpty(type) && type != "Normal")
            styleKey = type;
        else
            styleKey = isCritical ? "Critical" : "Normal";

        FloatingTextStyleSO style = FloatingTextStyleRegistry.Instance != null
            ? FloatingTextStyleRegistry.Instance.GetStyle(styleKey)
            : null;

        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        if (style != null)
            textObj.SetUp(text, style, vec_float);
        else
        {
            // 레지스트리가 없을 때 폴백 색상
            Color color = isCritical ? Color.yellow : Color.white;
            if (type == "MISS") color = Color.gray;
            textObj.SetUp(text, color, vec_float, isCritical);
        }
    }

    private void ShowHealText(float amount)
    {
        if (amount <= 0.001f) return;
        string text = $"+{amount:F1}";

        FloatingTextStyleSO style = FloatingTextStyleRegistry.Instance != null
            ? FloatingTextStyleRegistry.Instance.GetStyle("Heal")
            : null;

        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        if (style != null)
            textObj.SetUp(text, style, vec_float);
        else
            textObj.SetUp(text, Color.green, vec_float, false);
    }

    /// <summary>
    /// 상태이상 발동 타이틀(중독, 부식 등)을 텍스트로 표시합니다.
    /// CharacterStat의 상태이상 발동 이병트에 연결해서 호출하세요.
    /// </summary>
    public void ShowStatusText(string statusName)
    {
        FloatingTextStyleSO style = FloatingTextStyleRegistry.Instance != null
            ? FloatingTextStyleRegistry.Instance.GetStyle(statusName)
            : null;

        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        if (style != null)
            textObj.SetUp(statusName, style, vec_float);
        else
            textObj.SetUp(statusName, Color.cyan, vec_float, false);
    }
}
