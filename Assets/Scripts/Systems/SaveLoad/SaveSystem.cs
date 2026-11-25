using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class SaveSystem
{
    private const string SaveFileName = "save_data.json";
    private static readonly object FileLock = new object();
    private static string SavePath => Path.Combine(WorkshopTrial.Active ? WorkshopTrial.SaveFolder : Application.persistentDataPath, SaveFileName);

    private enum FileState { Missing, Current, Invalid, Unsupported, Unreadable }

    // Read the version without SaveData's default for newly captured checkpoints.
    [Serializable]
    private sealed class SaveHeader
    {
        public int saveVersion = 0;
        public int currentFloor = 0;
    }

    public static bool Save(SaveData data)
    {
        lock (FileLock)
        {
            string path = SavePath;
            string backup = path + ".bak";
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (data == null || data.saveVersion != SaveData.CURRENT_VERSION || !HasValidCheckpoint(data))
                    throw new InvalidOperationException("Invalid run checkpoint.");

                FileState primary = Read(path, out _);
                if (PreventsOverwrite(primary) || PreventsOverwrite(Read(backup, out _)))
                    throw new InvalidOperationException("Existing save is unreadable or uses a different schema.");

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(payload, 0, payload.Length);
                    file.Flush(flushToDisk: true);
                }

                // Preserve the good backup when replacing a damaged primary.
                if (primary == FileState.Missing) File.Move(temporary, path);
                else File.Replace(temporary, path, primary == FileState.Current ? backup : null);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveSystem] Checkpoint was not written: {ex.Message}");
                return false;
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException ex) { Debug.LogWarning($"[SaveSystem] Temporary file cleanup failed: {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { Debug.LogWarning($"[SaveSystem] Temporary file cleanup failed: {ex.Message}"); }
            }
        }
    }

    public static SaveData Load() => ReadCheckpoint(logResult: true);

    public static bool SaveExists() => ReadCheckpoint(logResult: false) != null;

    public static bool DeleteSave()
    {
        lock (FileLock)
        {
            try
            {
                string path = SavePath;
                File.Delete(path + ".bak");
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveSystem] Checkpoint deletion failed: {ex.Message}");
                return false;
            }
        }
    }

    private static SaveData ReadCheckpoint(bool logResult)
    {
        lock (FileLock)
        {
            string path = SavePath;
            FileState primary = Read(path, out var data);
            if (primary == FileState.Current) return data;

            if (primary == FileState.Unsupported)
            {
                if (logResult) Debug.LogWarning("[SaveSystem] Saved run uses an unsupported schema. The file was preserved.");
                return null;
            }

            FileState backup = Read(path + ".bak", out data);
            if (backup == FileState.Current)
            {
                if (logResult) Debug.LogWarning("[SaveSystem] Restored the previous checkpoint from the backup.");
                return data;
            }

            if (logResult && (primary != FileState.Missing || backup != FileState.Missing))
                Debug.LogWarning($"[SaveSystem] No usable checkpoint (primary: {primary}, backup: {backup}).");
            return null;
        }
    }

    private static FileState Read(string path, out SaveData data)
    {
        data = null;
        try
        {
            string json = File.ReadAllText(path);
            var header = JsonUtility.FromJson<SaveHeader>(json);
            if (header == null) return FileState.Invalid;
            if (header.saveVersion != SaveData.CURRENT_VERSION)
                return header.saveVersion != 0 || header.currentFloor > 0 ? FileState.Unsupported : FileState.Invalid;

            data = new SaveData { playerHP = -1f };
            JsonUtility.FromJsonOverwrite(json, data);
            return HasValidCheckpoint(data) ? FileState.Current : FileState.Invalid;
        }
        catch (FileNotFoundException) { return FileState.Missing; }
        catch (DirectoryNotFoundException) { return FileState.Missing; }
        catch (ArgumentException) { return FileState.Invalid; }
        catch (IOException) { return FileState.Unreadable; }
        catch (UnauthorizedAccessException) { return FileState.Unreadable; }
    }

    private static bool PreventsOverwrite(FileState state) => state == FileState.Unsupported || state == FileState.Unreadable;

    private static bool HasValidCheckpoint(SaveData data)
        => data.currentFloor > 0 && data.playerHP >= 0f && !float.IsInfinity(data.playerHP);
}
