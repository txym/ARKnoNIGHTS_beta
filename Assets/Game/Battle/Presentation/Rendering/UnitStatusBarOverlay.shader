Shader "ARKnoNIGHTS/Unit Status Bar Overlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SpriteUvRect ("Sprite UV Rect", Vector) = (1,1,0,0)
        _ForceWhite ("Force White", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _MainTex;
            float4 _Color;
            float4 _SpriteUvRect;
            float _ForceWhite;
            v2f vert(appdata value) { v2f output; output.vertex = UnityObjectToClipPos(value.vertex); output.uv = value.uv; return output; }
            fixed4 frag(v2f value) : SV_Target
            {
                fixed4 sample = tex2D(_MainTex, value.uv * _SpriteUvRect.xy + _SpriteUvRect.zw);
                sample.rgb = lerp(sample.rgb, fixed3(1, 1, 1), _ForceWhite);
                return sample * _Color;
            }
            ENDCG
        }
    }
}
