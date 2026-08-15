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

            // 포맷이 안 맞으면 읽지 않는다. 억지로 읽으면 '슬롯이 조용히 비어 있는' 상태로 게임이
            // 시작돼서 원인을 찾기가 훨씬 어려워진다. 그냥 지우고 새 런으로 보낸다 —
            // 이 프로젝트는 아직 개발 중이라 세이브를 마이그레이션할 가치보다 확실함이 크다.
            // (파일을 지워두지 않으면 다음 실행 때 SaveExists() 가 true 라 '이어하기'가 계속 뜬다.)
            if (data.saveVersion != SaveData.CURRENT_VERSION)
            {
                Debug.LogWarning($"<color=orange>[SaveSystem]</color> 세이브 버전이 맞지 않아 폐기합니다 " +
                                 $"(파일 v{data.saveVersion} / 현재 v{SaveData.CURRENT_VERSION}). 새 게임으로 시작하세요.");
                DeleteSave();
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

    /// <summary>
    /// 이어할 수 있는 세이브가 있는가. <b>버전까지 본다.</b>
    ///
    /// 파일 존재만 보면, 구버전 세이브를 가진 사람에게 '이어하기'가 멀쩡히 떠 있다가 눌렀을 때
    /// Load 가 null 을 돌려주며 아무 일도 안 일어난다 — 버튼이 고장 난 것처럼 보인다.
    /// 여기서 같이 걸러서 애초에 그 선택지가 안 뜨게 한다.
    /// </summary>
    public static bool SaveExists()
    {
        if (!File.Exists(SavePath)) return false;

        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            return data != null && data.saveVersion == SaveData.CURRENT_VERSION;
        }
        catch (System.Exception ex)
        {
            // 깨진 파일도 '없는 것'으로 취급한다. 여기서 예외를 던지면 타이틀 화면이 통째로 죽는다.
            Debug.LogWarning($"<color=orange>[SaveSystem]</color> 세이브 파일을 읽을 수 없습니다: {ex.Message}");
            return false;
        }
    }
}
