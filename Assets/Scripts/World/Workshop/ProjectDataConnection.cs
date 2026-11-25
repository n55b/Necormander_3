using System;
using System.IO;
using UnityEngine;

internal static class ProjectDataConnection
{
    internal const string Marker="necromander-project-v1";
    private static string selected;
    private static bool loaded;
    private static string Config=>Path.Combine(Application.persistentDataPath,"LevelWorkshop","project-path.txt");
    internal static string Checkout
    {
        get
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath,".."));
#elif LEVEL_WORKSHOP
            if(!loaded)
            {
                loaded=true;
                if(File.Exists(Config))
                {
                    var root=File.ReadAllText(Config).Trim();
                    if(Valid(root)) selected=root;
                }
            }
            return selected;
#else
            return null;
#endif
        }
    }
    private static bool Valid(string root)=>!string.IsNullOrWhiteSpace(root) && Path.IsPathRooted(root) &&
        File.Exists(Path.Combine(root,"ProjectSettings","ProjectVersion.txt")) && File.Exists(Path.Combine(root,"DATA",".project")) &&
        File.ReadAllText(Path.Combine(root,"DATA",".project")).Trim()==Marker;
    internal static void Connect(string root)
    {
#if LEVEL_WORKSHOP && !UNITY_EDITOR
        root=Path.GetFullPath(root.Trim().Trim('"'));
        if(!Valid(root)) throw new IOException("Necromander 프로젝트 루트 폴더를 지정하세요.");
        WorkshopFiles.WriteAtomic(Config,root); selected=root; loaded=true; ProjectRooms.Reset();
        ProjectParameterBindings.ReloadCheckout();
        ProjectTranslations.ReloadCheckout();
#else
        throw new InvalidOperationException("체크아웃 선택은 Windows 제작판에서 사용할 수 있습니다.");
#endif
    }
}
