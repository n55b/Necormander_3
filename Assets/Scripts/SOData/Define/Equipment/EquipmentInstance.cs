using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실제로 소지 중인 장비 한 자루(런타임 상태). SO(정의)와 달리 인스턴스별 상태를 갖는다:
///   - rolledSkills: 획득 시 skillPool 에서 뽑아 굳힌 스킬들(대개 2개 = Q/E). "장비 뜰 때 고정".
///   - enhanceLevel: 이 장비의 강화 단계(장비 1개당 정수). 패시브·스킬 강화가 전부 이걸 참조.
///
/// 같은 EquipmentSO 라도 다음에 또 뜨면 다른 스킬 조합일 수 있으므로, 저장은 SO 이름만으론 부족하다 —
/// '굴린 결과(스킬 이름들)'까지 인스턴스에 남긴다(EquipmentSaveData).
/// </summary>
[System.Serializable]
public class EquipmentInstance
{
    public EquipmentSO baseData;
    public List<PlayerSkillSO> rolledSkills = new List<PlayerSkillSO>();
    public int enhanceLevel = 0;

    /// <summary>획득 시 호출. skillPool 에서 skillsToRoll 개를 중복 없이 뽑아 굳힌다.</summary>
    public static EquipmentInstance Roll(EquipmentSO so)
    {
        var inst = new EquipmentInstance { baseData = so };
        if (so == null) return inst;

        var pool = new List<PlayerSkillSO>();
        foreach (var s in so.skillPool) if (s != null) pool.Add(s);

        int n = Mathf.Min(Mathf.Max(0, so.skillsToRoll), pool.Count);
        for (int i = 0; i < n; i++)
        {
            int idx = Random.Range(0, pool.Count);
            inst.rolledSkills.Add(pool[idx]);
            pool.RemoveAt(idx); // 중복 없이
        }
        return inst;
    }

    /// <summary>
    /// 이벤트 방(스킬 교체)용 후보: 풀 전체 − 현재 바인딩된 것 = 아직 안 뽑힌 나머지.
    /// (설계: 5개 풀 중 바인딩 2개를 뺀 나머지 3개를 보상창처럼 띄운다.)
    /// </summary>
    public IEnumerable<PlayerSkillSO> RemainingPool()
    {
        if (baseData == null) yield break;
        foreach (var s in baseData.skillPool)
            if (s != null && !rolledSkills.Contains(s)) yield return s;
    }
}
