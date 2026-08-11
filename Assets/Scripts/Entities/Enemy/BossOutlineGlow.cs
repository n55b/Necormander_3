using UnityEngine;

/// <summary>
/// 캐릭터 스프라이트의 <b>외곽선을 따라</b> 빛나는 발광 오버레이.
///
/// 예전 카운터 연출은 <c>SetVisualFlash</c> 로 몸통 SpriteRenderer 의 color 를 통째로 덮어써서
/// 캐릭터가 단색 실루엣이 돼 버렸다. 문제가 두 가지였다:
///   · 무슨 모션을 하는 중인지 안 보인다(전신이 빨간/노란 덩어리가 된다).
///   · 본 마스터는 슈퍼아머가 상시(게이지 999999)라 CharacterVisualFeedback 이 이미 노란 아웃라인
///     오버레이를 항상 그리고 있다. 그 위에 몸통만 노랗게 칠하면 두 연출이 뒤섞여 구분이 안 된다.
///
/// 그래서 몸통을 건드리지 않고, 같은 스프라이트를 아웃라인 셰이더로 한 겹 더 그린다.
/// 구현은 이 코드베이스에 이미 있는 슈퍼아머 오버레이(CharacterVisualFeedback.SpawnSuperArmorOverlay)와
/// 같은 관례를 따른다 — 자식 SpriteRenderer + 매 프레임 스프라이트 동기화 + MaterialPropertyBlock.
///
/// MaterialPropertyBlock 을 쓰는 이유도 같다: <c>renderer.material</c> 로 색을 바꾸면 유닛마다
/// 머티리얼 인스턴스가 새로 생겨 배칭이 깨지고, 그 인스턴스는 수동으로 안 지우면 그대로 샌다.
/// </summary>
public class BossOutlineGlow : MonoBehaviour
{
    private SpriteRenderer _body;
    private SpriteRenderer _overlay;
    private MaterialPropertyBlock _mpb;
    private int _colorId;

    private Color _baseColor = Color.white;
    private float _intensity = 1f;
    private float _brightness = 1f;
    private float _lastAppliedBrightness = -1f;

    /// <summary>머티리얼이 배선돼 있어 실제로 그릴 수 있는 상태인가. false 면 호출측이 폴백 연출을 써야 한다.</summary>
    public bool IsUsable => _overlay != null;

    public bool IsVisible => _overlay != null && _overlay.enabled;

    /// <summary>
    /// 본체 스프라이트 위에 겹칠 오버레이를 만든다. outlineMaterial 이 비어 있으면 아무것도 만들지 않고
    /// <see cref="IsUsable"/> 가 false 로 남는다(= 배선을 깜빡해도 조용히 죽지 않고 호출측이 알 수 있다).
    /// </summary>
    public void Init(SpriteRenderer body, Material outlineMaterial, string colorProperty, float intensity)
    {
        _body = body;
        _intensity = Mathf.Max(0f, intensity);

        if (_body == null || outlineMaterial == null) return;

        _colorId = Shader.PropertyToID(string.IsNullOrEmpty(colorProperty) ? "_Color" : colorProperty);

        var go = new GameObject("BossOutlineGlow");
        go.transform.SetParent(_body.transform, false);
        go.layer = _body.gameObject.layer;

        _overlay = go.AddComponent<SpriteRenderer>();
        _overlay.sharedMaterial = outlineMaterial;
        _overlay.enabled = false;

        _mpb = new MaterialPropertyBlock();
    }

    /// <summary>지정한 색으로 아웃라인을 켠다(최대 밝기).</summary>
    public void Show(Color color)
    {
        bool colorChanged = _baseColor != color;
        _baseColor = color;
        if (_overlay == null) return;

        _overlay.enabled = true;
        SyncToBody();          // 켜는 프레임부터 바로 올바른 스프라이트로 보이게

        // ApplyColor 는 '밝기가 그대로면 건너뛰는' 최적화가 걸려 있다. 색만 바뀌고 밝기가 같은
        // 전환(깜빡임 최고점 → 판정 개시, 초록 → 빨강 등)에서 그 최적화에 걸려 색이 안 바뀌므로,
        // 색이 달라졌으면 캐시를 무효화해서 반드시 한 번 쓰게 한다.
        if (colorChanged) _lastAppliedBrightness = -1f;

        SetBrightness(1f);
    }

    /// <summary>
    /// 밝기만 0~1 로 조절한다(색은 유지). 판정 개시 전 유예 구간에서 깜빡이게 할 때 쓴다.
    /// </summary>
    public void SetBrightness(float brightness01)
    {
        _brightness = Mathf.Clamp01(brightness01);
        ApplyColor();
    }

    public void Hide()
    {
        if (_overlay == null) return;
        _overlay.enabled = false;
        _lastAppliedBrightness = -1f;
    }

    private void ApplyColor()
    {
        if (_overlay == null || _mpb == null) return;

        // 밝기가 그대로인 프레임에는 MPB 왕복을 생략한다(슈퍼아머 오버레이와 같은 최적화).
        if (Mathf.Abs(_brightness - _lastAppliedBrightness) <= 0.002f) return;
        _lastAppliedBrightness = _brightness;

        float k = _intensity * _brightness;
        _overlay.GetPropertyBlock(_mpb);
        // 알파는 원본 값을 유지한다 — 이 셰이더는 알파가 아니라 RGB 세기로 발광을 만든다.
        _mpb.SetColor(_colorId, new Color(_baseColor.r * k, _baseColor.g * k, _baseColor.b * k, _baseColor.a));
        _overlay.SetPropertyBlock(_mpb);
    }

    private void SyncToBody()
    {
        // 애니메이션이 매 프레임 스프라이트를 바꾸므로 계속 따라가야 한다.
        // sprite 대입은 공짜가 아니라(내부에서 메시/UV 재생성) 바뀐 프레임에만 쓴다.
        if (!ReferenceEquals(_overlay.sprite, _body.sprite)) _overlay.sprite = _body.sprite;

        _overlay.flipX = _body.flipX;
        _overlay.flipY = _body.flipY;
        _overlay.sortingLayerID = _body.sortingLayerID;

        // +1 은 슈퍼아머 오버레이가 이미 쓰고 있다(CharacterVisualFeedback.SyncSuperArmorOverlay).
        // 카운터 신호는 그보다 위에 떠야 상시 노란 아웃라인에 묻히지 않는다.
        _overlay.sortingOrder = _body.sortingOrder + 2;
    }

    // 본체 sortingOrder 는 YSortableObject 가 LateUpdate 에서 갱신하므로 여기서 맞춘다
    // (슈퍼아머 오버레이·빙결 VFX 도 같은 이유로 같은 타이밍에 동기화한다).
    private void LateUpdate()
    {
        if (_overlay == null || _body == null || !_overlay.enabled) return;
        SyncToBody();
    }
}
