using UnityEngine;
using UnityEditor;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;
using System.Collections.Generic;
using System.IO;
using System;

public class GemTranslationPipeline : MonoBehaviour
{
    [Serializable]
    public class TranslationItem 
    { 
        public string key; 
        public string englishText; 
        public string koreanText; 
    }
    
    [Serializable]
    public class TranslationList 
    { 
        public List<TranslationItem> items = new List<TranslationItem>(); 
    }

    [MenuItem("Tools/Localization Pipeline/1. Export Untranslated Gems (JSON)")]
    public static void ExportUntranslated()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("Reward Text Table");
        if (collection == null) collection = LocalizationEditorSettings.GetStringTableCollection("RewardTextTable");
        
        var koTable = collection.GetTable("ko") as StringTable;
        if (koTable == null && collection.StringTables.Count > 1) koTable = collection.StringTables[1];

        TranslationList exportList = new TranslationList();
        HashSet<string> processedKeys = new HashSet<string>();

        // 1. GrowthItemSO (Gem, Treasure 등) 검색
        string[] guids = AssetDatabase.FindAssets("t:GrowthItemSO");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("Deprecated")) continue;
            
            GrowthItemSO itemSO = AssetDatabase.LoadAssetAtPath<GrowthItemSO>(path);
            if (itemSO != null)
            {
                CheckAndAdd(itemSO.itemName, $"{itemSO.name}_Name", exportList, processedKeys, koTable);
                CheckAndAdd(itemSO.description, $"{itemSO.name}_Desc", exportList, processedKeys, koTable);
            }
        }

        // 2. Unique Effects 검색 (GemUniqueType enum 기반)
        foreach (GemUniqueType type in Enum.GetValues(typeof(GemUniqueType)))
        {
            if (type == GemUniqueType.None) continue;
            string key = $"UniqueEffect_{type}";
            CheckAndAdd("", key, exportList, processedKeys, koTable, true);
        }

        if (exportList.items.Count == 0)
        {
            Debug.Log("[Export] 새로 번역할 항목이 없습니다! (모두 번역됨)");
            return;
        }

        string jsonPath = Path.Combine(Application.dataPath, "../untranslated_gems.json");
        string json = JsonUtility.ToJson(exportList, true);
        File.WriteAllText(jsonPath, json);

        Debug.Log($"[Export] {exportList.items.Count}개의 미번역 항목을 추출했습니다! 경로: {jsonPath}\nJSON 파일을 열어서 koreanText를 채운 뒤 'translated_gems.json'으로 이름을 바꿔주세요.");
    }

    [MenuItem("Tools/Localization Pipeline/2. Import Translated Gems (JSON)")]
    public static void ImportTranslated()
    {
        string jsonPath = Path.Combine(Application.dataPath, "../translated_gems.json");
        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"[Import] 파일을 찾을 수 없습니다: {jsonPath}\n'untranslated_gems.json'을 번역한 뒤 이름을 변경해주세요.");
            return;
        }

        string json = File.ReadAllText(jsonPath);
        TranslationList list = JsonUtility.FromJson<TranslationList>(json);

        var collection = LocalizationEditorSettings.GetStringTableCollection("Reward Text Table");
        var sharedTable = collection.SharedData;
        
        var koTable = collection.GetTable("ko") as StringTable;
        if (koTable == null && collection.StringTables.Count > 1) koTable = collection.StringTables[1];

        var enTable = collection.GetTable("en") as StringTable;
        if (enTable == null) enTable = collection.StringTables[0];

        int count = 0;
        foreach (var item in list.items)
        {
            if (string.IsNullOrEmpty(item.koreanText)) continue;

            var entry = sharedTable.GetEntry(item.key);
            if (entry == null) entry = sharedTable.AddKey(item.key);
            
            if (!string.IsNullOrEmpty(item.englishText))
                enTable.AddEntry(item.key, item.englishText);
                
            koTable.AddEntry(item.key, item.koreanText);
            count++;
        }

        EditorUtility.SetDirty(koTable);
        EditorUtility.SetDirty(enTable);
        EditorUtility.SetDirty(sharedTable);

        // 변경된 SO 파일들에 LocalizedString 레퍼런스 연결
        LinkSOReferences(sharedTable);

        AssetDatabase.SaveAssets();
        Debug.Log($"[Import] {count}개의 번역 데이터를 성공적으로 주입하고 에셋들을 갱신했습니다!");
    }

    private static void CheckAndAdd(string originalText, string key, TranslationList list, HashSet<string> processedKeys, StringTable koTable, bool isUniqueEffect = false)
    {
        if (string.IsNullOrEmpty(originalText) && !isUniqueEffect) return;
        if (processedKeys.Contains(key)) return;

        // 이미 한국어 테이블에 번역이 존재하는지 확인
        var entry = koTable?.GetEntry(key);
        if (entry != null && !string.IsNullOrEmpty(entry.LocalizedValue) && !entry.LocalizedValue.Contains("No translation"))
        {
            return; // 이미 번역됨
        }

        list.items.Add(new TranslationItem 
        { 
            key = key, 
            englishText = originalText, 
            koreanText = "" 
        });
        processedKeys.Add(key);
    }

    private static void LinkSOReferences(SharedTableData sharedTable)
    {
        string[] guids = AssetDatabase.FindAssets("t:GrowthItemSO");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("Deprecated")) continue;
            GrowthItemSO itemSO = AssetDatabase.LoadAssetAtPath<GrowthItemSO>(path);
            if (itemSO != null)
            {
                TryLink(sharedTable, $"{itemSO.name}_Name", ref itemSO.localizedItemName);
                TryLink(sharedTable, $"{itemSO.name}_Desc", ref itemSO.localizedDescription);
                EditorUtility.SetDirty(itemSO);
            }
        }

    }

    private static void TryLink(SharedTableData sharedTable, string key, ref LocalizedString localizedString)
    {
        var entry = sharedTable.GetEntry(key);
        if (entry != null)
        {
            localizedString = new LocalizedString(sharedTable.TableCollectionNameGuid, entry.Id);
        }
    }
}
