using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Each row is one authored sprite clip against two backgrounds at native scale.
internal static class PixelMotionBoards
{
    internal static void Write(string output)
    {
        var folder = Path.Combine(output, "Motion");
        Directory.CreateDirectory(folder);
        var readable = new Dictionary<Texture2D, Texture2D>();
        try
        {
            const string melee = "Assets/Animations/Character/Monster/Doll/Melee";
            var clips = AssetDatabase.FindAssets("t:AnimationClip",new[]{melee}).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).ToList();
            var bossPath = "Assets/Resources/Sprites/Enemy/Enemy_11_BoneMaster.aseprite";
            clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(bossPath).OfType<AnimationClip>());
            var notes = new List<string>();
            var outlines = new List<string>{"sprite,localID,opaqueEdgeSamples,singleDarkTexel,twoOrMoreDarkTexels"};
            var inspected=new HashSet<Sprite>();
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.propertyName != "m_Sprite") continue;
                    var frames = AnimationUtility.GetObjectReferenceCurve(clip,binding);
                    var sprites = frames.Select(k=>k.value as Sprite).Where(s=>s!=null).ToArray();
                    if (sprites.Length == 0) continue;
                    // Common pivot keeps the authored foot motion in the comparison.
                    int left=Mathf.CeilToInt(sprites.Max(s=>s.pivot.x)), bottom=Mathf.CeilToInt(sprites.Max(s=>s.pivot.y));
                    int width=left+Mathf.CeilToInt(sprites.Max(s=>s.rect.width-s.pivot.x))+2;
                    int height=bottom+Mathf.CeilToInt(sprites.Max(s=>s.rect.height-s.pivot.y))+2;
                    var sheet = new Texture2D(width*sprites.Length, height*2,TextureFormat.RGB24,false);
                    try
                    {
                        for (int n=0;n<sprites.Length;n++)
                        {
                            var sprite=sprites[n];
                            if (!readable.TryGetValue(sprite.texture,out var atlas))
                            {
                                var previous=RenderTexture.active;
                                var rt=RenderTexture.GetTemporary(sprite.texture.width,sprite.texture.height,0,RenderTextureFormat.ARGB32);
                                try
                                {
                                    Graphics.Blit(sprite.texture,rt);
                                    RenderTexture.active=rt;
                                    atlas=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
                                    atlas.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); atlas.Apply();
                                    readable.Add(sprite.texture,atlas);
                                }
                                finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt); }
                            }
                            var region=sprite.rect;
                            if(inspected.Add(sprite)) MeasureEdges(sprite,atlas,outlines);
                            for (int row=0;row<2;row++)
                            {
                                Color bg=row==0 ? new Color32(42,33,52,255) : new Color32(155,176,111,255);
                                for (int y=0;y<height;y++)
                                for (int x=0;x<width;x++)
                                {
                                    int u=x-1-left+Mathf.RoundToInt(sprite.pivot.x);
                                    int v=y-1-bottom+Mathf.RoundToInt(sprite.pivot.y);
                                    Color c=u>=0 && v>=0 && u<region.width && v<region.height ?
                                        atlas.GetPixel((int)region.x+u,(int)region.y+v) : Color.clear;
                                    sheet.SetPixel(n*width+x,row*height+y,Color.Lerp(bg,c,c.a));
                                }
                            }
                        }
                        sheet.Apply();
                        string name=clip.name+"-"+binding.path.Replace('/','_');
                        File.WriteAllBytes(Path.Combine(folder,name+".png"),sheet.EncodeToPNG());
                        notes.Add(name+" | "+string.Join(", ",frames.Select(k=>k.time.ToString("0.###")+"s")));
                    }
                    finally { Object.DestroyImmediate(sheet); }
                }
            }
            File.WriteAllLines(Path.Combine(folder,"frames.txt"),notes);
            File.WriteAllLines(Path.Combine(folder,"outline-samples.csv"),outlines);
        }
        finally { foreach(var texture in readable.Values) Object.DestroyImmediate(texture); }
    }
    private static void MeasureEdges(Sprite sprite,Texture2D atlas,List<string> output)
    {
        var rect=sprite.rect;
        int w=(int)rect.width,h=(int)rect.height;
        Color[] region=atlas.GetPixels((int)rect.x,(int)rect.y,w,h);
        var pixels=System.Array.ConvertAll(region,c=>(Color32)c);
        int total=0,single=0,thick=0;
        void Edge(int first,int step,int length)
        {
            int n=0;
            while(n<length && pixels[first+n*step].a<128) n++;
            if(n==length) return;
            int run=PixelDensityAudit.BorderRun(pixels,length-n,first+n*step,step);
            total++;
            if(run==1) single++;
            if(run>=2) thick++;
        }
        for(int y=0;y<h;y++) { Edge(y*w,1,w);Edge((y+1)*w-1,-1,w); }
        for(int x=0;x<w;x++) { Edge(x,w,h);Edge((h-1)*w+x,-w,h); }
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string _,out long id);
        output.Add($"{sprite.name},{id},{total},{single},{thick}");
    }
}
