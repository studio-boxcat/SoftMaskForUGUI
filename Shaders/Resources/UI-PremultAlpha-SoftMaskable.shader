Shader "Hidden/UI/PremultAlpha (SoftMaskable)"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ColorMask ("Color Mask", Float) = 15
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile __ UNITY_UI_CLIP_RECT

            #include "Packages/com.coffee.softmask-for-ugui/Shaders/SoftMask.cginc"	// Add for soft mask
            #pragma shader_feature __ SOFTMASK_EDITOR	// Add for soft mask

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
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

                // XXX: alpha 뿐만 아니라 rgb 값도 함께 감쇄시킴.
                color *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);

                // XXX: alpha 뿐만 아니라 rgb 값도 함께 감쇄시킴.
                color *= SoftMask(IN.vertex, IN.worldPosition);	// Add for soft mask

                // XXX: 텍스쳐 샘플링을 제외한 인풋 버텍스 알파만을 곱해서 감쇄시킴.
                // color.rgb *= color.a;
                color.rgb *= IN.color.a;

                return color;
            }
            ENDCG
        }
    }
}
