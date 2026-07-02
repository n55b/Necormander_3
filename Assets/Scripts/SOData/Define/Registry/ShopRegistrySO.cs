using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 내 상점에서 등장할 수 있는 모든 아이템 풀을 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "ShopRegistry", menuName = "Necromancer/Registry/ShopRegistry")]
public class ShopRegistrySO : ScriptableObject
{
    [Header("상점에 등장할 보석 목록")]
    public List<GemSO> gemPool = new List<GemSO>();

    [Header("상점에 등장할 미니언 목록")]
    public List<MinionDataSO> minionPool = new List<MinionDataSO>();

#if UNITY_EDITOR
    [ContextMenu("Refresh Registry (Load All Active Items)")]
    public void RefreshRegistry()
    {
        gemPool.Clear();
        minionPool.Clear();

        // 1. 보석(Gem) 검색 (Deprecated 제외)
        string[] gemGuids = UnityEditor.AssetDatabase.FindAssets("t:GemSO");
        foreach (var guid in gemGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;
            
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GemSO>(path);
            if (asset != null) gemPool.Add(asset);
        }

        // 2. 미니언 데이터(MinionDataSO) 검색
        string[] minionGuids = UnityEditor.AssetDatabase.FindAssets("t:MinionDataSO");
        foreach (var guid in minionGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionDataSO>(path);
            if (asset != null) minionPool.Add(asset);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        
        Debug.Log($"<color=yellow>[ShopRegistry]</color> 자동 갱신 완료: 상점 보석({gemPool.Count}), 미니언({minionPool.Count})");
    }
#endif
}
