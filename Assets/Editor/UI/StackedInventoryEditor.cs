using UnityEngine;

/// <summary>아이템 주머니(V) 검사 묶음. batch 실행용 진입점(-executeMethod StackedInventoryEditor.Validate).
/// 아이템 데이터(수치·설명·목록 등록)는 아이템 편집기(Tools/기획/아이템 편집기)가 맡는다.
/// (PouchUI/PouchItemSlot 프리팹을 통째로 다시 만들던 저작 코드는 끝난 일회성이라 지웠다 — 스킨이 붙은 프리팹을 덮어썼다.)</summary>
public static class StackedInventoryEditor
{
    public static void Validate()
    {
        if (Application.isBatchMode)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        InventoryUICheck.Run();
        CriticalItemsCheck.Run();
        InventoryUICheck.CheckScenes();
    }
}
