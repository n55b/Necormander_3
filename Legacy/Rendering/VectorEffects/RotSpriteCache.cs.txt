using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도트 스프라이트를 RotSprite 방식으로 미리 N방향 회전시켜 캐싱한다.
///
/// 엔진 회전(transform.rotation)은 원본 도트를 저해상도 픽셀 RT 격자에 최근접 샘플링으로 찍기 때문에
/// 각도마다 픽셀이 빠지거나 겹쳐서 뭉개져 보인다. RotSprite 는
///   1) Scale2x(EPX) 를 3번 적용해 8배 확대 (외곽선 계단을 매끈한 대각선으로 다듬음)
///   2) 그 8배 이미지를 회전시켜 원래 크기로 최근접 샘플링
/// 해서 각 각도 결과가 손으로 찍은 도트처럼 깔끔하다.
///
/// 결과 스프라이트는 회전 0 으로 그리면 되므로 픽셀 RT 안에서 그대로 Y 정렬된다.
/// 같은 원본 스프라이트·방향 수는 한 번만 만들어 모든 인스턴스(플레이어/적)가 공유한다.
/// </summary>
public static class RotSpriteCache
{
    private static readonly Dictionary<(int, int), Sprite[]> Cache = new Dictionary<(int, int), Sprite[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Cache.Clear();

    /// <summary>
    /// source 를 0°부터 반시계 방향으로 360/steps 간격 회전시킨 스프라이트 배열. (index i = i * 360/steps 도)
    /// 원본 피벗이 결과 스프라이트의 피벗이 되므로 transform 회전과 같은 점을 중심으로 돈다.
    /// </summary>
    public static Sprite[] Get(Sprite source, int steps)
    {
        if (source == null || steps < 1) return null;
        var key = (source.GetInstanceID(), steps);
        if (Cache.TryGetValue(key, out var cached) && cached != null && cached.Length > 0 && cached[0] != null)
            return cached;

        Color32[] src = ReadSpritePixels(source, out int w, out int h);
        if (src == null) return null;

        // 1) Scale2x × 3 → 8배
        Color32[] big = src; int bw = w, bh = h;
        for (int i = 0; i < 3; i++) big = Scale2x(big, ref bw, ref bh);

        // 2) 회전 결과 캔버스: 피벗에서 가장 먼 모서리까지 거리 + 여유 1px
        Vector2 pivot = source.pivot; // 스프라이트 rect 기준 픽셀 좌표
        float maxDist = 0f;
        maxDist = Mathf.Max(maxDist, pivot.magnitude);
        maxDist = Mathf.Max(maxDist, (pivot - new Vector2(w, 0)).magnitude);
        maxDist = Mathf.Max(maxDist, (pivot - new Vector2(0, h)).magnitude);
        maxDist = Mathf.Max(maxDist, (pivot - new Vector2(w, h)).magnitude);
        int R = Mathf.CeilToInt(maxDist) + 1;
        int N = 2 * R + 1;
        // 원본 피벗의 픽셀 내 소수부를 유지 → 0° 결과가 원본과 픽셀 단위로 완전히 같다.
        float fx = pivot.x - Mathf.Floor(pivot.x);
        float fy = pivot.y - Mathf.Floor(pivot.y);
        Vector2 outPivot = new Vector2(R + fx, R + fy);

        var result = new Sprite[steps];
        var buffer = new Color32[N * N];
        for (int s = 0; s < steps; s++)
        {
            float rad = s * (2f * Mathf.PI / steps);
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 출력 픽셀 중심을 -θ 회전해서 원본 좌표로 역매핑
                float dx = x + 0.5f - outPivot.x;
                float dy = y + 0.5f - outPivot.y;
                float sx = pivot.x + ( dx * cos + dy * sin);
                float sy = pivot.y + (-dx * sin + dy * cos);
                int ix = Mathf.FloorToInt(sx * 8f);
                int iy = Mathf.FloorToInt(sy * 8f);
                buffer[y * N + x] = (ix >= 0 && iy >= 0 && ix < bw && iy < bh) ? big[iy * bw + ix] : default;
            }

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = source.name + "_rot" + s,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(buffer);
            tex.Apply(false, true);

            var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(outPivot.x / N, outPivot.y / N),
                                   source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sp.name = tex.name;
            sp.hideFlags = HideFlags.DontSave;
            result[s] = sp;
        }

        Cache[key] = result;
        return result;
    }

    /// <summary>스프라이트 영역 픽셀을 읽는다. Read/Write 꺼진(압축) 텍스처도 GPU 복사로 읽는다.</summary>
    private static Color32[] ReadSpritePixels(Sprite sprite, out int w, out int h)
    {
        Rect r = sprite.textureRect;
        w = Mathf.RoundToInt(r.width);
        h = Mathf.RoundToInt(r.height);
        int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y);
        Texture2D tex = sprite.texture;

        if (tex.isReadable)
        {
            try
            {
                var block = tex.GetPixels(x0, y0, w, h);
                var outPx = new Color32[block.Length];
                for (int i = 0; i < block.Length; i++) outPx[i] = block[i];
                return outPx;
            }
            catch { /* 포맷 문제면 아래 GPU 경로로 */ }
        }

        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        rt.filterMode = FilterMode.Point;
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var readTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        readTex.ReadPixels(new Rect(x0, y0, w, h), 0, 0, false);
        readTex.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        var px = readTex.GetPixels32();
        Object.Destroy(readTex);
        return px;
    }

    /// <summary>Scale2x(EPX). 색이 완전히 같은 이웃끼리만 대각선을 이어 도트 외곽을 매끈하게 2배 확대.</summary>
    private static Color32[] Scale2x(Color32[] src, ref int w, ref int h)
    {
        int ow = w * 2, oh = h * 2;
        var dst = new Color32[ow * oh];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color32 P = src[y * w + x];
            // y 가 위로 증가(텍스처 좌표)라서 '위' 이웃은 y+1
            Color32 A = y + 1 < h ? src[(y + 1) * w + x] : P; // 위
            Color32 B = x + 1 < w ? src[y * w + x + 1] : P;   // 오른쪽
            Color32 C = x - 1 >= 0 ? src[y * w + x - 1] : P;  // 왼쪽
            Color32 D = y - 1 >= 0 ? src[(y - 1) * w + x] : P; // 아래

            Color32 tl = P, tr = P, bl = P, br = P;
            if (!Eq(A, D) && !Eq(C, B))
            {
                if (Eq(C, A)) tl = A;
                if (Eq(A, B)) tr = B;
                if (Eq(D, C)) bl = C;
                if (Eq(B, D)) br = D;
            }

            int ox = x * 2, oy = y * 2;
            dst[oy * ow + ox]           = bl;
            dst[oy * ow + ox + 1]       = br;
            dst[(oy + 1) * ow + ox]     = tl;
            dst[(oy + 1) * ow + ox + 1] = tr;
        }
        w = ow; h = oh;
        return dst;
    }

    private static bool Eq(Color32 a, Color32 b)
    {
        // 완전 투명끼리는 RGB 가 달라도 같은 색으로 본다.
        if (a.a == 0 && b.a == 0) return true;
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }
}
