using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>저장 없이 F5 공동 토글, F4 초기화, 최종 스탯 표시를 검사한다.</summary>
public static class DPSMeterCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Debug/Verify DPS And Player Stats")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");
        var scene = EditorSceneManager.NewPreviewScene();
        var previousManager = GameManager.Instance;
        var previousSettings = InputSystem.settings;
        var previousKeyboard = Keyboard.current;
        var settings = Object.Instantiate(previousSettings);
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var culture = CultureInfo.CurrentCulture;
        float timeScale = Time.timeScale;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            InputSystem.settings = settings;
            settings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            var host = new GameObject("DPS stats check");
            host.SetActive(false); // 실제 게임 초기화/저장 루트는 실행하지 않는다.
            SceneManager.MoveGameObjectToScene(host, scene);
            var meter = host.AddComponent<DPSMeter>();
            GameManager.Instance = null;
            void Press(params Key[] keys)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                InputSystem.Update();
                Call(meter, "Update");
            }
            Press(Key.F5);
            Check((bool)Get(meter, "_showUI"), "F5 공동 표시 켜기");
            Check(((string)Get(meter, "_statsText")).Contains("기다리는 중"), "플레이어 미생성 안전 처리");
            Press();
            Press(Key.F5);
            Check(!(bool)Get(meter, "_showUI"), "F5 공동 표시 끄기");
            Press();
            Press(Key.F3);
            Check(!(bool)Get(meter, "_showUI"), "기존 F3 독립 토글 제거");
            Press(Key.C);
            Check(!(bool)Get(meter, "_showUI"), "C는 디버그 패널을 열지 않음");

            var stat = host.AddComponent<CharacterStat>();
            typeof(CharacterStat).GetProperty("Status").SetValue(stat, host.GetComponent<CharacterStatus>());
            var player = host.AddComponent<PlayerController>();
            Set(player, "stat", stat);
            var guard = host.AddComponent<PlayerParryController>();
            Set(guard, "_guard", 42f);
            Set(guard, "_guardBroken", true);
            var manager = host.AddComponent<GameManager>();
            Set(manager, "playerController", player);
            GameManager.Instance = manager;
            object source = new object();
            stat.Mods.AddFlat(source, StatType.Attack, 10f);
            stat.Mods.AddPercent(source, StatType.Attack, .5f);
            stat.Mods.AddFlat(source, StatType.Defense, 100f);
            stat.Mods.AddFlat(source, StatType.Evasion, .2f);
            stat.Mods.AddFlat(source, StatType.PhysDamageAmp, .25f);
            stat.Mods.AddFlat(source, StatType.SkillCooldownReduction, 20f);
            stat.Mods.AddFlat(source, StatType.MoveSpeed, 2f);
            Press(Key.F5);
            string text = (string)Get(meter, "_statsText");
            foreach (string expected in new[] { "물리 공격력: 30.00", "방어력 (피해 감소): 75.0%",
                "회피율: 20.0%", "물리 피해 증폭: 25.0%", "스킬 쿨타임 감소: 20.0%",
                "이동 속도: 7.00", "가드 게이지: 42.0 / 50.0", "소진 잠금: True" })
                Check(text.Contains(expected), "최종 계산값/단위: " + expected);
            stat.Mods.RemoveSource(source);
            Set(meter, "_nextStatsRefresh", 0f);
            Press();
            Check(((string)Get(meter, "_statsText")).Contains("물리 공격력: 10.00"), "열린 패널 버프 해제 갱신");

            Call(meter, "HandleDamage", host.GetComponent<CharacterHealth>(), new DamageInfo(25f));
            Check((float)Get(meter, "_totalDamage") == 25f, "기존 피해 집계 유지");
            Press(Key.F5); // 숨겨도 기록은 유지된다.
            Call(meter, "HandleDamage", host.GetComponent<CharacterHealth>(), new DamageInfo(15f));
            Check((float)Get(meter, "_totalDamage") == 40f, "닫힌 동안에도 피해 집계");
            Press();
            Press(Key.F5);
            Press(Key.F4);
            Check((float)Get(meter, "_totalDamage") == 0f && !(bool)Get(meter, "_isRecording")
                && (bool)Get(meter, "_showUI"), "F4는 기록만 초기화하고 공동 표시 유지");
            Object.DestroyImmediate(player);
            Set(meter, "_nextStatsRefresh", 0f);
            Press();
            Check(((string)Get(meter, "_statsText")).Contains("기다리는 중"), "씬 전환 중 파괴된 플레이어 안전 처리");
            Check(Time.timeScale == timeScale, "디버그 패널은 게임 시간을 변경하지 않음");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameManager.prefab");
            Check(prefab.GetComponentsInChildren<DPSMeter>(true).Length == 1, "기존 프리팹의 DPSMeter 하나만 사용");
            Debug.Log("[DPSMeterCheck] PASS: F5 on/off, F3/C unchanged, F4 reset, live final stats/units, guard, hidden recording, missing/destroyed player, prefab wiring.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            GameManager.Instance = previousManager;
            InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            InputSystem.settings = previousSettings;
            Object.DestroyImmediate(settings);
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static object Get(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("[DPSMeterCheck] FAIL: " + message);
    }
}
