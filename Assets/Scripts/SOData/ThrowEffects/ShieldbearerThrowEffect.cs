using UnityEngine;
using System.Collections;

/// <summary>
/// 방패병 전용 투척 효과: 착지 지점에 발생하는 여진 장판입니다.
/// </summary>
public class ShieldbearerThrowEffect : MonoBehaviour
{
    private float _damage;
    private float _duration;
    private float _slowAmount;
    private float _radius;
    private LayerMask _enemyLayer;
    private GameObject _attacker;

    public void Init(float damage, float duration, float slowAmount, float radius, LayerMask enemyLayer, GameObject attacker)
    {
        _damage = damage;
        _duration = duration;
        _slowAmount = slowAmount;
        _radius = radius;
        _enemyLayer = enemyLayer;
        _attacker = attacker;

        // 시각적 범위를 나타내기 위해 크기 조정
        transform.localScale = Vector3.one * radius * 2f;

        StartCoroutine(ExecuteEffect());
    }

    private IEnumerator ExecuteEffect()
    {
        float elapsed = 0f;
        while (elapsed < _duration)
        {
            ApplyEffect();
            elapsed += 0.5f; 
            yield return new WaitForSeconds(0.5f);
        }
        
        yield return new WaitForSeconds(0.2f);
        Destroy(gameObject);
    }

    private void ApplyEffect()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, _radius, _enemyLayer);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<CharacterStat>(out var stat))
            {
                // 1. 데미지 적용
                stat.GetDamage(new DamageInfo(_damage, DamageType.Physical, _attacker));
                
                // 2. 슬로우 적용 (아이디 기반 중첩 방지)
                // _slowAmount가 0.5라면 50% 감소율로 전달
                stat.ApplySlow("Shieldbearer_Slow", _slowAmount, 0.6f);
            }
        }
    }
}
