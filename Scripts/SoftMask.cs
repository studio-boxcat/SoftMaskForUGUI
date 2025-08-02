// ReSharper disable InconsistentNaming

#nullable enable

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Coffee.UISoftMask
{
    /// <summary>
    /// Soft mask.
    /// Use instead of Mask for smooth masking.
    /// </summary>
    public sealed class SoftMask : Mask, IMeshModifier
#if UNITY_EDITOR
        , ISelfValidator
#endif
    {
        /// <summary>
        /// Down sampling rate.
        /// </summary>
        public enum DownSamplingRate { None = 0, x1 = 1, x2 = 2, x4 = 4, x8 = 8, }

        private static readonly List<SoftMask> s_ActiveSoftMasks = new List<SoftMask>();
        private static int s_MainTexId;
        private static int s_SoftnessId;
        private static int s_Alpha;

        [SerializeField, OnValueChanged("SetMaskRtDirty")]
        private DownSamplingRate m_DownSamplingRate = DownSamplingRate.x4;
        public DownSamplingRate downSamplingRate
        {
            get => m_DownSamplingRate;
            set
            {
                if (m_DownSamplingRate == value) return;
                m_DownSamplingRate = value;
                SetMaskRtDirty();
            }
        }

        [SerializeField, Range(0, 1), OnValueChanged("SetMaskRtDirty")]
        private float m_Softness = 1;
        public float softness
        {
            get => m_Softness;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(m_Softness, value)) return;
                m_Softness = value;
                SetMaskRtDirty();
            }
        }

        [SerializeField, Range(0f, 1f), OnValueChanged("SetMaskRtDirty")]
        private float m_Alpha = 1;
        public float alpha
        {
            get => m_Alpha;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(m_Alpha, value)) return;
                m_Alpha = value;
                SetMaskRtDirty();
            }
        }

        [NonSerialized, ShowInInspector, ReadOnly, PreviewField, HorizontalGroup("Preview"), HideLabel]
        private Mesh? _graphicMesh; // hook graphic mesh into SoftMask.
        [NonSerialized, ShowInInspector, ReadOnly, PreviewField, HorizontalGroup("Preview"), HideLabel]
        private RenderTexture? _maskRt;
        private MaterialPropertyBlock? _mpb;
        private CommandBuffer? _cb;


        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            SetMaskRtDirty();
            return base.GetModifiedMaterial(baseMaterial);
        }

        void IMeshModifier.ModifyMesh(MeshBuilder mb)
        {
            _graphicMesh ??= MeshPool.CreateDynamicMesh();
            _graphicMesh.Clear();
            mb.FillMesh(_graphicMesh);
            SetMaskRtDirty();
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            SetMaskRtDirty();

            // Register.
            if (s_ActiveSoftMasks.IsEmpty())
                Canvas.willRenderCanvases += UpdateMaskTextures;
            s_ActiveSoftMasks.Add(this);

            graphic.SetVerticesDirty();

            base.OnEnable();

            SetAllMaskablesDirty(this);
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        protected override void OnDisable()
        {
            // Unregister.
            s_ActiveSoftMasks.Remove(this);
            if (s_ActiveSoftMasks.IsEmpty())
                Canvas.willRenderCanvases -= UpdateMaskTextures;

            // Destroy objects.
            _mpb?.Clear();
            _mpb = null;
            _cb?.Release();
            _cb = null;

            DestroyImmediate(_graphicMesh);
            _graphicMesh = null;

            if (_maskRt)
            {
                RenderTexture.ReleaseTemporary(_maskRt);
                _maskRt = null;
            }

            base.OnDisable();

            SetAllMaskablesDirty(this);
        }

        private void OnTransformParentChanged()
        {
            if (isActiveAndEnabled)
                SetMaskRtDirty();
        }

        private bool _markRtDirty;

        private void SetMaskRtDirty() => _markRtDirty = true;

        internal RenderTexture PopulateMaskRt()
        {
            // Check the size of soft mask buffer.
            GetDownSamplingSize(m_DownSamplingRate, out var w, out var h);

            if (_maskRt && _maskRt!.width == w && _maskRt.height == h)
                return _maskRt;

            if (_maskRt)
            {
                L.I($"[SoftMask] Resizing soft mask buffer: {w}x{h}, down sampling rate: {m_DownSamplingRate}.");
                _maskRt!.Release(); // release the buffer to change the size.
                _maskRt.width = w;
                _maskRt.height = h;
            }
            else
            {
                L.I($"[SoftMask] Creating soft mask buffer: {w}x{h}, down sampling rate: {m_DownSamplingRate}.");
                _maskRt = RenderTexture.GetTemporary(w, h, depthBuffer: 0, RenderTextureFormat.R8);
            }

            SetMaskRtDirty();
            return _maskRt;
        }

        /// <summary>
        /// Update all soft mask textures.
        /// </summary>
        private static void UpdateMaskTextures()
        {
            foreach (var sm in s_ActiveSoftMasks)
            {
                if (sm.transform.UnsetHasChanged())
                    sm._markRtDirty = true;
                if (sm._markRtDirty)
                    sm.UpdateMaskRt();
            }
        }

        /// <summary>
        /// Update the mask texture.
        /// </summary>
        private void UpdateMaskRt()
        {
            // L.I("[SoftMask] Updating mask buffer: " + this, this);

            if (_graphicMesh is null)
            {
                L.W("[SoftMask] No graphic mesh found. Skipping mask update.");
                return;
            }

            var cam = CanvasUtils.ResolveWorldCamera(graphic);
            if (!cam)
            {
                L.W("[SoftMask] No camera found: " + name);
                return;
            }

            Profiler.BeginSample("UpdateMaskRt");

            _markRtDirty = false;

            if (_cb is null)
            {
                _cb = new CommandBuffer();
                _mpb = new MaterialPropertyBlock();
            }

            // CommandBuffer.
            Profiler.BeginSample("Initialize CommandBuffer");
            _cb.Clear();
            _cb.SetRenderTarget(PopulateMaskRt());
            _cb.ClearRenderTarget(false, true, backgroundColor: default);
            Profiler.EndSample();

            // Set view and projection matrices.
            Profiler.BeginSample("Set view and projection matrices");
            _cb.SetViewProjectionMatrices(cam.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(cam.projectionMatrix, renderIntoTexture: false));
            Profiler.EndSample();

            // Draw soft masks.
            Profiler.BeginSample("Draw Mesh");

            if (s_MainTexId is 0)
            {
                s_MainTexId = Shader.PropertyToID("_MainTex");
                s_SoftnessId = Shader.PropertyToID("_Softness");
                s_Alpha = Shader.PropertyToID("_Alpha");
            }

            // Set material property.
            _mpb!.SetTexture(s_MainTexId, graphic.mainTexture);
            _mpb.SetFloat(s_SoftnessId, m_Softness);
            _mpb.SetFloat(s_Alpha, m_Alpha);

            // Draw mesh.
            var mat = GetSharedMaskMaterial();
            _cb.DrawMesh(_graphicMesh, transform.localToWorldMatrix, mat, 0, 0, _mpb);

            Profiler.EndSample();

            Graphics.ExecuteCommandBuffer(_cb);
            Profiler.EndSample();
        }

        [NonSerialized] private static Material? _maskMat;
        private static Material GetSharedMaskMaterial() => _maskMat ??= Resources.Load<Material>("SoftMask");

        private static readonly List<SoftMaskable> _maskables = new();
        private static void SetAllMaskablesDirty(SoftMask sm)
        {
            sm.GetComponentsInChildren(includeInactive: false, _maskables); // no need to clear
            foreach (var maskable in _maskables)
                maskable.graphic.SetMaterialDirty();
        }

        /// <summary>
        /// Gets the size of the down sampling.
        /// </summary>
        private static void GetDownSamplingSize(DownSamplingRate rate, out int w, out int h)
        {
            w = Screen.currentResolution.width;
            h = Screen.currentResolution.height;

            if (rate == DownSamplingRate.None)
                return;

            var aspect = (float) w / h;
            if (w < h)
            {
                h = Mathf.ClosestPowerOfTwo(h / (int) rate);
                w = Mathf.CeilToInt(h * aspect);
            }
            else
            {
                w = Mathf.ClosestPowerOfTwo(w / (int) rate);
                h = Mathf.CeilToInt(w / aspect);
            }
        }

#if UNITY_EDITOR
        void ISelfValidator.Validate(SelfValidationResult result)
        {
            if (graphic && graphic.canvas && graphic.canvas.renderMode is not RenderMode.ScreenSpaceCamera)
                result.AddError("SoftMask only works with ScreenSpaceCamera render mode: " + graphic.canvas.renderMode);

            var graphics = this.GetGraphicsInChildrenShared();
            foreach (var g in graphics)
            {
                // Skip self.
                if (g.gameObject.RefEq(gameObject))
                    continue;

                if (g.HasComponent<SoftMask>())
                    result.AddError($"Nested SoftMask found in {g.name}.");
                if (g is not NonDrawingGraphic && g.NoComponent<SoftMaskable>())
                    result.AddError($"SoftMaskable component is missing in {g.name}.");
            }
        }
#endif
    }
}