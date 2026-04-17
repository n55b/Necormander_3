using UnityEngine;

/// <summary>
/// 사제 전용 투척 효과: 착지 지점 주변 아군 미니언들의 체력을 즉시 회복시킵니다.
/// </summary>
public class PriestThrowEffect : MonoBehaviour
{
    public void Init(float healAmount, float radius, LayerMask allyLayer)
    {
        // 시각적 범위를 나타내기 위해 크기 조정
        transform.localScale = Vector3.one * radius * 2f;

        // 1. 범위 내 아군 탐색 및 회복 수행
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, allyLayer);
        int healCount = 0;

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<CharacterStat>(out var stat))
            {
                stat.Heal(healAmount);
                healCount++;
            }
        }

        Debug.Log($"<color=green>[Priest Effect]</color> {radius} 범위 내 {healCount}명의 아군을 회복시켰습니다.");

        // 2. 효과 연출 후 파괴
        Destroy(gameObject, 1.0f);
    }
}
