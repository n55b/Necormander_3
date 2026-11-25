using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

internal static class GroundShadowRenderCheck
{
    internal static IEnumerator Verify(Camera camera)
    {
        var field = camera.GetComponent<GroundShadowField>();
        var npc = Object.FindObjectsByType<YSortableObject>(FindObjectsSortMode.None)
            .FirstOrDefault(n => n.name == "MinionSelectNPC");
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var hands = player.GetComponentsInChildren<AirborneShadow>().Where(part =>
            part.GetComponent<SpriteRenderer>().enabled && !part.GetComponent<SpriteRenderer>().forceRenderingOff).ToArray();
        if (field == null || npc == null || hands.Length != 2)
            throw new InvalidOperationException($"Missing village shadow sources, visible hands {hands.Length}.");
        var brain = camera.GetComponent<CinemachineBrain>();
        bool follow = brain.enabled;
        Vector3 oldPosition = camera.transform.position;
        float speed = Time.timeScale;
        var lightObject = new GameObject("Shadow test lantern");
        var lamp = lightObject.AddComponent<Light2D>();
        lamp.lightType = Light2D.LightType.Point;
        lamp.pointLightInnerAngle = lamp.pointLightOuterAngle = 360;
        lamp.pointLightOuterRadius = 4;
        lamp.intensity = 3;
        lightObject.AddComponent<GroundShadowLight>();
        try
        {
            brain.enabled = false;
            Time.timeScale = 0;
            var ground = npc.ShadowGroundPosition;
            camera.transform.position = new Vector3(ground.x, ground.y, oldPosition.z);
            var masks = new bool[2][];
            const int width = 96, height = 80;
            for (int side = 0; side < 2; ++side)
            {
                lightObject.transform.position = ground + new Vector3(side == 0 ? -1.25f : 1.25f, 1.1f);
                npc.CastsGroundShadow = false;
                yield return null; yield return null; yield return null;
                var plain = Capture(camera, ground, width, height);
                npc.CastsGroundShadow = true;
                yield return null; yield return null; yield return null;
                var cast = Capture(camera, ground, width, height, "NpcShadow" + side);
                masks[side] = Difference(plain, cast);
            }
            int changed = masks[0].Zip(masks[1], (a,b) => a != b).Count(v => v);
            if (masks.Any(mask => mask.Count(x => x) < 8) || changed < 12)
                throw new InvalidOperationException($"NPC did not cast different silhouettes under the two lights ({changed}).");

            var foot = player.GetComponentInChildren<YSortableObject>().ShadowGroundPosition;
            camera.transform.position = new Vector3(foot.x, foot.y, oldPosition.z);
            lightObject.transform.position = foot + new Vector3(-1.3f,1.2f);
            foreach (var hand in hands)
            {
                hand.enabled = false;
                yield return null; yield return null; yield return null;
                var plain = Capture(camera, foot, 128, 96);
                hand.enabled = true;
                yield return null; yield return null; yield return null;
                var cast = Capture(camera, foot, 128, 96, "PlayerHandShadows");
                int pixels = Difference(plain, cast).Count(v => v);
                if (pixels < 3) throw new InvalidOperationException(hand.name + " lost its floor shadow.");
                Debug.Log($"[GroundShadowCheck] {hand.name}: {pixels} logical pixels");
            }
            Debug.Log($"[GroundShadowCheck] NPC lamp moved across actor: {changed} changed silhouette pixels");
        }
        finally
        {
            npc.CastsGroundShadow = true;
            foreach (var hand in hands) if (hand != null) hand.enabled = true;
            Object.Destroy(lightObject);
            Time.timeScale = speed;
            camera.transform.position = oldPosition;
            brain.enabled = follow;
            if (follow) PresentationRegressionCheck.UpdateCamera(brain);
        }
    }

    internal static IEnumerator InspectScene(Camera camera, string scene)
    {
        var brain = camera.GetComponent<CinemachineBrain>();
        var origin = camera.transform.position;
        bool follow = brain.enabled;
        float speed = Time.timeScale;
        var atlasPixels = new Dictionary<Texture2D,Color32[]>();
        var lines = new List<string> { "object,sprite,instances,contactX,contactY,soleX,soleY,verticalGapPixels" };
        try
        {
            brain.enabled = false;
            Time.timeScale = 0;
            var groups = Object.FindObjectsByType<YSortableObject>(FindObjectsSortMode.None)
                .Where(a => a.CastsGroundShadow && a.Renderer != null && a.Renderer.enabled &&
                    !a.Renderer.forceRenderingOff && a.Renderer.sprite != null)
                .GroupBy(a => a.Renderer.sprite).OrderBy(g => g.Key.name);
            foreach (var group in groups)
            {
                var actor = group.First();
                var r = actor.Renderer;
                var sprite = group.Key;
                var atlas = sprite.texture;
                if (!atlasPixels.TryGetValue(atlas, out var pixels))
                {
                    pixels = SpriteGroundContactBaker.ReadAtlas(atlas);
                    atlasPixels.Add(atlas, pixels);
                }
                var rect = sprite.rect;
                Vector2 SupportRow(bool top)
                {
                    int height = (int)rect.height;
                    for (int row=0; row<height; row++)
                    {
                        int y = top ? height-1-row : row;
                        int left = -1, right = -1;
                        int offset = ((int)rect.y+y)*atlas.width + (int)rect.x;
                        for (int x=0; x<(int)rect.width; x++)
                        {
                            if (pixels[offset+x].a < 128) continue;
                            if (left < 0) left = x;
                            right = x;
                        }
                        if (left >= 0) return new Vector2((left+right+1)*.5f,top ? y+1 : y);
                    }
                    return new Vector2(0,-1);
                }
                var low = SupportRow(false);
                if (low.y < 0) continue;
                var high = SupportRow(true);
                Vector3 point = default;
                foreach (var instance in group)
                {
                    var art = instance.Renderer;
                    bool top = (art.localToWorldMatrix.m11 < 0) != art.flipY;
                    Vector2 support = top ? high : low;
                    Vector2 signs = new Vector2(art.flipX ? -1 : 1, art.flipY ? -1 : 1);
                    var local = Vector2.Scale((support-sprite.pivot)/sprite.pixelsPerUnit,signs);
                    var foot = art.localToWorldMatrix.MultiplyPoint3x4(local);
                    var contactWorld = instance.ShadowGroundPosition;
                    var contactLocal = (Vector2)art.localToWorldMatrix.inverse.MultiplyPoint3x4(contactWorld);
                    var texel = Vector2.Scale(contactLocal,signs)*sprite.pixelsPerUnit + sprite.pivot;
                    int x = Mathf.FloorToInt(texel.x);
                    int y = (int)support.y - (top ? 1 : 0);
                    bool touchesInk = x>=0 && x<(int)rect.width &&
                        pixels[((int)rect.y+y)*atlas.width + (int)rect.x+x].a >= 128;
                    if (!touchesInk || Mathf.Abs(foot.y-contactWorld.y) > 1.1f * PixelViewport.WorldUnitsPerPixel)
                        throw new InvalidOperationException($"Detached ground contact on {instance.name} / {sprite.name} at {texel}");
                    if (instance == actor) point = foot;
                }
                var contact = actor.ShadowGroundPosition;
                lines.Add(FormattableString.Invariant($"{actor.name},{sprite.name},{group.Count()},{contact.x:F3},{contact.y:F3},{point.x:F3},{point.y:F3},{(point.y-contact.y)/PixelViewport.WorldUnitsPerPixel:F2}"));
                camera.transform.position = new Vector3(contact.x, contact.y, origin.z);
                yield return null; yield return null;
                Capture(camera, contact, 152, 120, scene + "-Shadow-" + atlas.name + "-" + sprite.name.Replace('/', '_'));
            }
            File.WriteAllLines(Path.Combine(ProjectVerification.Output, scene + "-ShadowContacts.csv"), lines);
            var terrain = Object.FindFirstObjectByType<WallContactShadows>();
            if (terrain != null)
            {
                var floor = terrain.GetComponent<Tilemap>();
                var wall = floor.transform.parent.Find("Wall").GetComponent<Tilemap>();
                var occupied = new List<Vector3Int>();
                foreach (var c in wall.cellBounds.allPositionsWithin) if (wall.HasTile(c)) occupied.Add(c);
                var profile = Resources.Load<TerrainReliefProfile>("WorldAtmosphere/TerrainRelief");
                var straight = occupied.Where(c => wall.GetTile(c) == profile.cliffFoot &&
                    wall.GetTile(c + Vector3Int.left) == profile.cliffFoot &&
                    wall.GetTile(c + Vector3Int.right) == profile.cliffFoot).ToArray();
                var best = (straight.Length > 0 ? straight.AsEnumerable() : occupied)
                    .Where(c => wall.HasTile(c) && !wall.HasTile(c + Vector3Int.down) &&
                        floor.HasTile(floor.WorldToCell(wall.GetCellCenterWorld(c + Vector3Int.down))))
                    .OrderBy(c => (wall.GetCellCenterWorld(c) - (origin + Vector3.right * 6)).sqrMagnitude).First();
                var center = wall.GetCellCenterWorld(best);
                camera.transform.position = new Vector3(center.x, center.y, origin.z);
                yield return null; yield return null;
                var surface = terrain.transform.Find("Wall contact shadows").gameObject;
                bool visible = surface.activeSelf;
                var settings = CameraSettingsSO.Instance;
                float angle = settings.groundShadowAngle;
                int renders = 0;
                void Drawn(UnityEngine.Rendering.ScriptableRenderContext context, Camera drawn)
                { if (drawn == camera) renders++; }
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += Drawn;
                try
                {
                    surface.SetActive(false);
                    renders = 0;
                    while (renders < 3) yield return null;
                    var plain = Capture(camera, center, 160, 96, scene + "-TerrainPlain");
                    surface.SetActive(true);
                    renders = 0;
                    while (renders < 3) yield return null;
                    var lit = Capture(camera, center, 160, 96, scene + "-TerrainCast");
                    settings.groundShadowAngle = angle - 180;
                    renders = 0;
                    while (renders < 3) yield return null;
                    var reverse = Capture(camera, center, 160, 96, scene + "-TerrainReverse");
                    Debug.Log($"[TerrainReceiver] {surface.name} chunks={surface.transform.childCount} count={terrain.ReceiverCount} angle={angle} strength={settings.groundShadowStrength} key={Shader.GetGlobalVector("_GroundKey")}");
                    var cast = Difference(plain, lit);
                    int painted = cast.Count(value => value);
                    int changed = cast.Zip(Difference(plain,reverse), (a,b) => a != b).Count(value => value);
                    Debug.Log($"[TerrainRelief] {scene} cell {best}, {painted} shadow pixels, {changed} changed with light direction");
                    if (painted < 12 || changed < 12) throw new InvalidOperationException("Terrain relief did not respond to lighting in " + scene);
                }
                finally { UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= Drawn; surface.SetActive(visible); settings.groundShadowAngle = angle; }
            }
        }
        finally
        {
            Time.timeScale = speed;
            camera.transform.position = origin;
            brain.enabled = follow;
            if (follow) PresentationRegressionCheck.UpdateCamera(brain);
        }
    }

    private static bool[] Difference(Color32[] a, Color32[] b)
    {
        var mask = new bool[a.Length];
        for (int i = 0; i < a.Length; ++i)
            mask[i] = a[i].r - b[i].r + a[i].g - b[i].g + a[i].b - b[i].b > 16;
        return mask;
    }

    private static Color32[] Capture(Camera camera, Vector3 ground, int width, int height, string name = null)
    {
        var at = camera.WorldToScreenPoint(ground);
        var old = RenderTexture.active;
        var texture = new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active = camera.targetTexture;
            texture.ReadPixels(new Rect(Mathf.Round(at.x) - width/2, Mathf.Round(at.y) - height/3,width,height),0,0);
            texture.Apply();
            if (name != null) File.WriteAllBytes(Path.Combine(ProjectVerification.Output, name + ".png"),texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        finally
        {
            RenderTexture.active = old;
            Object.DestroyImmediate(texture);
        }
    }
}
