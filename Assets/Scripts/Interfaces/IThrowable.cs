using UnityEngine;

public interface IThrowable
{
    // [추가] 유닛 정보를 인터페이스 수준에서 제공
    CommandData MinionType { get; }
    MinionDataSO MinionData { get; }
    Transform transform { get; }

    void OnPickedUp();

    /// <summary>
    /// 오브젝트가 던져졌을 때 호출됩니다.
    /// </summary>
    /// <param name="targetPosition">마우스 클릭 지점 (목표 위치)</param>
    /// <param name="chargeRatio">차징 정도 (0~1)</param>
    void OnThrown(Vector2 targetPosition, float chargeRatio);

    void OnLanded();

    // [추가] 투척 물리 수치 (성장 수치 연동용)
    float MaxSpeed { get; }
    float MinSpeed { get; }
    float FullChargeSpeed { get; }
    float JumpHeight { get; }
    float StraightHeight { get; }

    // [추가] 클러스터 투척 연동용
    void PrepareForClusterThrow(float chargeRatio, bool isDirect);
    void SetImpacted(bool value);
}
