using UnityEngine;

/// <summary>
/// 투사체를 발사하는 원거리 유닛용 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "RangedAIPattern", menuName = "Necromancer/AI/RangedPattern")]
public class RangedAIPatternSO : BaseAIPatternSO
{
    [Header("원거리 공격 설정")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 15f;
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private float launchOffset = 0.5f;

    private void OnEnable()
    {
        // 원거리 유닛은 기본적으로 근접용 장판(Telegraph)을 자동 생성하지 않습니다.
        spawnTelegraph = false;
    }

    protected override void ExecuteBasicAttack(BaseEntity entity)
    {
        if (projectilePrefab == null || target == null) return;

        Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
        GameObject projectileObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        Projectile projectile = projectileObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Init(target.position, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
        }
    }
}
