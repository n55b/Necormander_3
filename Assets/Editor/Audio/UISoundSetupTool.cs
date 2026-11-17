using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 소리 셋업 (메뉴: Tools > 사운드)
///  · UI 소리 설정 열기         : Resources/UI/UISoundSettings.asset 을 연다. 없으면 만든다.
///  · UI 버튼에 소리 붙이기     : 인게임 UI 프리팹의 버튼·아이템 칸에 'UI 버튼 소리' 컴포넌트를 붙인다(이미 있으면 그대로 둠).
/// </summary>
public static class UISoundSetupTool
{
    public const string SettingsPath = "Assets/Resources/UI/UISoundSettings.asset";

    /// <summary>게임에서 열림/닫힘 신호를 보내는 창들. (코드 이름, 설명)</summary>
    public static readonly (string name, string label)[] KnownWindows =
    {
        ("Reward", "보상 선택"),
        ("HandSlot", "소환수 습득 · 교체"),
        ("Augment", "증강 선택"),
        ("Map", "전체 지도 (M)"),
        ("Shop", "상점"),
        ("Dialogue", "대화"),
        ("Pouch", "아이템 주머니 (V)"),
        ("PlayerInfo", "장착 정보 (C)"),
        ("PlayerInfoDetails", "장착 정보 · 상세 펼치기"),
        ("Option", "옵션 (ESC)"),
        ("GameOver", "게임 오버 (열림만)"),
        ("GameClear", "게임 클리어 (열림만)"),
    };

    [MenuItem("Tools/사운드/UI 소리 설정 열기", priority = 10)]
    public static void OpenSettings()
    {
        var s = EnsureSettings();
        Selection.activeObject = s;
        EditorGUIUtility.PingObject(s);
    }

    [MenuItem("Tools/사운드/UI 버튼에 소리 붙이기", priority = 11)]
    public static void AttachButtonSounds()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[UI 소리] 플레이 중에는 프리팹을 고칠 수 없습니다.");
            return;
        }
        EnsureSettings();
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/UI" })
            .Select(AssetDatabase.GUIDToAssetPath).ToList();
        if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/ShopPopupUI.prefab") != null)
            paths.Add("Assets/Resources/UI/ShopPopupUI.prefab");

        var log = new StringBuilder();
        int added = 0, motions = 0;
        foreach (var path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponentsInChildren<Button>(true).Length == 0) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = AttachIn(root, out int m);
                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"  {path}: 소리 {n}개, 호버 모션 {m}개");
                    added += n;
                    motions += m;
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log($"[UI 소리] 버튼 소리 컴포넌트 {added}개, 호버 모션 {motions}개를 붙였습니다.\n{log}");
    }

    /// <summary>소리 컴포넌트가 없는 버튼에만 소리(+호버 모션)를 붙인다. 반환값 = 붙인 소리 수.</summary>
    public static int AttachIn(GameObject root, out int motions)
    {
        // 상점 구매 버튼은 성공/실패 소리가 따로 나므로 클릭 소리를 끈다.
        var buyButtons = new HashSet<Button>();
        foreach (var slot in root.GetComponentsInChildren<ShopPopupSlot>(true))
            if (new SerializedObject(slot).FindProperty("buyButton")?.objectReferenceValue is Button b) buyButtons.Add(b);

        int count = 0;
        motions = 0;
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            // 이미 소리가 붙은 버튼은 기획자가 손본 버튼이다 — 소리 값도, 지운 호버 모션도 되살리지 않는다.
            if (button.GetComponent<UISoundOnInteract>() != null) continue;
            if (button.GetComponent<UIHoverMotion>() == null)
            {
                var motion = button.gameObject.AddComponent<UIHoverMotion>();
                // 버튼의 클릭 영역은 고정하고, 표시용 자식만 들어 올린다.
                var label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
                motion.target = button.targetGraphic != null && button.targetGraphic.transform != button.transform
                    ? button.targetGraphic.rectTransform : label != null && label.transform != button.transform ? label.rectTransform : button.transform as RectTransform;
                motions++;
            }
            var s = button.gameObject.AddComponent<UISoundOnInteract>();
            var parent = button.transform.parent;
            if (button.GetComponent<PouchSlotUI>() != null) s.kind = UISoundOnInteract.Kind.Slot;
            else if (parent != null && parent.name.StartsWith("Sort")) s.kind = UISoundOnInteract.Kind.Tab;
            else s.kind = UISoundOnInteract.Kind.Button;
            if (buyButtons.Contains(button)) s.playClick = false;
            count++;
        }
        return count;
    }

    // ─── 설정 파일 ───────────────────────────────────────────────────
    public static UISoundSettings EnsureSettings()
    {
        var s = AssetDatabase.LoadAssetAtPath<UISoundSettings>(SettingsPath);
        if (s != null) return s;

        s = ScriptableObject.CreateInstance<UISoundSettings>();
        foreach (var (name, label) in KnownWindows)
            s.windows.Add(new UISoundSettings.WindowSound { uiName = name, label = label });
        AssetDatabase.CreateAsset(s, SettingsPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[UI 소리] 설정 파일을 만들었습니다: {SettingsPath}");
        return s;
    }

    /// <summary>목록에 없는 창 이름을 추가한다(사운드 테스트 툴에서 씀).</summary>
    public static void AddWindow(UISoundSettings s, string uiName, string label = "")
    {
        if (s == null || string.IsNullOrEmpty(uiName) || s.FindWindow(uiName) != null) return;
        Undo.RecordObject(s, "UI 소리 창 추가");
        s.windows.Add(new UISoundSettings.WindowSound { uiName = uiName, label = label });
        EditorUtility.SetDirty(s);
        s.InvalidateCache();
    }
}
