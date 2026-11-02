using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>격리 Play Mode에서 실제 체력과 HUD의 회복 도착·표시 동기화를 검사한다.</summary>
public static class HealingFeedbackValidation
{
    private const string Key = "HealingFeedbackValidation.Running";
    private const string HudPath = "Assets/Prefabs/UI/Player State/PlayerStateUI.prefab";
    private static IEnumerator checks;
    private static FeedbackOverlay overlay;
    private static PlayerStateUI hud;
    private static CharacterHealth health;
    private static CharacterStat stat;
    private static Image fill;
    private static FeedbackRecipe recipe;
    private static string catalogSnapshot;
    private static int passed;

    // 명시적인 배치 실행 전용. 사용자 작업 중인 씬에서는 실행하지 않는다.
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("격리 batchmode 검사용입니다.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    private static void Resume() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (checks == null) checks = Check();
            if (!checks.MoveNext()) Finish(0);
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }
    private static IEnumerator Wait(float seconds)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end) yield return null;
    }
    private static IEnumerator Check()
    {
        var camera = new GameObject("회복 검사 카메라").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 8;
        camera.transform.position = new Vector3(0, 0, -10);
        var canvas = new GameObject("검사 HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlayerStateUI>(HudPath), canvas.transform);
        hud.gameObject.SetActive(true);
        fill = Get<Image>(hud, "hpSprite");
        stat = new GameObject("검사 체력").AddComponent<CharacterStat>();
        health = stat.GetComponent<CharacterHealth>();
        health.Init(stat, null);
        catalogSnapshot = EditorJsonUtility.ToJson(PolishFeedback.Catalog);
        recipe = PolishFeedback.Catalog.Find(FeedbackCue.Heal);
        overlay = Object.Instantiate(PolishFeedback.Catalog.overlayPrefab);
        health.SetHP(40);
        hud.Initialize(health);
        Canvas.ForceUpdateCanvases();
        yield return null;

        // 실제 Heal API와 실제 HUD 프리팹으로 도착 전/후 표시를 확인한다.
        health.Heal(10);
        Require(health.CurHP == 50, "실제 체력 즉시 회복");
        Expect(.4f, "도착 전 표시 보류");
        int mote = (Get<int>(overlay, "cursor") - 1) % Get<Image[]>(overlay, "motes").Length;
        var target = Get<Func<Vector2?>[]>(overlay, "destinations")[mote]();
        var track = (RectTransform)fill.transform.parent;
        var expected = RectTransformUtility.WorldToScreenPoint(null, track.TransformPoint(new Vector3(Mathf.Lerp(track.rect.xMin, track.rect.xMax, .45f), track.rect.center.y)));
        Require(target.HasValue && Vector2.Distance(target.Value, expected) < 1f, "실제 빈 구간 중앙에 도착");
        var wait = Wait(recipe.duration * .5f); while (wait.MoveNext()) yield return null;
        Expect(.4f, "비행 중 먼저 차지 않음");
        Require(Get<Image[]>(overlay, "motes")[mote].color.a > .9f, "구슬은 도착 전 사라지지 않음");
        wait = Wait(recipe.duration * .5f + .08f); while (wait.MoveNext()) yield return null;
        Require(fill.fillAmount > .4f && fill.fillAmount < .5f, "도착 후 점진적으로 채움");
        wait = Wait(.35f); while (wait.MoveNext()) yield return null;
        Expect(.5f, "회복 완료 후 정확히 합류");

        health.Heal(10); health.Heal(15);
        Expect(.5f, "연속 회복을 중복 합산하지 않음");
        wait = Wait(1.1f); while (wait.MoveNext()) yield return null;
        Expect(.75f, "연속 회복 완료");
        health.SetHP(95); health.Heal(20);
        Expect(.95f, "초과 회복은 실제 5만 보류");
        wait = Wait(1.1f); while (wait.MoveNext()) yield return null;
        Expect(1f, "최대 체력 제한");
        int cursor = Get<int>(overlay, "cursor"); health.Heal(10);
        Require(cursor == Get<int>(overlay, "cursor"), "만피에서 구슬 생성 없음");

        health.SetHP(40); health.Heal(10); health.SetHP(35);
        Expect(.35f, "비행 중 피격은 즉시 반영");
        wait = Wait(1.1f); while (wait.MoveNext()) yield return null;
        Expect(.35f, "늦게 도착해도 피해 되돌리지 않음");
        health.Heal(10); health.SetHP(0);
        wait = Wait(1.1f); while (wait.MoveNext()) yield return null;
        Expect(0, "사망 후 회복 표시 부활 없음");

        health.SetHP(40); recipe.enabled = false; health.Heal(10);
        Expect(.5f, "효과 꺼짐 즉시 반영"); recipe.enabled = true;
        health.Heal(10); overlay.gameObject.SetActive(false);
        Expect(.6f, "화면 연출 비활성화도 보류 해제"); overlay.gameObject.SetActive(true);
        health.Heal(10); hud.gameObject.SetActive(false); hud.gameObject.SetActive(true);
        Expect(.7f, "HUD 재활성화 시 보류 없음");
        health.Heal(10);
        for (int i = 0; i < 17; i++) overlay.Pulse(FeedbackCue.StackUp, Vector3.zero);
        Expect(.8f, "풀 슬롯 재사용 때 체력 대기 유실 없음");

        health.SetHP(40); hud.Initialize(health); hud.Initialize(health); health.Heal(10);
        Expect(.4f, "재초기화 이벤트 중복 없음");
        Time.timeScale = 0;
        wait = Wait(1.1f); while (wait.MoveNext()) yield return null;
        Expect(.5f, "일시정지 중에도 UI 흡수 완료"); Time.timeScale = 1;
        health.Heal(10); Set(stat, "baseMaxHP", 120f); hud.RefreshHP();
        Expect(.5f, "최대 체력 변경 즉시 재동기화");
        health.Heal(10); Object.Destroy(overlay.gameObject);
        yield return null;
        Expect(70f / 120f, "씬 정리/연출 파괴 시 보류 해제");
        Debug.Log($"[HealingFeedbackValidation] PASS: {passed} checks; actual HP, live prefab gap target, flight/absorption, overlapping/capped heals, damage/death, disable/pool reuse, pause, reinitialize, max HP and destruction.");
    }
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Expect(float amount, string label) => Require(Mathf.Abs(fill.fillAmount - amount) < .001f, label + $" ({fill.fillAmount:F3}/{amount:F3})");
    private static void Require(bool value, string label)
    {
        if (!value) throw new Exception("회복 연출 검사 실패: " + label);
        passed++;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        if (catalogSnapshot != null) EditorJsonUtility.FromJsonOverwrite(catalogSnapshot, PolishFeedback.Catalog);
        Time.timeScale = 1;
        EditorApplication.Exit(code);
    }
}
