using UnityEngine;

/// <summary>
/// 대상을 추적하는 화염구 투사체입니다.
/// Projectile을 상속받아 충돌 및 기본 설정을 공유합니다.
/// </summary>
public class TrackingFireball : Projectile
{
    protected Transform _targetTransform;

    public void Init(Transform target, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetTransform = target;
        // 부모의 Init을 활용하되, targetPos는 현재 타겟 위치로 초기화
        base.Init(target.position, damage, targetLayer, shooter, customSpeed, customLifeTime);
        this.transform.localRotation = Quaternion.identity; // 초기 회전값을 리셋
    }

    public void InitLinear(Vector2 targetPos, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetTransform = null;
        base.Init(targetPos, damage, targetLayer, shooter, customSpeed, customLifeTime);
    }

    protected override void Move()
    {
        if (_targetTransform == null)
        {
            // 타겟이 없으면 마지막 방향으로 직선 이동
            base.Move();
            return;
        }

        // 타겟 방향으로 부드럽게 회전하며 이동
        Vector2 targetDir = ((Vector2)_targetTransform.position - (Vector2)transform.position).normalized;
        
        // 이동
        transform.position = Vector2.MoveTowards(transform.position, _targetTransform.position, speed * Time.deltaTime);
    }

    protected override void OnHitTarget(CharacterStat targetStat)
    {
        // 화염구는 마법 데미지로 처리 (26/07/17: 주석대로 실제 Magic 태그를 달았다)
        // isRanged/hitFrom 은 베이스의 IDamageable 경로와 반드시 같아야 한다 — 지금은 그쪽이 먼저
        // 잡아서 여기까지 안 오지만, 빠뜨려 두면 나중에 경로가 바뀌는 순간 유도탄이 '근접'이 된다.
        DamageInfo info = new DamageInfo(_damage, DamageType.Magic, _shooter,
                                         isRanged: true, hitFrom: (Vector2)transform.position);
        targetStat.Health.GetDamage(info);
        Destroy(gameObject);
    }

    public override void InitDeflected(Vector2 newDirection, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetTransform = null; // 패리 시 유도 대상 해제 (직선 비행)
        base.InitDeflected(newDirection, damage, targetLayer, shooter, customSpeed, customLifeTime);
    }
}
