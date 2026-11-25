using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(menuName = "Rendering/Terrain Relief")]
public sealed class TerrainReliefProfile : ScriptableObject
{
    [Tooltip("앞면이 반복되는 바위 타일")] public TileBase cliffFace;
    [Tooltip("바위와 잔디가 만나는 반투명 밑단 타일")] public TileBase cliffFoot;
    [Range(0, 1)] public float footInset = .6875f;
    [Range(.25f, 4)] public float wallHeight = 1f;
}
