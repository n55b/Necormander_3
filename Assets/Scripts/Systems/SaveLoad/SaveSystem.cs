using System.IO;
using UnityEngine;

public static class SaveSystem
{
    private static readonly string SaveFileName = "save_data.json";
    private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    public static void Save(SaveData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
            Debug.Log($"<color=green>[SaveSystem]</color> Game saved successfully to: {SavePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>[SaveSystem]</color> Failed to save game: {ex.Message}");
        }
    }

    public static SaveData Load()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                Debug.Log("<color=yellow>[SaveSystem]</color> Save file not found.");
                return null;
            }

            string json = File.ReadAllText(SavePath);
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null)
            {
                Debug.LogError("<color=red>[SaveSystem]</color> Failed to deserialize save data (result is null).");
                return null;
            }

            Debug.Log($"<color=green>[SaveSystem]</color> Game loaded successfully from: {SavePath}");
            return data;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>[SaveSystem]</color> Failed to load game: {ex.Message}");
            return null;
        }
    }

    public static void DeleteSave()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
                Debug.Log("<color=yellow>[SaveSystem]</color> Save file deleted.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>[SaveSystem]</color> Failed to delete save file: {ex.Message}");
        }
    }

    public static bool SaveExists()
    {
        return File.Exists(SavePath);
    }
}
