using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 투척 충격이 발생했을 때의 실제 게임플레이 효과와 연출을 실행하는 매니저입니다.
/// </summary>
public class ThrowImpactManager : MonoBehaviour
{
    public void Initialize()
    {
        Debug.Log("<color=cyan>[ThrowImpactManager]</color> Initialized.");
    }

    /// <summary>
    /// 투척 충격 효과 시퀀스를 시작합니다.
    /// </summary>
    public void ProcessThrowImpact(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir)
    {
        StartCoroutine(ExecuteImpactRoutine(recipe, impactPos, travelDir));
    }

    private IEnumerator ExecuteImpactRoutine(ThrowRecipe recipe, Vector2 impactPos, Vector2 travelDir)
    {
        int totalExecutions = recipe.GetTotalExecutionCount();
        
        // Area 모드의 경우 첫 프레임에 대상 캐싱
        List<GameObject> targets = null;
        if (recipe.targetingMode == TargetingMode.Area)
        {
            targets = recipe.ScanAreaTargets(impactPos);
        }

        for (int i = 0; i < totalExecutions; i++)
        {
            recipe.Execute(i, impactPos, travelDir, targets);
            
            if (i < totalExecutions - 1)
                yield return new WaitForSeconds(0.1f);
        }
    }
}
