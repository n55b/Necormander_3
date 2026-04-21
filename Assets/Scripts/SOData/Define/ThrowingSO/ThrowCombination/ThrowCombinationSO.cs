using UnityEngine;
using System.Collections.Generic;

namespace Necromancer.Player
{
    /// <summary>
    /// 두 미니언의 조합식과 결과 효과를 정의하는 SO입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCombinationData", menuName = "Necromancer/Data/CombinationData")]
    public class ThrowCombinationSO : ScriptableObject
    {
        public string combinationName;

        [Header("핵심 조합 조건 (순서 무관)")]
        public CommandData minionTypeA;
        public CommandData minionTypeB;

        [Header("결과 효과")]
        public BaseCombinationEffectSO combinationEffect;

        [Header("서포터 가능 목록")]
        [SerializeField] private List<CommandData> validSupporters = new List<CommandData>();

        /// <summary>
        /// 두 유닛 타입이 이 조합의 핵심 조건과 일치하는지 확인합니다.
        /// </summary>
        public bool IsMatch(CommandData type1, CommandData type2)
        {
            return (type1 == minionTypeA && type2 == minionTypeB) ||
                   (type1 == minionTypeB && type2 == minionTypeA);
        }

        /// <summary>
        /// 특정 유닛 타입이 이 조합의 서포터로 참여 가능한지 확인합니다.
        /// </summary>
        public bool IsValidSupporter(CommandData type)
        {
            return validSupporters.Contains(type);
        }
    }
}
