using UnityEngine;

/// <summary>
/// 자가등록형(씬에 배치되어 스스로 Instance 를 잡는) 싱글턴 MonoBehaviour 의 공통 베이스.
///
/// - Awake 에서 중복 인스턴스를 파괴하는 가드를 자동 제공한다(기존에 제각각이던 3가지 초기화 방식 통일).
/// - Instance 할당 타이밍은 종전과 동일하게 Awake 이므로 초기화 순서 위험이 없다.
/// - 하위 클래스는 Awake 대신 <see cref="OnAwake"/> 를 오버라이드해 초기화 로직을 넣는다.
///   (Awake 를 직접 오버라이드할 경우 반드시 base.Awake() 를 먼저 호출할 것.)
///
/// 주의: GameManager 가 순서를 직접 제어하는 매니저(Camera/HitStop/Inventory/PlayerSkillInventory/Reward)는
/// 이 베이스를 쓰지 않고 기존의 Initialize() 규약을 유지한다.
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    public static T Instance { get; private set; }

    [SerializeField] protected bool persistAcrossScenes = false;

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = (T)this;
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
        OnAwake();
    }

    /// <summary>하위 클래스의 초기화 로직. Awake 에서 Instance 확정 직후 호출된다.</summary>
    protected virtual void OnAwake() { }

    protected virtual void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
