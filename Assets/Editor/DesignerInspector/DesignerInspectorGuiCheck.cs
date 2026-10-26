using System;
using UnityEditor;
using UnityEngine;

// 실제 IMGUI 이벤트 안에서 펼친 목록/중첩 필드/UnityEvent를 그려 예외를 검사한다.
public sealed class DesignerInspectorGuiCheck : EditorWindow
{
    private Editor inspected;
    private Vector2 scroll;
    private int draws;
    private Exception failure;
    private int fontAtlasAssertions;
    [MenuItem("Tools/기획/인스펙터 그리기 검사")]
    public static void Run()
    {
        var window = CreateInstance<DesignerInspectorGuiCheck>();
        Application.logMessageReceived += window.RecordLog;
        try
        {
            window.titleContent = new GUIContent("인스펙터 검증");
            window.position = new Rect(0, 0, 460, 900);
            window.ShowUtility();
            string[] assets = {
                "Assets/Prefabs/Enemy/Enemy.prefab", "Assets/Prefabs/Effect/Hit/Slash Impact.prefab",
                "Assets/SOData/Enemy/Enemy AI Patterns/Boss/Bone Master Phase 2 AI Pattern.asset",
                "Assets/Resources/FeedbackCatalog.asset"
            };
            foreach (var path in assets)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset is GameObject root)
                {
                    foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (component != null && DesignerInspectorView.IsProjectType(component.GetType())) window.Check(component);
                }
                else if (asset != null) window.Check(asset);
                else throw new Exception("검증 에셋 없음: " + path);
            }
            if (window.draws == 0) throw new Exception("IMGUI 이벤트가 실행되지 않았습니다.");
            Debug.Log("[DesignerInspectorGUI] PASS " + window.draws + " GUI draws; Unity TextCore atlas assertions=" + window.fontAtlasAssertions);
        }
        finally { Application.logMessageReceived -= window.RecordLog; if (window.inspected != null) DestroyImmediate(window.inspected); window.Close(); DestroyImmediate(window); }
    }
    private void RecordLog(string message, string stack, LogType type)
    {
        // 배치 에디터의 CJK 동적 폰트 아틀라스 생성 assertion은 따로 기록한다.
        // 네이티브 로그 콜백은 stack을 비우기도 한다. 원본 스택은 Editor.log에 남는다.
        if (message.StartsWith("Assertion failed on expression:", StringComparison.Ordinal) && message.Contains("kDontSaveInEditor"))
        { fontAtlasAssertions++; return; }
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            failure = new Exception(message);
    }
    private void Check(UnityEngine.Object target)
    {
        if (inspected != null) DestroyImmediate(inspected);
        inspected = Editor.CreateEditor(target);
        var p = inspected.serializedObject.GetIterator();
        while (p.NextVisible(true)) if (p.hasVisibleChildren) p.isExpanded = true;
        SendEvent(new Event { type = EventType.Layout });
        SendEvent(new Event { type = EventType.Repaint });
        if (failure != null) throw new Exception(target.name + " 인스펙터 그리기 실패", failure);
    }
    private void OnGUI()
    {
        if (inspected == null) return;
        try
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            inspected.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
            draws++;
        }
        catch (Exception e) { failure = e; throw; }
    }
}
