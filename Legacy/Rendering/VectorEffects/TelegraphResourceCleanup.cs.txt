using UnityEngine;

/// <summary>
/// 절차적으로 만든 <see cref="Mesh"/>/<see cref="Material"/>을 오브젝트와 함께 정리하는 꼬리표.
///
/// Unity는 GameObject를 Destroy해도 코드로 만든 Mesh/Material은 자동 해제하지 않는다
/// (렌더러가 물고 있던 것은 <c>sharedMesh</c>/<c>sharedMaterial</c>이라 더욱 그렇다).
/// 본 마스터의 부채꼴 전조(<see cref="BoneMasterTelegraphUtil.SpawnCone"/>)는 근접전 내내
/// <b>기본 공격 한 번마다 한 쌍씩</b> 만들어지므로, 안 치우면 전투가 길어질수록 계속 쌓인다.
///
/// 전조는 코루틴의 Destroy, 부위파괴 시의 CleanupDanglingTelegraphs, 보스 사망 등
/// 여러 경로로 사라진다. 어느 경로로 죽든 OnDestroy는 반드시 지나가므로 여기에 붙여 둔다.
/// </summary>
public class TelegraphResourceCleanup : MonoBehaviour
{
    [SerializeField] private Mesh mesh;
    [SerializeField] private Material material;

    public void Track(Mesh m, Material mat)
    {
        mesh = m;
        material = mat;
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
