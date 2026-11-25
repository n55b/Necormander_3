using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Tracks an actor's aim and draws its foot locator on the logical pixel frame.</summary>
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(1100)]
public class EntityDirectionIndicator : MonoBehaviour
{
    public enum Kind { [InspectorName("자동")] Auto, [InspectorName("플레이어")] Player, [InspectorName("적")] Enemy }

    [SerializeField] private Kind kind = Kind.Auto;
    [Tooltip("공용 발밑 표시 크기에 곱하는 프리팹별 배율.")]
    [Min(0.01f)]
    [SerializeField] private float indicatorScale = 1f;
    [Tooltip("AI가 강제 조준 갱신을 멈춘 뒤 마지막 조준을 유지하는 시간(초).")]
    [Min(0f)]
    [SerializeField] private float aimOverrideHold = 0.25f;

    public static readonly List<EntityDirectionIndicator> EnemyIndicators = new List<EntityDirectionIndicator>();

    private Kind resolved;
    private Transform unit;
    private PlayerController player;
    private BaseEntity entity;
    private CharacterHealth health;
    private SpriteRenderer spriteRenderer;
    private Vector2 lastAim = Vector2.right;
    private Vector2 lastScreenAim;
    private int lastSize;
    private Vector2 aimOverride;
    private float aimOverrideExpiry;
    private bool healthResolved;

    public bool IsEnemy => resolved == Kind.Enemy;
    public Vector2 WorldPosition => unit != null ? (Vector2)unit.position : (Vector2)transform.position;
    public bool IsAlive => ResolveHealth() == null || !health.IsDead;
    public bool IsActive => isActiveAndEnabled && IsAlive
        && (entity == null || entity.SpriteRenderer == null || entity.SpriteRenderer.enabled);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => EnemyIndicators.Clear();

    public void SetAimOverride(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        aimOverride = direction.normalized;
        aimOverrideExpiry = Time.time + aimOverrideHold;
    }

    public void ClearAimOverride() => aimOverrideExpiry = 0f;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        player = GetComponentInParent<PlayerController>();
        entity = GetComponentInParent<BaseEntity>();
        resolved = kind == Kind.Auto ? (player != null ? Kind.Player : Kind.Enemy) : kind;
        unit = resolved == Kind.Player && player != null ? player.transform
            : entity != null ? entity.transform : (transform.parent != null ? transform.parent : transform);
        spriteRenderer.enabled = false;
        PixelVectorShape.Apply(spriteRenderer, PixelVectorShape.Kind.Locator, PixelVectorShape.Stroke.Indicator);
    }

    private void OnEnable()
    {
        if (IsEnemy) EnemyIndicators.Add(this);
    }

    private void OnDisable()
    {
        if (IsEnemy) EnemyIndicators.Remove(this);
        if (spriteRenderer != null) spriteRenderer.enabled = false;
    }

    private CharacterHealth ResolveHealth()
    {
        // The parent can finish initializing after the child's Awake.
        if (!healthResolved)
        {
            health = resolved == Kind.Player ? player?.Stat?.Health : entity?.Stats?.Health;
            healthResolved = health != null;
        }

        return health;
    }

    private void LateUpdate()
    {
        var camera = PixelViewport.WorldCamera;
        bool show = PixelVectorShape.IsApplied(spriteRenderer) && unit != null && camera != null && IsActive;
        if (spriteRenderer.enabled != show) spriteRenderer.enabled = show;
        if (!show) return;

        Vector2 aim = ComputeAim();
        if (aim.sqrMagnitude > 0.0001f) lastAim = aim.normalized;

        var settings = CameraSettingsSO.Instance;
        int baseSize = IsEnemy ? settings.enemyLocatorPixels : settings.playerLocatorPixels;
        int size = Mathf.Max(4, Mathf.RoundToInt(baseSize * indicatorScale));
        Vector2 screenAim = camera.transform.InverseTransformDirection(lastAim);
        screenAim.Normalize();

        if (size != lastSize || screenAim != lastScreenAim)
        {
            PixelVectorShape.SetScreenShape(spriteRenderer, new Vector2(size, size), screenAim, lastSize == 0);
            lastScreenAim = screenAim;
            lastSize = size;
        }
    }

    private Vector2 ComputeAim()
    {
        if (!IsEnemy)
        {
            if (Mouse.current != null && PixelViewport.TryPointerToWorld(Mouse.current.position.ReadValue(), out var mouseWorld))
                return mouseWorld - (Vector2)unit.position;
            return lastAim;
        }

        if (Time.time < aimOverrideExpiry) return aimOverride;

        if (entity != null && entity.Target != null)
        {
            Vector2 toTarget = (Vector2)entity.Target.position - (Vector2)unit.position;
            if (toTarget.sqrMagnitude > 0.0001f) return toTarget;
        }

        if (entity != null && entity.MoveVelocity.sqrMagnitude > 0.01f) return entity.MoveVelocity;
        return lastAim;
    }
}
