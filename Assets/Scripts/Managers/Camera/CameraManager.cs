using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance;

    private CinemachineImpulseSource _impulseSource;
    private CinemachineCamera _vcam;
    private float _defaultOrthoSize;

    public void Initialize()
    {
        Instance = this;
        _impulseSource = GetComponent<CinemachineImpulseSource>();
        _vcam = GetComponent<CinemachineCamera>();
        if (_vcam != null) _defaultOrthoSize = _vcam.Lens.OrthographicSize;
        Debug.Log("<color=cyan>[CameraManager]</color> Initialized.");
    }

    public void HitShakeCamera()
    {
        _impulseSource.GenerateImpulse();
    }

    public void SetZoom(bool zoomOut)
    {
        if (_vcam != null)
        {
            var lens = _vcam.Lens;
            lens.OrthographicSize = zoomOut ? _defaultOrthoSize * 1.3f : _defaultOrthoSize;
            _vcam.Lens = lens;
        }
    }
}
