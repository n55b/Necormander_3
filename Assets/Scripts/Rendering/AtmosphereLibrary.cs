using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Pixel atmosphere profiles")]
public sealed class AtmosphereLibrary : ScriptableObject
{
    [Serializable] public struct Look
    {
        public Color ambient;
        [Tooltip("안개 속 직사광과 바닥에 맺히는 빛의 색")] public Color shaftColor;
        [Tooltip("직사광이 닿지 않는 곳의 간접광 색")] public Color shadeColor;
        [Range(0, 1)] public float depth;
        [Tooltip("바닥에 드리우는 구름 그림자의 최대 농도")] [Range(0, 1)] public float cloudShadow;
        [Min(0)] public float fog, glow, shafts, wind;
        [Tooltip("환경 입자의 밀도와 원경의 발광 입자량")] [Range(0, 2)] public float airborne;
        [Tooltip("팔레트 배합: X는 민트, Y는 장밋빛, 기본색은 은청색")] public Vector2 airPalette;
        [Min(.1f)] public float globalIntensity;
        public static Look Blend(Look from, Look to, float t) => new Look
        {
            depth = Mathf.Lerp(from.depth, to.depth, t),
            cloudShadow = Mathf.Lerp(from.cloudShadow, to.cloudShadow, t),
            shaftColor = Color.Lerp(from.shaftColor, to.shaftColor, t),
            shadeColor = Color.Lerp(from.shadeColor, to.shadeColor, t),
            globalIntensity = Mathf.Lerp(from.globalIntensity, to.globalIntensity, t),
            airborne = Mathf.Lerp(from.airborne, to.airborne, t), airPalette = Vector2.Lerp(from.airPalette, to.airPalette, t),
            ambient = Color.Lerp(from.ambient, to.ambient, t), fog = Mathf.Lerp(from.fog, to.fog, t),
            glow = Mathf.Lerp(from.glow, to.glow, t), shafts = Mathf.Lerp(from.shafts, to.shafts, t), wind = Mathf.Lerp(from.wind, to.wind, t)
        };
    }
    public Look village, dungeon, boss, sanctuary, portal;
    [Min(.01f)] public float blendSeconds = .45f;

    public Look Resolve(string scene, RoomInstance room, out string origin)
    {
        if (scene == "VillageScene") { origin = "마을"; return village; }
        if (room != null && room.roomType == RoomType.Boss) { origin = "보스 방"; return boss; }
        if (room != null && (room.roomType == RoomType.Shop || room.roomType == RoomType.Reward || room.roomType == RoomType.EnhanceShop))
        { origin = "상점·보상 방"; return sanctuary; }
        origin = "던전"; return dungeon;
    }
}
