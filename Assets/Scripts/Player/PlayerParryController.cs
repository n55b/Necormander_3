using UnityEngine;
using System.Collections;

/// <summary>
/// 플레이어의 투사체 패리(Parry) 시스템을 담당하는 컨트롤러입니다.
/// </summary>
public class PlayerParryController : MonoBehaviour
{
    [Header("패리 기본 설정")]
    [SerializeField] private float parryRadius = 2.5f;
    [SerializeField] private float parryAngle = 160f;
    
    [Header("패리 타이머 설정")]
    [Tooltip("체크하면 Animator의 Parry 애니메이션 클립 길이에 맞춰 판정/후딜레이 시간이 자동 조정됩니다. 체크 해제 시 아래 수동 설정값이 적용됩니다.")]
    [SerializeField] private bool useAnimationDuration = false;
    [SerializeField] private float parryActiveDuration = 0.05f;
    [SerializeField] private float parryRecoveryDuration = 0.2f;

    [Header("패리 시각 효과용 프리팹 (지정 시 사용, 미지정 시 기본 텔레그래프 메쉬 생성)")]
    [SerializeField] private GameObject parryTelegraphPrefab;

    private PlayerController _player;
    private bool _isParrying = false;
    private Coroutine _parryCoroutine;

    

    public event System.Action OnParryStart;
    public event System.Action OnParrySuccess;
    public event System.Action OnParryFail;
public bool IsParrying => _isParrying;
    public float ParryRadius => parryRadius;
    public float ParryAngle => parryAngle;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    private void Start()
    {
        // 씬 시작 시 애니메이션 클립 연동 체크
        UpdateDurationsFromAnimation();
    }

    /// <summary>
    /// Animator Controller에 'Parry' 애니메이션 클립이 있을 경우, 
    /// 패리 판정 시간과 후딜레이 시간을 애니메이션 길이에 맞춰 동적으로 조절합니다.
    /// </summary>
    public void UpdateDurationsFromAnimation()
    {
        if (!useAnimationDuration)
        {
            Debug.Log($"<color=cyan>[Parry]</color> 인스펙터 수동 시간 적용됨! 판정: {parryActiveDuration:F3}초, 후딜레이: {parryRecoveryDuration:F3}초");
            return;
        }

        float clipLength = GetAnimationClipLength("Parry");
        if (clipLength > 0f)
        {
            // 애니메이션이 존재할 경우
            // 패리 감지(Active Window)는 0.05초 또는 전체 길이의 20% 중 작은 값으로 설정하고,
            // 나머지 길이를 후딜레이(Recovery)로 채워서 총 길이를 애니메이션 길이와 완전히 일치시킵니다.
            parryActiveDuration = Mathf.Min(0.05f, clipLength * 0.2f);
            parryRecoveryDuration = clipLength - parryActiveDuration;

            Debug.Log($"<color=cyan>[Parry]</color> Parry 애니메이션 감지됨! 판정: {parryActiveDuration:F3}초, 후딜레이: {parryRecoveryDuration:F3}초 (총 길이: {clipLength:F3}초)");
        }
        else
        {
            // 애니메이션이 없을 경우 인스펙터 설정 시간 그대로 사용
            Debug.Log($"<color=cyan>[Parry]</color> Parry 애니메이션을 찾지 못해 인스펙터 값을 사용합니다. 판정: {parryActiveDuration:F3}초, 후딜레이: {parryRecoveryDuration:F3}초");
        }
    }

    // state name suffix ("Parry" or "Parry_Success") -> matching clip length, or -1 if not found.
    // Uses EndsWith so that "Parry" never accidentally matches the "Parry_Success" clip.
    private float GetAnimationClipLength(string stateNameSuffix)
    {
        if (_player == null) return -1f;

        // PlayerController 하위 혹은 본인에게 붙어 있는 Animator 검색
        Animator anim = GetComponentInChildren<Animator>();
        if (anim == null) anim = GetComponent<Animator>();

        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var clip in anim.runtimeAnimatorController.animationClips)
            {
                if (clip.name.EndsWith(stateNameSuffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    return clip.length;
                }
            }
        }
        return -1f;
    }

    public void TryStartParry()
    {
        if (_isParrying) return;
        
        // 이동 불가/입력 막힘 상태일 때도 패리 방지
        if (_player.Stat.Health.IsDead) return;

        // 대시 중일 때는 패리 방지
        if (_player.IsDashing) return;

        // 공격 중일 때는 기존 공격을 캔슬하고 패리 시도
        var meleeCtrl = GetComponent<MeleeCombatController>();
        if (meleeCtrl != null && meleeCtrl.IsAttacking)
        {
            meleeCtrl.CancelAttack();
        }

        // 투척 충전(차징) 중일 때도 캔슬하고 패리 시도
        var throwCtrl = GetComponentInChildren<ThrowController>();
        if (throwCtrl != null && throwCtrl.IsCharging)
        {
            throwCtrl.InputHandler.ResetCharging();
        }

        // 플레이어 액티브 스킬 시전 중일 때도 캔슬하고 패리 시도
        if (_player.IsCastingSkill)
        {
            _player.CancelActiveSkill();
        }

        // 스킬 시전 중일 때도 패리 방지 (지속 시즈 모드 등 액티브스킬 매니저 판정)
        var activeSkillCtrl = GetComponent<ActiveSkillManager>();
        if (activeSkillCtrl != null)
        {
            bool isAnySkillActive = (activeSkillCtrl.SkillSlot1 != null && activeSkillCtrl.SkillSlot1.IsActive) ||
                                    (activeSkillCtrl.SkillSlot2 != null && activeSkillCtrl.SkillSlot2.IsActive);
            if (isAnySkillActive) return;
        }

        // 마우스 조준 방향 벡터 획득
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
        mousePos.z = 0;
        Vector2 mouseDir = (mousePos - transform.position).normalized;

        // 플레이어 바라보는 방향 조준선에 동기화
        if (mouseDir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
        else if (mouseDir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);

        // 패리 시퀀스 시작
        if (_parryCoroutine != null) StopCoroutine(_parryCoroutine);
        _parryCoroutine = StartCoroutine(ParrySequence(mouseDir));
    }

private IEnumerator ParrySequence(Vector2 mouseDir)
    {
        _isParrying = true;
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.Parry, parryMoveSpeedMultiplier); // 감속

        OnParryStart?.Invoke();

        // 1. Play parry animation
        // Lock the Idle/Walk auto-transition (same reason as the attack animation) and reset the cache
        _player.canChangeState = false;
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Parry", "Idle");
        yield return null; // 패링 준비 자세가 최소 1프레임은 화면에 보이도록 강제 대기

        // 2. Draw the visual telegraph sector
        CreateParryVisualSector(mouseDir);

        bool success = false;
        float elapsed = 0f;

        // Scan every frame during the active window
        while (elapsed < parryActiveDuration)
        {
            if (CheckAndDeflectProjectiles(mouseDir))
            {
                success = true;
                break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (success)
        {
            OnParrySuccess?.Invoke();

            // Play the Parry_Success motion, then wait for it before returning to Idle.
            _player.ResetAnimStateCache();
            _player.PlayAllAnim("Parry_Success", "Parry");

            float successClipLength = GetAnimationClipLength("Parry_Success");
            // Always wait at least a minimum time so the success pose is never skipped within the same frame
            yield return new WaitForSeconds(successClipLength > 0f ? successClipLength : parryRecoveryDuration);

            EndParry();
        }
        else
        {
            OnParryFail?.Invoke();
            // 실패 시 후딜레이 돌입 (여전히 IsParrying 상태 및 감속 유지)
            yield return new WaitForSeconds(parryRecoveryDuration);
            EndParry();
        }
    }

    private void EndParry()
    {
        _isParrying = false;
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.Parry); // 이속 복구

        // Release the Idle/Walk transition lock regardless of how the parry animation ends.
        _player.canChangeState = true;
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _parryCoroutine = null;
    }

    private bool CheckAndDeflectProjectiles(Vector2 mouseDir)
    {
        // parryRadius 반경 내의 Collider2D 검색 (Trigger 포함)
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, parryRadius);
        bool deflectedAny = false;
        LayerMask enemyLayer = LayerMask.GetMask("Enemy");

        foreach (var col in colliders)
        {
            if (col == null || col.isTrigger == false) continue;

            Projectile proj = col.GetComponent<Projectile>();
            if (proj == null) continue;

            // 이미 적군을 대상으로 하는 투사체(반사됨)는 다시 반사하지 않음
            if ((proj.TargetLayer.value & (1 << LayerMask.NameToLayer("Enemy"))) != 0)
            {
                continue;
            }

            // 플레이어에서 투사체로의 벡터
            Vector2 dirToProj = ((Vector2)col.transform.position - (Vector2)transform.position).normalized;
            float angle = Vector2.Angle(mouseDir, dirToProj);

            // 160도 범위 이내인지 체크 (사잇각 <= 80도)
            if (angle <= parryAngle / 2f)
            {
                proj.Deflect(gameObject, enemyLayer, mouseDir);
                deflectedAny = true;
            }
        }

        return deflectedAny;
    }

    [SerializeField] private float parryMoveSpeedMultiplier = 0.3f; // 패리 시 이속 배율

    private void CreateParryVisualSector(Vector2 dir)
    {
        // 커스텀 프리팹이 있다면 그것을 사용
        if (parryTelegraphPrefab != null)
        {
            GameObject customGo = Instantiate(parryTelegraphPrefab, transform.position, Quaternion.identity);
            float angleCustom = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            customGo.transform.rotation = Quaternion.Euler(0, 0, angleCustom);
            customGo.transform.localScale = new Vector3(parryRadius, parryRadius, 1f);

            // [수정] 커스텀 프리팹에 콜라이더가 있다면 적의 길을 막지 않게 isTrigger로 강제 변경
            Collider2D[] colliders = customGo.GetComponentsInChildren<Collider2D>();
            foreach (var col in colliders)
            {
                col.isTrigger = true;
            }

            Destroy(customGo, parryActiveDuration);
            return;
        }

        // 프리팹이 없다면 코드로 직접 부채꼴 2D 메쉬 및 라인을 렌더링
        GameObject go = new GameObject("ParryTelegraphSector");
        go.transform.position = transform.position;
        go.transform.rotation = Quaternion.identity;

        MeshFilter meshFilter = go.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();

        // 빌드에서도 안전하게 핑크색 에러 없이 렌더링되게 Sprites/Default 셰이더 적용
        Shader spriteShader = Shader.Find("Sprites/Default");
        Material mat = new Material(spriteShader != null ? spriteShader : Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        mat.color = new Color(0.2f, 0.6f, 1f, 0.4f); // 반투명 파란색 (아군)
        meshRenderer.material = mat;

        // 부채꼴 메쉬 작성
        Mesh mesh = new Mesh();
        int segments = 20;
        int vertexCount = segments + 2;
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero; // 중심점

        float centerAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float startAngle = centerAngle - (parryAngle / 2f);
        float angleStep = parryAngle / segments;

        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle), 0f) * parryRadius;
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;

        // 테두리 외곽선 LineRenderer
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.startWidth = 0.05f;
        lr.endWidth = 0.05f;
        lr.positionCount = vertexCount + 1;
        lr.material = mat;
        lr.startColor = new Color(0.2f, 0.8f, 1f, 0.8f); // 뚜렷한 하늘색 테두리
        lr.endColor = new Color(0.2f, 0.8f, 1f, 0.8f);

        Vector3[] linePositions = new Vector3[vertexCount + 1];
        linePositions[0] = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            linePositions[i + 1] = vertices[i + 1];
        }
        linePositions[vertexCount] = Vector3.zero;
        lr.SetPositions(linePositions);

        Destroy(go, parryActiveDuration);
    }
}
