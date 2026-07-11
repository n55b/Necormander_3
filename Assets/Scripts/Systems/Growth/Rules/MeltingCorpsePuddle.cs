using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 유니크 보석 '녹아내리는 시체' (MeltingCorpse) 효과로 생성되는 장판입니다.
/// </summary>
public class MeltingCorpsePuddle : MonoBehaviour
{
    private float damagePerSecond;
    private float radius;
    private float duration = 5f;
    private float tickInterval = 1f;
    private float tickTimer = 0f;

    public void Init(float dps, float r)
    {
        damagePerSecond = dps;
        radius = r;
        tickTimer = tickInterval; // 처음 생성 시 바로 데미지 주도록
        Destroy(gameObject, duration);
    }

    private void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= tickInterval)
        {
            tickTimer -= tickInterval;
            DealDamage();
        }
    }

    private void DealDamage()
    {
        LayerMask enemyLayer = Layers.EnemyMask;
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius, enemyLayer);
        foreach (var col in colls)
        {
            var targetHealth = col.GetComponentInChildren<CharacterHealth>();
            if (targetHealth != null && !targetHealth.IsDead)
            {
                // 장판 피해는 Fixed 타입으로 적용
                targetHealth.GetDamage(new DamageInfo(damagePerSecond, DamageType.Fixed, null));
            }
        }
    }
}
