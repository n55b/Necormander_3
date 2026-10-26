using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>별도 batch Editor에서만 실행하는 기존 연출 Catalog 연결 회귀 검사.</summary>
public static class ExistingFeedbackValidation
{
    private const string Key = "ExistingFeedbackValidation.Running";
    private static FeedbackCatalog catalog;
    private static string snapshot;
    private static IEnumerator checks;
    private static int count;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("별도 batchmode에서 실행하세요.");
        catalog = AssetDatabase.LoadAssetAtPath<FeedbackCatalog>(FeedbackSetup.CatalogPath);
        Require(catalog.existingEffects.Length == 12, "기존 연출 12종");
        foreach (var entry in catalog.existingEffects) Require(entry.sources.Length > 0 && entry.sources.All(s => s != null), entry.title + " 연결");
        var originals = catalog.existingEffects.SelectMany(e => e.sources).Distinct().Where(o => !(o is SceneAsset))
            .ToDictionary(o => o, EditorJsonUtility.ToJson);
        string recipes = JsonUtility.ToJson(new Recipes { entries = catalog.effects });
        var off = catalog.existingEffects.First(e => e.cue == ExistingFeedbackCue.Squash);
        bool oldEnabled = off.enabled; off.enabled = false;
        ExistingFeedbackCatalogEditor.Rebuild(catalog);
        Require(!catalog.existingEffects.First(e => e.cue == ExistingFeedbackCue.Squash).enabled, "목록 갱신 시 사용 여부 유지");
        catalog.existingEffects.First(e => e.cue == ExistingFeedbackCue.Squash).enabled = oldEnabled;
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
        foreach (var pair in originals) Require(pair.Value == EditorJsonUtility.ToJson(pair.Key), "원본 값 유지: " + pair.Key.name);
        Require(recipes == JsonUtility.ToJson(new Recipes { entries = catalog.effects }), "기존 66개 신규 설정 유지");

        var impact = catalog.existingEffects.First(e => e.cue == ExistingFeedbackCue.Slash).sources.OfType<ImpactFeedback>().First();
        var data = new SerializedObject(impact); float old = data.FindProperty("lifetime").floatValue;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        data.FindProperty("lifetime").floatValue = old + .1f; data.ApplyModifiedProperties();
        Require(Mathf.Approximately(Get<float>(impact, "lifetime"), old + .1f), "원본 수치 직접 편집");
        Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); data.Update();
        Require(Mathf.Approximately(data.FindProperty("lifetime").floatValue, old), "원본 수치 Undo 복원");
        foreach (string name in new[] { "PixelDitherUI", "PixelFeedbackSprite" })
            Require(!ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>($"Assets/Material/Feedback/{name}.shader")), name + " 컴파일");
        FeedbackCheck.Run();
        Debug.Log($"[ExistingFeedbackValidation] PASS: {count} original-value, links, Undo and shader checks.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true); EditorApplication.update += Tick; EditorApplication.EnterPlaymode();
    }
    [Serializable] private class Recipes { public FeedbackRecipe[] entries; }
    [InitializeOnLoadMethod] private static void Resume() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try { if (checks == null) checks = Check(); if (!checks.MoveNext()) Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    private static IEnumerator Check()
    {
        catalog = PolishFeedback.Catalog; snapshot = EditorJsonUtility.ToJson(catalog);
        var root = new GameObject("그림 변형 검사");
        var picture = new GameObject("그림").transform; picture.SetParent(root.transform);
        var squash = root.AddComponent<VisualSquashStretch>(); Set(squash, "visual", picture);
        squash.PlayAttack();
        var wait = Wait(.04f); while (wait.MoveNext()) yield return null;
        Require(picture.localScale != Vector3.one, "기존 스쿼시 발동");
        Toggle(ExistingFeedbackCue.Squash, false); yield return null; yield return null;
        Require(picture.localScale == Vector3.one, "끄면 변형 즉시 복구"); Toggle(ExistingFeedbackCue.Squash, true);

        var slash = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/Hit/Slash Impact.prefab"));
        Toggle(ExistingFeedbackCue.Slash, false); yield return null; yield return null;
        Require(slash.GetComponentsInChildren<Renderer>().All(r => !r.enabled), "슬래시 도중 끄기");
        Toggle(ExistingFeedbackCue.Slash, true); yield return null; yield return null;
        Require(slash.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "슬래시 다시 켜기");
        Object.Destroy(slash);

        var smoke = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/Walk_Smog_Effect.prefab"));
        Toggle(ExistingFeedbackCue.WalkSmoke, false); yield return null; yield return null;
        Require(smoke == null, "걷기 연기 즉시 정리"); Toggle(ExistingFeedbackCue.WalkSmoke, true);
        smoke = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/DashSmog/DashSmogEffect.prefab"));
        Toggle(ExistingFeedbackCue.DashSmoke, false); yield return null; yield return null;
        Require(smoke == null, "대시 연기 즉시 정리"); Toggle(ExistingFeedbackCue.DashSmoke, true);

        var ghostHost = new GameObject("잔상 검사", typeof(SpriteRenderer));
        var ghosts = ghostHost.AddComponent<DashAfterimage>(); ghosts.ForceEmit = true;
        wait = Wait(.08f); while (wait.MoveNext()) yield return null;
        var activeGhosts = Get<System.Collections.Generic.List<SpriteRenderer>>(ghosts, "_spawned");
        Require(activeGhosts.Count > 0, "기존 잔상 생성");
        Toggle(ExistingFeedbackCue.Afterimage, false); yield return null; yield return null;
        Require(activeGhosts.All(g => !g.gameObject.activeSelf), "기존 잔상 모두 회수");

        Toggle(ExistingFeedbackCue.Dither, false); PolishFeedback.RefreshShaderSettings();
        Require(Shader.GetGlobalFloat("_FeedbackDitherDisabled") == 1, "디더링만 해제");
        Toggle(ExistingFeedbackCue.Dither, true); PolishFeedback.RefreshShaderSettings();
        Require(Shader.GetGlobalFloat("_FeedbackDitherDisabled") == 0, "원래 디더링 설정 복구");

        var curtain = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ScreenFadeCanvas.prefab"));
        var fader = curtain.GetComponent<Fader>(); var group = curtain.GetComponent<CanvasGroup>();
        Toggle(ExistingFeedbackCue.Fade, false);
        bool black = false, done = false, ready = false;
        fader.FadeOutIn(FadeSignal.방이동, () => black = true, () => done = true, () => ready);
        yield return null; yield return null;
        Require(black && !done && group.alpha == 1f && group.blocksRaycasts, "페이드 꺼도 준비 전 암전/입력차단 유지");
        ready = true; yield return null; yield return null;
        Require(done && group.alpha == 0 && !group.blocksRaycasts, "준비 후에만 화면 공개·완료 콜백");

        var ui = new GameObject("흔들림 검사", typeof(RectTransform));
        var rect = (RectTransform)ui.transform; rect.anchoredPosition = new Vector2(7, 9);
        var shaker = ui.AddComponent<UIShaker>(); shaker.Shake();
        wait = Wait(.04f); while (wait.MoveNext()) yield return null;
        Toggle(ExistingFeedbackCue.UIShake, false); yield return null; yield return null;
        Require(rect.anchoredPosition == new Vector2(7, 9), "UI 흔들림 원점 복구");
        var time = new GameObject("시간 검사").AddComponent<HitStopManager>(); time.Initialize();
        time.DoHitStop(.15f); Require(Time.timeScale < 1, "기존 히트스톱");
        Toggle(ExistingFeedbackCue.HitStop, false); yield return null; yield return null;
        Require(Time.timeScale == 1, "히트스톱 중 끄면 시간 복구");
        Toggle(ExistingFeedbackCue.HitStop, true); time.DoHitStop(.15f); Time.timeScale = 0;
        Toggle(ExistingFeedbackCue.HitStop, false); yield return null; yield return null;
        Require(Time.timeScale == 0, "UI 일시정지 소유권 유지"); Time.timeScale = 1;

        catalog.enabled = false;
        foreach (var cue in Enum.GetValues(typeof(ExistingFeedbackCue)).Cast<ExistingFeedbackCue>().Where(c => c != ExistingFeedbackCue.None))
            Require(!PolishFeedback.ExistingEnabled(cue), "전체 끄기: " + cue);
        Require(!PolishFeedback.Enabled(FeedbackCue.Heal), "신규 연출도 전체 끄기");
        Debug.Log($"[ExistingFeedbackValidation] PASS: runtime {count} checks + original-value/Undo/shader/native-slash checks before Play Mode.");
    }
    private static IEnumerator Wait(float seconds)
    { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
    private static void Toggle(ExistingFeedbackCue cue, bool on) => catalog.existingEffects.First(e => e.cue == cue).enabled = on;
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); count++; }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        if (snapshot != null) EditorJsonUtility.FromJsonOverwrite(snapshot, catalog);
        Time.timeScale = 1; Shader.SetGlobalFloat("_FeedbackDitherDisabled", 0);
        EditorApplication.Exit(code);
    }
}
