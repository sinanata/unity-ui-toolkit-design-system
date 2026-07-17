// The wall face of an RT-hosted corridor exhibit (see WorldSpaceCorridor.MakeRtPanel):
// draws the exhibit panel's RenderTexture, straight alpha over whatever mesh sits
// behind it — the mounting plate shows through exactly as it does under the native
// world-space panels. Hand-written CG on purpose: URP's variant stripping only zeroes
// URP-family shaders, so this survives a WebGL build from a Resources folder the same
// way the DsFx material shaders do, with no KeepAlive plumbing.
Shader "Hidden/DsShowcase/RtScreen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
