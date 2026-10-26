using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Aseprite;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>원본 그림은 보존하고 기존 게임 프리팹의 애니메이션 연결만 갱신한다.</summary>
public static class GameplaySpriteAnimations
{
    private const string Chest = "Assets/Resources/Sprites/Object/Chest.aseprite";
    private const string Dash = "Assets/Resources/Sprites/Effect/Dash_Effect.aseprite";
    private const string Move = "Assets/Resources/Sprites/Effect/Move_Effect.aseprite";
    private const string DashFolder = "Assets/Animations/Effect/Dash";
    private const string DashPrefab = "Assets/Prefabs/Effect/DashSmog/DashSmogEffect.prefab";
    private const string MovePrefab = "Assets/Prefabs/Effect/Walk_Smog_Effect.prefab";
    private static readonly string[] Chests = {
        "Assets/Prefabs/Interactables/RoomRewardBox.prefab",
        "Assets/Prefabs/Interactables/EliteRewardBox.prefab"
    };

    [MenuItem("Tools/아트/상자·이동·대시 애니메이션 다시 연결")]
    public static void Refresh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이를 종료한 뒤 실행하세요.");
        foreach (string source in new[] { Chest, Dash, Move })
        {
            AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AsepriteImporter)AssetImporter.GetAtPath(source);
            importer.importMode = FileImportModes.AnimatedSprite;
            importer.layerImportMode = LayerImportModes.MergeFrame;
            importer.spritePixelsPerUnit = 32f;
            importer.pivotSpace = PivotSpaces.Canvas;
            // 닫힌 상자 몸통의 바닥(원본 Y=37)을 기존 콜라이더 원점에 유지한다.
            // 새로 포함된 그림자는 원점 아래에 놓이며 뚜껑을 열어도 피벗은 고정된다.
            importer.pivotAlignment = source == Chest ? SpriteAlignment.Custom : SpriteAlignment.Center;
            if (source == Chest) importer.customPivotPosition = new Vector2(0.5f, 11f / 48f);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.generateAnimationClips = true;
            importer.generateModelPrefab = true;
            importer.SaveAndReimport();
        }
        foreach (string prefab in Chests)
            Bind(prefab, Controller(Chest), Sprite(Chest, "Frame_0"), false);
        Bind(MovePrefab, Controller(Move), Sprite(Move, "Frame_0"), true);

        // 현재 Aseprite importer는 사각형 편집 capability를 제공하지 않는다.
        // 원본 importer 데이터를 강제 변경하지 않고 기존 Viper 방식처럼 별도 Sprite 에셋을 만든다.
        // 캔버스 밖 작업용 셀은 제외하고, 피벗과 프레임 시간은 원본 그대로 보존한다.
        EnsureFolder(DashFolder);
        var sourceAssets = AssetDatabase.LoadAllAssetsAtPath(Dash);
        var canvas = ((AsepriteImporter)AssetImporter.GetAtPath(Dash)).canvasSize;
        string spritePath = DashFolder + "/DashCanvasSprites.asset";
        var old = AssetDatabase.LoadAllAssetsAtPath(spritePath).OfType<Sprite>().ToDictionary(s => s.name);
        var mapped = new Dictionary<Sprite, Sprite>();
        foreach (var source in sourceAssets.OfType<Sprite>())
        {
            Vector2 anchor = source.rect.position + source.pivot;
            Rect canvasRect = new Rect(anchor - canvas * 0.5f, canvas);
            Rect rect = Rect.MinMaxRect(Mathf.Max(source.rect.xMin, canvasRect.xMin), Mathf.Max(source.rect.yMin, canvasRect.yMin),
                Mathf.Min(source.rect.xMax, canvasRect.xMax), Mathf.Min(source.rect.yMax, canvasRect.yMax));
            Require(rect.width > 0 && rect.height > 0, "캔버스 밖에만 존재하는 프레임: " + source.name);
            Vector2 pivot = anchor - rect.position;
            var copy = UnityEngine.Sprite.Create(source.texture, rect, new Vector2(pivot.x / rect.width, pivot.y / rect.height),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.zero, false);
            copy.name = source.name;
            if (old.TryGetValue(copy.name, out var existing))
            {
                EditorUtility.CopySerialized(copy, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(copy);
                copy = existing;
            }
            else if (AssetDatabase.LoadMainAssetAtPath(spritePath) == null) AssetDatabase.CreateAsset(copy, spritePath);
            else AssetDatabase.AddObjectToAsset(copy, spritePath);
            mapped.Add(source, copy);
        }
        var clips = new Dictionary<string, AnimationClip>();
        foreach (string state in new[] { "Up", "Down" })
        {
            var original = sourceAssets.OfType<AnimationClip>().Single(c => c.name == state);
            var copy = Object.Instantiate(original); copy.name = state;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(copy))
            {
                Require(binding.path == "" && binding.type == typeof(SpriteRenderer), "대시 스프라이트 트랙 구조 변경됨");
                var keys = AnimationUtility.GetObjectReferenceCurve(copy, binding);
                for (int i = 0; i < keys.Length; i++)
                    if (keys[i].value is Sprite source) keys[i].value = mapped[source];
                AnimationUtility.SetObjectReferenceCurve(copy, binding, keys);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(copy); settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            string path = DashFolder + "/Dash_" + state + ".anim";
            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (saved == null) { AssetDatabase.CreateAsset(copy, path); saved = copy; }
            else { EditorUtility.CopySerialized(copy, saved); EditorUtility.SetDirty(saved); Object.DestroyImmediate(copy); }
            clips.Add(state, saved);
        }
        string controllerPath = DashFolder + "/Dash_Effect.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)
            ?? AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine = controller.layers[0].stateMachine;
        foreach (var pair in clips)
        {
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == pair.Key) ?? machine.AddState(pair.Key);
            state.motion = pair.Value; state.speed = 1f;
            if (pair.Key == "Down") machine.defaultState = state;
            EditorUtility.SetDirty(state);
        }
        EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
        Bind(DashPrefab, controller, mapped[Sprite(Dash, "Frame_6")], true);
        AssetDatabase.SaveAssets();
        Validate();
    }

    private static Sprite Sprite(string source, string name) => AssetDatabase.LoadAllAssetsAtPath(source).OfType<Sprite>().Single(s => s.name == name);
    private static RuntimeAnimatorController Controller(string source) => AssetDatabase.LoadAllAssetsAtPath(source).OfType<RuntimeAnimatorController>().Single();
    private static void Bind(string path, RuntimeAnimatorController controller, Sprite sprite, bool enabled)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var animator = root.GetComponent<Animator>();
            Require(animator != null && root.GetComponent<SpriteRenderer>() != null, path + " 렌더러/Animator 누락");
            animator.runtimeAnimatorController = controller; animator.enabled = enabled;
            root.GetComponent<SpriteRenderer>().sprite = sprite;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int split = folder.LastIndexOf('/'); string parent = folder.Substring(0, split);
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, folder.Substring(split + 1));
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[GameplaySpriteAnimations] " + message);
    }

    [MenuItem("Tools/아트/상자·이동·대시 애니메이션 검사")]
    public static void Validate()
    {
        foreach (string path in Chests.Concat(new[] { DashPrefab, MovePrefab }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = Object.Instantiate(prefab);
            try
            {
                bool chest = Chests.Contains(path);
                var animator = instance.GetComponent<Animator>();
                Require(animator != null && animator.runtimeAnimatorController != null, "Animator 참조 누락 " + path);
                Require(animator.enabled == !chest, "평소 상자는 닫혀 있어야 함 " + path);
                var renderer = instance.GetComponent<SpriteRenderer>();
                Require(renderer.sprite != null && renderer.sprite.pixelsPerUnit == 32f, "32 PPU 스프라이트 누락");
                foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
                {
                    var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    Require(bindings.Length == 1 && bindings[0].path == "", "루트 스프라이트 트랙 필요 " + clip.name);
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
                    foreach (var key in keys)
                    {
                        clip.SampleAnimation(instance, key.time);
                        Require(renderer.sprite == key.value, "프레임 바인딩 실패 " + clip.name);
                    }
                    Require(clip.length > 0f, "빈 클립");
                    if (chest) Require(clip.name == "Chest_Open" && !clip.isLooping, "상자 열기 모션 불일치");
                }
                if (!chest) Require(instance.GetComponent<AutoDestroyEffect>() != null, "이펙트 자동 정리 누락");
            }
            finally { Object.DestroyImmediate(instance); }
        }
        foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(DashFolder + "/DashCanvasSprites.asset").OfType<Sprite>())
        {
            Require(sprite.bounds.min.x >= -0.251f && sprite.bounds.max.x <= 0.251f
                && sprite.bounds.min.y >= -0.251f && sprite.bounds.max.y <= 0.251f, "대시 캔버스 밖 그림 포함 " + sprite.name);
        }
        CheckDashDirections();
        Debug.Log("[GameplaySpriteAnimations] PASS: 2 reward chests, walking/dash prefabs, source frame bindings, 32 PPU, canvas crop, 8 dash directions, automatic cleanup component.");
    }

    private static void CheckDashDirections()
    {
        var owner = new GameObject("Dash direction check");
        var created = new List<GameObject>();
        try
        {
            var dodge = owner.AddComponent<MeleeDodgeController>();
            using (var so = new SerializedObject(dodge))
            {
                so.FindProperty("dashEffectPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DashPrefab);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var direction = typeof(MeleeDodgeController).GetField("_dashDir", flags);
            var play = typeof(MeleeDodgeController).GetMethod("PlayDashEffect", flags);
            foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right, new Vector2(-1,1), new Vector2(1,1), new Vector2(-1,-1), new Vector2(1,-1) })
            {
                var before = new HashSet<AutoDestroyEffect>(Object.FindObjectsByType<AutoDestroyEffect>(FindObjectsSortMode.None));
                direction.SetValue(dodge, dir.normalized);
                play.Invoke(dodge, null);
                var fx = Object.FindObjectsByType<AutoDestroyEffect>(FindObjectsSortMode.None).Single(e => !before.Contains(e));
                created.Add(fx.gameObject);
                var animator = fx.GetComponent<Animator>();
                animator.Update(0f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName(dir.y > 0f ? "Up" : "Down"), "대시 방향별 상태 선택 실패 " + dir);
                Require(Mathf.Sign(fx.transform.localScale.x) == (dir.x < 0f ? 1f : -1f), "대시 좌우 반전 실패 " + dir);
            }
        }
        finally
        {
            foreach (var fx in created) Object.DestroyImmediate(fx);
            Object.DestroyImmediate(owner);
        }
    }
}
