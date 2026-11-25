using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Exercises the artist's file watcher, including restoration, in a disposable project.
public static class ArtistWorkflowCheck
{
    private const string Source="ARTIST/ENEMY/ArcherElite.aseprite";
    private const string Asset="Assets/Resources/Sprites/Enemy/ArcherElite.png";
    private const string Prefab="Assets/Prefabs/Enemy/Elite/Archer Elite.prefab";
    private static byte[] original,output;
    private static bool restoring;
    private static double next,deadline;
    private static Color32 baseline;
    private static GameObject actor;
    private static long spriteID;
    public static void Run()
    {
        if(!Application.isBatchMode || PlayerSettings.companyName!="NecromanderVerification")
            throw new InvalidOperationException("Run this check in the isolated verification project.");
        original=File.ReadAllBytes(Source);
        output=File.ReadAllBytes(Asset);
        baseline=PixelAtCenter(output);
        actor=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        var sprite=Body();
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string _,out spriteID);
        ArtistSourceSync.Start();
        var script=Path.GetFullPath(Path.Combine("Library","artist-roundtrip.lua"));
        File.WriteAllText(script,"local s=app.activeSprite; local c=s.cels[1]; c.image:drawPixel(s.width//2-c.position.x,s.height//2-c.position.y,app.pixelColor.rgba(237,19,193,255)); s:saveAs(app.params.output)");
        var exe=ArtistSourceSync.FindAseprite();
        try
        {
            var start=new ProcessStartInfo(exe,"-b \""+Path.GetFullPath(Source)+"\" --script-param \"output="+Path.GetFullPath(Source)+"\" --script \""+script+"\"")
                {UseShellExecute=false,CreateNoWindow=true};
            using var process=Process.Start(start);
            if(process==null || !process.WaitForExit(15000) || process.ExitCode!=0) throw new InvalidOperationException("Aseprite edit failed.");
            deadline=EditorApplication.timeSinceStartup+90;
            EditorApplication.update+=Tick;
        }
        catch { Restore(); throw; }
    }
    private static Sprite Body() => actor.GetComponentsInChildren<SpriteRenderer>(true)
        .Select(r=>r.sprite).First(s=>s!=null && AssetDatabase.GetAssetPath(s)==Asset);
    private static Color32 PixelAtCenter(byte[] bytes)
    {
        var image=new Texture2D(2,2);
        try
        {
            if(!image.LoadImage(bytes)) throw new InvalidDataException(Asset);
            return image.GetPixel(image.width/2,image.height-1-image.height/2);
        }
        finally {UnityEngine.Object.DestroyImmediate(image);}
    }
    private static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next) return;
        next=EditorApplication.timeSinceStartup+.15;
        try
        {
            if(next>deadline) throw new TimeoutException("Aseprite round trip: "+ArtistSourceSync.Status);
            Color32 pixel=PixelAtCenter(File.ReadAllBytes(Asset));
            var expected=restoring?baseline:new Color32(237,19,193,255);
            if(!pixel.Equals(expected)) return;
            var sprite=Body();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string _,out long currentID);
            if(spriteID!=currentID || sprite.rect.size!=new Vector2(68,60)) throw new InvalidDataException("Prefab sprite identity changed.");
            var rt=RenderTexture.GetTemporary(sprite.texture.width,sprite.texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active;
            var sample=new Texture2D(1,1,TextureFormat.RGBA32,false);
            try
            {
                Graphics.Blit(sprite.texture,rt);RenderTexture.active=rt;
                sample.ReadPixels(new Rect(rt.width/2,rt.height-1-rt.height/2,1,1),0,0);
                sample.Apply();
                Color32 gpu=sample.GetPixel(0,0);
                if(Mathf.Abs(gpu.r-expected.r)+Mathf.Abs(gpu.g-expected.g)+Mathf.Abs(gpu.b-expected.b)>5 || Mathf.Abs(gpu.a-expected.a)>2)
                    throw new InvalidDataException($"Prefab GPU pixel {gpu}, expected {expected}.");
            }
            finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(sample); }
            if(!restoring) {restoring=true;File.WriteAllBytes(Source,original);return;}
            Debug.Log("[ArtistWorkflowCheck] PASS artist save, PNG export, prefab GPU pixel and restoration with stable sprite ID");
            Restore();EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);Restore();EditorApplication.Exit(1);}
    }
    private static void Restore()
    {
        EditorApplication.update-=Tick;
        ArtistSourceSync.Stop();
        File.WriteAllBytes(Source,original);File.WriteAllBytes(Asset,output);
        AssetDatabase.ImportAsset(Asset,ImportAssetOptions.ForceSynchronousImport);
        if(actor!=null) UnityEngine.Object.DestroyImmediate(actor);
    }
}
