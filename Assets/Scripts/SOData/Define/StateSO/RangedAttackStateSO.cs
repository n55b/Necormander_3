using UnityEngine;

/// <summary>
/// 원거리 사격 행동을 정의합니다.
/// AttackStateSO를 상속받아 공격 속도 주기에 맞춰 투사체를 발사합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewRangedAttackState", menuName = "Necromancer/Attack States/RangedAttack")]
public class RangedAttackStateSO : AttackStateSO
{
    [Header("원거리 공격 설정")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 15f; // 투사체 속도
    [SerializeField] private float lifeTime = 3f;        // 투사체 수명
    [SerializeField] private float launchOffset = 0.5f;

    protected override bool CanPerformAction(EntityFSM fsm)
    {
        return fsm.target != null && projectilePrefab != null;
    }

    protected override void PerformAction(EntityFSM fsm)
    {
        Vector2 spawnPos = (Vector2)fsm.transform.position + (Vector2.up * launchOffset);
        GameObject projectileObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        if (fsm.TryGetComponent<BaseEntity>(out var entity))
        {
            Projectile projectile = projectileObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                // SO에 설정된 속도와 수명을 전달합니다.
                projectile.Init(fsm.target.position, fsm.stats.ATK, entity.opponentLayer, fsm.gameObject, projectileSpeed, lifeTime);
            }
        }
    }
}
