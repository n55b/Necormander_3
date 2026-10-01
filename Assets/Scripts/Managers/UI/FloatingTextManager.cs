using System.Collections.Generic;
using UnityEngine;

public class FloatingTextManager : Singleton<FloatingTextManager>
{

    public GameObject textPrefab;
    public int poolSize = 20;

    [Header("디버그/테스트용")]
    [Tooltip("체크 해제하면 데미지 숫자(MISS, 쉴드, 처형 포함) 팝업이 뜨지 않습니다. 힐/상태 텍스트는 영향 없음.")]
    [SerializeField] private bool showDamageNumbers = true;
    public bool ShowDamageNumbers => showDamageNumbers;
    public void SetShowDamageNumbers(bool value) => showDamageNumbers = value;

    private Queue<TextFloating> textPool = new Queue<TextFloating>();

    [Header("알림 텍스트 줄 쌓기")]
    [Tooltip("연달아 뜬 알림 텍스트 사이의 세로 간격(월드 단위).")]
    [SerializeField] private float noticeLineSpacing = 0.4f;

    [Tooltip("한 대상에 동시에 보일 최대 줄 수. 넘치면 가장 오래된 텍스트부터 서둘러 사라진다.")]
    [SerializeField, Min(1)] private int noticeMaxLines = 4;

    /// <summary>대상별로 지금 떠 있는 알림 텍스트(0번 = 가장 최신, 맨 아래).</summary>
    private readonly Dictionary<Transform, List<TextFloating>> _noticeStacks = new Dictionary<Transform, List<TextFloating>>();
    private static readonly List<Transform> _pruneBuffer = new List<Transform>();
    private const int NoticePruneThreshold = 32;

    // ShowOnPlayer 캐시: 플레이어가 바뀌거나 지점이 파괴됐을 때만 다시 찾는다.
    private Component _cachedPlayer;
    private Transform _cachedPlayerPoint;


    protected override void OnAwake()
    {
        InitializePool();
    }

    // Queue setting
    void InitializePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(textPrefab, transform);
            TextFloating text = obj.GetComponent<TextFloating>();
            // 프리팹이 활성 상태면 SetActive(false) 의 OnDisable 이 이미 풀에 넣는다.
            // ReturnToPool 은 중복을 막으므로 여기서 한 번 더 불러도 안전하다.
            obj.SetActive(false);
            ReturnToPool(text);
        }
    }

    public TextFloating GetFromPool()
    {
        while (true)
        {
            if (textPool.Count == 0) InitializePool();

            TextFloating text = textPool.Dequeue();
            if (text == null) continue; // 씬 전환 등으로 파괴된 항목은 건너뛴다

            text.InPool = false;
            text.gameObject.SetActive(true);
            return text;
        }
    }

    public void ReturnToPool(TextFloating text)
    {
        if (text == null || text.InPool) return;
        text.InPool = true;
        text.NoticeKey = null;
        text.gameObject.SetActive(false);
        textPool.Enqueue(text);
    }


    /// <summary>
    /// 대상 위치에 알림 텍스트(보상·획득·회복·상태이상 등)를 한 줄 띄운다. 데미지 숫자는 이 경로를 쓰지 않는다.
    /// 새 텍스트는 맨 아래에 뜨고, 같은 대상에 아직 떠 있는 텍스트들을 noticeLineSpacing 간격이 되도록 위로 밀어 올린다.
    /// 시간 창이 아니라 실제 높이로 간격을 맞추므로, 떠오르는 속도나 시간 정지 여부와 관계없이 겹치지 않는다.
    /// 텍스트는 생성 위치에 고정되므로 대상이 파괴돼도 끝까지 보인다.
    /// </summary>
    public void Show(string message, Color color, Transform target, float scaleMultiplier = 1f)
    {
        if (target == null || string.IsNullOrEmpty(message)) return;

        // 풀에서 꺼내기 전에 정리해야, 방금 반납된 텍스트가 같은 대상에 재사용될 때 목록에 두 번 남지 않는다.
        List<TextFloating> stack = GetNoticeStack(target);

        var t = GetFromPool();
        if (t == null) return;
        t.SetUp(message, color, target, false, scaleMultiplier);
        t.OverrideOffset(Vector3.zero);
        t.NoticeKey = target;
        stack.Insert(0, t);

        float prevY = t.OffsetY;
        for (int i = 1; i < stack.Count; i++)
        {
            stack[i].RaiseOffsetY(prevY + noticeLineSpacing);
            prevY = stack[i].OffsetY;
            if (i >= noticeMaxLines) stack[i].ExpireSoon();
        }
    }

    /// <summary>대상의 알림 목록을 꺼내면서, 이미 사라졌거나 다른 용도로 재사용된 텍스트를 걸러낸다.</summary>
    private List<TextFloating> GetNoticeStack(Transform target)
    {
        if (!_noticeStacks.TryGetValue(target, out var stack))
        {
            if (_noticeStacks.Count >= NoticePruneThreshold) PruneNoticeStacks();
            stack = new List<TextFloating>(noticeMaxLines + 1);
            _noticeStacks[target] = stack;
        }
        RemoveDeadNotices(stack, target);
        return stack;
    }

    private static void RemoveDeadNotices(List<TextFloating> stack, Transform key)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var t = stack[i];
            if (t == null || !t.isActiveAndEnabled || t.NoticeKey != key) stack.RemoveAt(i);
        }
    }

    /// <summary>떠 있는 텍스트가 하나도 없는 대상의 기록을 지운다(적이 계속 죽어도 딕셔너리가 안 불어나게).</summary>
    private void PruneNoticeStacks()
    {
        _pruneBuffer.Clear();
        foreach (var kv in _noticeStacks)
        {
            RemoveDeadNotices(kv.Value, kv.Key);
            if (kv.Value.Count == 0) _pruneBuffer.Add(kv.Key);
        }
        foreach (var key in _pruneBuffer) _noticeStacks.Remove(key);
        _pruneBuffer.Clear();
    }

    /// <summary>
    /// 매니저가 없으면(마을/로딩/씬 언로드 중) 조용히 넘어가는 Show. 외부에서는 Instance?.Show 대신 이것을 쓴다
    /// (UnityEngine.Object 에 ?. 를 쓰면 Unity 의 파괴 판정을 우회하므로).
    /// </summary>
    public static void ShowNotice(string message, Color color, Transform target, float scaleMultiplier = 1f)
    {
        var manager = Instance;
        if (manager != null) manager.Show(message, color, target, scaleMultiplier);
    }

    /// <summary>
    /// 플레이어 머리 위 텍스트 지점(FloatingTextSpawner.FloatPoint)에 알림 텍스트를 띄운다.
    /// 회복·상태이상 텍스트와 같은 지점을 쓰므로 서로 겹치지 않고 함께 쌓인다.
    /// 매니저나 플레이어가 없으면(마을/로딩 등) 조용히 넘어간다.
    /// </summary>
    public static void ShowOnPlayer(string message, Color color)
    {
        var manager = Instance;
        if (manager == null) return;
        Component player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (player == null) return;
        manager.Show(message, color, manager.ResolvePlayerPoint(player));
    }

    private Transform ResolvePlayerPoint(Component player)
    {
        if (player != _cachedPlayer || _cachedPlayerPoint == null)
        {
            var spawner = player.GetComponentInChildren<FloatingTextSpawner>();
            _cachedPlayerPoint = spawner != null ? spawner.FloatPoint : player.transform;
            _cachedPlayer = player;
        }
        return _cachedPlayerPoint;
    }
}
