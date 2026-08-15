using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 우클릭 아이콘 스프라이트가 아직 없을 때 쓰는 임시 아이콘 생성기.
///
/// 아트가 나오기 전까지 슬롯/NPC 목록이 전부 빈칸으로 보이는 걸 막는 용도다.
/// <see cref="RightClickDataSO.icon"/> 에 스프라이트를 꽂으면 이 경로는 아예 안 탄다 —
/// 폴백은 '없을 때만' 동작해야지, 진짜 아이콘 위에 겹쳐 그리면 안 된다.
///
/// 색은 그 우클릭의 텔레그래프 부채꼴 색(sectorColor)을 그대로 쓴다. 그래야 목록에서 본 색과
/// 실제로 바닥에 깔리는 부채꼴 색이 일치해서, 아이콘이 임시여도 학습에 도움이 된다.
///
/// 색당 하나만 만들어 캐시한다. 매 프레임 호출돼도(UI 갱신) 텍스처가 쌓이지 않는다.
/// </summary>
public static class RightClickIconFactory
{
    private const int Size = 32;
    private const int Border = 3;
    private const int CornerCut = 4; // 모서리를 깎아 '아이콘'처럼 보이게 한다(단순 사각형은 미완성으로 읽힌다)

    private static readonly Dictionary<uint, Sprite> _cache = new Dictionary<uint, Sprite>();

    public static Sprite Get(Color color)
    {
        uint key = Key(color);
        if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

        var sprite = Build(color);
        _cache[key] = sprite;
        return sprite;
    }

    /// <summary>Color32 4바이트를 그대로 키로 쓴다. float 색을 키로 쓰면 미세한 오차로 캐시가 계속 빗나간다.</summary>
    private static uint Key(Color color)
    {
        Color32 c = color;
        return (uint)(c.r << 24 | c.g << 16 | c.b << 8 | c.a);
    }

    private static Sprite Build(Color color)
    {
        // 테두리는 원색보다 밝게, 안쪽은 살짝 어둡게 — 납작한 단색보다 아이콘으로 읽힌다.
        Color edge = new Color(Mathf.Min(1f, color.r * 1.4f + 0.15f),
                               Mathf.Min(1f, color.g * 1.4f + 0.15f),
                               Mathf.Min(1f, color.b * 1.4f + 0.15f), 1f);
        Color fill = new Color(color.r * 0.75f, color.g * 0.75f, color.b * 0.75f, 1f);
        Color clear = new Color(0f, 0f, 0f, 0f);

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // 픽셀아트 프로젝트라 보간하면 혼자 뭉개져 보인다
            wrapMode = TextureWrapMode.Clamp,
            // 씬에 저장되거나 에디터 종료 시 경고를 뱉지 않게 한다(런타임 생성물).
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                pixels[y * Size + x] = PixelAt(x, y, edge, fill, clear);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);

        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        sprite.name = "RightClickIcon(auto)";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Color PixelAt(int x, int y, Color edge, Color fill, Color clear)
    {
        // 네 모서리를 대각으로 깎는다. 가장 가까운 모서리까지의 맨해튼 거리로 판정.
        int dx = Mathf.Min(x, Size - 1 - x);
        int dy = Mathf.Min(y, Size - 1 - y);
        if (dx + dy < CornerCut) return clear;

        // 깎인 대각선 바로 안쪽 + 상하좌우 가장자리가 테두리다.
        bool onEdge = dx < Border || dy < Border || dx + dy < CornerCut + Border;
        return onEdge ? edge : fill;
    }
}
