using UnityEngine;

/// <summary>
/// 폭탄병 전용 투척 효과: 착지 지점에 강력한 1회성 광역 피해를 입힙니다.
/// </summary>
public class BomberThrowEffect : MonoBehaviour
{
    public void Init(float damage, float radius, LayerMask enemyLayer, GameObject attacker)
    {
        // 1. 시각적 레이어 설정 (유닛들 위로 보이도록)
        if (TryGetComponent<SpriteRenderer>(out var sr))
        {
            sr.sortingLayerName = "Unit";
            sr.sortingOrder = 1;
        }

        // 2. 시각적 범위를 나타내기 위해 크기 조정 (반지름 * 2 = 지름)
        transform.localScale = Vector3.one * radius * 2f;

        // 3. 즉발성 강력 광역 공격 수행
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, enemyLayer);
        int hitCount = 0;

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<CharacterStat>(out var stat))
            {
                // 강력한 한 방 (Physical 데미지)
                stat.GetDamage(new DamageInfo(damage, DamageType.Physical, attacker));
                hitCount++;
            }
        }

        Debug.Log($"<color=red>[Bomber Effect]</color> {radius} 범위 내 {hitCount}명의 적에게 폭발 피해를 입혔습니다.");

        // 4. 짧은 연출 시간 후 파괴 (파티클/스프라이트가 보일 시간)
        Destroy(gameObject, 0.5f); 
    }
}
