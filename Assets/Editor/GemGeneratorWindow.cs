#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class GemGeneratorWindow
{
    [MenuItem("Necromancer/Generate All Unique Gems")]
    public static void GenerateGems()
    {
        // 예시 경로: "Assets/SOData/Rewards/Gems/NewCategory"
        // 앞으로 추가될 보석들만 이 아래에 CreateGemSO를 호출하여 자동 생성되도록 작성하시면 됩니다.
        
        // --- [새로운 보석들 생성 위치] ---
        // CreateGemSO("Assets/SOData/Rewards/Gems/Category", "Gem_Unique_NewName", "New Name", "Desc", GemUniqueType.None, SynergyCategory.Common, GemSynergyGroup.None);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>새로운 보석 SO 데이터들이 성공적으로 생성(또는 갱신)되었습니다!</color>");
    }

    private static void CreateGemSO(string path, string fileName, string gemName, string desc, GemUniqueType uniqueType, SynergyCategory category, GemSynergyGroup synergyGroup)
    {
        string fullPath = $"{path}/{fileName}.asset";
        GemSO gem = AssetDatabase.LoadAssetAtPath<GemSO>(fullPath);
        bool isNew = false;
        
        if (gem == null)
        {
            gem = ScriptableObject.CreateInstance<GemSO>();
            isNew = true;
        }

        // 유니크 타입 이름 대신, 전달받은 멋진 이름(gemName)을 사용합니다.
        gem.itemName = gemName;
        gem.description = desc;
        gem.rarity = ItemRarity.Legendary; 
        gem.category = category;
        gem.synergyGroup = synergyGroup;
        
        var effect = new GemUniqueEffect { uniqueType = uniqueType, displayDescription = desc };
        gem.effects = new System.Collections.Generic.List<GemEffect> { effect };

        if (isNew)
        {
            AssetDatabase.CreateAsset(gem, fullPath);
        }
        else
        {
            EditorUtility.SetDirty(gem);
        }
    }
}
#endif
