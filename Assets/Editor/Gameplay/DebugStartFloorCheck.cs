using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>씬마다 새 GameManager로 시작 층 결정을 검사한다. 세이브/씬 에셋은 변경하지 않는다.</summary>
public static class DebugStartFloorCheck
{
    [MenuItem("Tools/Map/Verify Debug Start Floor")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 끈 뒤 실행하세요.");

        const BindingFlags privateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        var pending = typeof(GameManager).GetField("_pendingNextFloor", privateStatic);
        var reset = typeof(GameManager).GetMethod("ResetFloorTransition", privateStatic);
        var resolve = typeof(GameManager).GetMethod("ResolveStartingFloor", privateInstance);
        var previousPending = pending.GetValue(null);
        var previousManager = GameManager.Instance;
        bool previousTutorial = TutorialFlow.IsRunning;
        var scene = EditorSceneManager.NewPreviewScene();
        var managers = new List<GameManager>();
        try
        {
            TutorialFlow.IsRunning = false;
            reset.Invoke(null, null);

            int Enter(int debugFloor, int savedFloor)
            {
                var host = new GameObject("Debug floor check");
                SceneManager.MoveGameObjectToScene(host, scene);
                host.SetActive(false); // Awake/Start의 저장 읽기·게임 초기화는 실행하지 않는다.
                var gm = host.AddComponent<GameManager>();
                gm.debugStartFloor = debugFloor;
                gm.currentFloor = (int)resolve.Invoke(gm, new object[] { savedFloor });
                managers.Add(gm);
                return gm.currentFloor;
            }

            Check(Enter(0, 3) == 3, "디버그 꺼짐: 기존 저장 층 유지");
            Check(Enter(1, 4) == 1, "첫 진입: 저장 층 대신 디버그 시작 층");
            for (int floor = 2; floor <= 5; floor++)
            {
                pending.SetValue(null, floor); // GoToNextFloor가 다음 씬 로드 직전에 전달하는 값
                Check(Enter(1, floor) == floor, $"debug=1을 유지한 채 {floor}층 진행");
                Check((int)pending.GetValue(null) == 0, "층 이동 요청은 한 번만 소비");
            }
            Check(Enter(3, 1) == 3, "새 던전 진입: 디버그 시작 층 다시 적용");
            pending.SetValue(null, 4);
            Check(Enter(3, 1) == 4, "진행 중에는 오래된 저장 층보다 실제 이동 층 우선");

            pending.SetValue(null, 5);
            TutorialFlow.IsRunning = true;
            Check(Enter(4, 3) == 0, "튜토리얼은 항상 0층");
            TutorialFlow.IsRunning = false;
            Check(Enter(4, 1) == 4, "튜토리얼 이후 첫 던전 진입");
            pending.SetValue(null, 5);
            reset.Invoke(null, null);
            Check(Enter(2, 4) == 2, "도메인 리로드 없이 새 Play 시작 시 초기화");

            // 실제 층 설정도 확인: 1층은 즉시 클리어, 2층은 엘리트 전투.
            var tuning = AssetDatabase.LoadAssetAtPath<MapGenerationDataSO>("Assets/SOData/Map/MapGenerationData.asset");
            var manager = managers[managers.Count - 1];
            typeof(GameManager).GetField("currentStageMapData", privateInstance).SetValue(manager, tuning);
            GameManager.Instance = manager;
            var elite = manager.gameObject.AddComponent<EliteRoomEvent>();
            Check(tuning != null && !elite.SkipEncounterForCurrentFloor, "2층 엘리트 전투 활성");
            manager.currentFloor = 1;
            Check(elite.SkipEncounterForCurrentFloor, "1층 엘리트 생략 유지");

            Debug.Log("[DebugStartFloorCheck] PASS: first entry, sequential floors, new entry, tutorial, " +
                "Play reset, floor-1 skip / floor-2 encounter. No saves or scenes changed.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            GameManager.Instance = previousManager;
            TutorialFlow.IsRunning = previousTutorial;
            pending.SetValue(null, previousPending);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[DebugStartFloorCheck] FAIL: " + message);
    }
}
