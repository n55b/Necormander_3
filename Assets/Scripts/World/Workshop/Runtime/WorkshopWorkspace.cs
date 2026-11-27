using System.IO;
using UnityEngine;

// The creator's local folder for its own settings files (floor.json, village.json).
internal static class WorkshopWorkspace
{
    private static string home;
    internal static string Home => home ??= Path.Combine(Application.persistentDataPath,"LevelWorkshop");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { home=null; }
#if UNITY_EDITOR
    internal static void UseTestHome(string path) { home=path; }
    internal static void UseDefaultHome() { home=null; }
#endif
}
