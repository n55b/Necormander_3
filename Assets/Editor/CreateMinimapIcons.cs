#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class CreateMinimapIconsPrefab
{
    static CreateMinimapIconsPrefab()
    {
        try
        {
            string prefabPath = "Assets/Prefabs/Map/MiniMapIcons.prefab";
            // 파일이 이미 존재한다면 매 도메인 릴로드마다 덮어쓰는 짓을 엄격히 금지 (타임스탬프 꼬임 완치)
            if (!File.Exists(prefabPath))
            {
                Execute();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[CreateMinimapIconsPrefab] Auto-execution delayed: " + ex.Message);
        }
    }

    [MenuItem("Tools/Create Minimap Icons Prefab")]
    public static void Execute()
    {
        try
        {
            // 1. 부모 오브젝트 생성
            GameObject parentObj = new GameObject("MiniMapIcons");

            // 2. PlayerSkillMinimapIcon 자식 생성
            GameObject playerIcon = new GameObject("PlayerSkillMinimapIcon", typeof(SpriteRenderer));
            playerIcon.transform.SetParent(parentObj.transform, false);
            playerIcon.transform.localScale = new Vector3(5f, 5f, 5f);
            playerIcon.layer = 15; // 미니맵 레이어
            var srPlayer = playerIcon.GetComponent<SpriteRenderer>();
            if (srPlayer != null)
            {
                srPlayer.sortingOrder = 2;
                srPlayer.color = new Color(0f, 1f, 1f, 1f); // Cyan
                string pathPlayer = AssetDatabase.GUIDToAssetPath("75f5f34dc1b5347e0b8351032682f224");
                if (!string.IsNullOrEmpty(pathPlayer))
                {
                    srPlayer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pathPlayer);
                }
            }

            // 3. MinionSkillMinimapIcon 자식 생성
            GameObject minionIcon = new GameObject("MinionSkillMinimapIcon", typeof(SpriteRenderer));
            minionIcon.transform.SetParent(parentObj.transform, false);
            minionIcon.transform.localScale = new Vector3(5f, 5f, 5f);
            minionIcon.layer = 15; // 미니맵 레이어
            var srMinion = minionIcon.GetComponent<SpriteRenderer>();
            if (srMinion != null)
            {
                srMinion.sortingOrder = 2;
                srMinion.color = new Color(0f, 1f, 0f, 1f); // Green
                string pathMinion = AssetDatabase.GUIDToAssetPath("a86470a33a6bf42c4b3595704624658b");
                if (!string.IsNullOrEmpty(pathMinion))
                {
                    srMinion.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pathMinion);
                }
            }

            // 4. 프리팹으로 저장
            string prefabPath = "Assets/Prefabs/Map/MiniMapIcons.prefab";
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Map"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                {
                    AssetDatabase.CreateFolder("Assets", "Prefabs");
                }
                AssetDatabase.CreateFolder("Assets/Prefabs", "Map");
            }
            
            PrefabUtility.SaveAsPrefabAsset(parentObj, prefabPath);
            GameObject.DestroyImmediate(parentObj);

            // 포스 리임포트 및 리프레시 수행
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Prefabs/Map/CombatCenterTrigger.prefab", ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            Debug.Log("<color=green>[EditorScript]</color> Successfully created and force-imported MiniMapIcons prefab.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[CreateMinimapIconsPrefab] Failed to execute generation: " + ex.ToString());
        }
    }

    [MenuItem("Tools/Attach Triggers and Icons to Normal Rooms")]
    public static void AttachToNormalRooms()
    {
        try
        {
            string folderPath = "Assets/Prefabs/Map/Room Map Prefabs/Normal/Test";
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError("Target folder not found: " + folderPath);
                return;
            }

            string[] prefabFiles = Directory.GetFiles(folderPath, "*.prefab");
            GameObject triggerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/CombatCenterTrigger.prefab");
            GameObject iconsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/MiniMapIcons.prefab");
            
            // 유저님이 요청하신 원형 장판 예고 이펙트 로드
            GameObject vfxPrefabToAssign = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Skill Visual Effects/Center Skill Hitbox Circle Prefab.prefab");
            if (vfxPrefabToAssign == null)
            {
                // 없으면 Fallback용 장판 로드
                vfxPrefabToAssign = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Skill Visual Effects/TelegraphHitbox Prefab.prefab");
            }

            if (triggerPrefab == null || iconsPrefab == null)
            {
                Debug.LogError("Required prefabs (CombatCenterTrigger or MiniMapIcons) not found at Assets/Prefabs/Map/");
                return;
            }

            foreach (string file in prefabFiles)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(file);
                RoomInstance room = root.GetComponent<RoomInstance>();

                if (room != null)
                {
                    // 1. CombatCenterTrigger 자식 주입
                    Transform existingTrigger = root.transform.Find("CombatCenterTrigger");
                    if (existingTrigger == null)
                    {
                        GameObject triggerInstance = (GameObject)PrefabUtility.InstantiatePrefab(triggerPrefab, root.transform);
                        triggerInstance.name = "CombatCenterTrigger";
                        triggerInstance.transform.localPosition = Vector3.zero;
                    }

                    // 2. MiniMapIcons 자식 주입 및 직렬화 필드 맵
                    Transform existingIcons = root.transform.Find("MiniMapIcons");
                    GameObject iconsObj = null;
                    if (existingIcons == null)
                    {
                        iconsObj = (GameObject)PrefabUtility.InstantiatePrefab(iconsPrefab, root.transform);
                        iconsObj.name = "MiniMapIcons";
                        iconsObj.transform.localPosition = Vector3.zero;
                    }
                    else
                    {
                        iconsObj = existingIcons.gameObject;
                    }

                    if (iconsObj != null)
                    {
                        var serializedObject = new SerializedObject(room);
                        var prop = serializedObject.FindProperty("miniMapIconsParent");
                        if (prop != null)
                        {
                            prop.objectReferenceValue = iconsObj.transform;
                            serializedObject.ApplyModifiedProperties();
                        }
                    }

                    // 3. NormalRoomEvent 컴포넌트의 spawnVfxPrefab 인스펙터 슬롯에 에셋 정식 주입
                    NormalRoomEvent roomEvent = root.GetComponentInChildren<NormalRoomEvent>();
                    if (roomEvent != null)
                    {
                        var serializedEvent = new SerializedObject(roomEvent);
                        var vfxProp = serializedEvent.FindProperty("spawnVfxPrefab");
                        if (vfxProp != null)
                        {
                            vfxProp.objectReferenceValue = vfxPrefabToAssign;
                            serializedEvent.ApplyModifiedProperties();
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, file);
                    Debug.Log($"<color=green>[EditorScript]</color> Successfully injected CombatCenterTrigger, MiniMapIcons & spawnVfxPrefab to {Path.GetFileName(file)}");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.Refresh();
            Debug.Log("<color=green>[EditorScript]</color> Completed programmatic injection including VFX.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[CreateMinimapIconsPrefab] Failed to attach elements: " + ex.ToString());
        }
    }
}
#endif
