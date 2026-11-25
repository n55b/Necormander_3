using UnityEngine;

/// <summary>
/// Arrow_Indicator.png의 대각 화살표(0), 옆 화살표(1), 하단 로케이터(2)를
/// 최초 1회만 로드해 캐싱한다.
/// 방향 로케이터(<see cref="EntityDirectionIndicator"/>)와 화면 밖 화살표
/// (<see cref="OffscreenEnemyArrowManager"/>)가 함께 쓴다.
///
/// 로케이터: '아래(-Y)를 보고 있는, 꼭짓점 달린 원'. 조준 방향으로 회전시켜 쓴다.
/// 화살표: 화면 밖 적을 가리키는 작은 화살표. 방향으로 회전시켜 쓴다.
/// </summary>
public static class ArrowIndicatorSprites
{
    private const string ResourcePath = "Sprites/UI/Arrow_Indicator";

    private static bool _loaded;
    private static Sprite _allyLocator;
    private static Sprite _enemyLocator;
    private static Sprite _arrowSide;
    private static Sprite _arrowDiagonal;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        _loaded = false;
        _allyLocator = _enemyLocator = _arrowSide = _arrowDiagonal = null;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        // PNG의 Sprite Editor에서 저작한 슬라이스를 이름으로 찾는다.
        Sprite[] sprites = Resources.LoadAll<Sprite>(ResourcePath);
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError($"[ArrowIndicatorSprites] '{ResourcePath}' 에서 스프라이트를 찾지 못했습니다.");
            return;
        }

        foreach (var s in sprites)
        {
            switch (s.name)
            {
                case "Arrow_Indicator_2": _allyLocator = _enemyLocator = s; break;
                case "Arrow_Indicator_1": _arrowSide = s; break;
                case "Arrow_Indicator_0": _arrowDiagonal = s; break;
            }
        }

        if (_enemyLocator == null || _allyLocator == null || _arrowSide == null)
            Debug.LogWarning("[ArrowIndicatorSprites] Arrow_Indicator PNG의 0 / 1 / 2 슬라이스를 확인하세요.");
    }

    public static Sprite AllyLocator   { get { EnsureLoaded(); return _allyLocator; } }
    public static Sprite EnemyLocator  { get { EnsureLoaded(); return _enemyLocator; } }
    public static Sprite ArrowSide     { get { EnsureLoaded(); return _arrowSide; } }
    public static Sprite ArrowDiagonal { get { EnsureLoaded(); return _arrowDiagonal; } }
}
