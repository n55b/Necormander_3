using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 돌진(고속 이동) 중 스프라이트 잔상(고스트)을 남기는 컴포넌트입니다.
/// [수정] 평타 넉백 등 돌진이 아닌 고속 이동에도 잔상이 남는 문제가 있어, AI 패턴이 BeginDash/EndDash로
/// 명시적으로 연 "돌진 윈도우" 동안 + speedThreshold 이상 실제 이동 중일 때만 방출하도록 변경했습니다.
/// (TrailRenderer 대신 스프라이트 고스팅을 쓰는 이유: 캐릭터 실루엣이 남아야 "빠름"으로 읽히고,
///  개별 SpriteRenderer라 Y-sort 정렬과도 충돌하지 않습니다.)
/// </summary>
public class DashAfterimage : MonoBehaviour
{
    [Header("발동 조건")]
    [Tooltip("이 속도(유닛/초) 이상으로 움직일 때만 잔상을 남깁니다. 평상시 이동속도보다 높게 설정하세요.")]
    [SerializeField] private float speedThreshold = 4.5f;

    [Header("잔상 설정")]
    [Tooltip("잔상 1장 생성 간격(초)")]
    [SerializeField] private float spawnInterval = 0.045f;
    [Tooltip("잔상 1장이 사라지기까지 걸리는 시간(초)")]
    [SerializeField] private float fadeTime = 0.25f;
    [Tooltip("잔상 색상(알파 포함). 엘리트는 붉은 틴트 권장")]
    [SerializeField] private Color tint = new Color(1f, 1f, 1f, 0.45f);

    /// <summary>true로 두면 속도와 무관하게 강제 방출 (AI 패턴에서 수동 제어가 필요할 때만 사용, 선택)</summary>
    public bool ForceEmit { get; set; }

    private SpriteRenderer _source;
    private Rigidbody2D _rb;
    private Vector3 _lastPos;
    private float _timer;
    private float _dashWindowUntil; // BeginDash로 열린 잔상 방출 윈도우의 만료 시각 (Time.time 기준)

    private readonly Queue<SpriteRenderer> _pool = new Queue<SpriteRenderer>();
    private readonly List<SpriteRenderer> _spawned = new List<SpriteRenderer>();

    private void Awake()
    {
        _source = GetComponent<SpriteRenderer>();
        if (_source == null) _source = GetComponentInChildren<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _lastPos = transform.position;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _source == null) return;

        // 실제 이동 속도 계산 (돌진 윈도우 안에서도 윈드업 등 정지 구간엔 잔상이 안 나오게 하기 위함)
        float posSpeed = ((transform.position - _lastPos) / dt).magnitude;
        _lastPos = transform.position;
        float rbSpeed = _rb != null ? _rb.linearVelocity.magnitude : 0f;

        // [수정] 속도만으로 자동 발동하던 기존 방식은 평타 넉백 등 돌진이 아닌 고속 이동에도
        // 잔상이 남는 문제가 있어, "돌진 윈도우(BeginDash~EndDash)가 열려 있는 동안" +
        // "실제로 빠르게 움직이는 중"일 때만 방출하도록 변경했습니다.
        bool inDashWindow = Time.time < _dashWindowUntil;
        bool emitting = ForceEmit || (inDashWindow && Mathf.Max(posSpeed, rbSpeed) >= speedThreshold);

        if (!emitting)
        {
            _timer = spawnInterval; // 다음 발동 시 첫 장이 즉시 나오도록
            return;
        }

        _timer += dt;
        if (_timer < spawnInterval) return;
        _timer = 0f;
        SpawnGhost();
    }

    private void SpawnGhost()
    {
        SpriteRenderer ghost = _pool.Count > 0 ? _pool.Dequeue() : CreateGhost();
        ghost.gameObject.SetActive(true);
        ghost.sprite = _source.sprite; // 현재 애니메이션 프레임 그대로 복제
        ghost.flipX = _source.flipX;
        ghost.flipY = _source.flipY;
        ghost.color = tint;

        Transform t = ghost.transform;
        t.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
        t.localScale = _source.transform.lossyScale;

        ghost.sortingLayerID = _source.sortingLayerID;
        ghost.sortingOrder = _source.sortingOrder - 1; // 본체 바로 뒤

        StartCoroutine(FadeAndRecycle(ghost));
    }

    private SpriteRenderer CreateGhost()
    {
        GameObject go = new GameObject(name + "_Ghost");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        _spawned.Add(sr);
        return sr;
    }

    private IEnumerator FadeAndRecycle(SpriteRenderer ghost)
    {
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            if (ghost == null) yield break;
            Color c = tint;
            c.a = Mathf.Lerp(tint.a, 0f, t / fadeTime);
            ghost.color = c;
            yield return null;
        }
        if (ghost == null) yield break;
        ghost.gameObject.SetActive(false);
        _pool.Enqueue(ghost);
    }

    private void OnDestroy()
    {
        // 고스트는 월드에 스폰되므로, 본체가 죽어도 유령 오브젝트가 남지 않게 정리
        foreach (SpriteRenderer sr in _spawned)
        {
            if (sr != null) Destroy(sr.gameObject);
        }
        _spawned.Clear();
        _pool.Clear();
    }


    /// <summary>
    /// 돌진 패턴 시작 시 호출: maxDuration초 동안 잔상 방출 윈도우를 엽니다.
    /// 코루틴이 스턴/사망 등으로 중간에 끊겨도 윈도우가 자동 만료되므로 잔상이 켜진 채 방치되지 않습니다.
    /// 윈도우가 열려 있어도 speedThreshold 이상으로 실제 이동 중일 때만 잔상이 나옵니다.
    /// </summary>
    public void BeginDash(float maxDuration)
    {
        _dashWindowUntil = Time.time + maxDuration;
    }

    /// <summary>돌진 패턴 종료 시 호출: 잔상 방출 윈도우를 즉시 닫습니다.</summary>
    public void EndDash()
    {
        _dashWindowUntil = 0f;
    }
}
