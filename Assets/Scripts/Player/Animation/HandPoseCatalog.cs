using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class HandPoseCatalog : ScriptableObject
{
    [Serializable]
    public struct Pose
    {
        public Sprite frame, left, right;
        public Vector2 leftOffset, rightOffset;
    }

    public Pose[] poses;
    private Dictionary<Sprite, Pose> byFrame;

    public bool TryGet(Sprite frame, out Pose pose)
    {
        pose = default;
        if (frame == null) return false;
        if (byFrame == null)
        {
            byFrame = new Dictionary<Sprite, Pose>(poses.Length);
            foreach (var entry in poses) byFrame.Add(entry.frame, entry);
        }
        return byFrame.TryGetValue(frame, out pose);
    }
}
