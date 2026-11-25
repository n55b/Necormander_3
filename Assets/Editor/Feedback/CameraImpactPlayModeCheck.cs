using System;
using System.Collections;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

public static class CameraImpactPlayModeCheck
{
    // Called from the village after a real PlayerInput attack deals damage to the scarecrow.
    public static IEnumerator Run(Camera output)
    {
        var manager = CameraManager.Instance;
        var clock = HitStopManager.Instance;
        Require(manager != null && clock != null, "Village camera and clock");
        yield return null;
        yield return null;
        Require(manager.Trace.Any(s => s.Contains("적용")), "actual scarecrow hit reaches screen effects");

        var hit = new DamageInfo(1, category: DamageCategory.BasicAttack);
        var profile = PolishFeedback.Catalog.Impact(ImpactKind.Finisher);
        int duration = profile.shakeMs;
        float decay = profile.decay;
        var rig = manager.GetComponent<CinemachineCamera>();
        float normal = rig.Lens.OrthographicSize;
        var hud = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .First(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera == output);
        var ground = UnityEngine.Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None)
            .First(map => map.name == "Ground");
        var display = PixelViewport.CalculateDisplayRect(Screen.width, Screen.height);
        Vector2 hudPoint = PixelViewport.CanvasToGamePoint(hud, hud.transform.position);
        var target = GameObject.Find("Enemy Scarecrow").transform;
        var motion = CameraSettingsSO.Instance.cameraMotion;
        try
        {
            CameraSettingsSO.Instance.cameraMotion = PixelMotionStep.Whole;
            profile.shakeMs = 380;
            profile.decay = 1;
            manager.Request(ImpactKind.Normal, Vector2.right, hit);
            manager.Request(ImpactKind.Finisher, Vector2.right, hit);
            manager.Request(ImpactKind.Finisher, Vector2.right, hit);
            yield return null;
            yield return null;
            Require(manager.Trace.Any(s => s.Contains("Finisher") && s.Contains("적용")) &&
                manager.Trace.Any(s => s.Contains("동일 타격")), "one winning impulse for a shared strike");
            float peak = 0;
            float until = Time.realtimeSinceStartup + .42f;
            while (Time.realtimeSinceStartup < until)
            {
                peak = Mathf.Max(peak, rig.State.PositionCorrection.magnitude * PixelViewport.Height / (2f * normal));
                Require(PixelViewport.TryWorldToGamePoint(target.position, out var point) &&
                    PixelViewport.TryPointerToWorld(PixelViewport.GameToScreenPoint(point, display), out var world) &&
                    Vector2.Distance(world, target.position) < .002f, "aim projects back to the displayed target during recoil");
                Require(Vector2.Distance(hudPoint, PixelViewport.CanvasToGamePoint(hud, hud.transform.position)) <= .05f, "screen HUD stays still");
                yield return null;
            }
            Require(peak > .1f && peak <= profile.pixels + .05f, $"camera displacement in logical pixels: {peak}");
            Require(rig.State.PositionCorrection.sqrMagnitude < .000001f, "shake returns to follow position");
            float pixel = output.orthographicSize * 2 / PixelViewport.Height;
            Vector3 origin = ground != null ? ground.CellToWorld(Vector3Int.zero) : Vector3.zero;
            Vector3 pixels = (output.transform.position - origin) / pixel;
            Require(Mathf.Abs(pixels.x - Mathf.Round(pixels.x)) < .001f && Mathf.Abs(pixels.y - Mathf.Round(pixels.y)) < .001f,
                "render camera aligns to the logical grid");

            hit.impactPulse++;
            manager.Request(ImpactKind.Summon, Vector2.left, hit);
            yield return null;
            yield return null;
            Require(manager.Trace.Last().Contains("Summon") && manager.Trace.Last().Contains("적용"), "new pulse can play");
            var extension = manager.GetComponent<ImpactCameraOffset>();
            manager.WarpCamera(target, Vector3.zero);
            manager.Request(ImpactKind.Summon, Vector2.left, hit);
            yield return null;
            var room = MapGenerator.Instance != null ? MapGenerator.Instance.CurrentRoom : null;
            var boundary = room != null ? room.groundTilemap : ground;
            var edge = rig.State;
            edge.RawPosition = boundary.transform.TransformPoint(boundary.localBounds.min) +
                new Vector3(normal * PixelViewport.Width / PixelViewport.Height, normal, 0);
            edge.PositionCorrection = Vector3.zero;
            extension.PrePipelineMutateCameraStateCallback(rig, ref edge, 0);
            if (edge.PositionCorrection.x > 0)
                edge.RawPosition = boundary.transform.TransformPoint(boundary.localBounds.max) -
                    new Vector3(normal * PixelViewport.Width / PixelViewport.Height, normal, 0);
            extension.InvokePostPipelineStageCallback(rig, CinemachineCore.Stage.Finalize, ref edge, 0);
            Require(Mathf.Abs(edge.PositionCorrection.x) < .0001f, "outward recoil clipped at room edge");
            manager.SetPixelZoom(2);
            Require(Mathf.Approximately(rig.Lens.OrthographicSize, PixelViewport.Height / (4f * CameraSettingsSO.Instance.assetsPPU)), "2x integer zoom");
            manager.SetZoom(false);
            Require(Mathf.Approximately(rig.Lens.OrthographicSize, normal), "zoom restores lens");

            float peakZoom = 0;
            float endCombo = Time.unscaledTime + .65f;
            do
            {
                extension.SendMessage("PushZoom", 1f);
                yield return null;
                peakZoom = Mathf.Max(peakZoom, (1f - rig.State.Lens.OrthographicSize / normal) * PixelViewport.Height / 2);
            } while (Time.unscaledTime < endCombo);
            Require(peakZoom > 1 && peakZoom <= CameraSettingsSO.Instance.comboZoomMaxPixels + .05f,
                $"sustained combo stays within {peakZoom:F2} edge pixels");
            float settleUntil = Time.unscaledTime + 1.3f;
            while (Time.unscaledTime < settleUntil) yield return null;
            float remaining = (1f - rig.State.Lens.OrthographicSize / normal) * PixelViewport.Height / 2;
            Require(remaining < peakZoom * .65f && remaining >= -.01f, $"combo eases out to {remaining:F2}px");

            Debug.Log($"[ComboZoom] Sustained peak {peakZoom:F2}px; {remaining:F2}px after 1.3s of recovery.");
            var groups = UnityEngine.Object.FindObjectsByType<HudMotionElement>(FindObjectsSortMode.None);
            Require(groups.Length > 1, "independent HUD groups");
            var dashHud = UnityEngine.Object.FindFirstObjectByType<DashCooldownUI>();
            var pips = new UnityEditor.SerializedObject(dashHud).FindProperty("worldPips").objectReferenceValue as RectTransform;
            Vector2 Center(HudMotionElement group)
            {
                var canvas = group.GetComponentInParent<Canvas>();
                Vector2 point = PixelViewport.CanvasToGamePoint(canvas,group.transform.position);
                // Dash charges follow the player's feet during a camera warp. Measure their wave at that anchor.
                return pips != null && group.transform.IsChildOf(pips)
                    ? point - PixelViewport.CanvasToGamePoint(canvas,pips.position) : point;
            }
            var rest = groups.Select(Center).ToArray();
            float crest = 0;
            manager.Request(ImpactKind.BossLanding, Vector2.up);
            until = Time.unscaledTime + .35f;
            while (Time.unscaledTime < until)
            {
                for (int i = 0; i < groups.Length; i++)
                    crest = Mathf.Max(crest, Vector2.Distance(rest[i], Center(groups[i])));
                yield return null;
            }
            Require(manager.Trace.Any(s => s.Contains("BossLanding") && s.Contains("적용")) &&
                crest > .8f && crest < 5f && Mathf.Approximately(Time.timeScale, 1),
                $"boss landing sends a bounded HUD wave during normal gameplay time ({crest:F2}px)");
            Debug.Log($"[HudImpact] {groups.Length} independent groups, landing peak {crest:F2}px");
            manager.Request(ImpactKind.BossDeath, Vector2.up, hit);
            yield return null;
            Require(Time.timeScale < PolishFeedback.Catalog.slowMotionScale, "boss final hit starts with hit stop");
            float deadline = Time.realtimeSinceStartup + 1f;
            while (Time.timeScale < PolishFeedback.Catalog.slowMotionScale && Time.realtimeSinceStartup < deadline) yield return null;
            Require(Mathf.Approximately(Time.timeScale, PolishFeedback.Catalog.slowMotionScale), "boss slow motion follows stop");
            HitStopManager.SetPaused(true, HitStopManager.PauseOwner.Reward);
            HitStopManager.SetPaused(true, HitStopManager.PauseOwner.Options);
            HitStopManager.SetPaused(false, HitStopManager.PauseOwner.Options);
            yield return new WaitForSecondsRealtime(.22f);
            Require(Time.timeScale == 0, "menu owns pause throughout effect expiry");
            HitStopManager.SetPaused(false, HitStopManager.PauseOwner.Reward);
            Require(Time.timeScale == 1, "unpause restores normal speed");
            Debug.Log($"[CameraImpact] PASS: scarecrow, deduplication, repeat, boss sequence, pause, grid, zoom and decay. Peak={peak:F2}px.");
        }
        finally
        {
            CameraSettingsSO.Instance.cameraMotion = motion;
            profile.shakeMs = duration;
            profile.decay = decay;
            manager.SetZoom(false);
            HitStopManager.ResetForScene();
        }
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("[CameraImpact] " + message);
    }
}

