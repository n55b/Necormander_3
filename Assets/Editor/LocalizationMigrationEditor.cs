using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.Localization.Settings;
using UnityEditor.Localization;

public class LocalizationMigrationEditor : EditorWindow
{
    [MenuItem("Tools/Localization/1. Clear Reward Text Table")]
    public static void ClearRewardTable()
    {
        var stringTableCollection = LocalizationEditorSettings.GetStringTableCollection("Reward Text Table");
        if (stringTableCollection == null)
            stringTableCollection = LocalizationEditorSettings.GetStringTableCollection("RewardTextTable");

        if (stringTableCollection == null)
        {
            Debug.LogError("테이블을 찾을 수 없습니다.");
            return;
        }

        var sharedTable = stringTableCollection.SharedData;
        Undo.RecordObject(sharedTable, "Clear Table");
        
        // 모든 엔트리 삭제
        sharedTable.Entries.Clear();
        foreach (var table in stringTableCollection.StringTables)
        {
            if (table != null)
            {
                Undo.RecordObject(table, "Clear Table");
                table.Clear();
                EditorUtility.SetDirty(table);
            }
        }

        EditorUtility.SetDirty(sharedTable);
        AssetDatabase.SaveAssets();
        Debug.Log("[Migration] Reward Text Table의 모든 데이터가 초기화되었습니다.");
    }

    [MenuItem("Tools/Localization/2. Migrate GrowthItemSO Texts")]
    public static void MigrateGrowthItems()
    {
        var stringTableCollection = LocalizationEditorSettings.GetStringTableCollection("Reward Text Table");
        if (stringTableCollection == null)
        {
            stringTableCollection = LocalizationEditorSettings.GetStringTableCollection("RewardTextTable");
            if (stringTableCollection == null) return;
        }

        var sharedTable = stringTableCollection.SharedData;
        var stringTable = stringTableCollection.GetTable("en") as StringTable;
        if (stringTable == null) stringTable = stringTableCollection.StringTables[0];

        if (sharedTable == null || stringTable == null) return;

        int count = 0;

        // 1. GrowthItemSO 마이그레이션
        string[] itemGuids = AssetDatabase.FindAssets("t:GrowthItemSO");
        foreach (string guid in itemGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("Deprecated")) continue; // Deprecated 제외

            GrowthItemSO itemSO = AssetDatabase.LoadAssetAtPath<GrowthItemSO>(path);
            if (itemSO != null)
            {
                Undo.RecordObject(itemSO, "Migrate Localization");

                if (!string.IsNullOrEmpty(itemSO.itemName))
                {
                    string keyName = $"{itemSO.name}_Name";
                    var entry = sharedTable.GetEntry(keyName);
                    if (entry == null)
                    {
                        entry = sharedTable.AddKey(keyName);
                        stringTable.AddEntry(keyName, itemSO.itemName);
                    }
                    itemSO.localizedItemName = new LocalizedString(sharedTable.TableCollectionNameGuid, entry.Id);
                }

                if (!string.IsNullOrEmpty(itemSO.description))
                {
                    string keyDesc = $"{itemSO.name}_Desc";
                    var entry = sharedTable.GetEntry(keyDesc);
                    if (entry == null)
                    {
                        entry = sharedTable.AddKey(keyDesc);
                        stringTable.AddEntry(keyDesc, itemSO.description);
                    }
                    itemSO.localizedDescription = new LocalizedString(sharedTable.TableCollectionNameGuid, entry.Id);
                }
                EditorUtility.SetDirty(itemSO);
                count++;
            }
        }

        // 2. MinionLineageSO 마이그레이션
        string[] lineageGuids = AssetDatabase.FindAssets("t:MinionLineageSO");
        foreach (string guid in lineageGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("Deprecated")) continue;

            MinionLineageSO lineageSO = AssetDatabase.LoadAssetAtPath<MinionLineageSO>(path);
            if (lineageSO != null)
            {
                Undo.RecordObject(lineageSO, "Migrate Localization");

                MigrateGrowthItemData(lineageSO.baseItemData, $"{lineageSO.name}_Base", sharedTable, stringTable);
                MigrateGrowthItemData(lineageSO.techAItemData, $"{lineageSO.name}_TechA", sharedTable, stringTable);
                MigrateGrowthItemData(lineageSO.techBItemData, $"{lineageSO.name}_TechB", sharedTable, stringTable);

                EditorUtility.SetDirty(lineageSO);
                count++;
            }
        }

        EditorUtility.SetDirty(sharedTable);
        EditorUtility.SetDirty(stringTable);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Migration] 성공적으로 {count}개의 데이터(GrowthItemSO & Lineage)를 마이그레이션했습니다!");
    }

    private static void MigrateGrowthItemData(GrowthItemData data, string prefix, SharedTableData sharedTable, StringTable stringTable)
    {
        if (data == null) return;

        if (!string.IsNullOrEmpty(data.itemName))
        {
            string keyName = $"{prefix}_Name";
            var entry = sharedTable.GetEntry(keyName);
            if (entry == null)
            {
                entry = sharedTable.AddKey(keyName);
                stringTable.AddEntry(keyName, data.itemName);
            }
            data.localizedItemName = new LocalizedString(sharedTable.TableCollectionNameGuid, entry.Id);
        }

        if (!string.IsNullOrEmpty(data.description))
        {
            string keyDesc = $"{prefix}_Desc";
            var entry = sharedTable.GetEntry(keyDesc);
            if (entry == null)
            {
                entry = sharedTable.AddKey(keyDesc);
                stringTable.AddEntry(keyDesc, data.description);
            }
            data.localizedDescription = new LocalizedString(sharedTable.TableCollectionNameGuid, entry.Id);
        }
    }
}
