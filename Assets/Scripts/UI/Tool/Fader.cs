using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아무 오브젝트에나 붙이면 '붙은 오브젝트'를 페이드시킨다. 고를 게 없다 — 붙인 데가 곧 대상이다.
///
///   UI에 붙이면        → 그 UI가 스르륵 사라짐/나타남
///   스프라이트에 붙이면 → 그 스프라이트가 사라짐/나타남
///   빈 오브젝트에 붙이면 → 화면 전체가 깜깜해짐/밝아짐
///
/// 어느 경우든 뜻은 똑같다: 사라지기 = 보이던 게 안 보이게 된다, 나타나기 = 다시 보인다.
/// (화면 전체는 검은 커튼이 반대로 움직이지만 그 반전은 이 컴포넌트가 숨긴다.)
///
/// 연출의 양(시간/유지/색/커브)은 반응 목록의 각 줄에 있다. 같은 화면 페이드라도
/// 문 이동은 0.2초 검정, 층 이동은 2초 흰색처럼 신호마다 다르게 줄 수 있다.
/// UIShaker/CameraShaker와 똑같은 모양이다.
/// </summary>
[DisallowMultipleComponent]
public class Fader : MonoBehaviour
{
    public enum SignalAction
    {
        사라졌다나타나기,
        사라지기,
        나타나기,
    }

    [System.Serializable]
    public class Reaction
    {
        [Tooltip("이 줄이 어느 페이드에 대한 설정인지.\n\n" +
                 "[UI·스프라이트에 붙인 Fader] 이 신호가 오면 아래 설정대로 알아서 페이드한다. " +
                 "예: 신호=방이동으로 두면 문을 지날 때 이 UI도 같이 사라진다.\n\n" +
                 "[화면 커튼(ScreenFadeCanvas)] 커튼은 신호를 듣지 않는다. 문·포탈·FadeAction이 직접 부르면서 " +
                 "이름으로 이 줄을 찾아 아래 수치만 쓴다.\n\n" +
                 "'없음' = 버튼이나 코드가 인자 없이 FadeOut()/FadeIn()을 부를 때 쓰는 줄.")]
        public FadeSignal signal = FadeSignal.방이동;

        [Tooltip("이 신호를 받았을 때 뭘 할지. (커튼은 항상 사라졌다나타나기로 직접 불린다)")]
        public SignalAction action = SignalAction.사라졌다나타나기;

        [Tooltip("사라지거나 나타나는 데 걸리는 시간(초).")]
        public float duration = 0.3f;

        [Tooltip("'사라졌다나타나기'에서 완전히 사라진 채로 머무는 시간(초). 이 사이에 On Blackout이 불린다.")]
        public float holdDuration = 0.1f;

        [Tooltip("빨라졌다 느려지는 모양. 기본은 부드러운 EaseInOut.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("화면 전체를 덮을 때의 커튼 색. 흰 페이드/붉은 페이드용. (UI·스프라이트를 페이드할 땐 안 쓰임)")]
        public Color color = Color.black;
    }

    [Tooltip("페이드 종류별 수치. 필요한 만큼 줄을 추가하면 된다.\n" +
             "코드나 FadeAction이 종류 이름으로 줄을 찾아 그 수치로 페이드한다 — 그래서 같은 커튼이어도 " +
             "문 이동은 0.2초 검정, 층 이동은 2초 흰색으로 다르게 줄 수 있다.\n" +
             "버튼/코드가 인자 없이 부르면 '없음' 줄을 쓴다(없으면 맨 윗줄).")]
    [SerializeField] private List<Reaction> reactions = new List<Reaction> { new Reaction() };

    [Header("UI 열고 닫기용 (화면 전체를 덮는 경우엔 둘 다 안 쓰임)")]

    [Tooltip("[켤 때] UI가 '열릴 때' 스르륵 나타나게 하고 싶을 때.\n\n" +
             "이 오브젝트가 SetActive(true) 되는 순간 투명에서 시작해 알아서 나타난다. " +
             "UI를 여는 기존 코드나 버튼 배선을 하나도 안 건드려도 등장 연출이 붙는다.\n\n" +
             "[끌 때] 처음부터 보이는 채로 시작한다. 나타내려면 FadeIn()을 직접 불러야 한다.\n\n" +
             "※ 화면 전체 페이드(빈 오브젝트에 붙인 경우)엔 켜지 말 것 — 게임 시작하자마자 암전이 걷히는 연출이 된다.")]
    [SerializeField] private bool fadeInOnEnable = false;

    [Tooltip("[켤 때] UI가 '닫힐 때' 스르륵 사라지게 하고 싶을 때.\n\n" +
             "사라지기가 끝나면 자기를 SetActive(false) 한다. 이게 없으면 UI가 투명해질 뿐 계속 살아있어서 " +
             "안 보이는 채로 화면에 남는다.\n\n" +
             "[짝으로 해야 할 일] 닫기 버튼 OnClick에 걸린 'SetActive(false)' 배선을 지우고 이 컴포넌트의 " +
             "FadeOut()으로 바꿔라. SetActive(false)를 그대로 두면 즉시 꺼져버려서 페이드가 아예 안 보인다.")]
    [SerializeField] private bool deactivateOnFadeOutComplete = false;

    private enum Mode { UI, Sprite, FullScreen }

    private static readonly Reaction Fallback = new Reaction();

    private Mode _mode;
    private bool _resolved;
    private CanvasGroup _group;
    private SpriteRenderer[] _sprites;
    private Coroutine _running;

    // 보이는 정도. 1 = 보임, 0 = 안 보임. 모드에 상관없이 이 뜻으로 통일한다.
    private float _visible = 1f;

    /// <summary>화면 전체 페이드는 세상에 하나뿐이므로 Fader도 하나만 두고 모두가 공유한다.
    /// 검은 커튼(씬이 바뀌어도 안 죽음) 위에 얹혀 있어서, 이걸 부른 오브젝트가 도중에 파괴돼도 페이드가 끝까지 돈다.
    /// 양은 ScreenFadeCanvas 프리팹의 Fader 반응 목록에서 조절한다.
    ///
    /// 이 씬에 ScreenFadeCanvas를 안 넣어뒀으면 null. 부르는 쪽은 반드시 null이어도 '할 일'은 하도록 짜야 한다
    /// (연출만 생략, 이동/로드는 정상 수행 — 검은 화면에 갇히는 것보다 낫다).</summary>
    public static Fader FullScreenFader
    {
        get
        {
            var ctrl = ScreenCurtain.Instance;
            if (ctrl == null)
            {
                if (!_warnedNoCurtain && Application.isPlaying)
                {
                    _warnedNoCurtain = true;
                    Debug.LogWarning("[Fader] 이 씬에 ScreenFadeCanvas가 없어서 화면 페이드가 생략된다. " +
                                     "Assets/Prefabs/UI/ScreenFadeCanvas 프리팹을 씬에 하나 넣어라.");
                }
                return null;
            }

            var f = ctrl.GetComponent<Fader>();
            if (f == null) f = ctrl.gameObject.AddComponent<Fader>(); // 커튼 위에 붙으면 알아서 FullScreen으로 잡힌다
            return f;
        }
    }

    private static bool _warnedNoCurtain;

    private void Awake()
    {
        EnsureResolved();
        // 화면 커튼에 fadeInOnEnable를 켜면 게임 시작하자마자 암전이 걷히는 연출이 된다. UI 전용으로 막는다.
        if (fadeInOnEnable && _mode != Mode.FullScreen) SetVisibleInstant(false, Row(FadeSignal.없음));
    }

    /// <summary>뭘 페이드시킬지 첫 사용 시점에 한 번만 결정한다.
    /// (Awake에서 하면 코드로 AddComponent할 때 컴포넌트가 채 붙기도 전에 굳어버린다.)</summary>
    private void EnsureResolved()
    {
        if (_resolved) return;
        _resolved = true;

        _mode = ResolveMode();

        if (_mode == Mode.UI)
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        }
        else if (_mode == Mode.Sprite)
        {
            _sprites = GetComponentsInChildren<SpriteRenderer>(true);
        }
    }

    /// <summary>붙은 오브젝트를 보고 뭘 페이드시킬지 결정. 기획자가 고를 일이 없다.</summary>
    private Mode ResolveMode()
    {
        // 검은 커튼 본인이면 화면 전체. (커튼에도 CanvasGroup이 있어서 UI로 오판하는 걸 먼저 걸러낸다)
        if (GetComponent<ScreenCurtain>() != null) return Mode.FullScreen;

        if (GetComponent<CanvasGroup>() != null || GetComponentInChildren<Graphic>(true) != null) return Mode.UI;
        if (GetComponentInChildren<SpriteRenderer>(true) != null) return Mode.Sprite;

        return Mode.FullScreen; // 아무것도 안 붙은 빈 오브젝트 = 화면 전체
    }

    private void OnEnable()
    {
        // 화면 커튼(FullScreen)은 신호를 듣지 않는다. 커튼은 세상에 하나뿐인 공유 자원이라
        // '깜깜해진 순간'을 돌려받아야 하는 쪽(문/포탈/FadeAction)이 직접 부르고,
        // 그 커튼이 아래 FadeOutIn에서 "지금 방이동 페이드 중"이라고 방송해준다.
        // 여기서 같이 들으면 직접 호출 + 신호로 두 번 페이드된다.
        if (_mode != Mode.FullScreen) Signal.Listen<FadeSignal>(OnSignal);

        if (fadeInOnEnable && _mode != Mode.FullScreen) FadeIn();
    }

    private void OnDisable()
    {
        if (_mode != Mode.FullScreen) Signal.Unlisten<FadeSignal>(OnSignal);

        // 도중에 꺼지면 코루틴이 죽으면서 반투명인 채로 굳는다. 목표 상태로 확정해두고 끝낸다.
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
            if (_mode == Mode.FullScreen) SetVisibleInstant(true, Row(FadeSignal.없음)); // 암전인 채 방치 금지
        }
    }

    /// <summary>화면이 아닌 것들(UI·스프라이트)이 "방이동 중이다" 같은 순간에 같이 반응하는 경로.
    /// 목록에 그 신호 줄이 없으면 그냥 무시한다 — 안 적어둔 건 관심 없다는 뜻이다.</summary>
    private void OnSignal(FadeSignal signal)
    {
        if (signal == FadeSignal.없음) return;

        foreach (var r in reactions)
        {
            if (r == null || r.signal != signal) continue;

            switch (r.action)
            {
                case SignalAction.사라지기: Play(FadeRoutine(0f, r)); break;
                case SignalAction.나타나기: Play(FadeRoutine(1f, r)); break;
                default: Play(FadeOutInRoutine(r, null, null)); break;
            }
            return;
        }
    }

    /// <summary>이 종류의 설정 줄을 찾는다. 없으면 맨 윗줄로 폴백하되 반드시 경고한다 —
    /// 조용히 딴 줄 수치로 도는 게 제일 찾기 힘든 사고다(0.1초 암전 사이에 보스가 튀어나오는 식).</summary>
    private Reaction Row(FadeSignal signal)
    {
        foreach (var r in reactions)
        {
            if (r != null && r.signal == signal) return r;
        }

        if (signal != FadeSignal.없음)
        {
            Debug.LogWarning($"[Fader] '{name}'의 반응 목록에 '{signal}' 줄이 없어서 맨 윗줄 수치로 대신한다. " +
                             $"ScreenFadeCanvas의 Fader에 '{signal}' 줄을 추가해라.", this);
        }
        return reactions.Count > 0 && reactions[0] != null ? reactions[0] : Fallback;
    }

    // ─── 진입점 (인자 0개짜리는 UnityEvent 슬롯에 그대로 잡힌다) ───

    /// <summary>보이던 게 사라진다. 맨 윗줄 설정을 쓴다.</summary>
    public void FadeOut() => Play(FadeRoutine(0f, Row(FadeSignal.없음)));

    /// <summary>다시 보인다. 맨 윗줄 설정을 쓴다.</summary>
    public void FadeIn() => Play(FadeRoutine(1f, Row(FadeSignal.없음)));

    /// <summary>사라졌다가 다시 나타난다. 맨 윗줄 설정을 쓴다.</summary>
    public void FadeOutIn() => Play(FadeOutInRoutine(Row(FadeSignal.없음), null, null));

    /// <summary>코드용. 어느 신호의 설정으로 할지 이름만 고르고, 인스펙터에선 드래그할 수 없는 일
    /// (텔레포트처럼 런타임 대상이 필요한 것)을 암전 순간에 끼워넣는다.
    /// 인스펙터의 On Blackout도 똑같이 같이 불린다 — 기획자가 나중에 연출을 더 얹을 수 있게.</summary>
    public void FadeOutIn(FadeSignal signal, System.Action onBlackoutCallback, System.Action onCompleteCallback = null)
    {
        // "지금 이 페이드가 벌어진다"고 방송한다. 커튼은 자기가 안 듣지만(직접 불렸으니),
        // 이 순간에 같이 반응하고 싶은 UI·스프라이트 Fader들이 목록에 그 줄만 적어두면 알아서 따라온다.
        // → 부르는 쪽(문/포탈)은 누가 듣는지 몰라도 되고, 기획자는 코드 없이 연출을 얹을 수 있다.
        Signal.Fire(signal);

        Play(FadeOutInRoutine(Row(signal), onBlackoutCallback, onCompleteCallback));
    }

    private void Play(IEnumerator routine)
    {
        if (!isActiveAndEnabled) return;
        EnsureResolved();
        if (_running != null) StopCoroutine(_running); // 나중 호출이 이긴다. 현재 알파에서 이어감.
        _running = StartCoroutine(routine);
    }

    private IEnumerator FadeOutInRoutine(Reaction r, System.Action onBlackoutCallback, System.Action onCompleteCallback)
    {
        yield return FadeTo(0f, r);

        // 완전히 가려진 순간. '무슨 일이 일어날지'는 부르는 쪽이 들고 넘긴다 — Fader는 어떻게 보일지만 안다.
        // (Fader에 할 일을 꽂아두면 커튼을 모두가 공유하니 딴 페이드 때도 같이 터진다.)
        onBlackoutCallback?.Invoke();

        if (r.holdDuration > 0f)
        {
            float t = 0f;
            while (t < r.holdDuration) { t += Time.unscaledDeltaTime; yield return null; }
        }

        yield return FadeTo(1f, r);
        _running = null;
        onCompleteCallback?.Invoke();
    }

    private IEnumerator FadeRoutine(float goalVisible, Reaction r)
    {
        yield return FadeTo(goalVisible, r);
        _running = null;

        if (goalVisible <= 0f && deactivateOnFadeOutComplete && _mode != Mode.FullScreen) gameObject.SetActive(false);
    }

    /// <summary>순수 보간만. 완료 처리는 밖에서 한다 —
    /// 사라졌다나타나기 한가운데서 'deactivateOnFadeOutComplete'가 오브젝트를 꺼버려 나타나기가 영영 안 오는 사고를 막으려고 분리했다.
    /// 시간은 항상 unscaled: 히트스톱/일시정지 중에도 페이드는 흘러야 하고, 안 그러면 화면이 검은 채로 멈춘다.</summary>
    private IEnumerator FadeTo(float goalVisible, Reaction r)
    {
        CanvasGroup curtain = ResolveCurtain(r);
        SetBlocking(goalVisible, curtain);

        // 화면 전체는 커튼 하나를 여러 Fader가 공유할 수 있으니, 내 기억(_visible)이 아니라
        // 커튼의 '실제' 상태에서 이어가야 한다. 안 그러면 남이 깜깜하게 해둔 화면을 물려받을 때 한 번 번쩍인다.
        float start = (_mode == Mode.FullScreen && curtain != null) ? 1f - curtain.alpha : _visible;
        float t = 0f;
        while (t < r.duration && r.duration > 0f)
        {
            t += Time.unscaledDeltaTime;
            Apply(Mathf.LerpUnclamped(start, goalVisible, r.curve.Evaluate(Mathf.Clamp01(t / r.duration))), curtain);
            yield return null;
        }
        Apply(goalVisible, curtain);
    }

    /// <summary>화면 전체 모드에서 쓸 검은 커튼. 없으면 알아서 생긴다.</summary>
    private CanvasGroup ResolveCurtain(Reaction r)
    {
        EnsureResolved();
        if (_mode != Mode.FullScreen) return null;

        var ctrl = ScreenCurtain.Instance;
        if (ctrl == null) return null;

        ctrl.SetColor(r.color); // 색은 신호마다 다를 수 있어서 매번 맞춰준다
        return ctrl.Group;
    }

    private void SetVisibleInstant(bool visible, Reaction r)
    {
        EnsureResolved();
        CanvasGroup curtain = ResolveCurtain(r);
        SetBlocking(visible ? 1f : 0f, curtain);
        Apply(visible ? 1f : 0f, curtain);
    }

    /// <summary>visible(1=보임)을 실제 알파로 칠한다. 화면 전체 모드에선 커튼이라 반대로 뒤집는다.</summary>
    private void Apply(float visible, CanvasGroup curtain)
    {
        _visible = visible;

        if (_mode == Mode.FullScreen)
        {
            if (curtain != null) curtain.alpha = 1f - visible; // 화면이 사라진다 = 커튼이 나타난다
            return;
        }

        if (_group != null) _group.alpha = visible;

        if (_sprites != null)
        {
            foreach (var sr in _sprites)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = visible;
                sr.color = c;
            }
        }
    }

    /// <summary>사라지는 중인 UI가 클릭되면 버그다. 페이드 시작 시점에 바로 확정한다.</summary>
    private void SetBlocking(float goalVisible, CanvasGroup curtain)
    {
        bool shown = goalVisible > 0.5f;

        if (_mode == Mode.FullScreen)
        {
            if (curtain != null) curtain.blocksRaycasts = !shown; // 암전 중엔 뒤쪽 버튼이 안 눌려야 한다
            return;
        }

        if (_group != null)
        {
            _group.blocksRaycasts = shown;
            _group.interactable = shown;
        }
    }

    [ContextMenu("Test - FadeOutIn")]
    private void TestFadeOutIn() => FadeOutIn();
}
