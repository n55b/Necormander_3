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
        targetFilter = ThrowTargetFilter.Single;
        trajectoryFilter = ThrowTrajectoryFilter.Parabolic | ThrowTrajectoryFilter.Straight;
    }

    public override void OnImpact(ThrowRecipe recipe, Vector2 impactPoint, ThrowCluster cluster)
    {
        if (recipe.info.finalTarget != null && !recipe.state.hitTargets.Contains(recipe.info.finalTarget))
        {
            recipe.state.hitTargets.Add(recipe.info.finalTarget);
        }

        if (recipe.state.bounceCount >= maxBounces) return;

        GameObject nextTarget = FindNextTarget(recipe, impactPoint);
        if (nextTarget == null) return;

        bool hasPierceRemaining = recipe.state.pierceCount <= recipe.state.maxPierce && recipe.state.maxPierce > 0;

        if (hasPierceRemaining && recipe.state.isMaster)
        {
            SpawnEchoBounce(recipe, impactPoint, nextTarget);
        }
        else
        {
            ExecuteDirectBounce(recipe, impactPoint, nextTarget, cluster);
        }
    }

    private void SpawnEchoBounce(ThrowRecipe recipe, Vector2 impactPoint, GameObject nextTarget)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var throwController = player.GetComponentInChildren<ThrowController>();
        if (throwController == null) return;

        GameObject echoObj = Instantiate(throwController.clusterPrefab.gameObject, impactPoint, Quaternion.identity);
        ThrowCluster echoCluster = echoObj.GetComponent<ThrowCluster>();
        
        if (echoCluster != null)
        {
            echoCluster.Setup(new List<IThrowable>());
            echoCluster.SetVisualRadius(0.3f);
            var sr = echoCluster.GetVisualRenderer();
            if (sr != null) sr.color = new Color(0.4f, 0.8f, 1f, 0.6f);

            ThrowRecipe echoRecipe = new ThrowRecipe();
            echoRecipe.info.CopyFrom(recipe.info);
            echoRecipe.modifiers.CopyFrom(recipe.modifiers);
            echoRecipe.state.CopyFrom(recipe.state);
            echoRecipe.actions = new List<ImpactAction>(recipe.actions);
            
            echoRecipe.state.isMaster = false;
            echoRecipe.state.bounceCount = recipe.state.bounceCount + 1;
            
            Vector2 targetPos = nextTarget.transform.position;
            echoRecipe.info.finalTarget = nextTarget;
            echoRecipe.info.impactPoint = targetPos;
            
            echoRecipe.state.hitTargets.Add(recipe.info.finalTarget);
            
            float duration = Vector2.Distance(impactPoint, targetPos) / bounceSpeed;

            echoCluster.SetRecipe(echoRecipe);
            echoCluster.Launch(impactPoint, targetPos, duration, 1.0f, false, 0.5f);
            
            Debug.Log($"<color=cyan>[Bouncing]</color> Echo Spawned, targeting {nextTarget.name}");
        }
    }

    private void ExecuteDirectBounce(ThrowRecipe recipe, Vector2 impactPoint, GameObject nextTarget, ThrowCluster cluster)
    {
        if (cluster == null) return;

        ThrowRecipe bounceRecipe = new ThrowRecipe();
        bounceRecipe.info.CopyFrom(recipe.info);
        bounceRecipe.modifiers.CopyFrom(recipe.modifiers);
        bounceRecipe.state.CopyFrom(recipe.state);
        bounceRecipe.actions = new List<ImpactAction>(recipe.actions);

        bounceRecipe.state.bounceCount++;
        bounceRecipe.state.isBouncing = true;

        Vector2 targetPos = nextTarget.transform.position;
        float duration = Vector2.Distance(impactPoint, targetPos) / bounceSpeed;

        bounceRecipe.info.finalTarget = nextTarget;
        bounceRecipe.info.impactPoint = targetPos;
        
        if(recipe.info.finalTarget != null)
            bounceRecipe.state.hitTargets.Add(recipe.info.finalTarget);

        cluster.SetRecipe(bounceRecipe);

        var sr = cluster.GetVisualRenderer();
        if (sr != null) sr.color = new Color(0.6f, 0.9f, 1f, 0.8f);

        cluster.Launch(impactPoint, targetPos, duration, 1.0f, false, 0.5f);
        Debug.Log($"<color=cyan>[Bouncing]</color> Direct Bounce executed, targeting {nextTarget.name}");
    }

    private GameObject FindNextTarget(ThrowRecipe recipe, Vector2 currentPos)
    {
        LayerMask mask = (recipe.info.targetTeam == Team.Enemy) ? Layers.EnemyMask : Layers.PlayerArmy;
        Collider2D[] colls = Physics2D.OverlapCircleAll(currentPos, searchRadius, mask);
        
        GameObject bestTarget = null;
        float minTargetDist = float.MaxValue;

        foreach (var col in colls)
        {
            GameObject obj = col.gameObject;
            if (recipe.state.hitTargets.Contains(obj)) continue;
            var stat = obj.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = obj.GetComponentInChildren<CharacterStat>();
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
