
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Y좌표 기반 동적 스프라이트 정렬.
/// Y가 클수록(화면 위쪽/먼 곳) sortingOrder가 작아지고, Y가 작을수록(화면 아래쪽/가까운 곳) sortingOrder가 커집니다.
/// → 플레이어가 Wall보다 위쪽(Y가 큰 쪽)에 있으면 Wall에 가려지고, 아래쪽에 있으면 Wall보다 앞에 그려집니다.
///
/// 정적 오브젝트(Wall, 나무 등): isDynamic = false → 1회만 계산
/// 동적 오브젝트(플레이어, 미니언, 적): isDynamic = true → 매 프레임 갱신
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class YSortableObject : MonoBehaviour
{
    [Header("정렬 설정")]
    [Tooltip("매 프레임 위치가 바뀌는 오브젝트(플레이어, 몬스터 등)는 체크. 고정된 오브젝트(벽, 나무 등)는 해제.")]
    [SerializeField] private bool isDynamic = false;

    [Tooltip("정밀도. 값이 클수록 Y값 차이에 더 민감하게 sortingOrder가 갈립니다.")]
    [SerializeField] private int precision = 100;

    [Tooltip("기준 오프셋. 같은 Y값에서도 항상 위에 그려야 하는 오브젝트(예: 플레이어)에 약간 더한 값을 줍니다.")]
    [SerializeField] private int baseOffset = 0;

    [Tooltip("정렬 기준점을 다른 Transform으로 고정할 때 지정. 예: 손/무기 스프라이트가 자기 Y가 아니라 몸통 Y를 기준으로 정렬되도록. 비워두면 자기 자신의 Transform 사용.")]
    [SerializeField] private Transform pivotOverride;

    /// <summary>현재 씬에 활성화된 모든 YSortableObject 등록. 가림(occlusion) 판정 등에서 순회용으로 사용.</summary>
    public static readonly List<YSortableObject> ActiveInstances = new List<YSortableObject>();

    /// <summary>이 오브젝트의 SpriteRenderer (읽기 전용 노출).</summary>
    public SpriteRenderer Renderer => _renderer;

    
private SpriteRenderer _renderer;
    private Transform _pivot; // 정렬 기준점 (없으면 transform 사용)
    private int _lastOrder = int.MinValue;

    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _pivot = pivotOverride != null ? pivotOverride : transform;
    }

    private void OnEnable()
    {
        ActiveInstances.Add(this);
    }

    private void OnDisable()
    {
        ActiveInstances.Remove(this);
    }

    
private void Start()
    {
        // 정적 오브젝트는 1회만 계산
        if (!isDynamic) UpdateSortOrder();
    }

    private void LateUpdate()
    {
        if (isDynamic) UpdateSortOrder();
    }

    private void UpdateSortOrder()
    {
        int order = baseOffset - Mathf.RoundToInt(_pivot.position.y * precision);

        // 값이 안 바뀌면 대입 스킵 (불필요한 갱신 방지)
        if (order == _lastOrder) return;
        _lastOrder = order;

        _renderer.sortingOrder = order;
    }

    /// <summary>외부에서 피벗(정렬 기준점)을 다른 Transform으로 지정할 때 사용 (예: 발밑 기준).</summary>
    public void SetPivot(Transform pivot) => _pivot = pivot != null ? pivot : transform;
}
