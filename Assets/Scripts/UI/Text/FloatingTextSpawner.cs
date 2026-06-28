using UnityEngine;
using UnityEngine.UI;

public class FloatingTextSpawner : MonoBehaviour
{
    
    private CharacterStatus status;
private CharacterStat stats;

    [SerializeField] private Transform vec_float;

    [SerializeField] private bool isSubscribed = false;

    // CharacterStat에서 호출해줄 초기화 함수
public void Initialize(CharacterStat characterStat)
    {
        if (isSubscribed) return;

        this.stats = characterStat;

        if(stats == null) Debug.Log("stats가 null임");
        if(stats.Health == null) Debug.Log("health가 null");

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

        if (FloatingTextManager.instance == null) return;
        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        textObj.SetUp(text, color, vec_float, isCritical);
    }

    private void ShowHealText(float amount)
    {
        if (amount <= 0.001f) return;
        string text = $"+{amount:F1}"; // 소수점 첫째자리까지 힐량 표시
        Color color = Color.green;

        if (FloatingTextManager.instance == null) return;
        TextFloating textObj = FloatingTextManager.instance.GetFromPool();
        textObj.SetUp(text, color, vec_float, false);
    }

private void ShowStatusText(string statusName)
    {
        Color color = Color.gray;
        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        textObj.SetUp(statusName, color, vec_float);
    }
}
