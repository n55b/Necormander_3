using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EnemyAIDecisionCheck
{
    private const BindingFlags InstanceField = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PatternPath = "Assets/SOData/Enemy/Enemy AI Patterns/";

    [MenuItem("Tools/Combat/Verify Enemy AI Decisions")]
    public static void Run()
    {
        Check(EnemyAIDecisions.StandardState(AIState.Follow, false, true, false, 2f, 2f, 2f, true)
            == AIState.Attack, "normal attack includes the range boundary");
        Check(EnemyAIDecisions.StandardState(AIState.Follow, false, true, true, 2f, 2f, 3f, true)
            == AIState.Idle, "escort keeps distance even with attack ready");
        Check(EnemyAIDecisions.Follow(true, false, false) == EnemyFollowAction.Wait, "ranged cooldown waits outside retreat range");
        Check(EnemyAIDecisions.Follow(true, true, false) == EnemyFollowAction.ContinueRetreat, "an active retreat path is preserved");
        Check(EnemyAIDecisions.Follow(true, true, true) == EnemyFollowAction.BeginRetreat, "an arrived retreat can request a new path");
        Check(EnemyAIDecisions.Follow(false, true, true) == EnemyFollowAction.Chase, "ready ranged and melee enemies chase");
        Check(EnemyAIDecisions.Elite(false, true, true) == EliteAction.None, "elite does not restart an active basic attack");
        Check(EnemyAIDecisions.Elite(true, false, true) == EliteAction.Special, "elite special wins when both attacks are ready");
        Check(EnemyAIDecisions.Elite(false, false, true) == EliteAction.BasicAttack, "elite basic attack is used before the special is due");
        Check(EnemyAIDecisions.Elite(false, false, false) == EliteAction.Chase, "elite chases while neither attack is due");
        Check(EnemyAIDecisions.BoneMasterPhaseTwo(false, false, true) == AIState.Skill, "phase two opens with execution inside range during recovery");

        var scene = EditorSceneManager.NewPreviewScene();
        var standard = LoadPattern<ChargerAIPatternSO>("Enemy Charger Pattern.asset");
        var phaseOne = LoadPattern<BoneMasterAIPatternSO>("Boss/Bone Master AI Pattern.asset");
        var phaseTwo = LoadPattern<BoneMasterPhase2AIPatternSO>("Boss/Bone Master Phase 2 AI Pattern.asset");
        try
        {
            var go = new GameObject("AI decision fixture");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<SpriteRenderer>();
            var entity = go.AddComponent<BoneMasterController>();
            var stats = go.AddComponent<CharacterStat>();
            typeof(BaseEntity).GetField("_stats", InstanceField).SetValue(entity, stats);
            var target = new GameObject("Target");
            target.transform.SetParent(go.transform);

            standard.Init(entity);
            CheckTransition(standard, entity, AIState.Idle, "normal enemy without a target");
            entity.Target = target.transform;
            target.transform.position = Vector3.right * (stats.ATKRANGE + 1f);
            CheckTransition(standard, entity, AIState.Follow, "normal enemy pursues outside attack range");
            target.transform.position = Vector3.right * .5f;
            entity.AtkTimer = 0f;
            CheckTransition(standard, entity, AIState.Follow, "normal enemy pursues during cooldown");
            entity.AtkTimer = stats.AttackInterval;
            CheckTransition(standard, entity, AIState.Attack, "normal enemy attacks when its cooldown ends");
            entity.IsAttacking = true;
            entity.Target = null;
            CheckTransition(standard, entity, AIState.Attack, "normal attack retains the state until completion");
            entity.IsAttacking = false;

            phaseOne.Init(entity);
            entity.Target = target.transform;
            SetField(phaseOne, "_activationTime", Time.time + 1f);
            CheckTransition(phaseOne, entity, AIState.Idle, "phase one honors its spawn delay");
            SetField(phaseOne, "_activationTime", Time.time - 1f);
            SetField(phaseOne, "_specialLockUntil", Time.time + 1f);
            CheckTransition(phaseOne, entity, AIState.Follow, "phase one chases during recovery even in range");
            SetField(phaseOne, "_specialLockUntil", Time.time - 1f);
            target.transform.position = Vector3.right * (phaseOne.engageRange + 1f);
            CheckTransition(phaseOne, entity, AIState.Follow, "phase one chases outside its engage radius before timeout");
            SetField(phaseOne, "_chaseStartTime", Time.time - phaseOne.chaseTimeLimit - 1f);
            CheckTransition(phaseOne, entity, AIState.Attack, "phase one can attack outside range after the chase timeout");

            phaseTwo.Init(entity);
            entity.Target = target.transform;
            target.transform.position = Vector3.right * (phaseTwo.engageRange + 1f);
            CheckTransition(phaseTwo, entity, AIState.Follow, "phase two never opens execution outside range");
            SetField(phaseTwo, "_executionDone", true);
            SetField(phaseTwo, "_specialLockUntil", Time.time - 1f);
            CheckTransition(phaseTwo, entity, AIState.Follow, "phase two continues chasing outside range after recovery");
            target.transform.position = Vector3.right * phaseTwo.engageRange;
            SetField(phaseTwo, "_specialLockUntil", Time.time + 1f);
            CheckTransition(phaseTwo, entity, AIState.Follow, "phase two honors recovery after execution");
            SetField(phaseTwo, "_specialLockUntil", Time.time - 1f);
            CheckTransition(phaseTwo, entity, AIState.Attack, "phase two resumes attacks at the engage boundary");
        }
        finally
        {
            Object.DestroyImmediate(standard);
            Object.DestroyImmediate(phaseOne);
            Object.DestroyImmediate(phaseTwo);
            EditorSceneManager.ClosePreviewScene(scene);
        }

        Debug.Log("[EnemyAIDecisionCheck] ALL CHECKS PASSED");
    }

    private static T LoadPattern<T>(string path) where T : AIPatternSO
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(PatternPath + path);
        Check(asset != null, $"load {path}");
        return Object.Instantiate(asset);
    }

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, InstanceField).SetValue(target, value);

    private static void CheckTransition(AIPatternSO pattern, BaseEntity entity, AIState expected, string label)
    {
        pattern.GetType().GetMethod("UpdateStateTransitions", InstanceField).Invoke(pattern, new object[] { entity });
        Check(entity.CurrentState == expected, $"{label} (expected {expected}, got {entity.CurrentState})");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[EnemyAIDecisionCheck] FAIL: " + label);
        Debug.Log("[EnemyAIDecisionCheck] PASS: " + label);
    }
}
