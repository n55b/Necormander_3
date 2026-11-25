using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Instances belong to the scene that created them. Additively loaded scenes may share prefabs.
internal static class SceneEffectPool<T> where T : Component
{
    private static readonly Dictionary<T, Queue<T>> queues = new();
    private static readonly List<T> empty = new();

    internal static void Reset()
    {
        queues.Clear();
        empty.Clear();
        SceneManager.sceneUnloaded -= Prune;
        SceneManager.sceneUnloaded += Prune;
    }

    internal static Queue<T> For(T prefab)
    {
        if (!queues.TryGetValue(prefab, out var queue)) queues.Add(prefab, queue = new Queue<T>());
        return queue;
    }

    internal static T Take(T prefab)
    {
        if (queues.TryGetValue(prefab, out var queue))
            while (queue.Count > 0)
            {
                var instance = queue.Dequeue();
                if (instance != null) return instance;
            }
        return null;
    }

    private static void Prune(Scene unloaded)
    {
        foreach (var pair in queues)
        {
            var queue = pair.Value;
            int count = queue.Count;
            for (int i = 0; i < count; ++i)
            {
                var instance = queue.Dequeue();
                if (instance != null && instance.gameObject.scene != unloaded) queue.Enqueue(instance);
            }
            if (queue.Count == 0 || pair.Key == null) empty.Add(pair.Key);
        }
        foreach (var key in empty) queues.Remove(key);
        empty.Clear();
    }

#if LEVEL_WORKSHOP_QA
    internal static int RetainedCount
    {
        get
        {
            int count = 0;
            foreach (var queue in queues.Values) count += queue.Count;
            return count;
        }
    }
#endif
}
