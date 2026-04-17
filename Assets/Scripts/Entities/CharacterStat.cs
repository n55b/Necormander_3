using UnityEngine;

public class CharacterStat : MonoBehaviour
{
    [Header("캐릭터 기본 스탯")]
    [SerializeField] float MaxHP = 100f;
    [SerializeField] float curHP;
    [SerializeField] float Atk = 10f;
    [SerializeField] float AtkSpd = 1f;
    [SerializeField] float AtkRange = 2f;
    [SerializeField] float Def = 0f;
    [SerializeField] float MoveSpeed = 5f;
    [SerializeField] bool isDead = false;
    [SerializeField] bool invincible = false;

    public event System.Action OnDamageTaken;

    public float MAXHP => MaxHP;
    public float CURHP => curHP;
    public float ATK => Atk;
    public float ATKSPD => AtkSpd;
    public float ATKRANGE => AtkRange;
    public float DEF => Def;
    public float MOVESPEED => MoveSpeed;
    public bool IsDead => isDead;
    public bool Invincible { get { return invincible; } set { invincible = value; } }

    // [DEBUG] Damage Flash 관련 변수
    private SpriteRenderer _spriteRenderer;
    private Color _originalColor;
    private Coroutine _flashCoroutine;

    void Awake()
    {
        curHP = MaxHP;
        // [DEBUG] 자식 오브젝트를 포함하여 SpriteRenderer를 찾습니다.
        _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (_spriteRenderer != null) _originalColor = _spriteRenderer.color;
    }

    public void GetDamage(DamageInfo info)
    {
        if (isDead || invincible) return;

        float finalDamage = Mathf.Max(info.amount - Def, 1f);
        curHP -= finalDamage;

        OnDamageTaken?.Invoke();

        // [DEBUG] 피해 시각 피드백 (검은색 깜빡임)
        if (_spriteRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(Debug_FlashBlack());
        }

        if (curHP <= 0.0f)
        {
            curHP = 0;
            Die();
        }
    }

    // [DEBUG] 피해 발생 시 잠깐 검은색으로 변하게 하는 코루틴
    private System.Collections.IEnumerator Debug_FlashBlack()
    {
        _spriteRenderer.color = Color.black;
        yield return new UnityEngine.WaitForSeconds(0.1f);
        _spriteRenderer.color = _originalColor;
        _flashCoroutine = null;
    }

    private void Die()
    {
        isDead = true;
        Destroy(this.gameObject);
    }
}
