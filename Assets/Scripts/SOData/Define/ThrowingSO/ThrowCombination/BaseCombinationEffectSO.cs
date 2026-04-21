using UnityEngine;
using System.Collections.Generic;

namespace Necromancer.Player
{
    /// <summary>
    /// 조합 효과 발동 시 필요한 정보를 담는 컨텍스트입니다.
    /// </summary>
    public struct CombinationContext
    {
        public GameObject leadAttacker;   // 효과를 발생시킨 주체 유닛
        public Vector2 impactPosition;    // 착지/충돌 지점
        public float chargeRatio;         // 투척 차징 비율
        public List<AllyController> supporters; // 강화를 돕는 서포터 유닛들
    }

    /// <summary>
    /// 모든 던지기 조합 효과의 베이스 SO입니다.
    /// </summary>
    public abstract class BaseCombinationEffectSO : ScriptableObject
    {
        /// <summary>
        /// 실제 조합 효과를 실행하는 로직입니다.
        /// </summary>
        public abstract void Execute(CombinationContext context);
    }
}
