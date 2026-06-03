using UnityEngine;
using UnityEditor;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;
using System.IO;
using System.Collections.Generic;

public class UITranslationPopulator : EditorWindow
{
    [System.Serializable]
    public class UIItem
    {
        public string key;
        public string koreanText;
    }

    [System.Serializable]
    public class UIList
    {
        public List<UIItem> items;
    }

    [MenuItem("Tools/Localization/5. Populate UI Text Table")]
    public static void PopulateUITextTable()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("UI Text Table");
        if (collection == null) collection = LocalizationEditorSettings.GetStringTableCollection("UITextTable");

        if (collection == null)
        {
            Debug.LogError("'UI Text Table'을 찾을 수 없습니다.");
            return;
        }

        var sharedTable = collection.SharedData;
        var enTable = collection.GetTable("en") as StringTable;
        var koTable = collection.GetTable("ko") as StringTable;

        if (enTable == null) enTable = collection.StringTables[0];
        if (koTable == null && collection.StringTables.Count > 1) koTable = collection.StringTables[1];

        if (enTable == null || koTable == null)
        {
            Debug.LogError("테이블 언어를 확인할 수 없습니다.");
            return;
        }

        Undo.RecordObject(sharedTable, "Populate UI Text Table");
        Undo.RecordObject(enTable, "Populate UI Text Table");
        Undo.RecordObject(koTable, "Populate UI Text Table");

        string path = Path.Combine(Application.dataPath, "../ui_translated.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            UIList list = JsonUtility.FromJson<UIList>(json);
            foreach(var item in list.items)
            {
                AddEntry(sharedTable, enTable, koTable, item.key, item.key, item.koreanText);
            }
        }
        else
        {
            Debug.LogError($"[UI Populate] {path} 파일을 찾을 수 없습니다.");
        }

        EditorUtility.SetDirty(sharedTable);
        EditorUtility.SetDirty(enTable);
        EditorUtility.SetDirty(koTable);
        AssetDatabase.SaveAssets();

        Debug.Log("[UITranslationPopulator] UI Text Table에 고정 텍스트들을 성공적으로 등록했습니다!");
    }

    private static void AddEntry(SharedTableData shared, StringTable en, StringTable ko, string key, string enText, string koText)
    {
        var entry = shared.GetEntry(key);
        if (entry == null)
        {
            entry = shared.AddKey(key);
        }
        en.AddEntry(key, enText);
        ko.AddEntry(key, koText);
    }
}
