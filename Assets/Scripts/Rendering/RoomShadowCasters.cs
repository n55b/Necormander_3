using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// URP updates every enabled caster, including scenery in rooms with no light in reach.
[DefaultExecutionOrder(-100), DisallowMultipleComponent]
public sealed class RoomShadowCasters : MonoBehaviour
{
    private struct Prop
    {
        public ShadowCaster2D caster;
        public Transform pose;
        public Bounds local;
        public Matrix4x4 lastMatrix;
        public bool suspended;
    }

    private readonly List<Prop> props = new();
    private readonly List<Vector3> lamps = new();
    private bool dirty = true;

    internal static void Bind(MapGenerator map)
    {
        if (!map.TryGetComponent<RoomShadowCasters>(out var field))
            field = map.gameObject.AddComponent<RoomShadowCasters>();
        field.Restore();
        field.props.Clear();
        var roomCasters = new List<ShadowCaster2D>();
        foreach (var room in map.AllRooms)
        {
            room.GetComponentsInChildren(true, roomCasters);
            foreach (var caster in roomCasters)
            {
                if (!caster.enabled) continue;
                var path = caster.shapePath;
                if (path == null || path.Length < 3) continue;
                var bounds = new Bounds(path[0], Vector3.zero);
                foreach (var vertex in path) bounds.Encapsulate(vertex);
                field.props.Add(new Prop { caster = caster, pose = caster.transform, local = bounds });
            }
        }
        field.dirty = true;
    }

    private void Update()
    {
        bool changed = dirty;
        int count = 0;
        foreach (var light in GroundShadowLight.Active)
        {
            if (!light.isActiveAndEnabled || light.lightType == Light2D.LightType.Global) continue;
            // A freeform or sprite light may extend beyond its origin in any direction.
            if (light.lightType != Light2D.LightType.Point)
            {
                if (!dirty) Restore();
                return;
            }
            Vector2 position = light.transform.position;
            var disk = new Vector3(position.x, position.y, light.pointLightOuterRadius);
            if (count == lamps.Count) { lamps.Add(disk); changed = true; }
            else if (!lamps[count].Equals(disk)) { lamps[count] = disk; changed = true; }
            count++;
        }
        if (count < lamps.Count) { lamps.RemoveRange(count, lamps.Count - count); changed = true; }
        dirty = false;

        for (int i = 0; i < props.Count; ++i)
        {
            var prop = props[i];
            if (prop.caster == null) continue;
            var matrix = prop.pose.localToWorldMatrix;
            if (!changed && matrix.Equals(prop.lastMatrix)) continue;
            prop.lastMatrix = matrix;
            Vector2 center = matrix.MultiplyPoint3x4(prop.local.center);
            // Sum of transformed half-axis lengths bounds the entire polygon even under shear.
            var ext = prop.local.extents;
            float radius = matrix.MultiplyVector(Vector3.right * ext.x).magnitude +
                           matrix.MultiplyVector(Vector3.up * ext.y).magnitude + .25f;
            bool reachable = false;
            foreach (var lamp in lamps)
            {
                float reach = lamp.z + radius;
                if (((Vector2)lamp - center).sqrMagnitude > reach * reach) continue;
                reachable = true;
                break;
            }
            if (prop.suspended && reachable) { prop.caster.enabled = true; prop.suspended = false; }
            else if (prop.caster.enabled && !reachable) { prop.caster.enabled = false; prop.suspended = true; }
            props[i] = prop;
        }
    }

    private void OnDisable() => Restore();

    private void Restore()
    {
        for (int i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            if (!prop.suspended) continue;
            if (prop.caster != null) prop.caster.enabled = true;
            prop.suspended = false;
            props[i] = prop;
        }
        dirty = true;
    }
}
