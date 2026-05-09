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
        // 이미 구독 중이면 중복 구독 방지
        if (isSubscribed) return;

        this.stats = characterStat;

        if(stats == null) Debug.Log("stats가 null임");
        if(stats.Health == null) Debug.Log("health가 null");

        if (stats != null && stats.Health != null)
        {
            stats.Health.TakeDamageEvent += ShowDamageText;
            isSubscribed = true;
            Debug.Log($"{gameObject.name} 데미지 텍스트 구독 성공!");
        }
    }

    private void OnDisable()
    {
        // stats가 null인지 먼저 확인 (매우 중요!)
        if (stats != null)
        {
            // stats는 있지만 Health가 이미 파괴되었을 수도 있으므로 한 번 더 체크
            if (stats.Health != null)
            {
                stats.Health.TakeDamageEvent -= ShowDamageText;
                isSubscribed = false;
            }
        }
    }

    // OnDestroy도 동일하게 방어 코드를 작성합니다.
    private void OnDestroy()
    {
        if (stats != null && stats.Health != null)
        {
            stats.Health.TakeDamageEvent -= ShowDamageText;
        }
    }

    private void ShowDamageText(int damage, string type, bool isCritical)
    {
        string text = damage.ToString();
        Color color = Color.black;

        if (type == "Poison") color = Color.green;          // 중독뎀
        else if (type == "Corroded") color = Color.magenta; // 부식
        else if (type == "Shield") color = Color.grey;      // 쉴드
        else if (type == "Execution") color = Color.yellow; // 처형
        else if (type == "BloodPop") color = Color.red;     // 비폭

        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        textObj.SetUp(text, color, vec_float, isCritical);
    }

    private void ShowStatusText(string statusName)
    {
        Color color = Color.cyan;
        TextFloating textObj = FloatingTextManager.instance.GetFromPool();

        textObj.SetUp(statusName, color, vec_float);
    }
}
