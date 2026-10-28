using UnityEngine;

/// <summary>방 출입구의 비석 외형을 표시한다. 충돌과 이동은 기존 문이 담당한다.</summary>
[ExecuteAlways]
public sealed class PortalDoorVisual : MonoBehaviour
{
    [Header("포탈 비석")]
    [SerializeField] private SpriteRenderer firstPillar;
    [SerializeField] private Sprite inactiveSprite;
    [SerializeField] private Sprite activeSprite;
    [InspectorName("비석 높이 보정")]
    [Tooltip("문 위치를 기준으로 비석을 위아래로 옮긴다.")]
    [SerializeField] private float pillarHeight = -1f;
    [InspectorName("비석 크기")]
    [SerializeField, Min(0.01f)] private float pillarScale = 1f;

    private bool _open = true;
    private bool _needsRefresh;

    private void OnEnable() => Refresh();
    private void OnValidate() => _needsRefresh = true;

    public void SetOpen(bool open)
    {
        _open = open;
        Refresh();
    }

    private void Refresh()
    {
        if (firstPillar == null) return;
        // 문이 상하좌우로 회전해도 비석은 중심에 똑바로 세운다.
        firstPillar.transform.position = transform.position + Vector3.up * pillarHeight;
        firstPillar.transform.rotation = Quaternion.identity;
        firstPillar.transform.localScale = Vector3.one * pillarScale;
        firstPillar.sprite = _open ? activeSprite : inactiveSprite;
    }

    private void Update()
    {
        if (_needsRefresh) { _needsRefresh = false; Refresh(); }
    }
}
