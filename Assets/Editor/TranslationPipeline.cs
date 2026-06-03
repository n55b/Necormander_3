using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.Localization.Settings;
using UnityEditor.Localization;
using System.IO;

public class TranslationPipeline : EditorWindow
{
    [System.Serializable]
    public class TranslationData
    {
        public string key;
        public string englishText;
        public string koreanText; // 번역 시 사용할 필드
    }

    [System.Serializable]
    public class TranslationList
    {
        public List<TranslationData> items = new List<TranslationData>();
    }

    [MenuItem("Tools/Localization/3. Export Untranslated (JSON)")]
    public static void ExportUntranslated()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("Reward Text Table");
        if (collection == null) collection = LocalizationEditorSettings.GetStringTableCollection("RewardTextTable");
        if (collection == null) return;

        var enTable = collection.GetTable("en") as StringTable;
        var koTable = collection.GetTable("ko") as StringTable;

        if (enTable == null) enTable = collection.StringTables[0];
        if (koTable == null && collection.StringTables.Count > 1) koTable = collection.StringTables[1];

        if (enTable == null || koTable == null)
        {
            Debug.LogError("영어(en) 또는 한국어(ko) 테이블을 찾을 수 없습니다.");
            return;
        }

        TranslationList list = new TranslationList();

        foreach (var entry in enTable.Values)
        {
            var keyId = entry.KeyId;
            var koEntry = koTable.GetEntry(keyId);

            // 한국어 번역이 비어있으면 추출
            if (koEntry == null || string.IsNullOrEmpty(koEntry.LocalizedValue))
            {
                var sharedEntry = collection.SharedData.GetEntry(keyId);
                if (sharedEntry != null)
                {
                    list.items.Add(new TranslationData
                    {
                        key = sharedEntry.Key,
                        englishText = entry.LocalizedValue
                    });
                }
            }
        }

        string json = JsonUtility.ToJson(list, true);
        string path = Path.Combine(Application.dataPath, "../untranslated.json");
        File.WriteAllText(path, json);

        Debug.Log($"[Translation] {list.items.Count}개의 미번역 텍스트를 추출했습니다! -> {path}");
    }

    [MenuItem("Tools/Localization/4. Import Translated (JSON)")]
    public static void ImportTranslated()
    {
        string path = Path.Combine(Application.dataPath, "../translated.json");
        if (!File.Exists(path))
        {
            Debug.LogError($"[Translation] 번역 파일을 찾을 수 없습니다: {path}");
            return;
        }

        string json = File.ReadAllText(path);
        TranslationList list = JsonUtility.FromJson<TranslationList>(json);

        var collection = LocalizationEditorSettings.GetStringTableCollection("Reward Text Table");
        if (collection == null) collection = LocalizationEditorSettings.GetStringTableCollection("RewardTextTable");
        if (collection == null) return;

        var koTable = collection.GetTable("ko") as StringTable;
        if (koTable == null && collection.StringTables.Count > 1) koTable = collection.StringTables[1];

        if (koTable == null)
        {
            Debug.LogError("한국어(ko) 테이블을 찾을 수 없습니다.");
            return;
        }

        Undo.RecordObject(koTable, "Import Translated Texts");

        int count = 0;
        foreach (var item in list.items)
        {
            if (!string.IsNullOrEmpty(item.koreanText))
            {
                var entry = collection.SharedData.GetEntry(item.key);
                if (entry != null)
                {
                    koTable.AddEntry(item.key, item.koreanText);
                    count++;
                }
            }
        }

        EditorUtility.SetDirty(koTable);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Translation] 성공적으로 {count}개의 번역 데이터를 한국어 테이블에 주입했습니다!");
    }
}
