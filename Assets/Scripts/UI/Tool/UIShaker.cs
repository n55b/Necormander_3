using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이 UI 부품 하나를 흔든다. 흔들고 싶은 부품에만 붙이면 된다 — 안 붙인 부품은 안 흔들린다.
/// (그래서 "체력바는 세게, 미니언바는 안 흔들림"이 세기 0 같은 걸 넣을 필요 없이 그냥 된다.)
///
/// 부품 하나에 컴포넌트도 하나. 반응 목록에 "무슨 신호 → 얼마나 세게"를 줄줄이 적는다.
/// CameraShaker와 똑같은 모양이다 — 하나 익히면 나머지도 똑같다.
///
/// 주의: LayoutGroup/ContentSizeFitter의 '자식'에 붙이면 레이아웃이 위치를 도로 돌려놔서
/// 흔들리다 튕겨 돌아온다. 그런 자리면 Awake에서 경고를 띄운다 — 레이아웃이 안 건드리는
/// 자유 노드(예: 체력바 프리팹의 BG_HPBar)에 붙여라.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIShaker : MonoBehaviour
{
    [System.Serializable]
    public class Reaction
    {
        [Tooltip("이 신호가 오면 아래 설정대로 흔들린다.")]
        public ShakeSignal signal = ShakeSignal.플레이어피격;

        [Tooltip("얼마나 크게 흔들릴지(픽셀). 축 제한도 겸한다 — 가로로만 흔들려면 (12, 0), 세로로만이면 (0, 12).")]
        public Vector2 strength = new Vector2(12f, 12f);

        [Tooltip("흔들리는 시간(초).")]
        public float duration = 0.25f;

        [Tooltip("흔들리는 횟수. 클수록 잘게 떤다.")]
        public int vibrato = 20;

        [Tooltip("갈수록 잦아들게 할지. 끄면 끝까지 같은 세기로 떤다.")]
        public bool fadeOut = true;
    }

    [Tooltip("무슨 신호에 어떻게 흔들릴지. 필요한 만큼 줄을 추가하면 된다. 목록에 없는 신호엔 반응하지 않는다.")]
    [SerializeField]
    private List<Reaction> reactions = new List<Reaction> { new Reaction() };

    [Tooltip("시간이 멈춰도(히트스톱) 흔들린다. 맞는 순간이 곧 시간이 멈추는 순간이라 대부분 켜두는 게 맞다.")]
    [SerializeField] private bool unscaledTime = true;

    private RectTransform _rect;
    private Vector2 _origin;
    private Coroutine _running;
    private LayoutElement _layoutElement;
    private bool _hasLayoutParent;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _origin = _rect.anchoredPosition;

        _hasLayoutParent = transform.parent != null && transform.parent.GetComponent<LayoutGroup>() != null;

        if (_hasLayoutParent)
        {
            _layoutElement = GetComponent<LayoutElement>();
            if (_layoutElement == null) _layoutElement = gameObject.AddComponent<LayoutElement>();
        }
    }

    private void OnEnable() => Signal.Listen<ShakeSignal>(OnSignal);

    private void OnDisable()
    {
        Signal.Unlisten<ShakeSignal>(OnSignal);

        // 흔들리는 도중에 꺼지면 어긋난 채로 굳는다. 반드시 원위치로 되돌려두고 끝낸다.
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }
        if (_rect != null) _rect.anchoredPosition = _origin;
        if (_layoutElement != null) _layoutElement.ignoreLayout = false;
    }

    private void OnSignal(ShakeSignal signal)
    {
        foreach (var r in reactions)
        {
            if (r != null && r.signal == signal) { Shake(r); return; }
        }
    }

    /// <summary>인자가 없어서 버튼 OnClick이나 방 이벤트 슬롯에 그대로 잡힌다. 목록 첫 줄의 설정을 쓴다.</summary>
    public void Shake() => Shake(reactions.Count > 0 && reactions[0] != null ? reactions[0] : new Reaction());

    public void Shake(Reaction r)
    {
        if (!isActiveAndEnabled || r == null) return;

        // 겹쳐 맞으면 나중 것이 이긴다. 새로 시작하기 전에 반드시 원위치로 되돌려야
        // 연타당할 때 오프셋이 누적돼 부품이 흘러가는 일이 없다.
        if (_running != null) StopCoroutine(_running);
        _rect.anchoredPosition = _origin;

        if (_layoutElement != null) _layoutElement.ignoreLayout = true;

        _running = StartCoroutine(ShakeRoutine(r));
    }

    private IEnumerator ShakeRoutine(Reaction r)
    {
        float t = 0f;
        while (t < r.duration && r.duration > 0f)
        {
            t += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            float damp = r.fadeOut ? (1f - Mathf.Clamp01(t / r.duration)) : 1f;
            // vibrato = 흔드는 동안의 진동 횟수. 사인파의 부호가 뒤집히는 속도를 정한다.
            float phase = t * r.vibrato * Mathf.PI;
            Vector2 offset = new Vector2(
                Mathf.Sin(phase) * r.strength.x,
                Mathf.Cos(phase * 1.3f) * r.strength.y // x와 살짝 다른 주기 → 한 축으로 쏠리지 않고 지저분하게 떤다
            ) * damp;

            _rect.anchoredPosition = _origin + offset;
            yield return null;
        }

        _rect.anchoredPosition = _origin;

        if (_layoutElement != null)
        {
            _layoutElement.ignoreLayout = false;
            if (transform.parent is RectTransform parentRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        }

        _running = null;
    }

    [ContextMenu("Test - Shake")]
    private void TestShake() => Shake();
}
