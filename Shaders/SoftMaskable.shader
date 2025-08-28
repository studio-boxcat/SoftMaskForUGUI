Shader "Hidden/SoftMaskable"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Int) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Int) = 10 // OneMinusSrcAlpha
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile __ UNITY_UI_CLIP_RECT PREMULT

            #include "Packages/com.coffee.softmask-for-ugui/Shaders/SoftMask.cginc"	// Add for soft mask
            #pragma shader_feature __ SOFTMASK_EDITOR	// Add for soft mask

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);

                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);

                OUT.color = v.color * _Color;

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = tex2D(_MainTex, IN.texcoord) * IN.color;

                float factor = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect)
                    * SoftMask(IN.vertex, IN.worldPosition); // Add for soft mask

                #ifdef PREMULT // blend mode must be (One, OneMinusSrcAlpha).
                color *= factor; // alpha 뿐만 아니라 rgb 값도 함께 감쇄시킴.
                color.rgb *= IN.color.a; // 텍스쳐 샘플링을 제외한 인풋 버텍스 알파만을 곱해서 감쇄시킴.
                #else
                color.a *= factor; // (SrcAlpha, OneMinusSrcAlpha) for default
                #endif

                return color;
            }
            ENDCG
        }
    }
}