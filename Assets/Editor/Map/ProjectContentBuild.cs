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
    public static void StartRoomCheck()
    {
        if (!UnityEngine.Application.isBatchMode || UnityEngine.Application.companyName!="NecromanderVerification")
            throw new System.InvalidOperationException("Run in the isolated verification project.");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=null;
        EditorApplication.EnterPlaymode();
    }
    public static void BuildRoomCheck()
    {
        var options=new BuildPlayerOptions {
            scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
            locationPathName="Builds/ProjectContent/Game.exe",target=BuildTarget.StandaloneWindows64,
            extraScriptingDefines=new[]{"PROJECT_CONTENT_QA"}
        };
        if(BuildPipeline.BuildPlayer(options).summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Project content build failed");
    }
    public static void BuildCreatorCheck()
    {
        var options=new BuildPlayerOptions{
            scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).Append(WorkshopTrial.ScenePath).Distinct().ToArray(),
            locationPathName="Builds/ProjectContentCreator/Game.exe",target=BuildTarget.StandaloneWindows64,
            extraScriptingDefines=new[]{"LEVEL_WORKSHOP","PROJECT_CONTENT_QA"}
        };
        if(BuildPipeline.BuildPlayer(options).summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Creator connection check build failed.");
    }
    public override void PrepareForBuild(BuildPlayerContext context)
    {
        string root=ProjectContentStore.Root;
        ProjectContentStore.RecoverPending();
        if(!Directory.Exists(root)) return;
        var catalog=AssetDatabase.LoadAssetAtPath<WorkshopCatalog>(ProjectRooms.CatalogPath);
        foreach(string dir in Directory.Exists(Path.Combine(root,ProjectRooms.DirectoryName)) ?
            Directory.GetDirectories(Path.Combine(root,ProjectRooms.DirectoryName)) : System.Array.Empty<string>())
            if(ProjectRooms.HasFiles(dir))ProjectRooms.ValidateForBuild(Path.GetFileName(dir),catalog);
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
    // 플레이 중이나 WithPublished 검사 중에는 DATA 값이 적용된 에셋을 저장하지 않는다.
    private static string[] OnWillSaveAssets(string[] paths)=>UnityEngine.Application.isPlaying || ProjectParameterBindings.EditCheck ?
        paths.Where(path=>!ProjectParameterBindings.HasLiveValues(path) &&
            !ProjectTranslations.HasLiveValues(AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>(path)) &&
            !ProjectTranslations.HasLiveKeys(AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.SharedTableData>(path))).ToArray() : paths;
}
