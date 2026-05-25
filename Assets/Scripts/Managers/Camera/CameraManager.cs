using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance;

    private CinemachineImpulseSource _impulseSource;

    public void Initialize()
    {
        Instance = this;
        _impulseSource = GetComponent<CinemachineImpulseSource>();
        Debug.Log("<color=cyan>[CameraManager]</color> Initialized.");
    }

    public void HitShakeCamera()
    {
        _impulseSource.GenerateImpulse();
    }
}
