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

    private void ShowDamageText(int damage, DamageType dmgType, string typeStr, bool isCritical)
    {
        string text = typeStr == "MISS" ? "MISS" : damage.ToString();
        Color color = Color.white;
        
        if(this.transform.gameObject.layer == LayerMask.NameToLayer("Army"))
            color = Color.red;
        else
        {
            // 데미지 타입에 따른 색상 지정
            switch (dmgType)
            {
                case DamageType.Physical: color = Color.white; break;
                case DamageType.Fixed: color = Color.cyan; break; // 고정 데미지는 청록색
                case DamageType.BloodPop: color = Color.yellow; break; // 비폭은 노란색
                case DamageType.Bleed: color = Color.red; break; // 출혈은 붉은색
                case DamageType.Wound: color = new Color(1f, 0.5f, 0f); break; // 상처는 주황색
                case DamageType.Corrosion: color = Color.green; break; // 부식은 초록색
                case DamageType.Fracture: color = new Color(0.5f, 0f, 0.5f); break; // 골절은 보라색
                default: color = Color.white; break;
            }
        }

        // 특수한 팝업 스트링이 있을 경우 강제 덮어쓰기
        if (typeStr == "Shield") color = Color.grey;      // 쉴드
        else if (typeStr == "Execution") color = Color.yellow; // 처형
        else if (typeStr == "MISS") color = Color.gray;        // 회피
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

        if (FloatingTextManager.instance == null) return;
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

        if (FloatingTextManager.instance == null) return;
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
