#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// VillageScene 에 '소환수 선택' NPC 를 배치한다. 기존 마을 NPC(Map/NPC)를 복제해 외형/콜라이더 구성을
/// 그대로 쓰고, 상호작용 컴포넌트만 MinionSelectNPC 로 바꾼다. 다시 실행하면 기존 것을 지우고 새로 만든다.
/// </summary>
public static class MinionSelectNPCSetup
{
    private const string UIPrefabPath = "Assets/Prefabs/UI/Reward Selection/RewardSelectionUI.prefab";
    private const string NpcName = "MinionSelectNPC";

    [MenuItem("Tools/Village/Setup Minion Select NPC")]
    public static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "VillageScene") { Debug.LogError("[Setup] VillageScene 을 열고 실행하세요."); return; }

        var source = GameObject.Find("Map/NPC");
        if (source == null) { Debug.LogError("[Setup] Map/NPC 를 찾지 못했습니다."); return; }

        var old = GameObject.Find("Map/" + NpcName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var npc = Object.Instantiate(source, source.transform.parent);
        Undo.RegisterCreatedObjectUndo(npc, "Create Minion Select NPC");
        npc.name = NpcName;
        npc.transform.position = new Vector3(-4f, 2f, 0f); // 기존 NPC(4,2)의 맞은편

        var sr = npc.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = new Color(0.8f, 0.7f, 1f, 1f); // 구분용 옅은 보라 틴트(외형 교체 전까지)

        var range = npc.transform.Find("InteractRange");
        if (range == null) { Debug.LogError("[Setup] InteractRange 자식이 없습니다."); return; }

        foreach (var oldNpc in range.GetComponents<NPCBase>()) Object.DestroyImmediate(oldNpc);
        foreach (var popup in range.GetComponents<PopupSystem>()) Object.DestroyImmediate(popup);

        var select = range.gameObject.AddComponent<MinionSelectNPC>();
        var so = new SerializedObject(select);
        so.FindProperty("selectionUIPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<RewardSelectionUI>(UIPrefabPath);
        var nameProp = so.FindProperty("name");
        if (nameProp != null && nameProp.propertyType == SerializedPropertyType.String) nameProp.stringValue = "소환수 선택";
        so.ApplyModifiedPropertiesWithoutUndo();

        // 머리 위 이름표가 있으면 바꾼다.
        foreach (var t in npc.GetComponentsInChildren<TMPro.TMP_Text>(true)) t.text = "소환수 선택";
        foreach (var t in npc.GetComponentsInChildren<UnityEngine.UI.Text>(true)) t.text = "소환수 선택";

        // 메인 소환수 에셋에 강화 기본값(1.2/1.45/1.75)을 실제로 기록해 둔다(인스펙터에서 바로 조정 가능하게).
        foreach (var g in AssetDatabase.FindAssets("t:MainMinionDataSO"))
        {
            var m = AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null) EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = npc;
        Debug.Log("[Setup] MinionSelectNPC 배치 완료: " + npc.transform.position);
    }
}
#endif
