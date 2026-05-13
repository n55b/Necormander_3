using System.Collections.Generic;
using UnityEngine;

public class AllyManager : MonoBehaviour
{
    [System.Serializable]
    public class MinionInfo
    {
        public int InstanceId;
        public MinionDataSO Data;
        public float RespawnTimer;
        public bool IsDead;

        public MinionInfo(int id, MinionDataSO data)
        {
            InstanceId = id;
            Data = data;
            IsDead = false;
        }
    }

    [Header("아군 유닛들")]
    [SerializeField] List<AllyController> allys = new List<AllyController>();
    [SerializeField] List<MinionInfo> activeMinionInfos = new List<MinionInfo>();

    public event System.Action<MinionInfo> OnAllyRespawnStart;
    public event System.Action<MinionInfo> OnAllyRespawned;
    
    [SerializeField] bool isBattle = false;
    [SerializeField] LayerMask playerLayer;
    [SerializeField] float defaultRespawnTime = 3f;

    private void Update()
    {
        HandleRespawns();
    }

    private void HandleRespawns()
    {
        for (int i = activeMinionInfos.Count - 1; i >= 0; i--)
        {
            var info = activeMinionInfos[i];
            if (info.IsDead)
            {
                info.RespawnTimer -= Time.deltaTime;
                if (info.RespawnTimer <= 0)
                {
                    RespawnMinion(info);
                }
            }
        }
    }

    private void RespawnMinion(MinionInfo info)
    {
        info.IsDead = false;
        OnAllyRespawned?.Invoke(info);
        
        Vector3 spawnPos = transform.position;
        var sumController = GetComponentInParent<SummonController>();
        if (sumController != null)
        {
            var positions = sumController.GetSummonPositions2D(1, 2f);
            if (positions.Count > 0) spawnPos = positions[0];
        }

        AllyController newAlly = InternalSpawn(info.Data, spawnPos);
        if (newAlly != null)
        {
            info.InstanceId = newAlly.gameObject.GetInstanceID();
            Debug.Log($"<color=green>[AllyManager]</color> {info.Data.minionName} 재소환 완료 (ID: {info.InstanceId})");
        }
    }

    public void ReportDeath(int instanceId)
    {
        var info = activeMinionInfos.Find(i => i.InstanceId == instanceId);
        if (info != null && !info.IsDead)
        {
            info.IsDead = true;
            info.RespawnTimer = defaultRespawnTime; // 나중에 보석 등으로 이 시간 조절 가능
            OnAllyRespawnStart?.Invoke(info);
            Debug.Log($"<color=red>[AllyManager]</color> {info.Data.minionName} (ID: {instanceId}) 사망. {info.RespawnTimer}s 후 부활");
        }
    }

    public AllyController SpawnAlly(MinionDataSO data, Vector3 _position)
    {
        AllyController ally = InternalSpawn(data, _position);
        if (ally != null)
        {
            activeMinionInfos.Add(new MinionInfo(ally.gameObject.GetInstanceID(), data));
        }
        return ally;
    }

    private AllyController InternalSpawn(MinionDataSO data, Vector3 _position)
    {
        RemoveNullinAllys();
        if (data == null) return null;

        GameObject obj = GameManager.Instance.dataManager.CreateUnit(data, _position);
        if (obj == null) return null;
        
        AllyController _ally = obj.GetComponent<AllyController>();
        if (_ally != null)
        {
            _ally.player = this.gameObject.transform;
            _ally.SetBattleState(isBattle);
            allys.Add(_ally);
        }
        return _ally;
    }

    public void SetBattleState(bool _bool)
    {
        isBattle = _bool;
        RemoveNullinAllys();
        foreach (var ally in allys)
        {
            ally.SetBattleState(isBattle);
        }
    }

    public bool CheckAllyState()
    {
        RemoveNullinAllys();
        foreach (var ally in allys)
        {
            if (ally == null || ally.Brain == null || ally.Brain.Target == null) continue;
            if (ally.Brain.Target.gameObject.layer != playerLayer) return true;
        }
        return false;
    }

    private void RemoveNullinAllys()
    {
        allys.RemoveAll(item => !item);
    }

    public void ClearAll()
    {
        RemoveNullinAllys();
        var throwController = Object.FindFirstObjectByType<ThrowController>();
        if (throwController != null) throwController.ForceClear();
        foreach (var ally in allys)
        {
            if (ally != null) Destroy(ally.gameObject);
        }
        allys.Clear();
        activeMinionInfos.Clear();
        Debug.Log("<color=red>[AllyManager]</color> All allies cleared.");
    }
}
