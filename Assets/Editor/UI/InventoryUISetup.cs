using System;
using UnityEditor;
using UnityEngine;

/// <summary>0928 C/V UI 프리팹 검사. 실행 중 생성하지 않고 프리팹에 저장된 구조와 참조를 확인한다.
/// (프리팹을 처음부터 다시 만들던 Apply 메뉴 두 개는 끝난 일회성 저작이라 지웠다 — 다시 돌리면 스킨·주머니 개편을 덮어썼다.)</summary>
public static class InventoryUISetup
{
    private const string Explain = "Assets/Prefabs/UI/SkillExplainUI.prefab";
    private const string Pouch = "Assets/Prefabs/UI/PouchUI.prefab";
    private const string Picker = "Assets/Prefabs/UI/Hand Slot Selection/HandSlotSelectionUI.prefab";
    private const string Ground = "Assets/Resources/GroundItem.prefab";

    [MenuItem("Tools/UI/0928/Validate inventory UI")]
    public static void Validate()
    {
        foreach (string path in new[] { Explain, Pouch, Picker, Ground, "Assets/Resources/GroundMinion.prefab" })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) throw new Exception("누락: " + path);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) throw new Exception("Missing Script: " + path);
                if (!(behaviour is PouchUI || behaviour is MainMinionSelectionUI || behaviour is SkillExplainSlotUI || behaviour is GroundItem)) continue;
                var so = new SerializedObject(behaviour); var p = so.GetIterator();
                while (p.NextVisible(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "m_Script" && p.objectReferenceValue == null
                        // iconRenderer: 루트 SpriteRenderer를 쓰는 프리팹에서는 선택 참조다.
                        // itemDropSprite/fallbackItemIcon: 아이템 전용 그림이라 GroundMinion 에는 없다(GroundItem 이 null 이면 대체한다).
                        && !(behaviour is GroundItem && (p.name == "iconRenderer" || p.name == "itemDropSprite" || p.name == "fallbackItemIcon")))
                        throw new Exception($"누락된 참조: {path}/{behaviour.name}/{p.propertyPath}");
            }
        }
        Debug.Log("[InventoryUI] VALIDATE PASS — 프리팹 실제 자식/참조 확인.");
    }
}
