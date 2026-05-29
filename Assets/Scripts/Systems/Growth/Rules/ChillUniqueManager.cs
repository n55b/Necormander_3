using UnityEngine;

public class ChillUniqueManager : MonoBehaviour
{
    public static ChillUniqueManager Instance { get; private set; }

    private bool _hasTriggeredAbsoluteZeroThisRoom = false;

    // [BitingWind] 칼바람 로직 변수
    private float _bitingWindTimer = 0f;
    public float bitingWindInterval = 5f; 
    public float bitingWindChillStacks = 1f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
        }
    }

    private void Update()
    {
        if (InventoryManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null) return;

        // [유니크] 칼바람 (BitingWind) 주기적 발동
        if (InventoryManager.Instance.HasUniqueEffect(GemUniqueType.BitingWind))
        {
            _bitingWindTimer += Time.deltaTime;
            if (_bitingWindTimer >= bitingWindInterval)
            {
                _bitingWindTimer = 0f;
                ApplyBitingWind();
            }
        }
        else
        {
            _bitingWindTimer = 0f;
        }
    }

    private void ApplyBitingWind()
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        float radius = player.THROWRANGE;

        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        Collider2D[] colls = Physics2D.OverlapCircleAll(player.transform.position, radius, enemyLayer);

        foreach (var col in colls)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health != null && !health.IsDead)
            {
                var status = col.GetComponentInChildren<CharacterStatus>();
                if (status != null)
                {
                    status.AddDebuffStack(DebuffStackType.Chill, bitingWindChillStacks);
                }
            }
        }
    }

    private void OnEnable()
    {
        RoomInstance.OnPlayerEnteredRoom += HandlePlayerEnteredRoom;
    }

    private void OnDisable()
    {
        RoomInstance.OnPlayerEnteredRoom -= HandlePlayerEnteredRoom;
    }

    private void HandlePlayerEnteredRoom(RoomInstance room)
    {
        // 방 입장 시 플래그 리셋
        _hasTriggeredAbsoluteZeroThisRoom = false;
    }

    public void TriggerAbsoluteZero(Vector3 centerPosition)
    {
        if (InventoryManager.Instance == null || !InventoryManager.Instance.HasUniqueEffect(GemUniqueType.AbsoluteZero)) return;
        if (_hasTriggeredAbsoluteZeroThisRoom) return;

        _hasTriggeredAbsoluteZeroThisRoom = true;
        Debug.Log("<color=#00FFFF>[ChillUnique]</color> Absolute Zero Triggered!");

        // 반경 5 내 적에게 한기 50스택 부여
        float radius = 5f;
        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        Collider2D[] colls = Physics2D.OverlapCircleAll(centerPosition, radius, enemyLayer);

        foreach (var col in colls)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health != null && !health.IsDead)
            {
                var status = col.GetComponentInChildren<CharacterStatus>();
                if (status != null)
                {
                    status.AddDebuffStack(DebuffStackType.Chill, 50f);
                }
            }
        }
    }
}
