using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [반복 던지기] 능력을 구현하는 클래스입니다.
/// 투척 시 실제 미니언이 없는 "잔상 클러스터"를 하나 더 생성하여 발사합니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowRepeatAbility", menuName = "Necromancer/Growth/Throw Ability/Repeat")]
public class ThrowRepeatAbilitySO : ThrowAbilitySO
{
    [Header("Repeat Settings")]
    [SerializeField] private float delayBetweenThrows = 0.15f; // 잔상과의 간격
    [SerializeField] private Color afterimageColor = new Color(1f, 1f, 1f, 0.4f); // 투명한 잔상 색상

    public override void OnThrowLaunch(ThrowController controller, ThrowRecipe recipe, Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float ratio)
    {
        // 코루틴을 통해 약간의 시차를 두고 "빈 클러스터"를 하나 더 쏩니다.
        controller.StartCoroutine(LaunchAfterimage(controller, recipe, startPos, targetPos, duration, maxHeight, isDirect, ratio));
    }

    private IEnumerator LaunchAfterimage(ThrowController controller, ThrowRecipe recipe, Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float ratio)
    {
        yield return new WaitForSeconds(delayBetweenThrows);

        if (controller == null || recipe == null) yield break;

        // 1. 새로운 클러스터 생성
        ThrowCluster afterimage = Instantiate(controller.clusterPrefab, startPos, Quaternion.identity);
        
        // 2. 비주얼 설정 (SETUP 전에 반지름 정보를 먼저 설정)
        float originRadius = (controller.ActiveCluster != null) ? controller.ActiveCluster.GetCurrentRadius() : 0.4f;
        afterimage.Setup(new List<IThrowable>()); 
        afterimage.SetVisualRadius(originRadius); 
        
        var sr = afterimage.GetVisualRenderer();
        if (sr != null) sr.color = afterimageColor;

        // [핵심 해결책] 3. 레시피 깊은 복사 (Deep Copy)
        // 기존 레시피를 그대로 사용하면 hitTargets 리스트가 공유되어 첫 번째 타겟을 무시하게 됩니다.
        ThrowRecipe copyRecipe = new ThrowRecipe();
        copyRecipe.info.CopyFrom(recipe.info);
        copyRecipe.modifiers.CopyFrom(recipe.modifiers);
        copyRecipe.state.CopyFrom(recipe.state);
        
        // 중요: 잔상 전용 상태 설정
        copyRecipe.state.hitTargets.Clear(); // 피격 리스트 초기화 (독립적 타격 가능하게 함)
        copyRecipe.state.isMaster = false;  // 잔상은 유닛 생명주기에 관여하지 않음

        // 원본 액션 리스트 복사 (참조는 같아도 됨, 리스트 자체만 새로 생성)
        copyRecipe.actions = new List<ImpactAction>(recipe.actions);

        afterimage.SetRecipe(copyRecipe); 
        afterimage.Launch(startPos, targetPos, duration, maxHeight, isDirect, ratio);

        Debug.Log("<color=cyan>[Ability: Repeat]</color> Afterimage cluster launched with independent hit detection.");
    }
}
