using UnityEngine;
using System.Collections.Generic;

public class GenericHitbox : MonoBehaviour
{
    private float _damage;
    private LayerMask _targetLayer;
    private GameObject _attacker;
    private HashSet<Collider2D> _hitTargets = new HashSet<Collider2D>();
    

    [Header("Hit Effect")]
    [Tooltip("적중 시 생성할 타격 이펙트 프리팹 (HitEffect)")]
    public GameObject hitEffectPrefab;
private float _lifetime;

    public void Init(float damage, LayerMask targetLayer, GameObject attacker, float lifetime = 0.5f)
    {
        _damage = damage;
        _targetLayer = targetLayer;
        _attacker = attacker;
        _lifetime = lifetime;
        
        Destroy(gameObject, _lifetime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if the layer matches the target layer mask
        if ((_targetLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            if (_hitTargets.Contains(other)) return; // Prevent multiple hits on the same target

            // 플레이어는 충돌체가 자식에 있고 Stat이 부모에 있을 수 있으므로 InParent 우선 탐색
            CharacterStat targetStat = other.GetComponentInParent<CharacterStat>();
            if (targetStat == null)
            {
                // 미니언 중에는 Stat이 자식에 있는 경우가 있을 수 있으므로 Children 탐색
                int flyingLayer = LayerMask.NameToLayer("FlyingObject");
                foreach (var s in other.GetComponentsInChildren<CharacterStat>())
                {
                    if (s.gameObject.layer != flyingLayer)
                    {
                        targetStat = s;
                        break;
                    }
                }
            }

            if (targetStat != null && targetStat.Health != null && !targetStat.Health.IsDead)
            {
                targetStat.Health.GetDamage(new DamageInfo(_damage, DamageType.Physical, _attacker, false, 1f, true));
                _hitTargets.Add(other);
                SpawnHitEffect(other.transform.position);
            }
        }
    }



    /// <summary>
    /// 적중 위치에 타격 이펙트(HitEffect)를 생성합니다.
    /// </summary>
private void SpawnHitEffect(Vector3 position)
    {
        if (hitEffectPrefab == null) return;

        GameObject effect = Instantiate(hitEffectPrefab, position, transform.rotation);
        Destroy(effect, 1.0f);
    }
}
