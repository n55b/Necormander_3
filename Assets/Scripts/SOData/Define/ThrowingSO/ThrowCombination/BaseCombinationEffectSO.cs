using UnityEngine;
using System.Collections.Generic;

namespace Necromancer.Player
{
    /// <summary>
    /// ì¡°í•© ?¨ê³¼ ë°œë™ ???„ìš”???•ë³´ë¥??´ëŠ” ì»¨í…?¤íŠ¸?…ë‹ˆ??
    /// </summary>
    public struct CombinationContext
    {
        public GameObject leadAttacker;   // ?¨ê³¼ë¥?ë°œìƒ?œí‚¨ ì£¼ì²´ ? ë‹›
        public Vector2 impactPosition;    // ì°©ì?/ì¶©ëŒ ì§€??
        public float chargeRatio;         // ?¬ì²™ ì°¨ì§• ë¹„ìœ¨
        public List<AllyController> supporters; // ê°•í™”ë¥??•ëŠ” ?œí¬??? ë‹›??
    }

    /// <summary>
    /// ëª¨ë“  ?˜ì?ê¸?ì¡°í•© ?¨ê³¼??ë² ì´??SO?…ë‹ˆ??
    /// </summary>
    public abstract class BaseCombinationEffectSO : ScriptableObject
    {
        /// <summary>
        /// ?¤ì œ ì¡°í•© ?¨ê³¼ë¥??¤í–‰?˜ëŠ” ë¡œì§?…ë‹ˆ??
        /// </summary>
        public abstract void Execute(CombinationContext context);
    }
}
