using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class FeedbackCheck
{
    [MenuItem("Tools/Combat/Verify Visual Feedback")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode에서 실행하세요.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player Melee.prefab");
        var player = Object.Instantiate(prefab);
        var host = new GameObject("Feedback check hitbox");
        try
        {
            var pulse = player.GetComponent<VisualSquashStretch>();
            var body = player.transform.Find("Body");
            var original = body.localScale;
            var collider = player.GetComponent<Collider2D>();
            var originalSize = collider.bounds.size;
            var advance = typeof(VisualSquashStretch).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(pulse != null, "플레이어 프리팹에 실물 스쿼시 컴포넌트");
            for(int i=0;i<100;i++)
            {
                pulse.Play(i%2==0 ? "Attack" : "Hit");
                advance.Invoke(pulse, new object[]{.035f});
                Check(body.localScale != original, "실제 그림만 변형");
                Check(collider.bounds.size == originalSize, "충돌체 크기 불변");
                Check(body.localScale.x > 0f && body.localScale.y > 0f, "연속 호출에 뒤집힘 없음");
            }
            advance.Invoke(pulse,new object[]{1f});
            Check(body.localScale == original, "연속 발동 후 원본 스케일 정확히 복구");
            pulse.Stretch(); advance.Invoke(pulse,new object[]{.04f}); pulse.ResetVisual();
            Check(body.localScale == original, "취소 복구");

            var effect = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effect/Hit/Slash Impact.prefab");
            Check(effect != null && effect.GetComponent<ImpactFeedback>() != null && effect.GetComponentsInChildren<LineRenderer>().Length >= 10,
                "슬래시와 파편 실물 프리팹");
            var clip = effect.GetComponentInChildren<Animator>().runtimeAnimatorController.animationClips[0];
            Check(!clip.isLooping && clip.events.Length == 0, "연출용 복제 클립 단발, 피해 이벤트 없음");
            var enabledBinding = AnimationUtility.GetCurveBindings(clip).Single(b => b.propertyName == "m_Enabled");
            Check(AnimationUtility.GetEditorCurve(clip, enabledBinding).Evaluate(0f) > .5f, "타격 클립은 빈 대기 없이 즉시 표시");
            Check(effect.GetComponentInChildren<SpriteRenderer>().sprite.pixelsPerUnit == 32, "슬래시 32 PPU");
            var box = host.AddComponent<BaseHitBox>();
            int overrides = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:MainMinionDataSO"))
            {
                var data=AssetDatabase.LoadAssetAtPath<MainMinionDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                box.hitEffectPrefab=null; box.hitParticlePrefab=host;
                data.ApplyHitEffect(box);
                if (data.name == "Warlord Minion" || data.name == "Champion Minion")
                {
                    Check(box.hitEffectPrefab==effect && box.hitParticlePrefab==null && !data.CanEvolve, "최종형 2종만 복합 타격 연출");
                    overrides++;
                }
                else Check(box.hitEffectPrefab==null && box.hitParticlePrefab==host, "다른 미니언 기존 연출 보존");
            }
            Check(overrides==2,"타격 이펙트 대상 수");
            int enemies=0, minions=0;
            foreach(var path in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Enemy","Assets/Prefabs/Minion"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var feedback=asset.GetComponent<VisualSquashStretch>();
                Check(feedback!=null,"캐릭터 프리팹 스쿼시 누락: "+path);
                var serialized=new SerializedObject(feedback);
                if(asset.GetComponent<BaseEntity>()!=null)
                {
                    enemies++;
                    var renderer=serialized.FindProperty("deformRenderer").objectReferenceValue as SpriteRenderer;
                    Check(renderer!=null && renderer.sharedMaterial.HasProperty("_FeedbackScale"),"몬스터 그림 전용 변형: "+path);
                    Check(serialized.FindProperty("healthSource").objectReferenceValue!=null,"실제 피격 이벤트 연결: "+path);
                }
                else
                {
                    minions++;
                    var visual=serialized.FindProperty("visual").objectReferenceValue as Transform;
                    Check(visual!=null && visual.GetComponentInChildren<Collider2D>(true)==null,"소환수 그림 전용 변형: "+path);
                }
            }
            Check(enemies>0 && minions>0,"몬스터·소환수 프리팹 검증");
            foreach (var sr in player.GetComponentsInChildren<SpriteRenderer>())
                if(sr.name=="Silhouette") Check(sr.sharedMaterial.shader.name=="Necromancer/Pixel Feedback Sprite", "가림 실루엣 디더링 재질");
            var curtain=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ScreenFadeCanvas.prefab");
            Check(curtain.GetComponentInChildren<Image>().material.shader.name=="Necromancer/Pixel Dither UI", "기존 커튼에 디더링 재질 연결");
            foreach (string shader in new[]{"PixelFeedbackSprite", "PixelDitherUI", "ImpactAdditive"})
            {
                var s=AssetDatabase.LoadAssetAtPath<Shader>($"Assets/Material/Feedback/{shader}.shader");
                Check(s!=null && !ShaderUtil.ShaderHasError(s), "셰이더 오류: "+shader);
            }
            Check(!ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Material/Player/PlayerOramaticLit.shadergraph")),"캐릭터 정점 변형 셰이더");
            Debug.Log($"[FeedbackCheck] PASS: {enemies} enemy/{minions} minion prefabs; repeated squash/cancel, unchanged physics, 2 terminal impacts, dither and shaders.");
        }
        finally { Object.DestroyImmediate(player); Object.DestroyImmediate(host); }
    }
    private static void Check(bool valid, string message)
    { if(!valid) throw new InvalidOperationException("[FeedbackCheck] FAIL: "+message); }
}
