using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

internal static class AmbientBirdCheck
{
    internal static IEnumerator Village(Camera camera)
    {
        var flock = Object.FindFirstObjectByType<AmbientBirdFlock>();
        Require(flock != null, "village habitat");
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var body = player.GetComponent<Rigidbody2D>();
        var previous = body.position;
        float deadline = Time.realtimeSinceStartup + 7;
        while (flock.transform.Find("Dusk finch flock") == null && Time.realtimeSinceStartup < deadline) yield return null;
        var group = flock.transform.Find("Dusk finch flock");
        Require(group != null && group.childCount >= 3 && group.childCount <= 6, "3–6 grounded birds");
        var actors = group.GetComponentsInChildren<Animator>();
        var clips = actors[0].runtimeAnimatorController.animationClips;
        Require(clips.Length >= 5 && clips.All(c => c.name != "Fly" && c.name != "Idle" || c.isLooping),
            "flight and idle repeat their authored poses");
        var ground = flock.GetComponent<Tilemap>();
        var wall = ground.transform.parent.Find("Wall").GetComponent<Tilemap>();
        var blocked = ground.transform.parent.Find("Unsteppable").GetComponent<Tilemap>();
        foreach (var actor in actors)
        {
            var cell = ground.WorldToCell(actor.transform.position);
            Require(ground.HasTile(cell) && !wall.HasTile(cell) && !blocked.HasTile(cell), "clear roost tile");
            Require(actor.GetComponent<AirborneShadow>() != null && actor.GetComponent<YSortableObject>() != null, "shadow and world order");
        }
        Require(group.GetComponentInChildren<Collider2D>() == null, "visuals have no collision");
        try
        {
            bool peck = false;
            deadline = Time.realtimeSinceStartup + 4;
            while (!peck && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                peck = actors.Any(a => a.GetCurrentAnimatorStateInfo(0).IsName("Peck"));
            }
            Require(peck, "grounded birds peck");
            PresentationRegressionCheck.SaveFrame(camera.targetTexture, "Wildlife/Perched");
            var escape = CheckEscape(group, body, camera);
            while (escape.MoveNext()) yield return escape.Current;
        }
        finally
        {
            body.position = previous;
            Physics2D.SyncTransforms();
        }
        Debug.Log("[AmbientBirdCheck] PASS village roost, peck, approach, alert, takeoff, flight and cleanup.");
    }

    internal static IEnumerator Dungeon()
    {
        var map = MapGenerator.Instance;
        var original = map.CurrentRoom;
        var body = GameManager.Instance.PLAYERCONTROLLER.GetComponent<Rigidbody2D>();
        Vector2 previous = body.position;
        try
        {
            foreach (var room in map.AllRooms.Where(r => r.roomType == RoomType.Normal || r.roomType == RoomType.Spawn || r.roomType == RoomType.Reward))
            {
                map.SetCurrentRoom(room);
                var habitat = room.GetComponent<AmbientBirdFlock>();
                if (habitat == null) continue;
                float until = Time.realtimeSinceStartup + 2.4f;
                while (habitat.transform.Find("Dusk finch flock") == null && Time.realtimeSinceStartup < until) yield return null;
                var group = habitat.transform.Find("Dusk finch flock");
                if (group == null) continue;
                foreach (Transform bird in group) Require(room.IsFloorAt(bird.position) &&
                    !room.IsInDoorKeepOut(bird.position, 3, 2, 1), "room footprint and door clearance");
                map.SetCurrentRoom(null);
                Require(!group.gameObject.activeInHierarchy, "old room sleeps");
                map.SetCurrentRoom(room);
                Require(group.gameObject.activeInHierarchy && habitat.transform.Find("Dusk finch flock") == group, "revisit resumes same flock");
                var escape = CheckEscape(group, body, null);
                while (escape.MoveNext()) yield return escape.Current;
                Debug.Log("[AmbientBirdCheck] PASS generated room placement, door clearance and revisit.");
                yield break;
            }
            throw new InvalidOperationException("Expected a populated habitat in the verification layout.");
        }
        finally { body.position = previous; Physics2D.SyncTransforms(); map.SetCurrentRoom(original); }
    }

    private static IEnumerator CheckEscape(Transform group, Rigidbody2D body, Camera camera)
    {
        var actors = group.GetComponentsInChildren<Animator>();
        var feet = actors.Select(a => a.transform.parent).ToArray();
        var origins = feet.Select(f => (Vector2)f.position).ToArray();
        Vector2 from = body.position;
        Vector2 nearest = origins.OrderBy(p => Vector2.SqrMagnitude(from - p)).First();
        Vector2 dir = (from - nearest).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;
        body.position = nearest + dir * 3.4f;
        Physics2D.SyncTransforms();
        float gap = origins.Min(p => Vector2.Distance(p, body.position));
        Require(gap > 3.2f, "approach remains outside the old trigger radius");
        float began = Time.time, reaction = -1f, speed = 0f;
        bool alert = false, takeoff = false, flight = false;
        float deadline = Time.realtimeSinceStartup + 7f;
        var samplePositions = new Vector2[feet.Length];
        float sampleTime = -1;
        int frame = Time.frameCount;
        while (group != null && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            if (group == null || Time.frameCount == frame) continue;
            frame = Time.frameCount;
            for (int i = 0; i < actors.Length; i++)
            {
                var state = actors[i].GetCurrentAnimatorStateInfo(0);
                alert |= state.IsName("Alert");
                takeoff |= state.IsName("Takeoff");
                Vector2 foot = feet[i].position;
                if (reaction < 0 && (foot - origins[i]).sqrMagnitude > .001f) reaction = Time.time - began;
                if (flight || !state.IsName("Fly")) continue;
                flight = true;
                Require(actors[i].transform.position.y > feet[i].position.y, "flight separates feet and shadow");
                if (camera != null) PresentationRegressionCheck.SaveFrame(camera.targetTexture, "Wildlife/Flight");
            }
            if (Time.time - began < .45f) continue;
            if (sampleTime >= 0 && Time.time - sampleTime < .25f) continue;
            for (int i = 0; i < feet.Length; i++)
            {
                var position = (Vector2)feet[i].position;
                if (sampleTime >= 0) speed = Mathf.Max(speed, Vector2.Distance(samplePositions[i], position) / (Time.time - sampleTime));
                samplePositions[i] = position;
            }
            sampleTime = Time.time;
        }
        Require(alert && takeoff && flight && group == null && reaction >= 0 && reaction <= .24f && speed > 6.5f,
            $"escape at {gap:F2} units, first motion {reaction:F3}s, peak {speed:F1} units/s");
        Debug.Log($"[AmbientBirdCheck] {body.gameObject.scene.name}: range {gap:F2}, reaction {reaction:F3}s, peak {speed:F1} units/s");
    }

    private static void Require(bool ok, string condition)
    {
        if (!ok) throw new InvalidOperationException("[AmbientBirdCheck] " + condition);
    }
}
