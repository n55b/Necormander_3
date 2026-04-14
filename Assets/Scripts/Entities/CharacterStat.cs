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

    public float MAXHP => MaxHP;
    public float CURHP => curHP;
    public float ATK => Atk;
    public float ATKSPD => AtkSpd;
    public float ATKRANGE => AtkRange;
    public float DEF => Def;
    public float MOVESPEED => MoveSpeed;
    public bool IsDead => isDead;

    void Awake()
    {
        curHP = MaxHP;
    }

    public void GetDamage(DamageInfo info)
    {
        if (isDead) return;

        float finalDamage = Mathf.Max(info.amount - Def, 1f);
        curHP -= finalDamage;

        Debug.Log($"{gameObject.name}이(가) {finalDamage}의 {info.type} 데미지를 입었습니다. (남은 HP: {curHP})");

        if (curHP <= 0.0f)
        {
            curHP = 0;
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        Destroy(this.gameObject);
        Debug.Log($"<color=red><b>[DEATH]</b></color> {gameObject.name} 사망!");
    }
}
