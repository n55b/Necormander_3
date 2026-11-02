using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 폰트 일괄 교체 도구. (메뉴: Tools > Font Setup)
///  1) Report  : 현재 폰트 에셋 설정/폴백/사용처/머티리얼 프리셋을 기록한다.
///  2) Create  : ThinMulmaru(.ttf)로 TMP 폰트 에셋(Dynamic SDF)을 만든다.
///  3) Replace : 씬·프리팹·ScriptableObject 의 모든 폰트/머티리얼 참조와 TMP Settings 를 새 폰트로 바꾼다.
/// 결과는 콘솔과 Logs/FontSetup.log 에 남는다.
/// </summary>
public static class FontSetupTool
{
    const string FontDir       = "Assets/TextMesh Pro/Fonts";
    const string NewTtf        = FontDir + "/ThinMulmaru.ttf";
    const string NewMonoTtf    = FontDir + "/ThinMulmaru Mono.ttf";
    const string NewAsset      = FontDir + "/ThinMulmaru SDF.asset";
    const string NewMonoAsset  = FontDir + "/ThinMulmaru Mono SDF.asset";

    // 교체 대상(이전 폰트). 전부 ThinMulmaru SDF 하나로 모은다 — 굵기가 섞이지 않도록 한 벌만 쓴다.
    static readonly string[] OldFontPaths =
    {
        FontDir + "/Galmuri11 SDF.asset",
        FontDir + "/Galmuri11-Bold SDF.asset",
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset",
    };

    static readonly string[] SceneFolders      = { "Assets/Scenes" };
    static readonly string[] SkipFolders       = { "Assets/TextMesh Pro", "Assets/Plugins", "Assets/Samples" };

    const string LogFile   = "Logs/FontSetup.log";
    // 로그: <프로젝트>/Logs/FontSetup.log
 

    static void Log(string msg)
    {
        Debug.Log("[FontSetup] " + msg);
        var line = System.DateTime.Now.ToString("HH:mm:ss ") + msg;
        try { File.AppendAllText(LogFile, line + "\n"); } catch { }
         
         
    }

    static IEnumerable<TMP_FontAsset> OldFonts() =>
        OldFontPaths.Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>).Where(f => f != null);

    // ─── 1. Report ────────────────────────────────────────────────────
    [MenuItem("Tools/Font Setup/1. Report", priority = 1)]
    public static void Report()
    {
        var sb = new StringBuilder();

        sb.AppendLine("== Font assets ==");
        foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
            if (f == null) continue;
            var fb = f.fallbackFontAssetTable != null ? string.Join(", ", f.fallbackFontAssetTable.Where(x => x != null).Select(x => x.name)) : "";
            sb.AppendLine($"  {p}\n    mode={f.atlasPopulationMode} render={f.atlasRenderMode} point={f.faceInfo.pointSize} pad={f.atlasPadding} atlas={f.atlasWidth}x{f.atlasHeight} x{f.atlasTextures?.Length} multi={f.isMultiAtlasTexturesEnabled} chars={f.characterTable?.Count} src={(f.sourceFontFile ? f.sourceFontFile.name : "-")} weight={f.faceInfo.styleName}\n    fallback=[{fb}] boldSpacing={f.boldSpacing} boldStyle(normal/bold)={f.normalStyle}/{f.boldStyle}");
        }

        var settings = TMP_Settings.instance;
        sb.AppendLine("== TMP Settings ==");
        sb.AppendLine($"  default={(TMP_Settings.defaultFontAsset ? TMP_Settings.defaultFontAsset.name : "-")} fallbacks=[{string.Join(", ", (TMP_Settings.fallbackFontAssets ?? new List<TMP_FontAsset>()).Where(x => x != null).Select(x => x.name))}]");

        sb.AppendLine("== Materials using old atlases ==");
        var oldAtlases = new HashSet<Texture>(OldFonts().SelectMany(f => f.atlasTextures ?? new Texture2D[0]).Where(t => t != null));
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
                if (m.HasProperty("_MainTex") && oldAtlases.Contains(m.GetTexture("_MainTex")))
                    sb.AppendLine($"  {p} :: {m.name} ({m.shader.name})");
        }

        // 사용처 (씬·프리팹)
        var fontUse = new Dictionary<string, int>();
        var matUse  = new Dictionary<string, int>();
        int boldStyle = 0, bTags = 0, total = 0;
        var boldSamples = new List<string>();
        void Count(GameObject root, string ctx)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                total++;
                var fn = t.font ? t.font.name : "(none)";
                fontUse[fn] = fontUse.TryGetValue(fn, out var c) ? c + 1 : 1;
                var mn = t.fontSharedMaterial ? t.fontSharedMaterial.name : "(none)";
                matUse[mn] = matUse.TryGetValue(mn, out var c2) ? c2 + 1 : 1;
                if ((t.fontStyle & FontStyles.Bold) != 0) { boldStyle++; if (boldSamples.Count < 40) boldSamples.Add($"{ctx} :: {GetPath(t.transform)} font={fn}"); }
                if (t.text != null && t.text.Contains("<b>")) bTags++;
            }
        }
        ForEachPrefab((root, path) => { Count(root, path); return false; });
        ForEachScene((root, path) => { Count(root, path); return false; });

        sb.AppendLine($"== Usage (TMP_Text {total}) ==");
        foreach (var kv in fontUse.OrderByDescending(k => k.Value)) sb.AppendLine($"  font {kv.Key}: {kv.Value}");
        foreach (var kv in matUse.OrderByDescending(k => k.Value)) sb.AppendLine($"  mat  {kv.Key}: {kv.Value}");
        sb.AppendLine($"  fontStyle Bold: {boldStyle}, text with <b>: {bTags}");
        foreach (var s in boldSamples) sb.AppendLine("    bold " + s);

        sb.AppendLine("== Scripts mentioning fonts ==");
        foreach (var file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            var f = file.Replace('\\', '/');
            if (f.Contains("/Editor/")) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains("<b>") || lines[i].Contains("TMP_FontAsset") || lines[i].Contains("Galmuri") || lines[i].Contains("FontStyles.Bold") || lines[i].Contains(".font ="))
                    sb.AppendLine($"  {f}:{i + 1} {lines[i].Trim()}");
        }

        Log("Report\n" + sb);
    }

    // ─── 2. Create ────────────────────────────────────────────────────
    [MenuItem("Tools/Font Setup/2. Create ThinMulmaru Font Assets", priority = 2)]
    public static void CreateFontAssets()
    {
        var reference = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OldFontPaths[0]);
        CreateOne(NewTtf, NewAsset, reference);
        CreateOne(NewMonoTtf, NewMonoAsset, reference);
        AssetDatabase.SaveAssets();
    }

    static TMP_FontAsset CreateOne(string ttfPath, string assetPath, TMP_FontAsset reference)
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) { Log($"이미 있음: {assetPath}"); return existing; }

        var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null) { Log($"폰트 파일 없음: {ttfPath}"); return null; }

        // 한 벌의 SDF 로 통일한다. (예전 Galmuri 는 Regular=비트맵(RASTER), Bold=SDF 로 렌더 방식부터 달라서 굵기가 섞여 보였다.)
        // SDF 는 화면 해상도·비정수 배율·외곽선(TextFloating 등)에 모두 대응한다.
        const int point = 90, padding = 9;
        const GlyphRenderMode render = GlyphRenderMode.SDFAA;

        var fa = TMP_FontAsset.CreateFontAsset(font, point, padding, render, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        if (fa == null) { Log($"생성 실패: {ttfPath}"); return null; }

        var baseName = Path.GetFileNameWithoutExtension(assetPath);
        fa.name = baseName;
        AssetDatabase.CreateAsset(fa, assetPath);
        fa.atlasTextures[0].name = baseName + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = baseName + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        if (reference != null && reference.atlasTextures != null && reference.atlasTextures.Length > 0 && reference.atlasTextures[0] != null)
            { } // 필터 모드는 SDF 기본(Bilinear) 유지
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        Log($"생성: {assetPath} (point={point}, pad={padding}, render={render}, Dynamic, multi-atlas)");
        return fa;
    }

    // ─── 3. Replace ───────────────────────────────────────────────────
    [MenuItem("Tools/Font Setup/3. Replace Fonts Everywhere", priority = 3)]
    public static void ReplaceAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("플레이 모드에서는 실행할 수 없습니다."); return; }
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) { Log($"저장되지 않은 씬({EditorSceneManager.GetSceneAt(i).name})이 있어 중단합니다."); return; }

        var newFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NewAsset);
        if (newFont == null) { CreateFontAssets(); newFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NewAsset); }
        if (newFont == null) { Log("새 폰트 에셋이 없어 중단합니다."); return; }

        // 참조 치환 맵: 폰트, 폰트 기본 머티리얼, 아틀라스, 머티리얼 프리셋
        var map = new Dictionary<Object, Object>();
        foreach (var old in OldFonts())
        {
            map[old] = newFont;
            if (old.material != null) map[old.material] = newFont.material;
            if (old.atlasTextures != null)
                foreach (var t in old.atlasTextures) if (t != null) map[t] = newFont.atlasTexture;
        }
        foreach (var preset in MigratePresets(newFont)) map[preset.Key] = preset.Value;

        var sb = new StringBuilder();
        int changed = 0;

        // TMP Settings
        changed += ReplaceRefs(TMP_Settings.instance, map, sb, "TMP Settings");
        if (TMP_Settings.defaultFontAsset != newFont)
        {
            var so = new SerializedObject(TMP_Settings.instance);
            so.FindProperty("m_defaultFontAsset").objectReferenceValue = newFont;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed++;
        }
        // 폴백 목록에서 같은 폰트 중복 제거
        {
            var so = new SerializedObject(TMP_Settings.instance);
            var list = so.FindProperty("m_fallbackFontAssets");
            var seen = new HashSet<Object>();
            var keep = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var o = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null && o != newFont && seen.Add(o)) keep.Add(o);
            }
            list.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(TMP_Settings.instance);
        }

        // ScriptableObject 에셋
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (SkipFolders.Any(f => p.StartsWith(f))) continue;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                if (o == null || o is TMP_FontAsset || o is Material || o is Texture) continue;
                int n = ReplaceRefs(o, map, sb, p);
                if (n > 0) { EditorUtility.SetDirty(o); changed += n; }
            }
        }

        // 프리팹 → 씬
        ForEachPrefab((root, path) =>
        {
            int n = root.GetComponentsInChildren<Component>(true).Where(c => c != null).Sum(c => ReplaceRefs(c, map, sb, path));
            n += ClearBold(root, sb, path);
            changed += n;
            return n > 0;
        });
        ForEachScene((root, path) =>
        {
            int n = root.GetComponentsInChildren<Component>(true).Where(c => c != null).Sum(c => ReplaceRefs(c, map, sb, path));
            n += ClearBold(root, sb, path);
            changed += n;
            return n > 0;
        });

        AssetDatabase.SaveAssets();
        Log($"교체 완료: 참조 {changed}개\n{sb}");
    }

    /// <summary>이전 아틀라스를 쓰던 머티리얼 프리셋(외곽선 등)을 새 폰트용으로 복제한다. 기본 머티리얼은 제외.</summary>
    static Dictionary<Material, Material> MigratePresets(TMP_FontAsset newFont)
    {
        var result = new Dictionary<Material, Material>();
        var olds = OldFonts().ToList();
        var oldAtlases = new HashSet<Texture>(olds.SelectMany(f => f.atlasTextures ?? new Texture2D[0]).Where(t => t != null));
        var oldDefaults = new HashSet<Material>(olds.Select(f => f.material).Where(m => m != null));

        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.StartsWith("Assets/TextMesh Pro/")) continue; // TMP 샘플 프리셋(LiberationSans Outline 등)은 아무도 안 쓴다
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null || oldDefaults.Contains(m) || !m.HasProperty("_MainTex")) continue;
            if (!oldAtlases.Contains(m.GetTexture("_MainTex"))) continue;

            var suffix = m.name;
            foreach (var f in olds) suffix = suffix.Replace(f.name, "").Trim(' ', '-');
            var newName = $"{newFont.name} - {(string.IsNullOrEmpty(suffix) ? "Preset" : suffix)}";
            var newPath = $"{FontDir}/{newName}.mat";
            var nm = AssetDatabase.LoadAssetAtPath<Material>(newPath);
            if (nm == null)
            {
                nm = new Material(m) { name = newName };
                AssetDatabase.CreateAsset(nm, newPath);
            }
            // 새 아틀라스와 그에 맞는 SDF 파라미터로 맞춘다. 외곽선·그림자 등 나머지 값은 원본 그대로.
            nm.SetTexture("_MainTex", newFont.atlasTexture);
            CopyAtlasParams(newFont.material, nm);
            EditorUtility.SetDirty(nm);
            result[m] = nm;
            Log($"프리셋 이전: {p} → {newPath}");
        }
        return result;
    }

    static void CopyAtlasParams(Material from, Material to)
    {
        foreach (var prop in new[] { "_TextureWidth", "_TextureHeight", "_GradientScale", "_WeightNormal", "_WeightBold" })
            if (from.HasProperty(prop) && to.HasProperty(prop)) to.SetFloat(prop, from.GetFloat(prop));
    }

    static int ReplaceRefs(Object target, Dictionary<Object, Object> map, StringBuilder sb, string ctx)
    {
        if (target == null) return 0;
        var so = new SerializedObject(target);
        var it = so.GetIterator();
        int n = 0;
        while (it.Next(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
            var v = it.objectReferenceValue;
            if (v == null || !map.TryGetValue(v, out var nv)) continue;
            it.objectReferenceValue = nv;
            n++;
            if (sb.Length < 200000)
                sb.AppendLine($"  {ctx} :: {(target is Component c ? GetPath(c.transform) + " (" + c.GetType().Name + ")" : target.name)} .{it.propertyPath}: {v.name} -> {nv.name}");
        }
        if (n > 0) { so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target); }
        return n;
    }

    // ─── helpers ─────────────────────────────────────────────────────
    /// <summary>visit 이 true 를 돌려주면 저장한다.</summary>
    static void ForEachPrefab(System.Func<GameObject, string, bool> visit)
    {
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !SkipFolders.Any(f => p.StartsWith(f)))
            .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p)) == PrefabAssetType.Variant ? 1 : 0)
            .ThenBy(p => p).ToList();
        foreach (var path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var type = PrefabUtility.GetPrefabAssetType(asset);
            if (type == PrefabAssetType.Model || type == PrefabAssetType.MissingAsset) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { if (visit(root, path)) PrefabUtility.SaveAsPrefabAsset(root, path); }
            catch (System.Exception e) { Log($"ERROR {path}: {e.Message}"); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    static void ForEachScene(System.Func<GameObject, string, bool> visit)
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Scene", SceneFolders).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool dirty = false;
                foreach (var r in scene.GetRootGameObjects()) dirty |= visit(r, path);
                if (dirty) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
        }
        finally
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    static string GetPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }


    /// <summary>
    /// 텍스트의 Bold 스타일을 끈다. ThinMulmaru 는 굵기가 한 벌뿐이라, Bold 를 켜면 TMP 가 가짜로 두껍게(SDF 팽창) 그려
    /// 옆 텍스트와 굵기가 또 섞인다. 강조가 필요하면 크기·색으로 준다.
    /// </summary>
    static int ClearBold(GameObject root, StringBuilder sb, string ctx)
    {
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if ((t.fontStyle & FontStyles.Bold) == 0) continue;
            t.fontStyle &= ~FontStyles.Bold;
            EditorUtility.SetDirty(t);
            n++;
            if (sb.Length < 200000) sb.AppendLine($"  {ctx} :: {GetPath(t.transform)} fontStyle Bold 해제");
        }
        return n;
    }


    /// <summary>
    /// ThinMulmaru 는 굵기가 한 벌뿐이다. TMP 의 가짜 볼드(Bold 스타일 / &lt;b&gt; 태그)가 남아 있어도
    /// 같은 굵기로 보이도록 폰트 에셋의 볼드 팽창·자간을 0 으로 만든다. (데이터 속 &lt;b&gt; 까지 한 번에 막는다)
    /// </summary>
    [MenuItem("Tools/Font Setup/4. Neutralize Fake Bold", priority = 4)]
    public static void NeutralizeBold()
    {
        foreach (var path in new[] { NewAsset, NewMonoAsset })
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null) continue;
            fa.boldStyle = 0f;
            fa.boldSpacing = 0f;
            if (fa.material != null && fa.material.HasProperty("_WeightBold"))
                fa.material.SetFloat("_WeightBold", fa.material.HasProperty("_WeightNormal") ? fa.material.GetFloat("_WeightNormal") : 0f);
            EditorUtility.SetDirty(fa);
            if (fa.material != null) EditorUtility.SetDirty(fa.material);
            Log($"가짜 볼드 무력화: {path}");
        }
        AssetDatabase.SaveAssets();
    }


    // ─── 5. 가독성 ────────────────────────────────────────────────────
    const float FaceDilate      = 0.25f;  // ThinMulmaru 획 두께 보정 (전체 공통)
    const string MenuPresetPath = FontDir + "/ThinMulmaru SDF - Menu.mat";
    static readonly Color DarkText = new Color32(0x1E, 0x1E, 0x1E, 0xFF);
    static readonly string[] MenuScenes = { "Assets/Scenes/StartScene.unity", "Assets/Scenes/OptionScene.unity" };

    /// <summary>
    /// 1) ThinMulmaru 머티리얼의 Face Dilate 를 올려 모든 텍스트의 획을 같은 비율로 두껍게 한다(굵기 섞임 없음).
    /// 2) 타이틀·옵션 씬 텍스트 대비 보정:
    ///    · 밝은 배경(버튼 등) 위 → 진한 글자색 + 기본 머티리얼
    ///    · 그 외(어두운/투명 배경) → 외곽선 + 그림자 프리셋(ThinMulmaru SDF - Menu)
    /// </summary>
    /// <summary>
    /// 타이틀·옵션 씬 텍스트만 읽기 좋게 만든다. 나머지 게임 텍스트는 ThinMulmaru 원래 두께 그대로다.
    ///  · 폰트 기본 머티리얼: Face Dilate 0 (얇은 원래 두께 — 인게임 전체)
    ///  · 밝은 배경(버튼 등) 위 → 진한 글자색 + 'Menu Plain' 프리셋(두께만 보정)
    ///  · 그 외(어두운/투명 배경) → 'Menu' 프리셋(두께 + 외곽선 + 그림자)
    /// </summary>
    [MenuItem("Tools/Font Setup/5. Improve Readability (Menu Scenes Only)", priority = 5)]
    public static void ImproveReadability()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("플레이 모드에서는 실행할 수 없습니다."); return; }
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) { Log($"저장되지 않은 씬({EditorSceneManager.GetSceneAt(i).name})이 있어 중단합니다."); return; }

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NewAsset);
        if (font == null || font.material == null) { Log("ThinMulmaru SDF 가 없습니다."); return; }

        // 0) 폰트 기본 머티리얼은 원래 두께로 (인게임 텍스트는 얇게 유지)
        foreach (var path in new[] { NewAsset, NewMonoAsset })
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null || fa.material == null) continue;
            fa.material.SetFloat("_FaceDilate", 0f);
            EditorUtility.SetDirty(fa.material);
        }

        // 1) 메뉴 전용 프리셋 두 개
        var plain = GetOrCreatePreset(font, MenuPlainPresetPath, "ThinMulmaru SDF - Menu Plain");
        plain.SetFloat("_FaceDilate", FaceDilate);
        EditorUtility.SetDirty(plain);

        var preset = GetOrCreatePreset(font, MenuPresetPath, "ThinMulmaru SDF - Menu");
        preset.SetFloat("_FaceDilate", FaceDilate);
        preset.EnableKeyword("OUTLINE_ON");
        preset.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 1f));
        preset.SetFloat("_OutlineWidth", 0.12f);
        preset.EnableKeyword("UNDERLAY_ON");
        preset.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.6f));
        preset.SetFloat("_UnderlayOffsetX", 0.6f);
        preset.SetFloat("_UnderlayOffsetY", -0.6f);
        preset.SetFloat("_UnderlayDilate", 0.1f);
        preset.SetFloat("_UnderlaySoftness", 0.15f);
        EditorUtility.SetDirty(preset);
        AssetDatabase.SaveAssets();

        // 2) 타이틀·옵션 씬 텍스트에만 적용
        var sb = new StringBuilder();
        int dark = 0, outlined = 0;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in MenuScenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool dirty = false;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        var bg = BackgroundColor(t.transform);
                        bool lightBg = bg.HasValue && bg.Value.a > 0.5f && Luminance(bg.Value) > 0.6f;
                        if (lightBg)
                        {
                            t.color = DarkText;
                            t.fontSharedMaterial = plain;
                            dark++;
                            sb.AppendLine($"  {path} :: {GetPath(t.transform)}  진한 글자 + Menu Plain");
                        }
                        else
                        {
                            t.fontSharedMaterial = preset;
                            outlined++;
                            sb.AppendLine($"  {path} :: {GetPath(t.transform)}  Menu (외곽선+그림자)");
                        }
                        EditorUtility.SetDirty(t);
                        dirty = true;
                    }
                if (dirty) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
        }
        finally
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
        AssetDatabase.SaveAssets();
        Log($"가독성 보정(메뉴 씬만): 진한 글자 {dark}개, 외곽선+그림자 {outlined}개, 폰트 기본 두께는 원래대로\n{sb}");
    }

    const string MenuPlainPresetPath = FontDir + "/ThinMulmaru SDF - Menu Plain.mat";

    /// <summary>폰트 기본 머티리얼에서 시작하는 프리셋. 아틀라스·SDF 값은 매번 폰트에 맞춘다.</summary>
    static Material GetOrCreatePreset(TMP_FontAsset font, string path, string name)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(font.material) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = font.material.shader;
        m.CopyPropertiesFromMaterial(font.material);
        m.shaderKeywords = font.material.shaderKeywords;
        return m;
    }

    /// <summary>텍스트 뒤에 깔린 가장 가까운 불투명 Image 색. (자기 자신 이후의 조상만 본다)</summary>
    static Color? BackgroundColor(Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent)
        {
            var img = p.GetComponent<UnityEngine.UI.Image>();
            if (img != null && img.enabled && img.color.a > 0.05f) return img.color;
        }
        return null;
    }

    static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
}
