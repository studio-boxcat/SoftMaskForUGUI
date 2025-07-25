#ifndef UI_SOFTMASK_INCLUDED
#define UI_SOFTMASK_INCLUDED

sampler2D _SoftMaskTex;
float4x4 _GameVP;
float4x4 _GameTVP;
half _MaskInteraction;

float CustomStep(float a, float x)
{
	return x >= a;
}

fixed Approximately(float4x4 a, float4x4 b)
{
	float4x4 d = a - b;
	d = float4x4(
			abs(d[0]),
			abs(d[1]),
			abs(d[2]),
			abs(d[3])
		);
		
	return step(
		max(d._m00,max(d._m01,max(d._m02,max(d._m03,
		max(d._m10,max(d._m11,max(d._m12,max(d._m13,
		max(d._m20,max(d._m21,max(d._m22,max(d._m23,
		max(d._m30,max(d._m31,max(d._m32,d._m33))))))))))))))),
		0.5);
}

#if SOFTMASK_EDITOR
float SoftMaskInternal(float4 clipPos, float4 wpos)
#else
float SoftMaskInternal(float4 clipPos)
#endif
{
	// normalize: (0, 0) to (1, 1)
	half2 view = clipPos.xy/_ScreenParams.xy;

	// translate into canvas space when it's in scene view.
	#if SOFTMASK_EDITOR
		fixed isSceneView = 1 - Approximately(UNITY_MATRIX_VP, _GameVP);
		float4 cpos = mul(_GameTVP, mul(UNITY_MATRIX_M, wpos));
		view = lerp(view, cpos.xy / cpos.w * 0.5 + 0.5, isSceneView);
	#endif

	// flip y-axis when UV starts at top (game view and scene view are different)
	#if UNITY_UV_STARTS_AT_TOP
		view.y = lerp(view.y, 1 - view.y, CustomStep(0, _ProjectionParams.x));
	#endif

	// sample the mask texture, and convert it with mask interaction.
	fixed mask = tex2D(_SoftMaskTex, view).r;
	half alpha = saturate(lerp(1, lerp(mask, 1 - mask, _MaskInteraction - 1), _MaskInteraction));

	// if the mask is not in the range of [0, 1], it will be discarded.
	#if SOFTMASK_EDITOR
	alpha *= CustomStep(0, view.x) * CustomStep(view.x, 1) * CustomStep(0, view.y) * CustomStep(view.y, 1);
	#endif

	return alpha;
}

#if SOFTMASK_EDITOR
	#define SOFTMASK_EDITOR_ONLY(x) x
	#define SoftMask(clipPos, worldPosition) SoftMaskInternal(clipPos, worldPosition)
#else
	#define SOFTMASK_EDITOR_ONLY(x)
	#define SoftMask(clipPos, worldPosition) SoftMaskInternal(clipPos)
#endif

#endif // UI_SOFTMASK_INCLUDED