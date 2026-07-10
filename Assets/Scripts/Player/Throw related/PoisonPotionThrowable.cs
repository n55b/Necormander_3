using UnityEngine;
using System.Collections.Generic;

public class PoisonPotionThrowable : ThrowableUnit
{
    [Header("Potion Settings")]
    public float explosionRadius = 2.5f;
    public float explosionDamage = 10f; // 에디터 수정 가능 (약한 피해)
    public float poisonStacks = 3f;     // 에디터 수정 가능 (중독 중첩)

    // 전역 포션 개수 제한을 위한 정적 리스트
    public static List<PoisonPotionThrowable> ActivePotions = new List<PoisonPotionThrowable>();
    public const int MAX_POTIONS = 5;

    public override CommandData MinionType => CommandData.None; // 미니언이 아님
    
    private bool _exploded = false;

    protected override void Awake()
    {
        // Collider가 없을 경우 ThrowableUnit.Awake 전에 달아줌
        if (GetComponent<Collider2D>() == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.3f;
            circle.isTrigger = true;
        }

        base.Awake();
        
        // 질량 및 중력 설정 (필요시)
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.mass = 0.5f;
        }
    }

    private void Start()
    {
        ActivePotions.Add(this);
        ManagePotionLimit();
    }

    private void OnDestroy()
    {
        ActivePotions.Remove(this);
    }

    private void ManagePotionLimit()
    {
        if (ActivePotions.Count > MAX_POTIONS)
        {
            // 가장 오래된 포션을 찾아서 삭제 (현재 들고있거나 날아가고 있는 오브젝트가 아닌 것 우선)
            int flyingLayer = Layers.FlyingObject;
            for (int i = 0; i < ActivePotions.Count; i++)
            {
                if (ActivePotions[i] != this && ActivePotions[i].gameObject.layer != flyingLayer)
                {
                    Destroy(ActivePotions[i].gameObject);
                    return;
                }
            }
            // 전부 다 던져지고 있거나 잡혀있다면 그냥 제일 오래된 것 삭제
            Destroy(ActivePotions[0].gameObject);
        }
    }

    public override void OnLanded()
    {
        base.OnLanded();

        // 클러스터(또는 적)와 부딪혔을 때만 폭발 처리 (상자와 동일 로직)
        if (_isImpacted)
        {
            Explode();
        }
    }

    // 만약 땅에 그냥 닿았을때 터져야 한다면 OnTriggerEnter2D 추가 가능하나
    // 현재 ThrowableBox 로직에서는 ThrowStrategy나 ArcMovement가 SetImpacted를 호출함

    private void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        LayerMask enemyLayer = Layers.EnemyMask;
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, enemyLayer);
        
        foreach (var col in colls)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health != null && !health.IsDead)
            {
                health.GetDamage(new DamageInfo(explosionDamage, DamageType.Physical, null)); 
                
                var status = col.GetComponentInChildren<CharacterStatus>();
                if (status != null)
                {
                    status.AddDebuffStack(DebuffStackType.BloodPop, poisonStacks);
                }
            }
        }

        // 폭발 이펙트 스폰
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius);
            Destroy(vfx, 1.0f);
        }

        Destroy(gameObject);
    }
}
