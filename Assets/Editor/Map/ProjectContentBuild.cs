using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.Linq;
using UnityEngine.SceneManagement;

// Package immutable project documents without creating a second tracked copy under Assets.
public sealed class ProjectContentBuild : BuildPlayerProcessor, IProcessSceneWithReport
{
    int UnityEditor.Build.IOrderedCallback.callbackOrder=>0;
    public void OnProcessScene(Scene scene,BuildReport report)
    {
        if(report==null) return;
        var entries=scene.name==WorkshopTrial.SceneName ? ProjectParameterBindings.All() : ProjectParameterBindings.Published();
        if(entries.Length==0) return;
        var holder=new UnityEngine.GameObject("Project content bindings");
        SceneManager.MoveGameObjectToScene(holder,scene);
        holder.AddComponent<ProjectParameterBindings>().entries=entries;
    }
    public override void PrepareForBuild(BuildPlayerContext context)
    {
        string root=ProjectContentStore.Root;
        ProjectContentStore.RecoverPending();
        if(!Directory.Exists(root)) return;
        var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(WorkshopCatalog.AssetPath);
        ProjectParameterBindings.ValidatePublished();
        ProjectDialogue.ValidatePublished();
        ProjectTranslations.ValidatePublished();
        ProjectWorldValidation.ValidatePublished(catalog);
        foreach(string file in Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories))
        {
            Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(file));
            context.AddAdditionalPathToStreamingAssets(file,"DATA/"+file.Substring(root.Length+1).Replace('\\','/'));
        }
    }
}

internal sealed class ProjectContentAssetSaveFilter : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)=>UnityEngine.Application.isPlaying ?
        paths.Where(path=>!ProjectParameterBindings.HasLiveValues(path) &&
            !ProjectTranslations.HasLiveValues(AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>(path)) &&
            !ProjectTranslations.HasLiveKeys(AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.SharedTableData>(path))).ToArray() : paths;
}
