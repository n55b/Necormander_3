using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>마을 원본을 저장하지 않고 실제 선택 버튼 → GroundItem 드랍을 검사한다.</summary>
public static class VillageLoadoutCheck
{
    [MenuItem("Tools/Items/Verify Village Item Drops")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || UIPopUpManager.Instance != null)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        var previousScene = SceneManager.GetActiveScene();
        var previousManager = GameManager.Instance;
        var randomState = UnityEngine.Random.state;
        var village = EditorSceneManager.OpenPreviewScene("Assets/Scenes/VillageScene.unity");
        Scene scratch = default;
        try
        {
            scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch); // 드랍 복제본도 임시 씬에만 생성한다.
            var roots = village.GetRootGameObjects();
            var ui = roots.SelectMany(r => r.GetComponentsInChildren<VillageDebugLoadout>(true)).Single();
            var manager = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            manager.dataManager = manager.GetComponentInChildren<DataManager>(true);
            GameManager.Instance = manager;
            var items = manager.dataManager.GET_GROWTH_REGISTRY().items.Where(i => i != null).ToList();
            Check(items.Count > 0, "등록 아이템");

            var settings = new SerializedObject(ui);
            Check(settings.FindProperty("includeItems").boolValue, "마을 아이템 목록 활성화");
            var origin = settings.FindProperty("itemDropOrigin").objectReferenceValue as Transform;
            Check(origin != null && origin.GetComponent<NPCBase>() != null, "드랍 기준 NPC 배선");
            Vector3 dropPosition = origin.position + (Vector3)settings.FindProperty("itemDropOffset").vector2Value;
            foreach (string field in new[] { "includeMinions", "includeEquipments", "includeRightClicks" })
                settings.FindProperty(field).boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(VillageDebugLoadout).GetMethod("Populate", flags).Invoke(ui, null);
            var buttons = (List<GameObject>)typeof(VillageDebugLoadout).GetField("_spawned", flags).GetValue(ui);
            Check(buttons.Count == items.Count, "모든 등록 아이템의 선택 버튼");

            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].GetComponent<Button>().onClick.Invoke();
                var drops = scratch.GetRootGameObjects().Select(r => r.GetComponent<GroundItem>()).Where(d => d != null).ToArray();
                Check(drops.Length == 1 && drops[0].Item == items[i], "선택한 아이템 한 개 드랍");
                Check(Vector3.Distance(drops[0].transform.position, dropPosition) <= 0.401f, "NPC 앞 드랍 위치");
                Check(drops[0].GetComponent<Collider2D>() != null && drops[0] is IInteractable, "기존 F 습득 경로");
                Check(!ui.gameObject.activeSelf, "성공 후 선택창 닫기");
                UnityEngine.Object.DestroyImmediate(drops[0].gameObject);
            }
            Debug.Log($"[VillageLoadoutCheck] PASS: 아이템 {items.Count}종 선택 → NPC 앞 드랍/F 습득 배선");
        }
        finally
        {
            GameManager.Instance = previousManager;
            UnityEngine.Random.state = randomState;
            SceneManager.SetActiveScene(previousScene);
            if (scratch.IsValid()) EditorSceneManager.CloseScene(scratch, true);
            EditorSceneManager.ClosePreviewScene(village);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[VillageLoadoutCheck] " + message);
    }
}
