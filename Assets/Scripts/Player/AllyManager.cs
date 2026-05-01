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
    
    [SerializeField] bool isBattle = false;
    [SerializeField] LayerMask playerLayer;
    [SerializeField] float defaultRespawnTime = 3f;

    private void Update()
    {
        HandleRespawns();
    }

    private void HandleRespawns()
    {
        foreach (var info in activeMinionInfos)
        {
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
        
        // 플레이어 주변 위치 계산
        Vector3 spawnPos = transform.position;
        var sumController = GetComponent<SummonController>();
        if (sumController != null)
        {
            var positions = sumController.GetSummonPositions2D(1, 2f);
            if (positions.Count > 0) spawnPos = positions[0];
        }

        // 실제 소환 (기존 SpawnAlly 로직 활용하되 Info는 업데이트)
        AllyController newAlly = InternalSpawn(info.Data, spawnPos);
        if (newAlly != null)
        {
            info.InstanceId = newAlly.gameObject.GetInstanceID();
            Debug.Log($"<color=green>[AllyManager]</color> {info.Data.minionName} 재소환 완료 (ID: {info.InstanceId})");
        }
    }

    // 사망 보고 (CharacterStat에서 호출)
    public void ReportDeath(int instanceId)
    {
        var info = activeMinionInfos.Find(i => i.InstanceId == instanceId);
        if (info != null)
        {
            info.IsDead = true;
            info.RespawnTimer = defaultRespawnTime;
            Debug.Log($"<color=red>[AllyManager]</color> {info.Data.minionName} (ID: {instanceId}) 사망 확인. {defaultRespawnTime}초 후 부활합니다.");
        }
        else
        {
            Debug.LogWarning($"<color=orange>[AllyManager]</color> ID {instanceId}에 해당하는 유닛 정보를 찾을 수 없어 부활시키지 못했습니다. (관리 리스트에 없음)");
        }
    }

    // 아군 유닛 소환 함수
    public AllyController SpawnAlly(MinionDataSO data, Vector3 _position)
    {
        AllyController ally = InternalSpawn(data, _position);
        if (ally != null)
        {
            // 새로운 관리 정보 추가
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

    // 아군 전투 중인지 상태 받아서 유닛들에게 뿌려주는 함수
    public void SetBattleState(bool _bool)
    {
        isBattle = _bool;

        RemoveNullinAllys(); // 리스트 정리

        foreach (var ally in allys)
        {
            ally.SetBattleState(isBattle);
        }
    }

    // 아군 유닛들이 전투 중인지 확인하는 함수
    public bool CheckAllyState()
    {
        RemoveNullinAllys();

        foreach (var ally in allys)
        {
            // 1. ally 및 브레인 유효성 체크
            if (ally == null || ally.Brain == null) continue;

            // 2. 현재 타겟이 없으면 전투 중이 아닌 것으로 간주 (Pass)
            if (ally.Brain.Target == null) continue;

            // 3. 타겟이 플레이어가 아니라면 (즉, 적군을 조준 중이라면) 전투 중으로 판단
            if (ally.Brain.Target.gameObject.layer != playerLayer)
            {
                return true;
            }
        }

        return false;
    }

    // 아군 리스트 null이 된 개체들 삭제
    private void RemoveNullinAllys()
    {
        allys.RemoveAll(item => !item);
    }
}
