using System.Collections.Generic;
using UnityEngine;

/// <summary>기존 타격 스프라이트/파티클 프리팹을 재생 후 재사용한다.</summary>
public sealed class PooledHitEffect : MonoBehaviour
{
    private static readonly Dictionary<PooledHitEffect, Queue<PooledHitEffect>> Pool = new Dictionary<PooledHitEffect, Queue<PooledHitEffect>>();
    private PooledHitEffect _source;
    private SpriteRenderer[] _sprites;
    private Color[] _spriteColors;
    private LineRenderer[] _lines;
    private Color[] _lineStarts, _lineEnds;
    private ParticleSystem[] _particles;
    private ParticleSystem.MinMaxGradient[] _particleColors;
    private Animator[] _animators;
    private float _age;
    public bool IsManaged => _source != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPool() => Pool.Clear();

    private void Awake()
    {
        _sprites = GetComponentsInChildren<SpriteRenderer>(true);
        _spriteColors = new Color[_sprites.Length];
        for (int i=0; i<_sprites.Length; i++) _spriteColors[i]=_sprites[i].color;
        _lines = GetComponentsInChildren<LineRenderer>(true);
        _lineStarts = new Color[_lines.Length]; _lineEnds = new Color[_lines.Length];
        for (int i=0; i<_lines.Length; i++) { _lineStarts[i]=_lines[i].startColor; _lineEnds[i]=_lines[i].endColor; }
        _particles = GetComponentsInChildren<ParticleSystem>(true);
        _particleColors = new ParticleSystem.MinMaxGradient[_particles.Length];
        for (int i=0; i<_particles.Length; i++) _particleColors[i]=_particles[i].main.startColor;
        _animators = GetComponentsInChildren<Animator>(true);
    }

    public static PooledHitEffect Spawn(PooledHitEffect prefab, Vector3 position, Quaternion rotation, bool tinted, Color tint)
    {
        PooledHitEffect instance=null;
        if (Pool.TryGetValue(prefab,out var queue)) while (queue.Count>0 && instance==null) instance=queue.Dequeue();
        if (instance==null)
        {
            instance=Instantiate(prefab,position,rotation);
            instance._source=prefab;
        }
        else
        {
            instance.RestoreColors();
            instance.transform.SetPositionAndRotation(position,rotation);
            instance.gameObject.SetActive(true);
            foreach (var animator in instance._animators) if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            { animator.Play(0,0,0f); animator.Update(0f); }
            foreach (var ps in instance._particles) if (ps != null && ps.gameObject.activeInHierarchy)
            { ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(true); }
        }
        instance._age=0f;
        if (tinted) instance.Tint(tint);
        return instance;
    }

    public static void PrewarmOne(PooledHitEffect prefab,int desired)
    {
        if (prefab==null || desired<=0) return;
        if (!Pool.TryGetValue(prefab,out var queue)) Pool[prefab]=queue=new Queue<PooledHitEffect>();
        int valid=0; foreach(var item in queue) if(item!=null) valid++;
        if(valid>=desired) return;
        var instance=Instantiate(prefab); instance._source=prefab;
        instance.gameObject.SetActive(false); queue.Enqueue(instance);
    }

    private void RestoreColors()
    {
        for(int i=0;i<_sprites.Length;i++) if(_sprites[i]!=null) _sprites[i].color=_spriteColors[i];
        for(int i=0;i<_lines.Length;i++) if(_lines[i]!=null) { _lines[i].startColor=_lineStarts[i]; _lines[i].endColor=_lineEnds[i]; }
        for(int i=0;i<_particles.Length;i++) if(_particles[i]!=null) { var main=_particles[i].main; main.startColor=_particleColors[i]; }
    }

    private void Tint(Color tint)
    {
        foreach(var sr in _sprites) if(sr!=null) sr.color=new Color(tint.r,tint.g,tint.b,sr.color.a);
        foreach(var line in _lines) if(line!=null)
        { line.startColor=new Color(tint.r,tint.g,tint.b,line.startColor.a); line.endColor=new Color(tint.r,tint.g,tint.b,line.endColor.a); }
        foreach(var ps in _particles) if(ps!=null) { var main=ps.main; main.startColor=new Color(tint.r,tint.g,tint.b,main.startColor.color.a); }
    }

    private void Update() { if(IsManaged && (_age+=Time.deltaTime)>=1f) Release(); }
    public void Release()
    {
        if (_source==null) { Destroy(gameObject); return; }
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        if(!Pool.TryGetValue(_source,out var queue)) Pool[_source]=queue=new Queue<PooledHitEffect>();
        if(queue.Count<32) queue.Enqueue(this); else Destroy(gameObject);
    }
}
