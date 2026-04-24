using UnityEngine;
using System.Collections;

/// <summary>
/// 적군 마법사가 소환하는 폭발 장판입니다.
/// 2초 동안 차오르는 연출 후 범위 내 아군에게 데미지를 줍니다.
/// </summary>
public class EnemyMagicianCircleAttack : MonoBehaviour
{
    [Header("비주얼 (Inspector에서 할당 필요)")]
    [SerializeField] private Transform fillingCircle; // 크기가 커질 내부 원
    [SerializeField] private SpriteRenderer outerBoundary; // 테두리

    private float _damage;
    private float _radius;
    private float _waitTime;
    private LayerMask _targetLayer;
    private GameObject _attacker;

    public void Init(float damage, LayerMask targetLayer, GameObject attacker, float radius, float waitTime)
    {
        _damage = damage;
        _targetLayer = targetLayer;
        _attacker = attacker;
        _radius = radius;
        _waitTime = waitTime;

        // 장판 비주얼 크기 설정 (반지름 기반)
        transform.localScale = new Vector3(_radius * 2, _radius * 2, 1);

        StartCoroutine(AttackSequence());
    }

    private IEnumerator AttackSequence()
    {
        float timer = 0f;
        float maxVisualScale = 0.9f; // 테두리 두께를 제외한 실제 가득 찬 스케일

        // 1. 설정된 대기 시간 동안 내부 원이 차오르는 연출
        while (timer < _waitTime)
        {
            timer += Time.deltaTime;
            float progress = timer / _waitTime;

            if (fillingCircle != null)
            {
                // 1.0이 아닌 maxVisualScale까지만 커지도록 조정
                float currentScale = progress * maxVisualScale;
                fillingCircle.localScale = new Vector3(currentScale, currentScale, 1);
            }
            yield return null;
        }

        // 2. 폭발 및 데미지 판정
        Explode();

        // 3. 소멸
        Destroy(gameObject);
    }

    private void Explode()
    {
        // 범위 내 모든 타겟 탐색
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayer);

        foreach (var col in hits)
        {
            if (col.TryGetComponent<CharacterStat>(out var stat))
            {
                DamageInfo info = new DamageInfo(_damage, DamageType.Magical, _attacker);
                stat.GetDamage(info);
            }
        }

        // 폭발 이펙트 (필요 시 여기에 추가)
        Debug.Log($"<color=purple>[Magic Circle]</color> 폭발! {hits.Length}명의 타겟 적중.");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
