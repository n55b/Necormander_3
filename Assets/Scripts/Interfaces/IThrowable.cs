using UnityEngine;

namespace Necromancer.Interfaces
{
    public interface IThrowable
    {
        void OnPickedUp();

        /// <summary>
        /// 오브젝트가 던져졌을 때 호출됩니다.
        /// </summary>
        /// <param name="targetPosition">마우스 클릭 지점 (목표 위치)</param>
        /// <param name="chargeRatio">차징 정도 (0~1)</param>
        void OnThrown(Vector2 targetPosition, float chargeRatio);

        void OnLanded();
    }
}
