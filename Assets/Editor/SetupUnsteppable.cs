using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;
using UnityEngine.AI;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;

public static class SetupUnsteppable
{
    [MenuItem("Tools/Setup Unsteppable Terrain")]
    public static void Run()
    {
        Debug.Log("=== SetupUnsteppable Start ===");
        
        // 1. 레이어 추가
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layersProp = tagManager.FindProperty("layers");
        string[] targetLayers = new string[] { "Unsteppable", "Player_Dash", "Enemy_Flying" };
        
        foreach (var target in targetLayers)
        {
            bool exists = false;
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                var prop = layersProp.GetArrayElementAtIndex(i);
                if (prop.stringValue == target)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                for (int i = 8; i < 32; i++)
                {
                    var prop = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(prop.stringValue))
                    {
                        prop.stringValue = target;
                        Debug.Log("Added Layer: " + target + " at index " + i);
                        break;
                    }
                }
            }
        }
        tagManager.ApplyModifiedProperties();
        
        // 2. Physics 2D 충돌 매트릭스 설정
        int unsteppableIdx = -1;
        int playerDashIdx = -1;
        int enemyFlyingIdx = -1;
        for (int i = 0; i < layersProp.arraySize; i++)
        {
            string val = layersProp.GetArrayElementAtIndex(i).stringValue;
            if (val == "Unsteppable") unsteppableIdx = i;
            if (val == "Player_Dash") playerDashIdx = i;
            if (val == "Enemy_Flying") enemyFlyingIdx = i;
        }
        
        if (unsteppableIdx != -1 && playerDashIdx != -1)
        {
            Physics2D.IgnoreLayerCollision(playerDashIdx, unsteppableIdx, true);
            Debug.Log("Set Physics2D.IgnoreLayerCollision(Player_Dash, Unsteppable, true)");
        }
        if (unsteppableIdx != -1 && enemyFlyingIdx != -1)
        {
            Physics2D.IgnoreLayerCollision(enemyFlyingIdx, unsteppableIdx, true);
            Debug.Log("Set Physics2D.IgnoreLayerCollision(Enemy_Flying, Unsteppable, true)");
        }
        
        // 3. NavMesh Area 설정
        var navSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0];
        var serializedNav = new SerializedObject(navSettings);
        var areasProp = serializedNav.FindProperty("areas");
        
        bool areaExists = false;
        int targetAreaIndex = -1;
        for (int i = 0; i < areasProp.arraySize; i++)
        {
            var area = areasProp.GetArrayElementAtIndex(i);
            var nameProp = area.FindPropertyRelative("name");
            if (nameProp.stringValue == "UnsteppableArea")
            {
                areaExists = true;
                targetAreaIndex = i;
                break;
            }
        }
        
        if (!areaExists)
        {
            for (int i = 3; i < areasProp.arraySize; i++)
            {
                var area = areasProp.GetArrayElementAtIndex(i);
                var nameProp = area.FindPropertyRelative("name");
                if (string.IsNullOrEmpty(nameProp.stringValue))
                {
                    nameProp.stringValue = "UnsteppableArea";
                    var costProp = area.FindPropertyRelative("cost");
                    costProp.floatValue = 1.0f;
                    targetAreaIndex = i;
                    Debug.Log("Added NavMesh Area: UnsteppableArea at index " + i);
                    break;
                }
            }
        }
        serializedNav.ApplyModifiedProperties();
        
        if (targetAreaIndex == -1)
        {
            Debug.LogError("Failed to register/find UnsteppableArea NavMesh Area!");
            return;
        }
        
        // 4. 적 및 미니언 프리팹 Area Mask 일괄 변경
        string[] agentPrefabsGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { "Assets/Prefabs/Enemy", "Assets/Prefabs/Ally" });
        foreach (string guid in agentPrefabsGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            
            var agent = prefab.GetComponentInChildren<NavMeshAgent>(true);
            if (agent != null)
            {
                int currentMask = agent.areaMask;
                int targetBit = 1 << targetAreaIndex;
                if ((currentMask & targetBit) != 0)
                {
                    agent.areaMask = currentMask & ~targetBit;
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                    Debug.Log($"Updated NavMeshAgent AreaMask for prefab: {path}");
                }
            }
        }
        
        // 5. BattleScene.unity 씬 수정
        GameObject gridObj = GameObject.Find("Map/Grid");
        if (gridObj != null)
        {
            Transform gridTrans = gridObj.transform;
            Transform existing = gridTrans.Find("Unsteppable");
            GameObject unsteppableObj;
            if (existing != null)
            {
                unsteppableObj = existing.gameObject;
            }
            else
            {
                unsteppableObj = new GameObject("Unsteppable");
                unsteppableObj.transform.SetParent(gridTrans, false);
            }
            
            SetupTilemapComponents(unsteppableObj, unsteppableIdx, targetAreaIndex);
            
            EditorSceneManager.MarkSceneDirty(gridObj.scene);
            EditorSceneManager.SaveScene(gridObj.scene);
            Debug.Log("Configured 'Unsteppable' Tilemap in active scene.");
        }
        else
        {
            Debug.LogWarning("Grid object 'Map/Grid' not found in the active scene.");
        }
        
        // 6. 방 프리팹 일괄 수정
        string[] roomPrefabsGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { "Assets/Prefabs/Map/Room Map Prefabs" });
        foreach (string guid in roomPrefabsGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            if (prefabRoot == null) continue;
            
            try
            {
                Transform gridTrans = prefabRoot.transform.Find("Grid");
                if (gridTrans == null) continue;
                
                Transform existing = gridTrans.Find("Unsteppable");
                GameObject unsteppableObj;
                
                bool isModified = false;
                
                if (existing != null)
                {
                    unsteppableObj = existing.gameObject;
                }
                else
                {
                    unsteppableObj = new GameObject("Unsteppable");
                    unsteppableObj.transform.SetParent(gridTrans, false);
                    isModified = true;
                }
                
                isModified |= SetupTilemapComponents(unsteppableObj, unsteppableIdx, targetAreaIndex);
                
                if (isModified)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    Debug.Log($"Configured 'Unsteppable' Tilemap in Prefab: {path}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
        
        Debug.Log("=== SetupUnsteppable Completed Successfully ===");
    }
    
    private static bool SetupTilemapComponents(GameObject go, int layerIdx, int areaIdx)
    {
        bool changed = false;
        
        if (go.layer != layerIdx)
        {
            go.layer = layerIdx;
            changed = true;
        }
        
        var tilemap = go.GetComponent<Tilemap>();
        if (tilemap == null)
        {
            go.AddComponent<Tilemap>();
            changed = true;
        }
        
        var renderer = go.GetComponent<TilemapRenderer>();
        if (renderer == null)
        {
            go.AddComponent<TilemapRenderer>();
            changed = true;
        }
        
        var collider = go.GetComponent<TilemapCollider2D>();
        if (collider == null)
        {
            go.AddComponent<TilemapCollider2D>();
            changed = true;
        }
        
        System.Type modifierType = System.Type.GetType("Unity.AI.Navigation.NavMeshModifier, Unity.AI.Navigation");
        if (modifierType == null)
        {
            modifierType = System.Type.GetType("UnityEngine.AI.NavMeshModifier, UnityEngine.AIModule");
        }
        
        if (modifierType != null)
        {
            var modifier = go.GetComponent(modifierType);
            if (modifier == null)
            {
                modifier = go.AddComponent(modifierType);
                changed = true;
            }
            
            var overrideAreaProp = modifierType.GetProperty("overrideArea");
            if (overrideAreaProp != null)
            {
                bool currentOverride = (bool)overrideAreaProp.GetValue(modifier);
                if (!currentOverride)
                {
                    overrideAreaProp.SetValue(modifier, true);
                    changed = true;
                }
            }
            
            var areaProp = modifierType.GetProperty("area");
            if (areaProp != null)
            {
                int currentArea = (int)areaProp.GetValue(modifier);
                if (currentArea != areaIdx)
                {
                    areaProp.SetValue(modifier, areaIdx);
                    changed = true;
                }
            }
        }
        else
        {
            Debug.LogWarning("NavMeshModifier type could not be resolved.");
        }
        
        return changed;
    }
}
