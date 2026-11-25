using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

internal static class LightingPanelCheck
{
    internal static IEnumerator Click(Mouse mouse, RectTransform rect, float horizontal)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            var uv = Rect.NormalizedToPoint(rect.rect,new Vector2(horizontal,.5f));
            Vector2 logical = PixelViewport.CanvasToGamePoint(canvas,rect.TransformPoint(uv));
            var display = PixelViewport.CalculateDisplayRect(Screen.width,Screen.height);
            var state = new MouseState { position = PixelViewport.GameToScreenPoint(logical,display) };
            InputSystem.QueueStateEvent(mouse,state); InputSystem.Update(); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse,state.WithButton(MouseButton.Left)); InputSystem.Update(); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse,state.WithButton(MouseButton.Left,false)); InputSystem.Update(); yield return null; yield return null;
        }

    internal static IEnumerator Run(Camera camera, Keyboard keyboard, Mouse mouse)
    {
        var panel = LightingDebugPanel.Instance;
        var settings = CameraSettingsSO.Instance;
        float original = settings.groundShadowAngle;
        void Key(bool down)
        {
            InputSystem.QueueStateEvent(keyboard, down ? new KeyboardState(UnityEngine.InputSystem.Key.F8) : new KeyboardState());
            InputSystem.Update();
        }
        Slider Get(string name) => Object.FindObjectsByType<Slider>(FindObjectsSortMode.None).Single(s => s.name == name);
        RectTransform Button(string name) => (RectTransform)Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Single(b => b.name == name && b.GetComponentInParent<Canvas>().name == "Lighting debug canvas").transform;
        try
        {
            Key(true); Key(false);
            yield return null; yield return null;
            if (UiResolutionCheck.Enabled)
            {
                var close = Button("닫기");
                var sizes = UiResolutionCheck.Verify("Lighting", (RectTransform)close.parent, close.GetComponent<Button>());
                while (sizes.MoveNext()) yield return sizes.Current;
            }
            Require(panel.IsOpen && LightingPreview.Active == null,"F8 opens without changing the scene");
            var click = Click(mouse, (RectTransform)Get("태양 방향").transform,.27f);
            while (click.MoveNext()) yield return click.Current;
            var state = LightingPreview.Active;
            Require(state != null && state.azimuth > 70 && state.azimuth < 125 && !GameManager.Instance.PLAYERCONTROLLER.GetComponent<MeleeCombatController>().IsAttacking,
                $"pointer changes the sun angle over the live panel ({state?.azimuth}, captured={LightingDebugPanel.CapturesPointer})");
            Get("태양 고도").value = 45;
            Get("그림자 강도").value = .8f;
            Get("빨강").value = 190; Get("초록").value = 145;
            Get("빛 강도").value = 1.25f;
            Get("하이라이트").value = .5f;
            yield return null; yield return null;
            var key = Shader.GetGlobalVector("_GroundKey");
            Vector2 expected = Quaternion.Euler(0,0,state.azimuth+180) * Vector3.right;
            Require(Vector2.Distance(expected,key) < .01f && Mathf.Abs(key.z-.8f) < .001f,"45 degree sun projects one unit per height");
            var atmosphere = WorldAtmosphere.Current.GetComponentsInChildren<MeshRenderer>().Single(r =>
                r.sharedMaterial.shader.name == "Necromancer/World Atmosphere").sharedMaterial;
            var global = Object.FindObjectsByType<UnityEngine.Rendering.Universal.Light2D>(FindObjectsSortMode.None)
                .First(light => light.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Global);
            Require(atmosphere.IsKeywordEnabled("SUN_HIGHLIGHT") && global.color.b > global.color.g,
                "directional highlights and global light color");
            PresentationRegressionCheck.SaveFrame(camera.targetTexture,"LightingPanel-Sun");
            var rim = CheckSunFacingEdges(camera, state);
            while (rim.MoveNext()) yield return rim.Current;
            click = Click(mouse, Button("분위기"),.5f);
            while (click.MoveNext()) yield return click.Current;
            Get("블룸 강도").value = 1.3f; Get("블룸 반경").value = 2; Get("블룸 밝기 기준").value = .25f;
            Get("지면 안개").value = .55f;
            yield return null; yield return null;
            var shape = atmosphere.GetVector("_BloomShape");
            Require(Mathf.Abs(shape.x-.25f)<.001f && Mathf.Abs(shape.y-2)<.001f && atmosphere.GetFloat("_Bloom")>1,
                "bloom threshold, radius and gain reach the renderer");
            var lowPlane = WorldAtmosphere.Current.transform.Find("Low mist and ground contact").GetComponent<MeshRenderer>();
            var farPlane = WorldAtmosphere.Current.transform.Find("Distant basin").GetComponent<MeshRenderer>();
            Get("공간 깊이").value = 0;
            yield return null; yield return null;
            Require(!lowPlane.enabled && !farPlane.enabled && Shader.GetGlobalFloat("_WorldDepthStrength") == 0,
                "F8 releases both world depth planes and picture parallax");
            Get("공간 깊이").value = 1;
            yield return null; yield return null;
            Require(lowPlane.enabled && farPlane.enabled && lowPlane.sharedMaterial.shader.isSupported &&
                lowPlane.sortingLayerName == "Ground" && farPlane.sortingLayerName == "BackGround" &&
                Shader.GetGlobalFloat("_WorldDepthStrength") > 0,"ground depth and backdrop restore beneath actors");
            Get("환경 입자량").value = 0;
            yield return null; yield return null;
            Require(atmosphere.GetVector("_AirLook").z == 0, "F8 mutes ambient motes");
            Get("환경 입자량").value = 1;
            yield return null; yield return null;
            Require(atmosphere.GetVector("_AirLook").z > 0, "F8 restores the room particle density");
            PresentationRegressionCheck.SaveFrame(camera.targetTexture,"LightingPanel-Atmosphere");
            click = Click(mouse, Button("외곽 안개"),.5f);
            while (click.MoveNext()) yield return click.Current;
            Get("외곽 폭").value = 32; Get("외곽 강도").value = .42f;
            yield return null; yield return null;
            Require(Mathf.Abs(Object.FindFirstObjectByType<FrameEdgeMist>().material.GetVector("_EdgeMist").y-32)<.01f,
                "edge mist responds to the live slider");
            Key(true); Key(false); yield return null;
            Require(!panel.IsOpen && LightingPreview.Active == state, "look remains visible after closing");
            Key(true); Key(false); yield return null;
            click = Click(mouse, Button("장면 기본값 복원"),.5f);
            while (click.MoveNext()) yield return click.Current;
            Require(LightingPreview.Active == null && settings.groundShadowAngle == original &&
                Mathf.Abs(atmosphere.GetVector("_Sun").w - LightingPreview.DefaultHighlight) < .001f,
                "restore releases overrides and restores the authored rim light");
            Debug.Log("[LightingPanel] PASS F8, pointer, sun, elevation, tint, highlight, bloom, mist, persistence and reset.");
        }
        finally { panel.SetOpen(false); Key(false); }
    }

    // Sample a neutral square in the actual world color pass. Reversing the sun must
    // light the opposite inside edge in both screen axes, including on D3D11.
    private static IEnumerator CheckSunFacingEdges(Camera camera, LightingPreview look)
    {
        const int size = 32;
        var art = new Texture2D(size,size,TextureFormat.RGB24,false) { filterMode = FilterMode.Point };
        for (int y=0; y<size; y++)
        for (int x=0; x<size; x++)
            art.SetPixel(x,y,x>=8 && x<24 && y>=8 && y<24 ? Color.gray : Color.black);
        art.Apply();
        var sprite = Sprite.Create(art,new Rect(0,0,size,size),Vector2.one*.5f,
            PixelViewport.Height/(2*camera.orthographicSize));
        var probe = new GameObject("Sun-facing rim probe",typeof(SpriteRenderer));
        probe.transform.SetParent(camera.transform,false);
        probe.transform.position = camera.ViewportToWorldPoint(new Vector3(.48f,.65f,5));
        var renderer = probe.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = "FlyingObject";
        renderer.sortingOrder = short.MaxValue;
        var image = new Texture2D(size,size,TextureFormat.RGB24,false);
        var previousAngle = look.azimuth;
        var previousTint = look.sun;
        float previousFog = look.mist, previousBloom = look.bloom, previousHighlight = look.highlight;
        float previousRadius = look.radius, previousThreshold = look.threshold;
        float previousSpeed = Time.timeScale;
        void ReadProbe()
        {
            var center = camera.WorldToViewportPoint(probe.transform.position);
            var frame = camera.targetTexture;
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(Mathf.RoundToInt(center.x*frame.width)-16,
                    Mathf.RoundToInt(center.y*frame.height)-16,size,size),0,0);
                image.Apply();
            }
            finally { RenderTexture.active = previous; }
        }
        try
        {
            look.mist = look.bloom = 0;
            look.sun = Color.white;
            look.highlight = 1;
            var directions = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            var contrast = new float[4];
            for (int i=0; i<directions.Length; i++)
            {
                look.azimuth = i*90;
                yield return null;
                yield return null;
                yield return null;
                ReadProbe();
                var toward = directions[i];
                var across = new Vector2(-toward.y,toward.x);
                for (int j=3; j<=12; j++)
                for (int k=-4; k<=4; k++)
                {
                    // Symmetric bands around the center between pixels 15 and 16.
                    Vector2 midpoint = Vector2.one*15.5f + across*k;
                    Vector2 pos = midpoint + toward*(j+.5f);
                    Vector2 neg = midpoint - toward*(j+.5f);
                    contrast[i] += image.GetPixel(Mathf.FloorToInt(pos.x),Mathf.FloorToInt(pos.y)).r
                        - image.GetPixel(Mathf.FloorToInt(neg.x),Mathf.FloorToInt(neg.y)).r;
                }
            }
            Debug.Log($"[SunRim] right/up/left/down contrast {string.Join(", ",contrast)}");
            Require(contrast[0]+contrast[2]>2 && contrast[1]+contrast[3]>2,
                "pixel highlights follow the sun on both axes");

            Time.timeScale = 0;
            look.highlight = 0;
            look.radius = 2;
            look.threshold = .12f;
            var spill = new float[2];
            for (int enabled = 0; enabled < 2; enabled++)
            {
                look.bloom = enabled;
                yield return null; yield return null; yield return null;
                ReadProbe();
                for (int y=12; y<20; y++)
                    spill[enabled] += (image.GetPixel(5,y).g + image.GetPixel(26,y).g)/16;
            }
            Debug.Log($"[PixelBloom] outside source, off={spill[0]:F3}, on={spill[1]:F3}");
            Require(spill[1] > spill[0]+.015f, "world highlights bloom into adjacent dark pixels");
        }
        finally
        {
            look.azimuth=previousAngle; look.sun=previousTint; look.mist=previousFog;
            look.bloom=previousBloom; look.highlight=previousHighlight;
            look.threshold=previousThreshold; look.radius=previousRadius; Time.timeScale=previousSpeed;
            Object.DestroyImmediate(probe); Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(image); Object.DestroyImmediate(art);
        }
    }

    private static void Require(bool pass,string message)
    { if (!pass) throw new InvalidOperationException("[LightingPanel] "+message); }
}
