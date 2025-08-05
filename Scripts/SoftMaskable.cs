// ReSharper disable InconsistentNaming

#nullable enable
using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Coffee.UISoftMask
{
    internal enum MaskInteraction : byte
    {
        VisibleInside = ((1 << 0) + (1 << 2) + (1 << 4) + (1 << 6)), // 170, 0b01
        VisibleOutside = (1 << 1) + (1 << 3) + (1 << 5) + (1 << 7), // 85, 0b10
    }

    /// <summary>
    /// Soft maskable.
    /// Add this component to Graphic under SoftMask for smooth masking.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    public sealed class SoftMaskable : MonoBehaviour, IMaterialModifier
#if UNITY_EDITOR
        , ISelfValidator
#endif
    {
        [SerializeField, OnValueChanged("SetMaterialDirty")]
        private MaskInteraction m_MaskInteraction = MaskInteraction.VisibleInside;

        [NonSerialized] private Graphic? _graphic;
        public Graphic graphic => _graphic ??= GetComponent<Graphic>();

        private MaterialLink? _materialLink;
        public Material? modifiedMaterial => _materialLink?.Material;

        Material IMaterialModifier.GetModifiedMaterial(Material baseMaterial)
        {
            // If this component is disabled, the material is returned as is.
            // If the parents do not have a soft mask component, the material is returned as is.
            if (!isActiveAndEnabled)
            {
                L.W("[SoftMaskable] is not active. Returning base material: " + this, this);
                return baseMaterial;
            }

            var softMask = transform.NearestUpwards_GOActiveAndCompEnabled<SoftMask>();
            if (!softMask)
            {
                L.I("[SoftMaskable] No SoftMask component found in the parent hierarchy. Returning base material: " + this, this);
                return baseMaterial;
            }

            // Generate soft maskable material.
            var maskRt = softMask!.PopulateMaskRt();
            MaterialCache.Rent(ref _materialLink, baseMaterial, m_MaskInteraction, maskRt);

            var mat = _materialLink!.Material;
#if DEBUG
            // XXX: material properties will be cleared after the scene or prefab is saved.
            if (_materialLink.IsMaterialConfigured() is false)
            {
                if (Editing.No(this)) L.E("[SoftMaskable] Material properties were cleared. Reconfiguring material: " + this, this);
                _materialLink.ConfigureMaterial();
                SoftMaskSceneViewHandler.SetUpGameVP(mat, graphic.canvas.worldCamera);
            }
#endif

            return mat;
        }

        /// <summary>
        /// Set the interaction for each mask.
        /// </summary>
        public void SetMaskInteraction(bool visibleInside)
        {
            m_MaskInteraction = visibleInside
                ? MaskInteraction.VisibleInside : MaskInteraction.VisibleOutside;
            SetMaterialDirty();
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        private void OnEnable()
        {
#if UNITY_EDITOR
            SoftMaskSceneViewHandler.Add(this);
#endif

            SetMaterialDirty();
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        private void OnDisable()
        {
#if UNITY_EDITOR
            SoftMaskSceneViewHandler.Remove(this);
#endif

            SetMaterialDirty();

            if (_materialLink is not null)
            {
                _materialLink.Release();
                _materialLink = null;
            }
        }

        private void SetMaterialDirty() => graphic.SetMaterialDirty();

#if UNITY_EDITOR
        void ISelfValidator.Validate(SelfValidationResult result)
        {
            var g = GetComponent<Graphic>();
            if (!g || !g.material || !g.material.shader)
            {
                result.AddError("Graphic component is missing or has no material/shader.");
                return;
            }

            if (MaterialCache.TryResolveShaderIndex(g.material.shader.name, out _) is false)
                result.AddError($"Shader '{g.material.shader.name}' is not supported by SoftMaskable.");

            if (m_MaskInteraction is not (MaskInteraction.VisibleInside or MaskInteraction.VisibleOutside))
                result.AddError($"Invalid mask interaction value: {m_MaskInteraction}.");

            if (g is MaskableGraphic { maskable: true })
                result.AddError("MaskableGraphic is enabled. SoftMaskable should be used with non-maskable graphics to avoid conflicts.");

            if (this.HasComponentInParent<SoftMask>(includeInactive: true) is false)
                result.AddError("SoftMaskable must be a child of SoftMask component to work properly.");

            if (ComponentSearch.AnyComponentExcept<IMaterialModifier>(this,
                    except1: typeof(MaskableGraphic), except2: typeof(SoftMaskable)))
            {
                result.AddError("SoftMaskable should not be used with other IMaterialModifier components " +
                                "except MaskableGraphic or SoftMaskable itself. " +
                                "This may cause unexpected behavior.");
            }
        }
#endif
    }
}