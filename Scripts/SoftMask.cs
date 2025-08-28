// ReSharper disable InconsistentNaming

#nullable enable

using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Coffee.UISoftMask
{
    /// <summary>
    /// Soft mask.
    /// Use instead of Mask for smooth masking.
    /// </summary>
    [ExecuteAlways]
    public sealed class SoftMask : MonoBehaviour, IMeshModifier, IMaterialModifier, IPostGraphicRebuildCallback
#if UNITY_EDITOR
        , ISelfValidator
#endif
    {
        [SerializeField, Required, ChildGameObjectsOnly, HideIf("_graphic_HideIf")]
        private Graphic _graphic = null!;
        [SerializeField, FormerlySerializedAs("m_ShowMaskGraphic")]
        private bool _showMaskGraphic = true;
        [SerializeField, Required, ChildGameObjectsOnly, PropertyOrder(1000)]
        [ListDrawerSettings(IsReadOnly = true)]
        private SoftMaskable[] _maskables = null!;

        private enum DownSamplingRate { None = 0, x1 = 1, x2 = 2, x4 = 4, x8 = 8, }

        [SerializeField, OnValueChanged("QueueRenderMaskRt")]
        private DownSamplingRate m_DownSamplingRate = DownSamplingRate.x4;
        [SerializeField, Range(0, 1), OnValueChanged("QueueRenderMaskRt")]
        private float m_Softness = 1;

        [SerializeField, Range(0f, 1f), OnValueChanged("QueueRenderMaskRt")]
        private float m_Alpha = 1;
        public float alpha
        {
            get => m_Alpha;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(m_Alpha, value)) return;
                m_Alpha = value;
                QueueRenderMaskRt();
            }
        }

        [NonSerialized, ShowInInspector, ReadOnly, PreviewField, HorizontalGroup("Preview"), HideLabel]
        [PropertyOrder(2000)]
        private RenderTexture? _maskRt;
        private MaterialPropertyBlock? _mpb;
        private CommandBuffer? _cb;


        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        public void OnEnable()
        {
            QueueRenderMaskRt();

            _graphic.SetMaterialDirty();

            foreach (var m in _maskables)
            {
                if (m) m.graphic.SetMaterialDirty();
                else L.W($"[SoftMask] Maskable is destroyed: {m.SafeName()}");
            }
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        private void OnDisable()
        {
            // Destroy objects.
            _mpb?.Clear();
            _mpb = null;
            _cb?.Release();
            _cb = null;

            if (_maskRt)
            {
                RenderTexture.ReleaseTemporary(_maskRt);
                _maskRt = null;
            }

            foreach (var m in _maskables)
            {
                if (m) m.graphic.SetMaterialDirty();
                else L.W($"[SoftMask] Maskable is destroyed: {m.SafeName()}");
            }
        }

        private void LateUpdate()
        {
            if (transform.UnsetHasChanged())
                QueueRenderMaskRt();
        }

        internal RenderTexture PopulateMaskRt()
        {
            Assert.IsTrue(enabled, $"[SoftMask] SoftMask is disabled: {name}");

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

            return _maskRt;
        }

        void IMeshModifier.ModifyMesh(MeshBuilder mb) => QueueRenderMaskRt();

        Material IMaterialModifier.GetModifiedMaterial(Material baseMaterial)
        {
            QueueRenderMaskRt();
            return baseMaterial;
        }

        private void QueueRenderMaskRt() =>
            CanvasUpdateRegistry.QueueGraphicRebuildCallback(this);

        void IPostGraphicRebuildCallback.PostGraphicRebuild()
        {
            // L.I("[SoftMask] Updating mask buffer: " + this, this);

            var cr = _graphic.canvasRenderer;
            cr.materialCount = _showMaskGraphic ? 1 : 0;

            var cam = CanvasUtils.ResolveWorldCamera(_graphic);
            if (!cam)
            {
                L.W("[SoftMask] No camera found: " + name);
                return;
            }

            Profiler.BeginSample("UpdateMaskRt");

            if (_cb is null)
            {
                _cb = new CommandBuffer();
                _mpb = new MaterialPropertyBlock();
            }

            // arrange command buffer
            _cb.Clear();
            _cb.SetRenderTarget(PopulateMaskRt());
            _cb.ClearRenderTarget(false, true, backgroundColor: default);
            _cb.SetViewProjectionMatrices(cam!.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(cam.projectionMatrix, renderIntoTexture: false));

            // set material property
            _mpb!.SetTexture(s_MainTexId, _graphic.mainTexture);
            _mpb.SetFloat(s_SoftnessId, m_Softness);
            _mpb.SetFloat(s_Alpha, m_Alpha);

            // draw mesh & execute command buffer
            _cb.DrawMesh(cr.GetMesh(), transform.localToWorldMatrix, GetSharedMaskMaterial(), 0, 0, _mpb);
            Graphics.ExecuteCommandBuffer(_cb);

            Profiler.EndSample(); // UpdateMaskRt
        }

        private static readonly int s_MainTexId;
        private static readonly int s_SoftnessId;
        private static readonly int s_Alpha;

        static SoftMask()
        {
            s_MainTexId = Shader.PropertyToID("_MainTex");
            s_SoftnessId = Shader.PropertyToID("_Softness");
            s_Alpha = Shader.PropertyToID("_Alpha");
        }

        private static Material? _sharedMaskMat;
        private static Material GetSharedMaskMaterial() => _sharedMaskMat ??= Resources.Load<Material>("SoftMask");

        /// <summary>
        /// Gets the size of the down sampling.
        /// </summary>
        private static void GetDownSamplingSize(DownSamplingRate rate, out int w, out int h)
        {
            (w, h) = Screen.currentResolution;

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
        private void Reset()
        {
            _graphic = GetComponent<Graphic>();
            _maskables = GetComponentsInChildren<SoftMaskable>(includeInactive: true);
        }

        private void OnValidate()
        {
            if (!_graphic)
                _graphic = GetComponent<Graphic>();
            if (_maskables is null || _maskables.Length == 0)
                _maskables = GetComponentsInChildren<SoftMaskable>(includeInactive: true);
        }

        private bool _graphic_HideIf() =>
            _graphic && _graphic.gameObject.RefEq(gameObject);

        void ISelfValidator.Validate(SelfValidationResult result)
        {
            // get original serialized property "m_RenderMode"
            var graphic = _graphic;
            if (graphic && graphic.canvas)
            {
                var renderMode = graphic.canvas.renderMode;
                // XXX: unity internally returns RenderMode.ScreenSpaceOverlay when there's no camera set.
                // most prefabs have no camera set, so we need to check the property directly.
                if (renderMode is RenderMode.ScreenSpaceOverlay)
                    renderMode = (RenderMode) new UnityEditor.SerializedObject(graphic.canvas).FindProperty("m_RenderMode").intValue;

                if (renderMode is not RenderMode.ScreenSpaceCamera)
                    result.AddError("SoftMask only works with ScreenSpaceCamera render mode: " + renderMode);
            }

            var graphics = this.GetGraphicsInChildrenShared();
            foreach (var g in graphics)
            {
                // Skip self.
                if (g.gameObject.RefEq(gameObject))
                    continue;

                if (g.HasComponent<SoftMask>())
                    result.AddError($"Nested SoftMask found in {g.name}.");

                // commented out to prevent false alarms.
                /*
                if (g is not NonDrawingGraphic && g.NoComponent<SoftMaskable>())
                    result.AddError($"SoftMaskable component is missing in {g.name}.");
                */
            }
        }
#endif
    }
}