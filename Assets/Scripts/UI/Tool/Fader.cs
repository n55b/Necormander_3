using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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
        [Tooltip("이 신호가 오면 아래 설정대로 페이드한다. '없음'이면 신호로는 안 불리고, 버튼/코드로 부를 때의 설정으로만 쓰인다.")]
        public FadeSignal signal = FadeSignal.방이동;

        [Tooltip("신호를 받았을 때 뭘 할지.")]
        public SignalAction action = SignalAction.사라졌다나타나기;

        [Tooltip("사라지거나 나타나는 데 걸리는 시간(초).")]
        public float duration = 0.3f;

        [Tooltip("'사라졌다나타나기'에서 완전히 사라진 채로 머무는 시간(초). 이 사이에 On Blackout이 불린다.")]
        public float holdDuration = 0.1f;

        [Tooltip("빨라졌다 느려지는 모양. 기본은 부드러운 EaseInOut.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("화면 전체를 덮을 때의 커튼 색. 흰 페이드/붉은 페이드용. (UI·스프라이트를 페이드할 땐 안 쓰임)")]
        public Color color = Color.black;

        [Tooltip("이 줄의 신호로 페이드했을 때만, 완전히 가려진 순간에 실행된다. = 아무도 못 보는 사이에 일을 처리하는 타이밍.\n\n" +
                 "화면(또는 UI)이 100% 가려진 시점이라 여기서 뭘 바꿔도 바뀌는 티가 안 난다. " +
                 "그래서 '뚝 끊기는 게 보이면 안 되는 일'을 꽂는다:\n" +
                 "  · 보스 등장 오브젝트 SetActive(true)\n" +
                 "  · UI 내용 갈아끼우기 / 씬 로드\n" +
                 "  · (순간이동은 방 이동 코드가 같은 타이밍에 이미 하고 있다)\n\n" +
                 "★ 반드시 '이 줄' 전용이다. 보스등장 줄에 꽂은 건 방이동 때 안 불린다.\n" +
                 "'사라졌다나타나기'면 한가운데서, '사라지기'면 다 사라진 뒤에 불린다. '나타나기'에선 안 불린다.\n" +
                 "얼마나 오래 가려져 있을지는 위의 '유지' 값으로 늘린다.")]
        public UnityEvent onBlackout;
    }

    [Tooltip("무슨 신호에 어떻게 페이드할지. 필요한 만큼 줄을 추가하면 된다. 목록에 없는 신호엔 반응하지 않는다. " +
             "버튼이나 코드가 인자 없이 부르면 맨 윗줄([0]) 설정을 쓴다.")]
    [SerializeField] private List<Reaction> reactions = new List<Reaction> { new Reaction() };

    [Header("UI 열고 닫기용 (화면 전체를 덮는 경우엔 둘 다 안 쓰임)")]

    [Tooltip("[켤 때] UI가 '열릴 때' 스르륵 나타나게 하고 싶을 때.\n\n" +
             "이 오브젝트가 SetActive(true) 되는 순간 투명에서 시작해 알아서 나타난다. " +
             "UI를 여는 기존 코드나 버튼 배선을 하나도 안 건드려도 등장 연출이 붙는다.\n\n" +
             "[끌 때] 처음부터 보이는 채로 시작한다. 나타내려면 FadeIn()을 직접 불러야 한다.\n\n" +
             "※ 화면 전체 페이드(빈 오브젝트에 붙인 경우)엔 켜지 말 것 — 게임 시작하자마자 암전이 걷히는 연출이 된다.")]
    [SerializeField] private bool 켜질때나타나기 = false;

    [Tooltip("[켤 때] UI가 '닫힐 때' 스르륵 사라지게 하고 싶을 때.\n\n" +
             "사라지기가 끝나면 자기를 SetActive(false) 한다. 이게 없으면 UI가 투명해질 뿐 계속 살아있어서 " +
             "안 보이는 채로 화면에 남는다.\n\n" +
             "[짝으로 해야 할 일] 닫기 버튼 OnClick에 걸린 'SetActive(false)' 배선을 지우고 이 컴포넌트의 " +
             "FadeOut()으로 바꿔라. SetActive(false)를 그대로 두면 즉시 꺼져버려서 페이드가 아예 안 보인다.")]
    [SerializeField] private bool 사라지면끄기 = false;

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
    /// 양은 ScreenFadeCanvas 프리팹의 Fader 반응 목록에서 조절한다.</summary>
    public static Fader FullScreenFader
    {
        get
        {
            var ctrl = ScreenFadeController.Instance;
            if (ctrl == null) return null;

            var f = ctrl.GetComponent<Fader>();
            if (f == null) f = ctrl.gameObject.AddComponent<Fader>(); // 커튼 위에 붙으면 알아서 FullScreen으로 잡힌다
            return f;
        }
    }

    private void Awake()
    {
        if (켜질때나타나기) SetVisibleInstant(false, Row(FadeSignal.없음));
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
        if (GetComponent<ScreenFadeController>() != null) return Mode.FullScreen;

        if (GetComponent<CanvasGroup>() != null || GetComponentInChildren<Graphic>(true) != null) return Mode.UI;
        if (GetComponentInChildren<SpriteRenderer>(true) != null) return Mode.Sprite;

        return Mode.FullScreen; // 아무것도 안 붙은 빈 오브젝트 = 화면 전체
    }

    private void OnEnable()
    {
        Signal.Listen<FadeSignal>(OnSignal);
        if (켜질때나타나기) FadeIn();
    }

    private void OnDisable()
    {
        Signal.Unlisten<FadeSignal>(OnSignal);

        // 도중에 꺼지면 코루틴이 죽으면서 반투명인 채로 굳는다. 목표 상태로 확정해두고 끝낸다.
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
            if (_mode == Mode.FullScreen) SetVisibleInstant(true, Row(FadeSignal.없음)); // 암전인 채 방치 금지
        }
    }

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

    /// <summary>이 신호의 설정 줄을 찾는다. 없으면 맨 윗줄, 그것도 없으면 기본값.</summary>
    private Reaction Row(FadeSignal signal)
    {
        foreach (var r in reactions)
        {
            if (r != null && r.signal == signal) return r;
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
    public void FadeOutIn(FadeSignal settingsFrom, System.Action onBlackoutCallback, System.Action onCompleteCallback = null)
    {
        Play(FadeOutInRoutine(Row(settingsFrom), onBlackoutCallback, onCompleteCallback));
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

        // 완전히 사라진 순간. 코드가 넘긴 일(텔레포트)을 먼저, 그 다음 기획자가 '이 줄'에 꽂아둔 연출.
        // 반드시 r.onBlackout이어야 한다 — 컴포넌트에 하나만 두면 커튼을 모두가 공유하니
        // 보스등장에 꽂은 게 방이동 때도 터진다.
        onBlackoutCallback?.Invoke();
        r.onBlackout?.Invoke();

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

        if (goalVisible > 0f) yield break; // 나타나기: 가려진 순간이 없으니 onBlackout도 없다

        // '사라지기'로도 다 사라진 순간은 온다. 여기서 안 불러주면 동작을 사라지기로 바꾼 순간
        // onBlackout에 꽂아둔 게 조용히 죽는다.
        r.onBlackout?.Invoke();

        if (사라지면끄기 && _mode != Mode.FullScreen) gameObject.SetActive(false);
    }

    /// <summary>순수 보간만. 완료 처리는 밖에서 한다 —
    /// 사라졌다나타나기 한가운데서 '사라지면끄기'가 오브젝트를 꺼버려 나타나기가 영영 안 오는 사고를 막으려고 분리했다.
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

        var ctrl = ScreenFadeController.Instance;
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
