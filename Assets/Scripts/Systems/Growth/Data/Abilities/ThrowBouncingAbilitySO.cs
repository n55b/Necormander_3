using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [튕기기] 능력을 구현하는 클래스입니다.
/// 단일 타겟 적중 시 주변의 다른 타겟에게 최대 2회 튕기며 효과를 전이시킵니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowBouncingAbility", menuName = "Necromancer/Growth/Throw Ability/Bouncing")]
public class ThrowBouncingAbilitySO : ThrowAbilitySO
{
    [Header("Bouncing Settings")]
    public float searchRadius = 6.0f;
    public float bounceSpeed = 15.0f;
    public int maxBounces = 2;

    public ThrowBouncingAbilitySO()
    {
        // 필터 기본 설정: 단일 타겟 모드일 때만 발동
        targetFilter = ThrowTargetFilter.Single;
        trajectoryFilter = ThrowTrajectoryFilter.Parabolic | ThrowTrajectoryFilter.Straight;
    }

    public override void OnImpact(ThrowRecipe recipe, Vector2 impactPoint, ThrowCluster cluster)
    {
        // 중복 타격 방지 등록
        if (recipe.info.finalTarget != null && !recipe.state.hitTargets.Contains(recipe.info.finalTarget))
        {
            recipe.state.hitTargets.Add(recipe.info.finalTarget);
        }

        // 최대 튕김 횟수 도달 확인
        if (recipe.state.bounceCount >= maxBounces) return;

        // 다음 타겟 검색
        GameObject nextTarget = FindNextTarget(recipe, impactPoint);
        if (nextTarget == null) return;

        // [핵심] 시너지 로직: 관통이 남았는가?
        bool hasPierceRemaining = recipe.state.pierceCount <= recipe.state.maxPierce && recipe.state.maxPierce > 0;

        if (hasPierceRemaining && recipe.state.isMaster)
        {
            // 1. 관통이 남은 '마스터'라면: 본체는 직진하고, '에코(복제본)'를 생성하여 튕김
            SpawnEchoBounce(recipe, impactPoint, nextTarget);
        }
        else
        {
            // 2. 관통이 없거나 이미 '에코'라면: 본체(혹은 에코)가 직접 튕김
            ExecuteDirectBounce(recipe, impactPoint, nextTarget, cluster);
        }
    }

    private void SpawnEchoBounce(ThrowRecipe recipe, Vector2 impactPoint, GameObject nextTarget)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var throwController = player.GetComponentInChildren<ThrowController>();
        if (throwController == null) return;

        // 에코 구체 생성 (유닛 없음, isMaster = false)
        GameObject echoObj = Instantiate(throwController.clusterPrefab.gameObject, impactPoint, Quaternion.identity);
        ThrowCluster echoCluster = echoObj.GetComponent<ThrowCluster>();
        
        if (echoCluster != null)
        {
            echoCluster.Setup(new List<IThrowable>());
            echoCluster.SetVisualRadius(0.3f);
            var sr = echoCluster.GetVisualRenderer();
            if (sr != null) sr.color = new Color(0.4f, 0.8f, 1f, 0.6f); // 에코는 더 투명하게

            // 레시피 복제 (에코 전용 데이터)
            ThrowRecipe echoRecipe = new ThrowRecipe();
            echoRecipe.info.CopyFrom(recipe.info);
            echoRecipe.modifiers.CopyFrom(recipe.modifiers);
            echoRecipe.actions = new List<ImpactAction>(recipe.actions); 
            
            echoRecipe.state.isMaster = false; // [중요] 얘는 유닛을 내리지 않음
            echoRecipe.state.bounceCount = recipe.state.bounceCount + 1;
            echoRecipe.state.hitTargets = new List<GameObject>(recipe.state.hitTargets); // 현재까지의 타겟 리스트 복사

            Vector2 targetPos = nextTarget.transform.position;
            float duration = Vector2.Distance(impactPoint, targetPos) / bounceSpeed;

            echoRecipe.info.finalTarget = nextTarget;
            echoRecipe.info.impactPoint = targetPos;

            var bounceComp = echoObj.AddComponent<BouncingSphere>();
            bounceComp.Init(echoRecipe, (targetPos - impactPoint).normalized);

            echoCluster.SetRecipe(echoRecipe);
            echoCluster.Launch(impactPoint, targetPos, duration, 1.0f, false, 0.5f);
            
            Debug.Log($"<color=cyan>[Bouncing]</color> Echo Spawned (Master is Piercing)");
        }
    }

    private void ExecuteDirectBounce(ThrowRecipe recipe, Vector2 impactPoint, GameObject nextTarget, ThrowCluster cluster)
    {
        if (cluster == null) return;

        recipe.state.bounceCount++;
        recipe.state.isBouncing = true;

        Vector2 targetPos = nextTarget.transform.position;
        float duration = Vector2.Distance(impactPoint, targetPos) / bounceSpeed;

        recipe.info.finalTarget = nextTarget;
        recipe.info.impactPoint = targetPos;

        var sr = cluster.GetVisualRenderer();
        if (sr != null) sr.color = new Color(0.6f, 0.9f, 1f, 0.8f);

        cluster.Launch(impactPoint, targetPos, duration, 1.0f, false, 0.5f);
        Debug.Log($"<color=cyan>[Bouncing]</color> Direct Bounce executed.");
    }

    private GameObject FindNextTarget(ThrowRecipe recipe, Vector2 currentPos)
    {
        // 원래 타겟팅했던 팀과 동일한 팀만 검색
        LayerMask mask = (recipe.info.targetTeam == Team.Enemy) ? LayerMask.GetMask("Enemy") : LayerMask.GetMask("Army", "Player");
        Collider2D[] colls = Physics2D.OverlapCircleAll(currentPos, searchRadius, mask);
        
        GameObject bestTarget = null;
        float minTargetDist = float.MaxValue;

        foreach (var col in colls)
        {
            GameObject obj = col.gameObject;
            
            // [수정] 블랙리스트(hitTargets)에 포함된 대상은 무조건 제외
            if (recipe.state.hitTargets.Contains(obj)) continue;

            // 죽은 대상 제외 (CharacterStat 확인)
            var stat = obj.GetComponentInChildren<CharacterStat>();
            if (stat != null && stat.Health.IsDead) continue;

            float d = Vector2.Distance(currentPos, obj.transform.position);
            if (d < minTargetDist)
            {
                minTargetDist = d;
                bestTarget = obj;
            }
        }
        return bestTarget;
    }
}

/// <summary>
/// 튕겨 나가는 구체가 목표에 도달했을 때 효과를 발동시키기 위한 브릿지 컴포넌트입니다.
/// </summary>
public class BouncingSphere : MonoBehaviour
{
    private ThrowRecipe _recipe;
    private Vector2 _travelDir;
    private bool _hasTriggered = false;

    public void Init(ThrowRecipe recipe, Vector2 dir)
    {
        _recipe = recipe;
        _travelDir = dir;
    }

    private void OnDestroy()
    {
        // ThrowCluster가 파괴될 때(OnLanded 호출 후) 효과 발동
        if (_hasTriggered || _recipe == null || GameManager.Instance == null || GameManager.Instance.throwImpactManager == null) return;
        _hasTriggered = true;

        // 다음 타격 실행
        GameManager.Instance.throwImpactManager.ProcessThrowImpact(_recipe, transform.position, _travelDir);
    }
}
