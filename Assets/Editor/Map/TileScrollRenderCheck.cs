using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// A saturated clear color exposes a crack even when the backdrop normally matches the grass.
internal static class TileScrollRenderCheck
{
    internal static IEnumerator Verify(Camera camera, string scene)
    {
        var brain = camera.GetComponent<CinemachineBrain>();
        bool follow = brain != null && brain.enabled;
        var origin = camera.transform.position;
        var clear = camera.clearFlags;
        var background = camera.backgroundColor;
        float speed = Time.timeScale;
        var suppressed = new List<Renderer>();
        var canvases = new List<Canvas>();
        var texture = new Texture2D(PixelViewport.Width, PixelViewport.Height,TextureFormat.RGB24,false);
        int cracks = 0, streak = 0, peak = 0;
        try
        {
            if (brain != null) brain.enabled = false;
            Time.timeScale = 0;
            float unit = camera.orthographicSize * 2 / PixelViewport.Height;
            var anchor = new Vector3(Mathf.Round(origin.x/unit)*unit, Mathf.Round(origin.y/unit)*unit,origin.z);
            float[] offsets = { 0,.22f,.48f,.499f,.5f,.501f,.52f,.76f,.99f };
            foreach (bool tilesOnly in new[] {false,true})
            {
                if (tilesOnly)
                {
                    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                        if (!(r is TilemapRenderer) && !r.forceRenderingOff) { r.forceRenderingOff = true; suppressed.Add(r); }
                    foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                        if (c.enabled && c.worldCamera == camera) { c.enabled = false; canvases.Add(c); }
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.magenta;
                }
                Color32[] basis = null;
                foreach (var dir in new[] {Vector2.up,Vector2.one})
                foreach (float offset in offsets)
                {
                    camera.transform.position = anchor + (Vector3)dir * (offset * unit);
                    yield return null; yield return null;
                    var prior = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = camera.targetTexture;
                        texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);
                        texture.Apply();
                    }
                    finally { RenderTexture.active = prior; }
                    var pixels = texture.GetPixels32();
                    if (basis == null) { basis = pixels; continue; }
                    int width = texture.width, longest = 0, holes = 0;
                    for (int y=4; y<texture.height-4; y++)
                    {
                        int run=0;
                        for (int x=4; x<width-4; x++)
                        {
                            int i=x+y*width;
                            bool hole = tilesOnly && Magenta(pixels[i]);
                            bool yellow = Yellow(pixels[i]);
                            if (!yellow && !hole) { run=0; continue; }
                            bool inside = true;
                            for (int row=-1;row<=1 && inside;row++)
                            for (int col=-1;col<=1 && inside;col++)
                            {
                                var before=basis[i+row*width+col];
                                inside = hole ? !Magenta(before) : !Yellow(before);
                            }
                            if (inside && hole) holes++;
                            run=inside && yellow ? run+1 : 0;
                            longest=Math.Max(longest,run);
                        }
                    }
                    int score = Math.Max(holes,longest);
                    if (score>peak)
                    {
                        peak=score;
                        File.WriteAllBytes(Path.Combine(ProjectVerification.Output,scene+"ScrollWorst.png"),texture.EncodeToPNG());
                    }
                    cracks=Math.Max(cracks,holes); streak=Math.Max(streak,longest);
                }
            }
        }
        finally
        {
            foreach (var r in suppressed) if(r!=null) r.forceRenderingOff=false;
            foreach (var c in canvases) if(c!=null) c.enabled=true;
            camera.clearFlags=clear; camera.backgroundColor=background; camera.transform.position=origin;
            Time.timeScale=speed;
            if (brain!=null) {brain.enabled=follow; if(follow) PresentationRegressionCheck.UpdateCamera(brain);}
            Object.Destroy(texture);
        }
        Debug.Log($"[TileScroll] {scene}: camera fractions 0..0.99, vertical and diagonal; internal holes={cracks}, new horizontal yellow run={streak}");
        if (cracks>0 || streak>=24) throw new InvalidOperationException($"{scene} tile scroll: {cracks} holes, {streak}px yellow line");
    }
    private static bool Yellow(Color32 c) => c.r>=145 && c.g>=140 && c.b<110 && Math.Abs(c.r-c.g)<80;
    private static bool Magenta(Color32 c) => c.r>=245 && c.b>=245 && c.g<=8;
}
