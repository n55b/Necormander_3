using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance;

    private CinemachineImpulseSource _impulseSource;

    void Start()
    {
        _impulseSource = GetComponent<CinemachineImpulseSource>();

        Instance = this;
    }

    public void HitShakeCamera()
    {
        _impulseSource.GenerateImpulse();
    }
}
