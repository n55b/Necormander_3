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
    [SerializeField] private float attackInterval = 3.0f; // 장판 소환 주기

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

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        // 마법사는 타겟이 있기만 하면 거리에 상관없이 공격 상태를 유지합니다. (제자리 포격)
        if (entity.Target != null)
        {
            entity.CurrentState = AIState.Attack;
        }
        else
        {
            entity.CurrentState = AIState.Idle;
        }
    }

    protected override void OnAttack(BaseEntity entity)
    {
        // 공격 중에는 절대로 움직이지 않음
        StopNavAgent(entity);

        // attackInterval ~ attackInterval + 1.0s 사이의 랜덤한 주기를 위해 
        // 매 프레임 체크하는 대신, 한 번 쏘고 나서 타이머를 미세하게 조절합니다.
        entity.AtkTimer += Time.deltaTime;
        
        // 브레인이 인스턴스화되므로 개별 유닛마다 독립적인 타이머 흐름을 가집니다.
        if (entity.AtkTimer >= attackInterval)
        {
            ExecuteBasicAttack(entity);
            
            // [중요] 다음 공격 타이머를 0이 아닌 -Random 값으로 설정하여 
            // 실질적으로 '공격 간격 + 랜덤 추가 시간' 효과를 줍니다.
            entity.AtkTimer = -Random.Range(0f, 1.0f);
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
            DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Magical, entity.gameObject, false, 1f, true, "", false, false, 0f);
            
            // 단발성 공격이므로 타격 판정은 0.2초간만 유지하고, waitTime 만큼 선딜레이(startDelay)를 줍니다.
            magicCircle.Init(info, entity.opponentLayer, 0.2f, waitTime);
        }

        Debug.Log($"<color=magenta>[Magician Pattern]</color> {entity.name}가 포격 실행 (주기: {attackInterval}s)");
    }
}
