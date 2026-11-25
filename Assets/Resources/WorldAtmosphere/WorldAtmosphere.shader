Shader "Necromancer/World Atmosphere"
{
    Properties { _Palette ("Artist swatches", 2D) = "white" {} }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass {
            Tags { "LightMode"="Universal2D" }
            // The framebuffer already contains the sharp artwork. Add only scattered light and mist.
            Blend One OneMinusSrcAlpha, Zero One
            ZWrite Off Cull Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ ATMOS_VIEW
            #pragma multi_compile_local_fragment _ SUN_HIGHLIGHT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmospherePlane.hlsl"
            #include "CloudLight.hlsl"
            TEXTURE2D(_CameraSortingLayerTexture);
            float _Mist, _Bloom, _Shafts, _AtmosphereWindTime, _HasFloorMask;
            float4 _AirLook, _ShadeTint;
            float _GroundSunWeight;
            int _PreviewView;
            float4 _HitLight, _AccentLight, _BloomShape, _Sun, _SunTint;
            float4 _LeftHandLight, _RightHandLight;
            TEXTURE2D(_FloorMask); float4x4 _FloorUV;
            int _GroundLightCount;
            float4 _GroundLights[8], _GroundCones[8], _GroundLampColors[8];
            float SkyAccess(float2 uv);
            half3 SourceGlow(float2 world, out half3 incident) {
                // Core, corona and outer pool have different radii. Distances are in world units.
                float2 hitDelta = world-_HitLight.xy, accentDelta = world-_AccentLight.xy;
                float hit = exp2(-5*dot(hitDelta,hitDelta))*_HitLight.z;
                float accent = exp2(-7*dot(accentDelta,accentDelta))*_AccentLight.z;
                incident = hit*Swatch(0,2) + accent*Swatch(8,5);
                float2 left = world-_LeftHandLight.xy, right = world-_RightHandLight.xy;
                float spirit = exp2(-2.7*dot(left,left))*_LeftHandLight.z +
                               exp2(-2.7*dot(right,right))*_RightHandLight.z;
                half3 soulLight = Swatch(8,5)*(.43*spirit);
                half3 glow = incident*.35 + soulLight*.18;
                incident += soulLight;
                [loop] for(int n=0;n<_GroundLightCount;n++) {
                    float2 delta = world-_GroundLights[n].xy;
                    float d2 = dot(delta,delta), inverseRadius = _GroundLights[n].z;
                    float u = d2*inverseRadius*inverseRadius;
                    if (u > 2) continue;
                    float4 cone = _GroundCones[n];
                    float angular = cone.w == 0 ? 1 : saturate((dot(delta,cone.xy)*rsqrt(max(.0001,d2))-cone.z)*cone.w);
                    float pool = saturate(1-u);
                    float transmission = 1;
                    if (_HasFloorMask > .5 && u > .035)
                    {
                        float2 receiver = mul(_FloorUV,float4(world,0,1)).xy;
                        float2 toLamp = mul((float2x2)_FloorUV,-delta);
                        float path = min(SkyAccess(receiver+.36*toLamp),SkyAccess(receiver+.70*toLamp));
                        transmission -= .66*(1-path)*SkyAccess(receiver);
                    }
                    half3 color = _GroundLampColors[n].rgb * (_GroundLights[n].w*angular);
                    incident += color*(pool*pool*transmission);
                    float spread = max(.25,_BloomShape.y*_BloomShape.y);
                    glow += color*((.55*exp2(-27*u/spread)+.16*pool*pool)*transmission+1.25*exp2(-110*d2/spread));
                }
                return glow;
            }
            half3 Bright(float2 uv) {
                half3 c = SAMPLE_TEXTURE2D(_CameraSortingLayerTexture,sampler_LinearClamp,uv).rgb;
                half peak = max(c.r,max(c.g,c.b));
                return c * smoothstep(_BloomShape.x*.74, _BloomShape.x+.12, peak);
            }
            half3 PixelBloom(float2 uv, float2 px) {
                // A connected kernel has one peak. Sparse, distant taps echo bright silhouettes.
                float sigma = clamp(_BloomShape.y*1.55, 1, 3.3);
                float4 w = exp2(-float4(1,4,9,16)/(sigma*sigma));
                float offsets[5] = { -3-w.w/(w.z+w.w), -1-w.y/(w.x+w.y), 0, 1+w.y/(w.x+w.y), 3+w.w/(w.z+w.w) };
                float weights[5] = { w.z+w.w, w.x+w.y, 1, w.x+w.y, w.z+w.w };
                half3 sum = 0;
                float total = 0;
                [unroll] for (int y=0; y<5; ++y)
                [unroll] for (int x=0; x<5; ++x)
                {
                    float2 delta = float2(offsets[x],offsets[y]);
                    float weight = weights[x]*weights[y];
                    sum += weight * Bright(uv+delta*px);
                    total += weight;
                }
                return sum/total;
            }
            float OpenToSun(float2 uv) {
                return SAMPLE_TEXTURE2D(_FloorMask,sampler_PointClamp,uv).r * all(uv>=0) * all(uv<1);
            }
            float SkyAccess(float2 uv) {
                // Interpolate the fraction of open sky across cell boundaries, in lighting only.
                return SAMPLE_TEXTURE2D(_FloorMask,sampler_LinearClamp,uv).r * all(uv>=0) * all(uv<1);
            }
            half3 SunHighlight(float2 uv,float2 world)
            {
                #ifdef SUN_HIGHLIGHT
                // Project the world-space sun into this camera's pixel axes, including RT flips.
                float2 stepUV = ddx(uv) * dot(_Sun.xy,normalize(ddx(world))) +
                                ddy(uv) * dot(_Sun.xy,normalize(ddy(world)));
                float3 weights = float3(.2126,.7152,.0722);
                float luma = dot(SAMPLE_TEXTURE2D(_CameraSortingLayerTexture,sampler_PointClamp,uv).rgb,weights);
                float neighbor = dot(SAMPLE_TEXTURE2D(_CameraSortingLayerTexture,sampler_PointClamp,uv+stepUV*1.5).rgb,weights);
                float rim = smoothstep(.025,.26,luma-neighbor) * sqrt(saturate(1-_Sun.z*_Sun.z));
                float top = smoothstep(.42,.9,luma) * _Sun.z * .36;
                return _SunTint.rgb * _Sun.w * (floor(saturate(rim+top)*5+.5)/5);
                #else
                return 0;
                #endif
            }
            half4 Frag(V i):SV_Target {
                float2 px = rcp(_ScaledScreenParams.xy);
                // The sorting-layer copy and this pass share the same render-target origin.
                float2 uv = i.pos.xy * px;
                float2 pixel = floor(i.world * 32);
                float2 p = (pixel+.5) / 32;
                half3 incident;
                half3 corona = SourceGlow(p,incident);
                half3 artwork = SAMPLE_TEXTURE2D(_CameraSortingLayerTexture,sampler_PointClamp,uv).rgb;
                #ifdef ATMOS_VIEW
                if (_PreviewView == 1) return half4(0,0,0,0);
                if (_PreviewView == 2) return half4(saturate(corona+Bright(uv)),1);
                #endif
                half3 glow = 0;
                UNITY_BRANCH if (_Bloom > 0)
                    glow = _Bloom*(.70*PixelBloom(uv,px)+corona);
                // Compress highlights before adding them to the sharp world. Preserve colour in bright cores.
                half3 scattered = glow/(1+glow) * (1-.70*saturate(artwork)) + SunHighlight(uv,i.world);
                float t = _AtmosphereWindTime;
                float2 maskUV = mul(_FloorUV, float4(p,0,1)).xy;
                float floorCell = _HasFloorMask > .5 ? OpenToSun(maskUV) : 1;
                #ifdef ATMOS_VIEW
                if (_PreviewView == 3) return half4(floorCell.xxx,1);
                #endif
                float across = dot(p,float2(-_Sun.y,_Sun.x));
                float along = dot(p,_Sun.xy);
                // Average light over a length of air. A bank interrupts the shaft gradually along its path.
                float2 skyUV = SkyUV(p);
                float2 rayStep = _Sun.xy * float2(2.8/27,2.8/19);
                float bank = CloudBank(skyUV);
                float alongRay = .25*bank + .5*CloudBank(skyUV+rayStep) + .25*CloudBank(skyUV+2*rayStep);
                float broad = 1-alongRay;
                float2 apertureUV = float2(along/18,across/9.5) - _CloudProjection.xy;
                float opening = CloudBank(apertureUV+float2(.24,.15));
                float lace = CloudBank(float2(along/12,across/6.4)-_CloudProjection.xy+float2(.54,.62));
                float cloud = .30+.44*alongRay+.20*opening;
                float reach = sqrt(saturate(1-_Sun.z*_Sun.z));
                float2 towardSun = mul((float2x2)_FloorUV,_Sun.xy)*reach;
                float access = _HasFloorMask > .5 ? .36+.38*SkyAccess(maskUV+towardSun*.9)+.26*SkyAccess(maskUV+towardSun*2.2) : 1;
                float window = (.82*opening+.33*lace)*(.34+.66*broad);
                float sunPatch = .11+.25*broad*broad + window;
                float shafts = _Shafts*_GroundSunWeight*access*lerp(.43+.48*broad,sunPatch,saturate(reach*2));
                float grain = Hash(pixel);
                float haze = smoothstep(.22,.78,cloud)*.56;
                float alpha = saturate(floor(haze*20+grain)/20 * _Mist)*floorCell;
                #ifdef ATMOS_VIEW
                if (_PreviewView == 4) return half4(alpha.xxx,1);
                #endif
                half3 silver = Swatch(1,7);
                half3 mint = Swatch(9,5);
                half3 rose = Swatch(8,4);
                half3 pearl = lerp(lerp(silver,mint,_AirLook.x),rose,_AirLook.y);
                half3 tint = lerp(Swatch(10,7),pearl,.22+.48*cloud);
                // Backscatter in the moving shafts and a luminous fringe on the denser fog banks.
                float fringe = 4*opening*(1-opening)*alongRay;
                float sunlight = saturate(shafts);
                float shadow = saturate(_Shafts)*(.68-.61*sunlight);
                // Bounce light takes the local surface colour. Suspended mist also scatters into dark pixels.
                scattered += artwork*(min(shafts,2)*_SunTint.rgb*.96 + min(incident,2)*.45);
                scattered += _SunTint.rgb*(shafts*(.016+.26*_Mist)*(.30+.66*cloud+.31*lace));
                scattered += pearl*.065*fringe*_Mist*floorCell;
                scattered += min(incident,2)*alpha*.42;
                UNITY_BRANCH if (_AirLook.z > 0 && floorCell > 0)
                {
                    float2 fromCamera = (i.world - _WorldSpaceCameraPos.xy)*32;
                    float deep = Glimmers(pixel,39,t,13.2)*(1-_WorldDepthStrength);
                    float near = Glimmers(floor(i.world*32-fromCamera*.105*_WorldDepthStrength),67,t*.74,82.7);
                    scattered += pearl * ((.42*deep+.72*near)*_AirLook.z*(.50+.46*broad));
                }
                // The ground plane carries the low bank behind silhouettes and floor telegraphs.
                alpha *= 1 - .76*_WorldDepthStrength;
                float opacity = alpha+shadow*(1-alpha);
                half3 light = tint*alpha + _ShadeTint.rgb*(shadow*.24) + scattered;
                // Roll off the top of the exposure range so pale faces and hands retain their sprite detail.
                half3 headroom = saturate(1-artwork*(1-opacity));
                half3 excess = max(0,light-.55*headroom);
                half3 shoulder = .45*headroom;
                light = min(light,.55*headroom) + excess*shoulder/max(.0001,excess+shoulder);
                return half4(light,opacity);
            }
            ENDHLSL
        }
    }
}
