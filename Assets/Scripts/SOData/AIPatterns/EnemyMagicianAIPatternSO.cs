using UnityEngine;

/// <summary>
/// 플레이어를 최우선으로 사냥하며, 원거리 장판 폭발 공격을 수행하는 적군 마법사 전용 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyMagicianAIPattern", menuName = "Necromancer/AI/EnemyMagicianPattern")]
public class EnemyMagicianAIPatternSO : BaseAIPatternSO
{
    [Header("마법사 전용 설정")]
    [SerializeField] private GameObject magicCirclePrefab;
    [SerializeField] private float explosionRadius = 2.0f;
    [SerializeField] private float waitTime = 2.0f;
    [SerializeField, Min(.01f), Tooltip("예고가 끝난 뒤 장판의 실제 피해 판정이 남아 있는 시간(초). 준비 시간은 대기 시간에서 조절합니다.")]
    private float hitActiveDuration = .2f;

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

            // 단발 폭발도 장판 피해다. 원거리 여부와 별개로 가드 스캔/직접 피격 모두 방어 불가.
            DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageRules.FromAttackType(entity.Stats.ATTACK_TYPE), entity.gameObject, 1f, "", false, false, 0f, isRanged: true, bypassGuard: true);
            
            // 예고 시간과 실제 판정 유지 시간을 별도로 조절한다.
            magicCircle.Init(info, entity.opponentLayer, hitActiveDuration, waitTime);
        }

        Debug.Log($"<color=magenta>[Magician Pattern]</color> {entity.name}가 포격 실행");
    }

    protected override void OnWindupStart(BaseEntity entity, float windupTime)
    {
        // 마법사 전용 장판은 ExecuteBasicAttack 시점에 생성되므로 선딜레이 장판 생성은 불필요
    }
}
