using System.Collections.Generic;

/// <summary>
/// 한 유닛의 런타임 스탯 보정치 저장소. CharacterStat 하나당 하나씩 들고 있다.
///
/// 예전엔 StatEventBus 가 ref 파라미터 4개(atk/hp/atkSpd/moveSpd)를 넘기는 방식이었는데,
/// 스탯이 10개로 늘면서 시그니처가 감당이 안 됐다. 그리고 그 버스는 구독자가 한 명도
/// 없어서 실제로는 아무것도 안 하고 있었다. 그래서 키-값 방식으로 갈아엎었다.
///
/// [보정 방식 2가지]
///   Flat    — 고정치 합산. 예: 서브 소환수의 최대 체력 +20
///   Percent — 비율 합산(합연산). 예: 유물 30% + 유물 30% = +60% (곱연산 아님, 기획 확정)
///
/// 최종값은 대체로 (base + Flat) * (1 + Percent) 로 조립한다. 조립은 CharacterStat 이 한다 —
/// 스탯마다 상한이나 반전 같은 사정이 달라서 여기서 일괄 처리하면 오히려 숨는다.
///
/// [source] 보정치를 건 주체(유물 인스턴스 등). 나중에 그 유물만 떼어낼 때 RemoveSource 로
/// 한 번에 걷어낸다. 없애는 쪽을 안 만들어두면 버프가 영원히 눌러앉는다.
/// </summary>
public class StatModifierRegistry
{
    private readonly Dictionary<StatType, float> _flat = new Dictionary<StatType, float>();
    private readonly Dictionary<StatType, float> _percent = new Dictionary<StatType, float>();

    // source -> 그 source 가 건 (스탯, 값, isPercent) 목록
    private readonly Dictionary<object, List<(StatType type, float value, bool percent)>> _bySource
        = new Dictionary<object, List<(StatType, float, bool)>>();

    /// <summary>고정치 보정을 건다. 예: AddFlat(relic, StatType.Health, 20f)</summary>
    public void AddFlat(object source, StatType type, float value) => Add(source, type, value, false);

    /// <summary>비율 보정을 건다. 0.3 = +30%. 같은 스탯에 여러 개면 합연산.</summary>
    public void AddPercent(object source, StatType type, float value) => Add(source, type, value, true);

    private void Add(object source, StatType type, float value, bool percent)
    {
        if (value == 0f) return;

        var table = percent ? _percent : _flat;
        table.TryGetValue(type, out float cur);
        table[type] = cur + value;

        if (source == null) return; // 뗄 수 없는 영구 보정
        if (!_bySource.TryGetValue(source, out var list))
        {
            list = new List<(StatType, float, bool)>();
            _bySource[source] = list;
        }
        list.Add((type, value, percent));
    }

    /// <summary>그 source 가 걸었던 보정치를 전부 되돌린다.</summary>
    public void RemoveSource(object source)
    {
        if (source == null || !_bySource.TryGetValue(source, out var list)) return;

        foreach (var (type, value, percent) in list)
        {
            var table = percent ? _percent : _flat;
            if (!table.TryGetValue(type, out float cur)) continue;
            table[type] = cur - value;
        }
        _bySource.Remove(source);
    }

    /// <summary>합산된 고정치. 없으면 0.</summary>
    public float Flat(StatType type) => _flat.TryGetValue(type, out float v) ? v : 0f;

    /// <summary>합산된 비율. 없으면 0 (= 보정 없음). 0.6 이면 +60%.</summary>
    public float Percent(StatType type) => _percent.TryGetValue(type, out float v) ? v : 0f;

    public void Clear()
    {
        _flat.Clear();
        _percent.Clear();
        _bySource.Clear();
    }
}
