using UnityEngine;

public class Effect : MonoBehaviour
{
    public void Destroy()
    {
        var pooled = GetComponentInParent<PooledHitEffect>();
        if (pooled != null && pooled.IsManaged) pooled.Release();
        else Destroy(gameObject);
    }
}
