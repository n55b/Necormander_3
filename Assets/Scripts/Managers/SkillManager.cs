using UnityEngine;
using System.Collections.Generic;
using Necromancer.Skills;
using System;

namespace Necromancer.Managers
{
    public class SkillManager : MonoBehaviour
    {
        public static SkillManager Instance { get; private set; }

        public enum SkillSlot { Q = 0, E = 1, R = 2 }

        [Header("Equipped Minion Data")]
        [SerializeField] private MinionDataSO[] equippedMinions = new MinionDataSO[3];

        [Header("Bullet Time Settings")]
        public float bulletTimeScale = 0.05f;
        
        // 내부 런타임 변수
        private bool isBulletTimeActive = false;
        private Queue<SkillTriggerEvent> triggerQueue = new Queue<SkillTriggerEvent>();
        private SkillTriggerEvent currentTriggerEvent;

        // UI 갱신 이벤트 (차후 구현)
        public event Action<bool> OnBulletTimeChanged;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            if (isBulletTimeActive && currentTriggerEvent != null)
            {
                currentTriggerEvent.timer -= Time.unscaledDeltaTime;
                if (currentTriggerEvent.timer <= 0f)
                {
                    EndBulletTime();
                }
                else
                {
                    // 불렛 타임 스킵 체크 (마우스 좌클릭)
                    if (Input.GetMouseButtonDown(0))
                    {
                        Debug.Log("<color=yellow>[SkillManager]</color> Bullet Time Skipped!");
                        EndBulletTime();
                    }
                }
            }

            // [테스트용] 디버그 트리거 발생 (에디터 전용 혹은 테스트용)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log("<color=red>[Debug]</color> 강제 패링 트리거 발생!");
                OnTriggerEvent(SkillTriggerType.Parry);
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log("<color=red>[Debug]</color> 강제 HardCC 트리거 발생!");
                OnTriggerEvent(SkillTriggerType.HardCC);
            }
#endif
        }

        // 미니언 습득 시 슬롯에 장착
        public void EquipMinion(SkillSlot slot, MinionDataSO data)
        {
            equippedMinions[(int)slot] = data;
            Debug.Log($"<color=cyan>[SkillManager]</color> {slot} 슬롯에 {data.minionName} 장착됨.");
        }

        // 미니언이 가진 특정 트리거가 있는지 검사하여 발동 가능한 슬롯 리스트 반환
        private List<SkillSlot> GetMatchingSlots(SkillTriggerType triggerType)
        {
            List<SkillSlot> matchingSlots = new List<SkillSlot>();
            for (int i = 0; i < 3; i++)
            {
                var minionData = equippedMinions[i];
                if (minionData != null && minionData.minionSkill != null)
                {
                    if (minionData.minionSkill.triggerType == triggerType)
                    {
                        matchingSlots.Add((SkillSlot)i);
                    }
                }
            }
            return matchingSlots;
        }

        // 외부에서 상황(트리거) 발생 시 호출
        public void OnTriggerEvent(SkillTriggerType triggerType)
        {
            var matchingSlots = GetMatchingSlots(triggerType);
            if (matchingSlots.Count > 0)
            {
                // 제일 지속시간이 긴 것을 기준으로 타임아웃 설정 (또는 고정값 사용 가능)
                float duration = 2.0f; 
                foreach(var slot in matchingSlots)
                {
                    float d = equippedMinions[(int)slot].minionSkill.triggerDuration;
                    if (d > duration) duration = d;
                }

                SkillTriggerEvent newEvent = new SkillTriggerEvent(triggerType, duration, matchingSlots);
                triggerQueue.Enqueue(newEvent);

                if (!isBulletTimeActive)
                {
                    ProcessNextTrigger();
                }
            }
        }

        private void ProcessNextTrigger()
        {
            if (triggerQueue.Count > 0)
            {
                currentTriggerEvent = triggerQueue.Dequeue();
                StartBulletTime();
            }
        }

        private void StartBulletTime()
        {
            isBulletTimeActive = true;
            Time.timeScale = bulletTimeScale;
            Debug.Log($"<color=magenta>[SkillManager]</color> 불렛 타임 진입! 트리거: {currentTriggerEvent.triggerType}");
            OnBulletTimeChanged?.Invoke(true);
        }

        public void EndBulletTime()
        {
            Time.timeScale = 1.0f;
            isBulletTimeActive = false;
            currentTriggerEvent = null;
            Debug.Log("<color=magenta>[SkillManager]</color> 불렛 타임 종료.");
            OnBulletTimeChanged?.Invoke(false);

            // 다음 큐에 대기 중인 이벤트가 있으면 연달아 처리
            if (triggerQueue.Count > 0)
            {
                ProcessNextTrigger();
            }
        }

        public bool IsBulletTimeActive() => isBulletTimeActive;

        public SkillTriggerEvent GetCurrentTrigger() => currentTriggerEvent;
        public MinionDataSO GetEquippedMinion(SkillSlot slot) => equippedMinions[(int)slot];

        // 플레이어 상시 스킬 발동
        public void ExecutePlayerSkill(SkillSlot slot, Transform playerTransform)
        {
            var minionData = equippedMinions[(int)slot];
            if (minionData != null && minionData.playerSkill != null)
            {
                minionData.playerSkill.ExecuteSkill(playerTransform);
            }
        }

        // 불렛 타임 중 미니언 연계 스킬 발동
        public void ExecuteMinionSkill(SkillSlot slot, Transform playerTransform)
        {
            if (!isBulletTimeActive || currentTriggerEvent == null) return;

            // 현재 발동 가능한 슬롯인지 확인
            if (currentTriggerEvent.availableSlots.Contains(slot))
            {
                var minionData = equippedMinions[(int)slot];
                if (minionData != null && minionData.minionSkill != null)
                {
                    minionData.minionSkill.ExecuteSkill(playerTransform);
                    Debug.Log($"<color=green>[SkillManager]</color> {slot} 연계 스킬 발동 성공!");
                    EndBulletTime(); // 발동 후 즉시 불렛 타임 종료
                }
            }
            else
            {
                Debug.Log($"<color=orange>[SkillManager]</color> {slot} 슬롯은 현재 활성화된 트리거({currentTriggerEvent.triggerType})와 일치하지 않습니다.");
            }
        }
    }

    public class SkillTriggerEvent
    {
        public SkillTriggerType triggerType;
        public float timer;
        public List<SkillManager.SkillSlot> availableSlots;

        public SkillTriggerEvent(SkillTriggerType type, float duration, List<SkillManager.SkillSlot> slots)
        {
            this.triggerType = type;
            this.timer = duration;
            this.availableSlots = slots;
        }
    }
}
