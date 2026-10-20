using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>실제 코루틴 중단을 검사한다. 씬/프리팹/저장 데이터는 저장하지 않는다.</summary>
public static class EliteTelegraphCleanupCheck
{
    private const string Pending = "EliteTelegraphCleanupCheck.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Batch mode only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        Resume();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
        EditorApplication.playModeStateChanged -= OnPlay;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    private static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.EraseBool(Pending);
        EditorApplication.playModeStateChanged -= OnPlay;
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Combat/Verify Elite Telegraph Cleanup In Play Mode")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required.");
        var brain = ScriptableObject.CreateInstance<EliteChargerAIPatternSO>();
        var other = ScriptableObject.CreateInstance<EliteChargerAIPatternSO>();
        var go = new GameObject("Elite cleanup test");
        var entity = go.AddComponent<EnemyController>();
        entity.enabled = false;
        typeof(BaseEntity).GetField("_runtimeBrain", Private).SetValue(entity, brain);
        GameObject unrelated = null;
        try
        {
            // 실제 도약 코루틴을 시작한 뒤 사망 처리와 같은 CancelAttack 경로로 중단한다.
            var routine = (IEnumerator)Call(brain, "MiniChargeStabRoutine", entity, Vector2.right, 3f, 60f);
            var coroutine = entity.StartCoroutine(routine);
            typeof(EliteChargerAIPatternSO).GetField("_basicAttackCoroutine", Private).SetValue(brain, coroutine);
            var landing = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                .Where(r => r.name == "Elite_Telegraph_Ring" || r.name == "Elite_Telegraph_Circle")
                .Select(r => r.gameObject).ToArray();
            Check(landing.Length == 2, "actual leap creates outline and fill");
            unrelated = (GameObject)Call(other, "CreateFallbackRing", Color.blue);
            entity.CancelAttack();
            Check(landing.All(o => o == null || !o.activeSelf), "death/cancel hides both leap markers immediately");
            Check(unrelated.activeSelf, "another enemy's marker is preserved");
            entity.CancelAttack();
            Check(unrelated.activeSelf, "repeated cancellation is safe");

            foreach (string pattern in new[] { "Pattern3_GroundSlam", "RunShockwaveRing" })
            {
                var attack = pattern == "Pattern3_GroundSlam"
                    ? (IEnumerator)Call(brain, pattern, entity)
                    : (IEnumerator)Call(brain, pattern, entity, Vector2.zero, 10f);
                var handle = entity.StartCoroutine(attack);
                typeof(EliteChargerAIPatternSO).GetField("_specialPatternCoroutine", Private).SetValue(brain, handle);
                var markers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.name.StartsWith("Elite_Telegraph_") && r.gameObject != unrelated)
                    .Select(r => r.gameObject).ToArray();
                Check(markers.Length == 1, pattern + " creates a marker");
                entity.CancelAttack();
                Check(markers.All(o => o == null || !o.activeSelf), pattern + " stops and removes its marker");
            }

            foreach (string shape in new[] { "CreateFallbackCircle", "CreateFallbackRect", "CreateFallbackRing" })
            {
                var visual = shape == "CreateFallbackCircle"
                    ? (GameObject)Call(brain, shape, Vector2.zero, 1f, Color.red)
                    : (GameObject)Call(brain, shape, Color.red);
                brain.OnAttackCancelled(entity);
                Check(visual == null || !visual.activeSelf, shape + " is cleaned on cancellation");
            }
            var pending = (GameObject)Call(brain, "CreateFallbackRing", Color.red);
            brain.Init(entity);
            Check(!pending.activeSelf, "reinitialization cleans remaining markers");
            var last = (GameObject)Call(brain, "CreateFallbackRing", Color.red);
            Object.DestroyImmediate(brain);
            brain = null;
            Check(last == null || !last.activeSelf, "brain destruction cleans remaining markers");
            Debug.Log("[EliteTelegraphCleanupCheck] ALL CHECKS PASSED");
        }
        finally
        {
            entity.StopAllCoroutines();
            typeof(BaseEntity).GetField("_runtimeBrain", Private).SetValue(entity, null);
            if (brain != null) Object.DestroyImmediate(brain);
            Object.DestroyImmediate(other);
            if (unrelated != null) Object.DestroyImmediate(unrelated);
            Object.DestroyImmediate(go);
        }
    }

    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private).Invoke(target, args);

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[EliteTelegraphCleanupCheck] FAIL: " + label);
        Debug.Log("[EliteTelegraphCleanupCheck] PASS: " + label);
    }
}
