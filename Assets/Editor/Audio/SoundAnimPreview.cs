using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 사운드 테스트 툴 · [오브젝트 소리] 탭의 '애니메이션과 함께 듣기'.
///
/// 대상의 애니메이션 하나를 고르면 그림(스프라이트 프레임)을 재생하면서, 애니메이션 이벤트가 걸린 순간에
/// 게임과 같은 규칙으로 사운드 세트의 소리를 낸다. 플레이 모드 없이 "휘두르는 그림과 소리가 맞는지"를 본다.
///
///  · OnHitEvent        → '무기 휘두르는 순간' 소리 (애니메이션 상태 이름 조건 포함)
///  · OnAttackEndEvent  → '공격 동작 끝' 소리
///  · PlaySound(이름)   → 그 이름의 소리
///  · '실제 적중' 소리는 게임에선 피해가 들어갈 때 나므로, '적중했다고 가정'을 켜면 OnHitEvent 순간에 같이 낸다.
///  · 지연(초)은 그대로 반영한다.
///
/// 에셋은 아무것도 바꾸지 않는다. 씬 오브젝트는 '씬에서도 움직이기'로 씬 뷰에 자세를 미리 보여줄 수 있다
/// (유니티 Animation 창 미리보기와 같은 방식이라 끄면 원래대로 돌아간다).
/// </summary>
public class SoundAnimPreview : IDisposable
{
    private sealed class ClipInfo
    {
        public AnimationClip clip;
        public Animator animator;
        public string label;
        public readonly List<string> states = new List<string>();      // 표시용 상태 이름
        public readonly HashSet<string> stateKeys = new HashSet<string>(StringComparer.Ordinal); // 이름 + 전체 경로
        public bool statesKnown;
        public ObjectReferenceKeyframe[] frames;
        public AnimationEvent[] events;
    }

    private sealed class Cue
    {
        public float time;        // 소리가 실제로 나는 시각 (이벤트 시각 + 지연)
        public float eventTime;
        public string eventLabel;
        public SoundEntry entry;  // null 이면 소리 없는 이벤트
        public string note;       // 안 나는 이유 / 참고
        public bool muted;
        public bool assumed;      // '적중했다고 가정'으로 넣은 소리
        public float length;      // 대략 길이 (막대 그리기용)
        public int row;
    }

    // ── 상태 ─────────────────────────────────────────────────────────
    private GameObject _root;
    private EntitySoundSet _set;
    private readonly List<ClipInfo> _clips = new List<ClipInfo>();
    private string[] _labels = new string[0];
    private int _clipIndex;
    private readonly List<Cue> _cues = new List<Cue>();
    private readonly List<SoundEntry> _rows = new List<SoundEntry>();
    private float _time;
    private bool _playing;
    private double _lastTick;
    private bool _loop = true;
    private int _speed = 2;
    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f };
    private static readonly string[] SpeedLabels = { "0.25배", "0.5배", "1배" };
    private bool _assumeHit = true, _spawnAtStart, _sceneSample;
    private readonly Dictionary<Cue, double> _flash = new Dictionary<Cue, double>();
    private readonly List<AudioSource> _pool = new List<AudioSource>();
    private AnimationModeDriver _driver;
    

    public float PreviewVolume = 1f;
    public bool SimplePreview;
    public Action<string> Log;

    /// <summary>창이 계속 다시 그려야 하는지 (재생 중이거나 번쩍임이 남아 있음).</summary>
    public bool NeedsRepaint => _playing || _flash.Count > 0;

    public SoundAnimPreview()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += StopSceneSample;
    }

    public void Dispose()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= StopSceneSample;
        Stop();
        StopSceneSample();
        foreach (var s in _pool) if (s != null) Object.DestroyImmediate(s.gameObject);
        _pool.Clear();
        if (_driver != null) Object.DestroyImmediate(_driver);
    }

    private void OnPlayModeChanged(PlayModeStateChange _) { Stop(); StopSceneSample(); _sceneSample = false; }

    private ClipInfo Current => _clipIndex >= 0 && _clipIndex < _clips.Count ? _clips[_clipIndex] : null;

    // ── 대상 ─────────────────────────────────────────────────────────
    public void SetTarget(EntitySoundSet set)
    {
        var root = set != null ? set.gameObject : null;
        if (root == _root && set == _set) return;
        string keep = Current != null ? Current.clip.name : null;
        Stop();
        StopSceneSample();
        _sceneSample = false;
        _root = root;
        _set = set;
        RebuildClips();
        int idx = keep != null ? _clips.FindIndex(c => c.clip.name == keep) : -1;
        _clipIndex = idx >= 0 ? idx : 0;
        _time = 0f;
    }

    private void RebuildClips()
    {
        _clips.Clear();
        if (_root == null) { _labels = new string[0]; return; }
        var animators = _root.GetComponentsInChildren<Animator>(true).Where(a => a.runtimeAnimatorController != null).ToList();
        foreach (var animator in animators)
        {
            var rac = animator.runtimeAnimatorController;
            foreach (var clip in rac.animationClips.Where(c => c != null).Distinct())
            {
                var info = new ClipInfo { clip = clip, animator = animator, events = AnimationUtility.GetAnimationEvents(clip) };
                info.statesKnown = CollectStates(rac, clip, info);
                var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                    .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
                    .OrderBy(b => b.path.Length).FirstOrDefault();
                if (binding.type != null) info.frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                string owner = animators.Count > 1 ? animator.name + " · " : "";
                int n = info.events.Count(e => e.functionName == "OnHitEvent" || e.functionName == "OnAttackEndEvent" || e.functionName == "PlaySound");
                info.label = $"{(n > 0 ? "♪ " : "")}{owner}{clip.name}  ({clip.length:0.00}초)";
                _clips.Add(info);
            }
        }
        // 소리 이벤트가 있는 애니메이션(공격·스킬)을 위로
        _clips.Sort((a, b) =>
        {
            int ea = a.events.Length > 0 ? 0 : 1, eb = b.events.Length > 0 ? 0 : 1;
            return ea != eb ? ea.CompareTo(eb) : string.CompareOrdinal(a.label, b.label);
        });
        _labels = _clips.Select(c => c.label.Replace("/", "∕")).ToArray(); // '/' 는 팝업에서 하위 메뉴가 된다
    }

    /// <summary>이 클립을 쓰는 Animator 상태 이름들. 오버라이드 컨트롤러도 원본 상태로 거슬러 찾는다.</summary>
    private static bool CollectStates(RuntimeAnimatorController rac, AnimationClip clip, ClipInfo info)
    {
        var map = new Dictionary<AnimationClip, AnimationClip>();
        int guard = 0;
        while (rac is AnimatorOverrideController oc && guard++ < 8)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pairs);
            foreach (var p in pairs)
                if (p.Key != null && p.Value != null && !map.ContainsKey(p.Key)) map[p.Key] = p.Value;
            rac = oc.runtimeAnimatorController;
        }
        var ac = rac as AnimatorController;
        if (ac == null) return false;
        foreach (var layer in ac.layers) Walk(layer.stateMachine, layer.name, clip, map, info);
        return true;
    }

    private static void Walk(AnimatorStateMachine sm, string path, AnimationClip clip, Dictionary<AnimationClip, AnimationClip> map, ClipInfo info)
    {
        if (sm == null) return;
        foreach (var cs in sm.states)
        {
            if (cs.state == null || !Uses(cs.state.motion, clip, map)) continue;
            if (!info.states.Contains(cs.state.name)) info.states.Add(cs.state.name);
            info.stateKeys.Add(cs.state.name);
            info.stateKeys.Add(path + "." + cs.state.name);
        }
        foreach (var sub in sm.stateMachines) Walk(sub.stateMachine, path + "." + sub.stateMachine.name, clip, map, info);
    }

    private static bool Uses(Motion motion, AnimationClip clip, Dictionary<AnimationClip, AnimationClip> map)
    {
        if (motion is AnimationClip c) return (map.TryGetValue(c, out var o) ? o : c) == clip;
        if (motion is BlendTree bt) return bt.children.Any(ch => Uses(ch.motion, clip, map));
        return false;
    }

    // ── 이벤트 → 소리 (게임 규칙과 같게) ─────────────────────────────
    private void BuildCues()
    {
        _cues.Clear();
        _rows.Clear();
        var info = Current;
        if (_set == null || info == null) return;

        bool isSkill = info.states.Append(info.clip.name).Any(s => s.IndexOf("skill", StringComparison.OrdinalIgnoreCase) >= 0);
        foreach (var ev in info.events.OrderBy(e => e.time))
        {
            switch (ev.functionName)
            {
                case "OnHitEvent":
                    AddTriggered(ev, SoundTrigger.OnAttackHit, "공격 타격 (OnHitEvent)", info);
                    if (_assumeHit) AddAssumed(ev, isSkill ? SoundTrigger.OnSkillDamageDealt : SoundTrigger.OnBasicDamageDealt, isSkill);
                    break;
                case "OnAttackEndEvent":
                    AddTriggered(ev, SoundTrigger.OnAttackEnd, "공격 끝 (OnAttackEndEvent)", info);
                    break;
                case "PlaySound":
                    AddNamed(ev);
                    break;
                default:
                    _cues.Add(new Cue
                    {
                        time = ev.time, eventTime = ev.time,
                        eventLabel = string.IsNullOrEmpty(ev.functionName) ? "(이름 없는 이벤트)" : ev.functionName,
                        note = "소리와 관계없는 이벤트", muted = true,
                    });
                    break;
            }
        }
        if (_spawnAtStart)
            foreach (var e in _set.sounds.Where(s => s != null && s.trigger == SoundTrigger.OnSpawn))
                AddEntry(0f, "나타날 때 (시작)", e, null, false);

        foreach (var c in _cues.Where(c => c.entry != null))
        {
            int row = _rows.IndexOf(c.entry);
            if (row < 0) { _rows.Add(c.entry); row = _rows.Count - 1; }
            c.row = row;
        }
    }

    private void AddTriggered(AnimationEvent ev, SoundTrigger trigger, string label, ClipInfo info)
    {
        var list = _set.sounds.Where(s => s != null && s.trigger == trigger).ToList();
        if (list.Count == 0)
        {
            _cues.Add(new Cue { time = ev.time, eventTime = ev.time, eventLabel = label, muted = true,
                note = trigger == SoundTrigger.OnAttackHit ? "이 순간에 나는 소리가 없습니다 (+ 소리 추가 ▼ > 공격)" : "이 순간에 나는 소리가 없습니다 (+ 소리 추가 ▼ > 공격 끝)" });
            return;
        }
        foreach (var e in list)
        {
            string why = null;
            if (!string.IsNullOrEmpty(e.animationState))
            {
                if (!info.statesKnown) why = null; // 상태를 알 수 없으면 일단 재생(아래 참고 문구)
                else if (!info.stateKeys.Contains(e.animationState))
                    why = $"'{e.animationState}' 상태에서만 납니다 — 이 애니메이션의 상태: {(info.states.Count > 0 ? string.Join(", ", info.states) : "없음")}";
            }
            var cue = AddEntry(ev.time, label, e, why, false);
            if (why == null && !string.IsNullOrEmpty(e.animationState) && !info.statesKnown)
                cue.note = AppendNote(cue.note, "상태 이름을 확인할 수 없어 일단 재생합니다");
        }
    }

    private void AddAssumed(AnimationEvent ev, SoundTrigger trigger, bool isSkill)
    {
        foreach (var e in _set.sounds.Where(s => s != null && s.trigger == trigger))
            AddEntry(ev.time, isSkill ? "스킬 실제 적중 (가정)" : "평타 실제 적중 (가정)", e, null, true);
    }

    private void AddNamed(AnimationEvent ev)
    {
        string name = ev.stringParameter ?? "";
        string label = $"PlaySound(\"{name}\")";
        var list = _set.sounds.Where(s => s != null && string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0)
        {
            _cues.Add(new Cue { time = ev.time, eventTime = ev.time, eventLabel = label, muted = true, note = $"'{name}' 이름의 소리가 없습니다" });
            return;
        }
        foreach (var e in list) AddEntry(ev.time, label, e, null, false);
    }

    private Cue AddEntry(float eventTime, string label, SoundEntry e, string muteReason, bool assumed)
    {
        string why = muteReason;
        if (why == null && !_set.listenAnimationEvents && label != "나타날 때 (시작)" && !assumed)
            why = "사운드 세트의 '애니메이션 이벤트 받기'가 꺼져 있습니다";
        if (why == null && !e.enabled) why = "'소리 켜기'가 꺼져 있습니다";
        if (why == null && !e.HasClip) why = "소리 파일이 비어 있습니다";
        float maxLen = e.clips == null ? 0f : e.clips.Where(c => c != null).Select(c => c.length).DefaultIfEmpty(0f).Max();
        float pitch = Mathf.Max(0.1f, (e.pitchMin + e.pitchMax) * 0.5f);
        var cue = new Cue
        {
            eventTime = eventTime, time = eventTime + Mathf.Max(0f, e.delay), eventLabel = label, entry = e,
            muted = why != null, note = why, assumed = assumed, length = maxLen / pitch,
        };
        if (e.delay > 0f) cue.note = AppendNote(cue.note, $"지연 {e.delay:0.##}초");
        _cues.Add(cue);
        return cue;
    }

    private static string AppendNote(string a, string b) => string.IsNullOrEmpty(a) ? b : a + " · " + b;

    // ── 재생 ─────────────────────────────────────────────────────────
    private float PlayEnd
    {
        get
        {
            var info = Current;
            float len = info != null ? Mathf.Max(info.clip.length, 0.05f) : 1f;
            foreach (var c in _cues) if (c.entry != null && !c.muted) len = Mathf.Max(len, c.time + 0.02f);
            return len;
        }
    }

    public void Play()
    {
        if (Current == null) return;
        BuildCues();
        if (_time >= PlayEnd - 0.001f) _time = 0f;
        _playing = true;
        _lastTick = EditorApplication.timeSinceStartup;
        FireBetween(_time, _time, true);
        SampleScene();
    }

    public void Pause() => _playing = false;

    public void Stop()
    {
        _playing = false;
        foreach (var s in _pool) if (s != null) s.Stop();
    }

    /// <summary>창의 Update 에서 매 프레임 부른다.</summary>
    public void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (_flash.Count > 0)
            foreach (var k in _flash.Where(p => now - p.Value > 0.25).Select(p => p.Key).ToList()) _flash.Remove(k);
        if (!_playing) return;
        float dt = Mathf.Min((float)(now - _lastTick), 0.1f); // 에디터가 잠깐 멈춰도 한꺼번에 몰아 재생하지 않게
        _lastTick = now;
        if (dt <= 0f) return;
        BuildCues();
        float end = PlayEnd;
        float to = _time + dt * Speeds[_speed];
        if (to >= end)
        {
            FireBetween(_time, end, false);
            if (_loop)
            {
                _time = Mathf.Repeat(to - end, end);
                FireBetween(0f, _time, true);
            }
            else { _time = end; _playing = false; }
        }
        else
        {
            FireBetween(_time, to, false);
            _time = to;
        }
        SampleScene();
    }

    private void FireBetween(float from, float to, bool inclusive)
    {
        foreach (var c in _cues)
        {
            if (c.entry == null || c.muted) continue;
            if ((inclusive ? c.time >= from : c.time > from) && c.time <= to) Fire(c);
        }
    }

    private void Fire(Cue c)
    {
        var clip = c.entry.PickClip();
        if (clip == null) return;
        float volume = c.entry.volume * (1f + UnityEngine.Random.Range(-c.entry.volumeVariation, c.entry.volumeVariation)) * PreviewVolume;
        PlayAudio(clip, volume, c.entry.PickPitch());
        _flash[c] = EditorApplication.timeSinceStartup;
        Log?.Invoke($"애니 미리보기 {c.time:0.00}초  ·  {c.entry.name}  ·  {clip.name}");
    }

    private void PlayAudio(AudioClip clip, float volume, float pitch)
    {
        if (SimplePreview) { CallAudioUtil("PlayPreviewClip", clip, 0, false); return; }
        _pool.RemoveAll(s => s == null);
        var src = _pool.FirstOrDefault(s => !s.isPlaying);
        if (src == null && _pool.Count < 8)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags("SoundAnimPreview", HideFlags.HideAndDontSave, typeof(AudioSource));
            src = go.GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            _pool.Add(src);
        }
        if (src == null) { src = _pool[0]; _pool.RemoveAt(0); _pool.Add(src); } // 가장 오래된 소리를 끊고 재사용
        src.Stop();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.Play();
    }

    private static MethodInfo[] _audioUtil;
    private static void CallAudioUtil(string method, params object[] args)
    {
        if (_audioUtil == null)
        {
            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            _audioUtil = type != null ? type.GetMethods(BindingFlags.Static | BindingFlags.Public) : new MethodInfo[0];
        }
        var m = _audioUtil.FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        try { m?.Invoke(null, args); } catch { /* 유니티 버전에 따라 없을 수 있음 */ }
    }

    // ── 씬 뷰 자세 미리보기 (씬 오브젝트만) ───────────────────────────
    private bool CanSceneSample => _root != null && !EditorUtility.IsPersistent(_root) && !Application.isPlaying && Current != null && Current.animator != null;
    private bool OtherAnimationPreview => AnimationMode.InAnimationMode() && (_driver == null || !AnimationMode.InAnimationMode(_driver));

    private void SampleScene()
    {
        if (!_sceneSample || !CanSceneSample || OtherAnimationPreview) { StopSceneSample(); return; }
        if (_driver == null)
        {
            _driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
            _driver.name = "SoundAnimPreview";
            _driver.hideFlags = HideFlags.HideAndDontSave;
        }
        if (!AnimationMode.InAnimationMode(_driver)) AnimationMode.StartAnimationMode(_driver);
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(Current.animator.gameObject, Current.clip, Mathf.Min(_time, Current.clip.length));
        AnimationMode.EndSampling();
        SceneView.RepaintAll();
    }

    public void StopSceneSample()
    {
        if (_driver != null && AnimationMode.InAnimationMode(_driver))
        {
            AnimationMode.StopAnimationMode(_driver);
            SceneView.RepaintAll();
        }
    }

    // ── 그리기 ───────────────────────────────────────────────────────
    public void Draw()
    {
        if (_set == null) return;
        if (_clips.Count == 0)
        {
            EditorGUILayout.LabelField("Animator 가 없는 대상입니다.", EditorStyles.miniLabel);
            return;
        }
        // Animation 창에서 이벤트를 옮기거나 추가하면 바로 보이도록 매번 다시 읽는다.
        if (Current != null) Current.events = AnimationUtility.GetAnimationEvents(Current.clip);
        BuildCues();

        DrawControls();
        var info = Current;
        using (new EditorGUILayout.HorizontalScope())
        {
            var pic = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
            DrawPicture(pic, info);
            GUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope())
                DrawTimeline(info);
        }
        DrawStatusLine();
    }

    

    /// <summary>한 줄: ▶ ⏮ [애니메이션] 시간 [속도] ⚙</summary>
    private void DrawControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent(_playing ? "❚❚" : "▶", _playing ? "멈춤" : "재생"), GUILayout.Width(28))) { if (_playing) Pause(); else Play(); }
            if (GUILayout.Button(new GUIContent("⏮", "처음으로"), GUILayout.Width(28))) { Stop(); _time = 0f; SampleScene(); }

            EditorGUI.BeginChangeCheck();
            _clipIndex = EditorGUILayout.Popup(Mathf.Clamp(_clipIndex, 0, _clips.Count - 1), _labels, GUILayout.MinWidth(80));
            if (EditorGUI.EndChangeCheck()) { Stop(); _time = 0f; BuildCues(); SampleScene(); }

            GUILayout.Label($"{_time:0.00}초", EditorStyles.miniLabel, GUILayout.Width(40));
            _speed = EditorGUILayout.Popup(_speed, SpeedLabels, GUILayout.Width(54));
            if (GUILayout.Button(new GUIContent("⚙", "옵션"), GUILayout.Width(26))) ShowOptions();
        }
    }

    private void ShowOptions()
    {
        var info = Current;
        var menu = new GenericMenu();
        if (info != null)
        {
            string states = !info.statesKnown ? "확인할 수 없음" : info.states.Count > 0 ? string.Join(", ", info.states) : "없음";
            menu.AddDisabledItem(new GUIContent("Animator 상태: " + states.Replace("/", "∕")));
            menu.AddSeparator("");
        }
        menu.AddItem(new GUIContent("반복 재생"), _loop, () => _loop = !_loop);
        if (_set != null && _set.sounds.Any(s => s != null && (s.trigger == SoundTrigger.OnBasicDamageDealt || s.trigger == SoundTrigger.OnSkillDamageDealt)))
            menu.AddItem(new GUIContent("적중했다고 가정 (실제 적중 소리도 재생)"), _assumeHit, () => _assumeHit = !_assumeHit);
        if (_set != null && _set.sounds.Any(s => s != null && s.trigger == SoundTrigger.OnSpawn))
            menu.AddItem(new GUIContent("등장 소리 포함"), _spawnAtStart, () => _spawnAtStart = !_spawnAtStart);
        if (CanSceneSample)
        {
            if (OtherAnimationPreview) menu.AddDisabledItem(new GUIContent("씬에서도 움직이기 (Animation 창 미리보기 중)"));
            else menu.AddItem(new GUIContent("씬에서도 움직이기"), _sceneSample, () =>
            {
                _sceneSample = !_sceneSample;
                if (_sceneSample) SampleScene(); else StopSceneSample();
            });
        }
        menu.ShowAsContext();
    }

    private void DrawPicture(Rect r, ClipInfo info)
    {
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
        Sprite sprite = null;
        if (info.frames != null)
            foreach (var k in info.frames) { if (k.time <= _time + 0.0001f) sprite = k.value as Sprite; else break; }
        if (sprite == null && info.frames != null && info.frames.Length > 0) sprite = info.frames[0].value as Sprite;
        if (sprite == null)
        {
            GUI.Label(r, info.frames == null ? "그림 프레임\n없음" : "", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        try
        {
            var t = sprite.texture;
            var tr = sprite.textureRect;
            var uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
            float aspect = tr.height > 0 ? tr.width / tr.height : 1f;
            var fit = r;
            if (aspect > 1f) { fit.height = r.width / aspect; fit.y += (r.height - fit.height) * 0.5f; }
            else { fit.width = r.height * aspect; fit.x += (r.width - fit.width) * 0.5f; }
            GUI.DrawTextureWithTexCoords(fit, t, uv);
        }
        catch (Exception)
        {
            var preview = AssetPreview.GetAssetPreview(sprite);
            if (preview != null) GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
        }
    }

    private void DrawTimeline(ClipInfo info)
    {
        const float ruler = 12f, eventsRow = 10f, rowH = 14f;
        int rows = Mathf.Max(1, _rows.Count);
        float height = Mathf.Max(64f, ruler + eventsRow + rows * rowH + 4f);
        var r = GUILayoutUtility.GetRect(120, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));

        float clipLen = Mathf.Max(info.clip.length, 0.05f);
        float view = clipLen;
        foreach (var c in _cues) if (c.entry != null) view = Mathf.Max(view, c.time + Mathf.Min(c.length, clipLen * 2f));
        view = Mathf.Min(view, clipLen * 3f);
        float X(float t) => r.x + Mathf.Clamp01(t / view) * r.width;

        bool dark = EditorGUIUtility.isProSkin;
        EditorGUI.DrawRect(r, dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.82f, 0.82f, 0.82f));
        if (view > clipLen) // 애니메이션이 끝난 뒤 이어지는 소리 꼬리
            EditorGUI.DrawRect(Rect.MinMaxRect(X(clipLen), r.y, r.xMax, r.yMax), new Color(0f, 0f, 0f, 0.22f));

        // 눈금 (숫자는 큰 눈금에만)
        float step = view > 3f ? 0.5f : view > 1.2f ? 0.25f : 0.1f;
        var tick = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        for (int i = 0; i * step <= view + 0.0001f; i++)
        {
            float t = i * step;
            bool major = i % 2 == 0;
            EditorGUI.DrawRect(new Rect(X(t), r.y + (major ? 0 : 5), 1, major ? ruler : ruler - 5), tick);
            if (major && i > 0) GUI.Label(new Rect(X(t) + 2, r.y - 2, 36, 14), t.ToString("0.##"), EditorStyles.miniLabel);
        }

        // 소리 이벤트만 (노란 표시). 소리와 관계없는 이벤트는 그리지 않는다.
        float y = r.y + ruler;
        var yellow = new Color(1f, 0.8f, 0.2f);
        foreach (var ev in info.events.Where(IsSoundEvent))
        {
            EditorGUI.DrawRect(new Rect(X(ev.time), y, 1, r.yMax - y), new Color(1f, 0.8f, 0.2f, 0.3f));
            var mark = new Rect(X(ev.time) - 3, y + 1, 7, 7);
            EditorGUI.DrawRect(mark, yellow);
            string name = ev.functionName == "PlaySound" ? $"PlaySound(\"{ev.stringParameter}\")" : ev.functionName;
            GUI.Label(new Rect(mark.x - 3, mark.y - 3, 13, 13), new GUIContent("", $"{ev.time:0.00}초  {name}"));
        }

        // 소리 막대: 초록 = 남, 주황 = 적중 가정, 회색 = 안 남. 이름은 막대 위에 한 번만.
        y += eventsRow;
        double now = EditorApplication.timeSinceStartup;
        var labeled = new HashSet<int>();
        foreach (var c in _cues.Where(c => c.entry != null).OrderBy(c => c.time))
        {
            var bar = Rect.MinMaxRect(X(c.time), y + c.row * rowH + 1, Mathf.Max(X(c.time) + 3, X(c.time + c.length)), y + c.row * rowH + rowH - 1);
            Color col = c.muted ? new Color(0.5f, 0.5f, 0.5f, 0.4f) : c.assumed ? new Color(1f, 0.55f, 0.2f, 0.75f) : new Color(0.3f, 0.8f, 0.45f, 0.75f);
            if (_flash.TryGetValue(c, out double at)) col = Color.Lerp(Color.white, col, Mathf.Clamp01((float)(now - at) / 0.25f));
            EditorGUI.DrawRect(bar, col);
            if (labeled.Add(c.row))
                GUI.Label(new Rect(bar.x + 2, bar.y - 1, r.xMax - bar.x - 2, rowH), c.entry.name, EditorStyles.miniLabel);
            GUI.Label(bar, new GUIContent("", $"{c.time:0.00}초  {c.entry.name}" + (c.note != null ? "\n" + c.note : "")));
        }

        // 재생 위치
        EditorGUI.DrawRect(new Rect(X(_time) - 1, r.y, 2, r.height), new Color(1f, 0.25f, 0.25f));

        // 눌러서 / 끌어서 시간 옮기기
        int id = GUIUtility.GetControlID(FocusType.Passive, r);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown when r.Contains(e.mousePosition) && e.button == 0:
                GUIUtility.hotControl = id;
                Scrub(r, view, e.mousePosition.x); e.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                Scrub(r, view, e.mousePosition.x); e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0; e.Use();
                break;
        }
        EditorGUIUtility.AddCursorRect(r, MouseCursor.SlideArrow);
    }

    private void Scrub(Rect r, float view, float mouseX)
    {
        _time = Mathf.Clamp((mouseX - r.x) / r.width * view, 0f, PlayEnd);
        _lastTick = EditorApplication.timeSinceStartup; // 끄는 동안 지나간 이벤트는 재생하지 않는다
        SampleScene();
    }

    /// <summary>문제가 있을 때만 한 줄. 자세한 이유는 막대·이벤트에 마우스를 올리면 나온다.</summary>
    private void DrawStatusLine()
    {
        var info = Current;
        if (info == null) return;
        if (!info.events.Any(IsSoundEvent))
        {
            EditorGUILayout.LabelField("소리 이벤트가 없는 애니메이션입니다.", EditorStyles.miniLabel);
            return;
        }
        int muted = _cues.Count(c => c.entry != null && c.muted);
        int silent = _cues.Count(c => c.entry == null && c.muted && IsSoundEventLabel(c.eventLabel));
        if (muted == 0 && silent == 0) return;
        var parts = new List<string>();
        if (muted > 0) parts.Add($"안 나는 소리 {muted}개");
        if (silent > 0) parts.Add($"소리 없는 이벤트 {silent}개");
        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.85f, 0.6f, 0.2f) } };
        EditorGUILayout.LabelField(new GUIContent("⚠ " + string.Join(" · ", parts) + "  (마우스를 올리면 이유)",
            string.Join("\n", _cues.Where(c => c.muted && c.note != null && (c.entry != null || IsSoundEventLabel(c.eventLabel)))
                .Select(c => $"{c.time:0.00}초 {(c.entry != null ? c.entry.name : c.eventLabel)}: {c.note}").Distinct())), style);
    }

    private static bool IsSoundEvent(AnimationEvent e) => e.functionName == "OnHitEvent" || e.functionName == "OnAttackEndEvent" || e.functionName == "PlaySound";
    private static bool IsSoundEventLabel(string label) => label != null && (label.Contains("OnHitEvent") || label.Contains("OnAttackEndEvent") || label.StartsWith("PlaySound"));
}
