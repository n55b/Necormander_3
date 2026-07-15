using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 내 상점에서 등장할 수 있는 모든 아이템 풀을 관리하는 레지스트리입니다.
/// (젬 효과가 전부 제거되면서 gemPool 도 함께 내렸습니다 — GEM_LEGACY.md 참조.
///  젬 에셋 자체는 SOData/Rewards/Gems 에 그대로 남아 있고, 소켓 배관도 살아 있습니다.)
/// </summary>
[CreateAssetMenu(fileName = "ShopRegistry", menuName = "Necromancer/Registry/ShopRegistry")]
public class ShopRegistrySO : ScriptableObject
{
    [Header("상점에 등장할 미니언 목록")]
    public List<MinionDataSO> minionPool = new List<MinionDataSO>();

#if UNITY_EDITOR
    [ContextMenu("Refresh Registry (Load All Active Items)")]
    public void RefreshRegistry()
    {
        minionPool.Clear();

        // 경로로 아군 소환수만 거른다 (MinionDataSO 는 적/엘리트/보스와 공용 타입이다).
        string[] minionGuids = UnityEditor.AssetDatabase.FindAssets("t:MinionDataSO");
        foreach (var guid in minionGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("/Minion/MinionData/")) continue;
            if (path.Contains("/Deprecated/")) continue;

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MinionDataSO>(path);
            if (asset != null) minionPool.Add(asset);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"<color=yellow>[ShopRegistry]</color> 자동 갱신 완료: 미니언({minionPool.Count})");
    }
#endif
}
