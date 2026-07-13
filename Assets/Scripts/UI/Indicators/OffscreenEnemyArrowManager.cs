using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 화면 밖으로 나간 적의 위치를 화면 끝(사거리)에 뜨는 작은 화살표로 알려준다.
/// 어디로 가야 적을 만나는지 방향을 가리킨다.
///
/// <b>GameManager 하위 "Gameplay Systems"에 붙는 매니저 컴포넌트.</b> 다른 매니저들과 동일하게
/// GameManager.InitializeGame()이 순서대로 <see cref="Initialize"/>를 호출한다(자기 Awake에서 자가초기화 X).
///
/// 화살표는 <b>Canvas UI가 아니라 월드 좌표 SpriteRenderer</b>다. 직교 카메라가 플레이어를 따라다니므로,
/// 매 프레임 카메라 가시 사각형 경계(월드 좌표)에 화살표를 놓으면 "화면 끝에 붙은" 것처럼 보인다.
/// 그래서 씬의 글로벌 캔버스가 필요 없다. 적 목록은 <see cref="EntityDirectionIndicator.EnemyIndicators"/>
/// 레지스트리에서 가져오고, 카메라 가시영역 경계와 방향 벡터의 교점으로 위치를 구한다.
/// 화살표 스프라이트는 풀링해 재사용한다(자식 "~OffscreenArrows" 아래).
/// </summary>
public class OffscreenEnemyArrowManager : MonoBehaviour
{
    public static OffscreenEnemyArrowManager Instance { get; private set; }

    [Header("동작")]
    [SerializeField] private bool overlayEnabled = true;
    [Tooltip("화면 끝에서 안쪽으로 띄우는 여백(월드 단위).")]
    [SerializeField] private float edgeMargin = 0.7f;
    [Tooltip("화살표 스프라이트 스케일.")]
    [SerializeField] private float arrowScale = 2.2f;
    [Tooltip("Arrow_Side 기본 스프라이트가 '왼쪽(-X)'을 보므로 방향각 + 180° 보정. 스프라이트 방향이 다르면 이 값만 바꾸면 됨.")]
    [SerializeField] private float arrowBaseAngle = 180f;
    [Tooltip("Y정렬 유닛(order ≈ -Y*100)보다 항상 위에 오도록 크게. 미니맵/HP 캔버스 레이어보다는 아래.")]
    [SerializeField] private int arrowSortingOrder = 10000;
    [Tooltip("비우면 SpriteRenderer 기본 정렬 레이어. 전용 오버레이 레이어가 있으면 그 이름.")]
    [SerializeField] private string sortingLayerName = "";
    [Tooltip("동시에 그릴 최대 화살표 수(초과 시 카메라에서 가까운 적 우선).")]
    [SerializeField] private int maxArrows = 16;
    [Tooltip("비워두면 Arrow.aseprite의 Arrow_Side를 런타임 로드.")]
    [SerializeField] private Sprite arrowSpriteOverride;
    [Tooltip("화살표에 쓸 머티리얼(비우면 SpriteRenderer 기본).")]
    [SerializeField] private Material arrowMaterial;

    private Camera _cam;
    private Sprite _arrowSprite;
    private Transform _pool;
    private bool _initialized;
    private readonly List<SpriteRenderer> _arrows = new List<SpriteRenderer>();
    private readonly List<EntityDirectionIndicator> _offscreen = new List<EntityDirectionIndicator>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    /// <summary>GameManager.InitializeGame()이 다른 매니저들과 같은 순서로 호출한다(CameraManager.Initialize 패턴).</summary>
    public void Initialize()
    {
        Instance = this;
        _arrowSprite = (arrowSpriteOverride != null) ? arrowSpriteOverride : ArrowIndicatorSprites.ArrowSide;

        var poolGo = new GameObject("~OffscreenArrows");
        _pool = poolGo.transform;
        _pool.SetParent(transform, false);

        _initialized = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (!_initialized || !overlayEnabled) { HideFrom(0); return; }

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null || !_cam.orthographic) { HideFrom(0); return; }

        Vector2 center = _cam.transform.position;
        float halfH = _cam.orthographicSize;
        float halfW = halfH * _cam.aspect;
        // 화면 안/밖 판정은 실제 가시영역, 화살표 배치는 여백만큼 안쪽.
        float ex = Mathf.Max(0.1f, halfW - edgeMargin);
        float ey = Mathf.Max(0.1f, halfH - edgeMargin);

        // 1) 화면 밖 적 수집
        _offscreen.Clear();
        var list = EntityDirectionIndicator.EnemyIndicators;
        for (int i = 0; i < list.Count; i++)
        {
            var ind = list[i];
            if (ind == null || !ind.IsActive) continue;
            Vector2 p = ind.WorldPosition;
            if (Mathf.Abs(p.x - center.x) <= halfW && Mathf.Abs(p.y - center.y) <= halfH) continue;
            _offscreen.Add(ind);
        }

        // 2) 너무 많으면 카메라에서 가까운 순으로 컷
        if (_offscreen.Count > maxArrows)
            _offscreen.Sort((a, b) =>
                (a.WorldPosition - center).sqrMagnitude.CompareTo((b.WorldPosition - center).sqrMagnitude));

        // 3) 화면 사각형 경계에 화살표 배치
        int used = 0;
        for (int i = 0; i < _offscreen.Count && used < maxArrows; i++)
        {
            Vector2 dir = _offscreen[i].WorldPosition - center;
            if (dir.sqrMagnitude < 0.0001f) continue;

            float t = float.MaxValue;
            if (Mathf.Abs(dir.x) > 0.0001f) t = Mathf.Min(t, ex / Mathf.Abs(dir.x));
            if (Mathf.Abs(dir.y) > 0.0001f) t = Mathf.Min(t, ey / Mathf.Abs(dir.y));
            Vector2 edge = center + dir * t;

            var sr = GetArrow(used);
            sr.enabled = true;
            sr.transform.position = new Vector3(edge.x, edge.y, 0f);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + arrowBaseAngle;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            sr.transform.localScale = Vector3.one * arrowScale;
            used++;
        }
        HideFrom(used);
    }

    private SpriteRenderer GetArrow(int index)
    {
        while (_arrows.Count <= index)
        {
            var go = new GameObject("Arrow" + _arrows.Count);
            go.transform.SetParent(_pool, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _arrowSprite;
            if (arrowMaterial != null) sr.sharedMaterial = arrowMaterial;
            sr.sortingOrder = arrowSortingOrder;
            if (!string.IsNullOrEmpty(sortingLayerName)) sr.sortingLayerName = sortingLayerName;
            sr.enabled = false;
            _arrows.Add(sr);
        }
        return _arrows[index];
    }

    private void HideFrom(int startIndex)
    {
        for (int i = startIndex; i < _arrows.Count; i++)
            if (_arrows[i] != null) _arrows[i].enabled = false;
    }
}
