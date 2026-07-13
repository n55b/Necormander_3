using UnityEngine;

/// <summary>
/// Assets/Resources/Sprites/Arrow.aseprite 안에 이름으로 잘려 있는 서브 스프라이트들을
/// (Ally_Locator / Enemy_Locator / Arrow_Side / Arrow_Diagonal) 최초 1회만 로드해 캐싱한다.
/// 방향 로케이터(<see cref="EntityDirectionIndicator"/>)와 화면 밖 화살표
/// (<see cref="OffscreenEnemyArrowManager"/>)가 함께 쓴다.
///
/// 로케이터: '아래(-Y)를 보고 있는, 꼭짓점 달린 원'. 조준 방향으로 회전시켜 쓴다.
/// 화살표: 화면 밖 적을 가리키는 작은 화살표. 방향으로 회전시켜 쓴다.
/// </summary>
public static class ArrowIndicatorSprites
{
    private const string ResourcePath = "Sprites/Arrow";

    private static bool _loaded;
    private static Sprite _allyLocator;
    private static Sprite _enemyLocator;
    private static Sprite _arrowSide;
    private static Sprite _arrowDiagonal;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        // .aseprite 는 여러 서브 스프라이트(스프라이트시트)로 임포트되므로 LoadAll 로 전부 받아 이름으로 매칭.
        Sprite[] sprites = Resources.LoadAll<Sprite>(ResourcePath);
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError($"[ArrowIndicatorSprites] '{ResourcePath}' 에서 스프라이트를 찾지 못했습니다. (Arrow.aseprite 임포트/경로 확인)");
            return;
        }

        foreach (var s in sprites)
        {
            switch (s.name)
            {
                case "Ally_Locator":    _allyLocator = s;    break;
                case "Enemy_Locator":   _enemyLocator = s;   break;
                case "Arrow_Side":      _arrowSide = s;      break;
                case "Arrow_Diagonal":  _arrowDiagonal = s;  break;
            }
        }

        if (_enemyLocator == null || _allyLocator == null || _arrowSide == null)
            Debug.LogWarning("[ArrowIndicatorSprites] 일부 스프라이트 이름을 못 찾음. Arrow.aseprite 의 슬라이스 이름을 확인하세요 " +
                             "(Ally_Locator / Enemy_Locator / Arrow_Side / Arrow_Diagonal).");
    }

    public static Sprite AllyLocator   { get { EnsureLoaded(); return _allyLocator; } }
    public static Sprite EnemyLocator  { get { EnsureLoaded(); return _enemyLocator; } }
    public static Sprite ArrowSide     { get { EnsureLoaded(); return _arrowSide; } }
    public static Sprite ArrowDiagonal { get { EnsureLoaded(); return _arrowDiagonal; } }
}
