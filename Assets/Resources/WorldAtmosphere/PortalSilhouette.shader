Shader "Necromancer/Portal Silhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Captured pose", 2D) = "white" {}
        _Opacity ("Visibility", Range(0,1)) = 1
        _Turn ("Facing blend", Range(0,1)) = 0
        _Pull ("Intake", Range(0,1)) = 0
        _Rim ("Released pixels", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct In { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Out { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 grain:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Rim;
            float _Pull, _Opacity, _Turn;
            float4 _Pose, _Mouth;
            float age(float2 cell, float2 uv)
            {
                float seed=frac(dot(cell,float2(.75487767,.56984029)));
                return saturate((sqrt(_Pull) - (.56*seed+.13*uv.y+.04)) / .27);
            }
            Out vert(In v)
            {
                Out o;
                o.position=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv;
                o.grain=0;
                if(v.color.g < .5)
                {
                    float2 grid=v.uv*_MainTex_TexelSize.zw;
                    float2 cell=floor(grid);
                    o.uv=(cell+.5)/_MainTex_TexelSize.zw;
                    float t=age(cell,o.uv);
                    o.grain=float2(1,smoothstep(0,.09,t)*(1-smoothstep(.79,1,t)));
                    if(o.grain.y<=0) { o.position.xy=3*o.position.w; return o; }
                    float phase=frac(dot(cell,float2(.381966,.21785)))*6.28318;
                    float2 source=_Pose.xy+(o.uv-.5)*_Pose.zw;
                    float2 drift=float2(cos(phase+t*2),sin(phase+t*2))*(8+10*frac(phase*3));
                    float2 p=lerp(source,_Mouth.xy,t*t)+drift*sin(t*3.14159);
                    p=floor(p)+step(.5,frac(grid));
                    o.position.xy=(p/float2(640,360)*2-1)*float2(1,_ProjectionParams.x)*o.position.w;
                }
                return o;
            }
            half4 frag(Out i):SV_Target
            {
                float2 cell=floor(i.uv*_MainTex_TexelSize.zw);
                float t=age(cell,i.uv);
                float2 uv=i.uv;
                half4 pose=lerp(tex2D(_MainTex,uv),tex2D(_MainTex,float2(1-uv.x,uv.y)),_Turn);
                pose.rgb=lerp(pose.rgb,_Rim.rgb*pose.a,.46*saturate(t*3));
                return pose * (i.grain.x>.5 ? i.grain.y : _Opacity*(1-smoothstep(0,.09,t)));
            }
            ENDCG
        }
    }
}
