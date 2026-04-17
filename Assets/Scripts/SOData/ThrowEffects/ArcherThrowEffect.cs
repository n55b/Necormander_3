using UnityEngine;

/// <summary>
/// 궁수 전용 투척 효과: 착지 지점 주변 넓은 범위에 즉시 화살 포화를 퍼붓습니다.
/// </summary>
public class ArcherThrowEffect : MonoBehaviour
{
    public void Init(float damage, float radius, LayerMask enemyLayer, GameObject attacker)
    {
        // 시각적 범위를 나타내기 위해 크기 조정 (반지름 * 2 = 지름)
        transform.localScale = Vector3.one * radius * 2f;

        // 1. 즉발성 광역 공격 수행
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, enemyLayer);
        int hitCount = 0;

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<CharacterStat>(out var stat))
            {
                stat.GetDamage(new DamageInfo(damage, DamageType.Physical, attacker));
                hitCount++;
            }
        }

        Debug.Log($"<color=yellow>[Archer Effect]</color> {radius} 범위 내 {hitCount}명의 적에게 화살 포화 피해를 입혔습니다.");

        // 2. 효과 연출 후 파괴 (파티클 등이 있다면 지속 시간을 늘릴 수 있음)
        Destroy(gameObject, 1.0f); 
    }
}
