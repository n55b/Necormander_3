using UnityEngine;

public class Projectile : MonoBehaviour
{
    [Header("설정 (참고용 - 실제는 SO에서 결정)")]
    [SerializeField] private float speed = 15f;
    [SerializeField] private float lifeTime = 3f;

    private float _damage;
    private LayerMask _targetLayer;
    private GameObject _shooter;

    public void Init(Vector2 targetPos, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _damage = damage;
        _targetLayer = targetLayer;
        _shooter = shooter;
        speed = customSpeed;
        lifeTime = customLifeTime;
        
        // 방향 계산 및 회전 설정
        Vector2 direction = (targetPos - (Vector2)transform.position).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);

        // 일정 시간 후 자동 파괴
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        transform.Translate(Vector3.right * speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. 벽이나 장애물에 부딪히면 파괴
        if (((LayerMask.GetMask("Wall", "Obstacle")) & (1 << other.gameObject.layer)) != 0)
        {
            Destroy(gameObject);
            return;
        }

        // 2. 타겟 레이어(적군)와 충돌 체크
        if ((_targetLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            if (other.TryGetComponent<CharacterStat>(out var targetStat))
            {
                DamageInfo info = new DamageInfo(_damage, DamageType.Physical, _shooter);
                targetStat.GetDamage(info);
                Destroy(gameObject);
            }
        }
    }
}
