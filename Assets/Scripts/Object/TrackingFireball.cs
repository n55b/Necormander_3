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

        // 회전 업데이트 (시각적)
        float angle = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    protected override void OnHitTarget(CharacterStat targetStat)
    {
        // 화염구는 마법 데미지로 처리
        DamageInfo info = new DamageInfo(_damage, DamageType.Magical, _shooter);
        targetStat.Health.GetDamage(info);
        Destroy(gameObject);
    }
}
