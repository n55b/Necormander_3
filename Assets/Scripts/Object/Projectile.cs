using UnityEngine;

/// <summary>
/// 모든 투사체의 기반 클래스입니다.
/// 기본적인 직선 이동과 충격 판정을 처리합니다.
/// </summary>
public class Projectile : MonoBehaviour
{
    [Header("Base Projectile Settings")]
    [SerializeField] protected float speed = 15f;
    [SerializeField] protected float lifeTime = 3f;

    protected float _damage;
    protected LayerMask _targetLayer;
    protected GameObject _shooter;
    protected Vector2 _direction;

    public virtual void Init(Vector2 targetPos, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _damage = damage;
        _targetLayer = targetLayer;
        _shooter = shooter;
        speed = customSpeed;
        lifeTime = customLifeTime;

        // 방향 계산 및 초기 회전 설정
        _direction = (targetPos - (Vector2)transform.position).normalized;
        float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);

        // 일정 시간 후 자동 파괴
        Destroy(gameObject, lifeTime);
    }

    protected virtual void Update()
    {
        Move();
    }

    protected virtual void Move()
    {
        // 기본값: 오른쪽(Forward) 방향으로 직선 이동
        transform.Translate(Vector3.right * speed * Time.deltaTime);
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        // 1. 벽이나 장애물에 부딪히면 파괴
        if (((LayerMask.GetMask("Wall", "Obstacle")) & (1 << other.gameObject.layer)) != 0)
        {
            OnHitObstacle(other);
            return;
        }

        // 2. 타겟 레이어와 충돌 체크
        if ((_targetLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            CharacterStat targetStat = GetTargetStat(other);
            
            if (targetStat != null)
            {
                OnHitTarget(targetStat);
            }
        }
    }

    protected virtual CharacterStat GetTargetStat(Collider2D other)
    {
        CharacterStat targetStat = other.GetComponent<CharacterStat>();
        if (targetStat == null)
        {
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
        return targetStat;
    }

    protected virtual void OnHitTarget(CharacterStat targetStat)
    {
        DamageInfo info = new DamageInfo(_damage, DamageType.Physical, _shooter, false, 1f, true);
        targetStat.Health.GetDamage(info);
        Destroy(gameObject);
    }

    protected virtual void OnHitObstacle(Collider2D other)
    {
        Destroy(gameObject);
    }
}
