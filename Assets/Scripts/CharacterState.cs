using System.ComponentModel;
using UnityEngine;

public class CharacterState:MonoBehaviour
{
    [Header("캐릭터 기본 스탯")]
    [SerializeField] float MaxHP;
    [SerializeField] float curHP;
    [SerializeField] float Atk;
    [SerializeField] float AtkSpd;
    [SerializeField] float AtkRange;
    [SerializeField] float Def;
    [SerializeField] float MoveSpeed;

    public float MAXHP {get {return MaxHP;}}
    public float CURHP {get {return curHP;}}
    public float ATK {get {return Atk;}}
    public float ATKSPD{get {return AtkSpd;}}
    public float ATKRANGE{get {return AtkRange;}}
    public float DEF {get {return Def;}}
    public float MOVESPEED {get {return MoveSpeed;}}
}
