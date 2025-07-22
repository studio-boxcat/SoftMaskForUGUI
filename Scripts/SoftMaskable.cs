// ReSharper disable InconsistentNaming

#nullable enable
using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Coffee.UISoftMask
{
    /// <summary>
    /// Soft maskable.
    /// Add this component to Graphic under SoftMask for smooth masking.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    public class SoftMaskable : MonoBehaviour, IMaterialModifier
#if UNITY_EDITOR
        , ISelfValidator
#endif
    {
        private const int kVisibleInside = (1 << 0) + (1 << 2) + (1 << 4) + (1 << 6); // 170
        private const int kVisibleOutside = (1 << 1) + (1 << 3) + (1 << 5) + (1 << 7); // 85
        private static readonly Hash128 k_InvalidHash = new Hash128();

        private static readonly int s_SoftMaskTexId = Shader.PropertyToID("_SoftMaskTex");
        private static readonly int s_StencilCompId = Shader.PropertyToID("_StencilComp");
        private static readonly int s_MaskInteractionId = Shader.PropertyToID("_MaskInteraction");

        [SerializeField, Tooltip("The interaction for each masks."), HideInInspector]
        private int m_MaskInteraction = kVisibleInside;

        [SerializeField, Tooltip("Use stencil to mask.")]
        private bool m_UseStencil = false;

        [NonSerialized] private Graphic? _graphic;
        private Hash128 _effectMaterialHash;

        /// <summary>
        /// The graphic associated with the soft mask.
        /// </summary>
        public Graphic graphic => _graphic ??= GetComponent<Graphic>();

        public Material? modifiedMaterial { get; private set; }

        public SoftMask? ResolveMask() => transform.NearestUpwards_GOActiveAndCompEnabled<SoftMask>();

        /// <summary>
        /// Perform material modification in this function.
        /// </summary>
        /// <returns>Modified material.</returns>
        /// <param name="baseMaterial">Configured Material.</param>
        Material IMaterialModifier.GetModifiedMaterial(Material baseMaterial)
        {
            var softMask = ResolveMask();
            modifiedMaterial = null;

            // If this component is disabled, the material is returned as is.
            // If the parents do not have a soft mask component, the material is returned as is.
            if (!isActiveAndEnabled || !softMask)
            {
                MaterialCache.Unregister(_effectMaterialHash);
                _effectMaterialHash = k_InvalidHash;
                return baseMaterial;
            }

            // Generate soft maskable material.
            var previousHash = _effectMaterialHash;
            _effectMaterialHash = new Hash128(
                (uint) baseMaterial.GetInstanceID(),
                (uint) softMask!.GetInstanceID(),
                (uint) m_MaskInteraction,
                (uint) (m_UseStencil ? 1 : 0)
            );

            // Generate soft maskable material.
            modifiedMaterial = MaterialCache.Register(baseMaterial, _effectMaterialHash, mat =>
            {
                mat.shader = Shader.Find(ResolveShaderName(mat.shader.name));
                mat.SetTexture(s_SoftMaskTexId, softMask.softMaskBuffer);
                mat.SetInt(s_StencilCompId, m_UseStencil ? (int) CompareFunction.Equal : (int) CompareFunction.Always);

#if UNITY_EDITOR
                SoftMaskSceneViewHandler.SetUpMaterialProperties(mat, graphic.canvas.worldCamera);
#endif

                var stencil = MaskUtilities.GetStencilDepth(transform);
                mat.SetVector(s_MaskInteractionId, new Vector4(
                    1 <= stencil ? (m_MaskInteraction >> 0 & 0x3) : 0,
                    2 <= stencil ? (m_MaskInteraction >> 2 & 0x3) : 0,
                    3 <= stencil ? (m_MaskInteraction >> 4 & 0x3) : 0,
                    4 <= stencil ? (m_MaskInteraction >> 6 & 0x3) : 0
                ));
            });

            // Unregister the previous material.
            MaterialCache.Unregister(previousHash);

            return modifiedMaterial;
        }

        /// <summary>
        /// Set the interaction for each mask.
        /// </summary>
        public void SetMaskInteraction(bool visibleInside)
        {
            m_MaskInteraction = visibleInside ? kVisibleInside : kVisibleOutside;
            graphic.SetMaterialDirty();
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        private void OnEnable()
        {
#if UNITY_EDITOR
            SoftMaskSceneViewHandler.Add(this);
#endif

            graphic.SetMaterialDirty();
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        private void OnDisable()
        {
#if UNITY_EDITOR
            SoftMaskSceneViewHandler.Remove(this);
#endif

            graphic.SetMaterialDirty();

            MaterialCache.Unregister(_effectMaterialHash);
            _effectMaterialHash = k_InvalidHash;
        }

        private static string ResolveShaderName(string shaderName)
        {
            return shaderName switch
            {
                "UI/Default" => "Hidden/UI/Default (SoftMaskable)",
                "MeowTower/UI/UI-PremultAlpha" => "Hidden/UI/PremultAlpha (SoftMaskable)",
                _ => throw new Exception($"Shader {shaderName} not supported.")
            };
        }

#if UNITY_EDITOR
        /// <summary>
        /// This function is called when the script is loaded or a value is changed in the inspector (Called in the editor only).
        /// </summary>
        private void OnValidate()
        {
            graphic.SetMaterialDirty();
        }

        void ISelfValidator.Validate(SelfValidationResult result)
        {
            var g = GetComponent<Graphic>();
            if (!g || !g.material || !g.material.shader)
            {
                result.AddError("Graphic component is missing or has no material/shader.");
                return;
            }

            var shader = g.material.shader;
            try
            {
                ResolveShaderName(shader.name);
            }
            catch (Exception e)
            {
                result.AddError(e.Message);
            }
        }
#endif
    }
}