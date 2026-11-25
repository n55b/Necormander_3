using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The analytic backend evaluates flight in the vertex shader. The compute backend integrates against the current floor mask.
public sealed class WorldSparkField : MonoBehaviour
{
    public enum Kind { EnemyHit, PlayerHit, Projectile, Reflect, Wall, Sprint, Pollen, Ember, UI, Arrival, Summon, Treasure, Coin, Leaf, Dust }
    private const int Capacity = 768;
    [StructLayout(LayoutKind.Sequential)]
    private struct Particle { public Vector2 point, velocity; public float birth, expires, solid, updated; }
    private readonly Particle[] initial = new Particle[Capacity];
    private ComputeBuffer particles;
#if LEVEL_WORKSHOP_QA
    internal bool HasParticleBuffer => particles != null && particles.IsValid();
#endif
    private ComputeShader flow;
    private int kernel;
    private ParticleLibrary library;
    public static WorldSparkField Current => instance;
    public int Requested { get; private set; }
    public int Emitted { get; private set; }
#if NECRO_RENDER_BENCH
    internal int VisibleEnemyHits { get; private set; }
#endif
    public int Offscreen { get; private set; }
    public int BudgetDrops { get; private set; }
    public int CollisionQueries { get; private set; }
    public bool UsesCompute => particles != null && library != null && library.simulation == ParticleLibrary.Simulation.Compute;
    public int AreaDrops { get; private set; }
    public int ActiveSlots
    {
        get { int active = 0; float now = Time.time; foreach (var p in initial) if (p.expires > now) active++; return active; }
    }
    private readonly int[] quadAreas = new int[Capacity];
    private int decorativePixels, nextLamp;
    private float earliestExpiry=float.PositiveInfinity;
    private void ReleaseExpiredAreas(float now)
    {
        if (now < earliestExpiry) return;
        earliestExpiry=float.PositiveInfinity;
        for(int i=Capacity/2;i<Capacity;i++)
        {
            if(quadAreas[i]==0) continue;
            if(initial[i].expires<=now) {decorativePixels-=quadAreas[i];quadAreas[i]=0;}
            else earliestExpiry=Mathf.Min(earliestExpiry,initial[i].expires);
        }
    }
    private uint sequence;
    private float ambientAt;
    private uint ambientSeed = 907u;
    private static int AirShape(Kind kind) => kind switch
    {
        Kind.Pollen => 1, Kind.Leaf => 2, Kind.Dust => 3, Kind.Ember => 4, _ => 0
    };
    private readonly Ring important = new Ring(0, Capacity/2), decorative = new Ring(Capacity/2, Capacity/2);
    private sealed class Ring
    {
        private readonly int start, size;
        private int cursor, first, count;
        internal Ring(int start, int size) { this.start = start; this.size = size; }
        internal int Take()
        {
            if (count == 0) first = cursor;
            int slot = start + cursor;
            cursor = (cursor + 1) % size;
            if (count == size) first = cursor; else count++;
            return slot;
        }
        internal void Flush(WorldSparkField field)
        {
            if (count == 0) return;
            int end = Mathf.Min(count, size-first);
            field.Upload(start+first, end);
            if (count>end) field.Upload(start,count-end);
            count=0;
        }
        internal void Reset() { cursor=first=0; count=size; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 start;
        public Vector4 cornerAndLife, velocity, contactAndColor;
    }
    private static WorldSparkField instance;
    private Mesh mesh;
    private Material material;
    private MeshRenderer batch;
    private readonly Vertex[] vertices = new Vertex[Capacity*4];
    private readonly RaycastHit2D[] firstHit = new RaycastHit2D[1];
    private ContactFilter2D solids;
    private int frame = -1, used, decorativeUsed;
    private float lastExpiry;
    private static readonly Vector2[] Corners = { new(-1,-1),new(-1,1),new(1,1),new(1,-1) };

    private void Awake()
    {
        instance = this;
        library = Resources.Load<ParticleLibrary>("WorldAtmosphere/Particles");
        if (SystemInfo.supportsComputeShaders && SystemInfo.graphicsShaderLevel >= 45)
        {
            flow = Resources.Load<ComputeShader>("WorldAtmosphere/ParticleFlow");
            if (flow != null)
            {
                kernel = flow.FindKernel("Advance");
                particles = new ComputeBuffer(Capacity, Marshal.SizeOf<Particle>());
                particles.SetData(initial);
            }
        }
        solids = new ContactFilter2D { useTriggers = false };
        solids.SetLayerMask(Layers.WallMask | Layers.ObjectMask | Layers.UnsteppableMask);
        var go = new GameObject("GPU combat sparks",typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(transform,false);
        batch = go.GetComponent<MeshRenderer>();
        batch.sortingLayerName = "FlyingObject";
        batch.sortingOrder = 20;
        batch.shadowCastingMode = ShadowCastingMode.Off;
        batch.receiveShadows = false;
        var shader = Resources.Load<Shader>("WorldAtmosphere/WorldSparks");
        material = new Material(shader);
        material.SetTexture("_Palette",Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette"));
        batch.sharedMaterial = material;
        mesh = new Mesh { name = "World spark ring" }; mesh.MarkDynamic();
        mesh.SetVertexBufferParams(vertices.Length, new VertexAttributeDescriptor(VertexAttribute.Position),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,dimension:4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,dimension:4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2,dimension:4));
        mesh.SetVertexBufferData(vertices,0,0,vertices.Length);
        var indices = new int[Capacity*6];
        for (int i=0;i<Capacity;i++) {
            int j=i*6,v=i*4;
            indices[j]=v; indices[j+1]=v+1; indices[j+2]=v+2;
            indices[j+3]=v; indices[j+4]=v+2; indices[j+5]=v+3;
        }
        mesh.SetTriangles(indices,0,false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one*100000);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        batch.enabled = false;
        DamageEventBus.OnAttackHitResolved += Hit;
        SceneManager.activeSceneChanged += SceneChanged;
    }

    private void SceneChanged(Scene previous, Scene next)
    {
        System.Array.Clear(vertices,0,vertices.Length);
        System.Array.Clear(initial,0,initial.Length);
        important.Reset(); decorative.Reset(); lastExpiry=0; batch.enabled=false;
        Requested=Emitted=Offscreen=BudgetDrops=CollisionQueries=AreaDrops=0;
        nextLamp=decorativePixels=0; earliestExpiry=float.PositiveInfinity;
        System.Array.Clear(quadAreas,0,quadAreas.Length);
        ambientAt = 0; ambientSeed = 907u; sequence=0;
    }

    private static void Hit(CharacterHealth target, CharacterStat attacker, DamageInfo info, bool critical)
    {
        if (target == null || info.category == DamageCategory.Debuff || info.isRedirected) return;
        Vector2 feet = target.transform.position;
        var sr = target.HitBody;
        Vector2 point = sr != null ? (Vector2)sr.bounds.center : feet;
        Vector2 from = info.hitFrom ?? (attacker != null ? (Vector2)attacker.transform.position : point-Vector2.up);
        bool strong = critical || info.feedbackComboFinish || info.category == DamageCategory.Parry;
        Vector2 direction = feet - from;
        if (direction.sqrMagnitude < .0001f) direction = Vector2.up;
        if (DamageRules.IsPlayerSourced(info.category) && info.amount > 0)
        {
            var motion = target.GetComponentInParent<PixelActorMotion>();
            var settings = CameraSettingsSO.Instance;
            float amplitude = settings != null ? settings.enemyRecoilPixels : 3f;
            motion?.Strike(sr, direction, amplitude * (strong ? 2f : 1f));
        }
        int count = strong ? 14 : 7;
        if (info.pulseTargetCount > 1 && instance != null && instance.library != null)
            count = Mathf.Min(count, Mathf.Max(1, instance.library.FrameBudget / info.pulseTargetCount));
        Emit(DamageRules.IsEnemyTier(info.category) ? Kind.PlayerHit : Kind.EnemyHit, point, direction, count, strongHit: strong);
    }

    public static void Emit(Kind kind, Vector2 position, Vector2 heading, int count, uint seed = 0, bool strongHit = false)
    {
        if (instance == null || !Application.isPlaying || Time.timeScale <= 0) return;
        var catalog = PolishFeedback.Catalog;
        if (catalog != null && (!catalog.enabled || catalog.intensity <= 0)) return;
        var camera = PixelArtUpscaleRig.RenderCamera;
        if (camera == null) return;
        instance.Requested += count;
        var viewport = camera.WorldToViewportPoint(position);
        if (viewport.x < -.1f || viewport.x > 1.1f || viewport.y < -.1f || viewport.y > 1.1f) { instance.Offscreen += count; return; }
        instance.Burst(kind, position, heading, count, seed == 0 ? ++instance.sequence : seed, strongHit || count > 7);
    }

    private static float Between(ref uint seed, float min, float max)
    {
        seed = unchecked(seed * 1664525u + 1013904223u);
        return Mathf.Lerp(min, max, (seed >> 8) * (1f / 16777216f));
    }

    private void Burst(Kind kind, Vector2 start, Vector2 heading, int count, uint seed, bool strongHit)
    {
        if (frame != Time.frameCount) { frame=Time.frameCount; used=decorativeUsed=0; }
        var recipe = library != null ? library.Find(kind) : null;
        if (recipe == null) return;
        int limit = library.FrameBudget;
        int accepted = Mathf.Max(0, Mathf.Min(count, limit-used));
        int radius=Mathf.Clamp(recipe.radiusPixels,1,6);
        int diameter=2*(Mathf.Max(radius,recipe.terrainCollision?2:1)+1)+1;
        int footprint=diameter*diameter;
        if (!recipe.important)
        {
            accepted = Mathf.Min(accepted, Mathf.Max(0, limit/2-decorativeUsed));
            if(decorativePixels+accepted*footprint>library.DecorativePixelBudget) ReleaseExpiredAreas(Time.time);
            int fits = Mathf.Min(accepted, Mathf.Max(0,library.DecorativePixelBudget - decorativePixels) / footprint);
            AreaDrops += accepted - fits;
            accepted = fits;
        }
        BudgetDrops += count-accepted;
        Emitted += accepted;
#if NECRO_RENDER_BENCH
        if (kind == Kind.EnemyHit && accepted > 0) VisibleEnemyHits++;
#endif
        used += accepted;
        if (!recipe.important) decorativeUsed += accepted;
        float direction = Mathf.Atan2(heading.y,heading.x);
        int air = AirShape(kind);
        bool trail = !recipe.terrainCollision;
        float spread = recipe.spreadDegrees * Mathf.Deg2Rad;
        for (int i=0;i<accepted;i++)
        {
            float angle = direction + Between(ref seed, -spread,spread);
            var axis = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            float speed = Between(ref seed,recipe.speed.x,recipe.speed.y);
            float life = Between(ref seed,recipe.lifetime.x,recipe.lifetime.y);
            Vector2 inward = axis*speed, outward = Vector2.zero;
            float contact = life;
            if (!UsesCompute && recipe.terrainCollision)
            {
                CollisionQueries++;
                if (Physics2D.Raycast(start,axis,solids,firstHit,speed*life)>0) {
                    contact=firstHit[0].distance/speed;
                    outward=Vector2.Reflect(inward,firstHit[0].normal)*.47f;
                }
            }
            int row = recipe.paletteRow, column = recipe.paletteColumn + i%3;
            if (air != 0) column = recipe.paletteColumn + (int)Between(ref seed, 0, 2.999f);
            int slot = (recipe.important ? important : decorative).Take();
            if(!recipe.important)
            {
                decorativePixels+=footprint-quadAreas[slot];
                quadAreas[slot]=footprint;
                earliestExpiry=Mathf.Min(earliestExpiry,Time.time+life);
            }
            initial[slot] = new Particle { point = start, velocity = inward, birth = Time.time, expires = Time.time+life,
                solid = recipe.terrainCollision ? 1 : 0, updated = Time.time };
            var data=new Vertex { start=new Vector3(start.x, start.y, air), velocity=new(inward.x,inward.y,outward.x,outward.y),
                contactAndColor=new(contact,column,row,kind == Kind.EnemyHit || kind == Kind.Projectile ? 0 : !trail && i%5==0 ? Mathf.Max(2,radius) : radius) };
            for (int j=0;j<4;j++) {
                data.cornerAndLife = new(Corners[j].x,Corners[j].y,Time.time,life);
                vertices[slot*4+j]=data;
            }
            lastExpiry=Mathf.Max(lastExpiry, Time.time+life);
        }
    }

    private void LateUpdate()
    {
        var camera = PixelArtUpscaleRig.RenderCamera;
        var air = WorldAtmosphere.Current;
        float density = air != null ? air.Airborne : 0f;
        if (camera != null && density > 0 && library != null && Time.time >= ambientAt)
        {
            ambientAt = Time.time + 1f / (library.AmbientRate * density);
            // Advance one persistent stream. Adjacent integer seeds produce nearly identical screen X coordinates.
            Between(ref ambientSeed, 0, 1);
            uint random = ambientSeed;
            var point = camera.ViewportToWorldPoint(new Vector3(Between(ref random,0f,1f),Between(ref random,0f,1f),-camera.transform.position.z));
            var floor = WorldAtmosphere.Current?.Floor;
            if (floor != null && floor.IsOpen(point))
            {
                bool outdoor = camera.gameObject.scene.name == "VillageScene";
                Kind kind = outdoor ? ((ambientSeed & 3) == 0 ? Kind.Leaf : Kind.Pollen) : Kind.Dust;
                float wind = Mathf.Sin(Time.time * .48f + point.y * .28f);
                var heading = outdoor ? new Vector2(.85f + wind * .28f, kind == Kind.Leaf ? -.45f : .55f) : new Vector2(1f, .12f * wind);
                Emit(kind, point, heading, 1, random);
            }
            var lamps = GroundShadowLight.Active;
            if (lamps.Count > 0 && (ambientSeed & 3) == 1)
            {
                nextLamp %= lamps.Count;
                var lamp = lamps[nextLamp++];
                if (lamp != null && lamp.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Point)
                    Emit(Kind.Ember, lamp.transform.position, Vector2.up, 1, random ^ 0xa11ceU);
            }
        }
        important.Flush(this); decorative.Flush(this);
        batch.enabled=Time.time < lastExpiry;
        if (!batch.enabled) return;
        material.SetFloat("_SparkTime",Time.time);
        material.SetFloat("_AirVisibility", Mathf.Clamp01(density));
        if (!UsesCompute) { material.DisableKeyword("FIELD_SIMULATED"); return; }
        material.EnableKeyword("FIELD_SIMULATED");
        material.SetBuffer("_Particles", particles);
        var mask = WorldAtmosphere.Current != null ? WorldAtmosphere.Current.Floor : null;
        bool hasMask = mask != null && mask.Texture != null;
        flow.SetTexture(kernel,"_FloorMask",hasMask ? mask.Texture : Texture2D.whiteTexture);
        if (hasMask) flow.SetMatrix("_FloorUV",mask.WorldToUV);
        flow.SetInt("_HasFloorMask",hasMask ? 1 : 0);
        flow.SetInt("_Capacity",Capacity);
        flow.SetFloat("_Now",Time.time);
        flow.SetBuffer(kernel,"_Particles",particles);
        flow.Dispatch(kernel,(Capacity+63)/64,1,1);
    }
    private void Upload(int slot,int count)
    {
        mesh.SetVertexBufferData(vertices,slot*4,slot*4,count*4,0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        particles?.SetData(initial,slot,slot,count);
    }
    private void OnDestroy()
    {
        DamageEventBus.OnAttackHitResolved -= Hit;
        SceneManager.activeSceneChanged -= SceneChanged;
        if (instance==this) instance=null;
        particles?.Release();
        Destroy(mesh); Destroy(material);
    }
}
