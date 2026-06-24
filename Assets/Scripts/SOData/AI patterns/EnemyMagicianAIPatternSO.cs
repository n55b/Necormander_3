using UnityEngine;

/// <summary>
/// 플레이어를 최우선으로 사냥하며, 원거리 장판 폭발 공격을 수행하는 적군 마법사 전용 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyMagicianAIPattern", menuName = "Necromancer/AI/EnemyMagicianPattern")]
public class EnemyMagicianAIPatternSO : RangedBaseAIPatternSO
{
    [Header("마법사 전용 설정")]
    [SerializeField] private GameObject magicCirclePrefab;
    [SerializeField] private float explosionRadius = 2.0f;
    [SerializeField] private float waitTime = 2.0f;

    protected override void UpdateTargeting(BaseEntity entity)
    {
        // [최우선] 플레이어 탐색 (사거리 무한 설정을 전제로 항상 타겟팅)
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            entity.Target = player.transform;
        }
        else
        {
            // 플레이어가 없으면 가장 가까운 아군 미니언
            base.UpdateTargeting(entity);
        }
    }

    protected override void ExecuteBasicAttack(BaseEntity entity)
    {
        if (magicCirclePrefab == null || entity.Target == null) return;

        // 플레이어의 현재 위치(발밑)에 장판 소환
        Vector3 spawnPos = entity.Target.position;
        GameObject circleObj = Instantiate(magicCirclePrefab, spawnPos, Quaternion.identity);

        // 장판 초기화 (범용 BaseHitBox 사용)
        BaseHitBox magicCircle = circleObj.GetComponent<BaseHitBox>();
        if (magicCircle != null)
        {
            // 크기 적용
            circleObj.transform.localScale = new Vector3(explosionRadius, explosionRadius, 1f);

            // 마법사 장판은 마법 데미지로 취급
            DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, false, 1f, true, "", false, false, 0f);
            
            // 단발성 공격이므로 타격 판정은 0.2초간만 유지하고, waitTime 만큼 선딜레이(startDelay)를 줍니다.
            magicCircle.Init(info, entity.opponentLayer, 0.2f, waitTime);
        }

        Debug.Log($"<color=magenta>[Magician Pattern]</color> {entity.name}가 포격 실행");
    }

    protected override void OnWindupStart(BaseEntity entity, float windupTime)
    {
        // 마법사 전용 장판은 ExecuteBasicAttack 시점에 생성되므로 선딜레이 장판 생성은 불필요
    }
}
