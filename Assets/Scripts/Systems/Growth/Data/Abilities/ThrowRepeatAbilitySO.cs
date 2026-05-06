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

        if (controller == null) yield break;

        // [추가] 원본 클러스터의 크기를 미리 계산 (현재 집고 있는 애들이 없으므로 수동 계산)
        // ThrowController에 현재 활성화된 클러스터가 없으므로, 레시피나 보관된 정보를 기반으로 크기를 정해야 함.
        // 일단 기본 반지름을 사용하거나, 클러스터의 로직을 활용합니다.
        
        // 1. 새로운 클러스터 생성
        ThrowCluster afterimage = Instantiate(controller.clusterPrefab, startPos, Quaternion.identity);
        
        // 2. 비주얼 설정 (미니언이 없어도 원이 보이도록 강제 설정 및 색상 변경)
        afterimage.Setup(new List<IThrowable>()); 
        
        // 원본과 비슷한 크기로 시각화 (기본값 0.35f 사용)
        afterimage.SetVisualRadius(0.35f); 
        
        var sr = afterimage.GetVisualRenderer();
        if (sr != null) sr.color = afterimageColor;

        // 3. 레시피 및 물리 발사 (실제 효과가 한번 더 발생함)
        afterimage.SetRecipe(recipe); 
        afterimage.Launch(startPos, targetPos, duration, maxHeight, isDirect, ratio);

        Debug.Log("<color=cyan>[Ability: Repeat]</color> Afterimage cluster launched.");
    }
}
