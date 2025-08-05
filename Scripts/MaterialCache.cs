// ReSharper disable InconsistentNaming

#nullable enable
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Coffee.UISoftMask
{
    internal class MaterialLink
    {
        private readonly ulong _hash;

        private readonly Material _material;
        public Material Material
        {
            get
            {
#if DEBUG
                if (!_material) L.E($"[SoftMask.MaterialLink] Material is destroyed or not initialized. Hash: {_hash}");
#endif
                return _material;
            }
        }

        private readonly int _shaderIndex;
        private readonly MaskInteraction _maskInteraction;
        private readonly RenderTexture _maskRt;
        private int _referenceCount;
        public int ReferenceCount => _referenceCount;

        public MaterialLink(ulong hash, byte shaderIndex, MaskInteraction maskInteraction, RenderTexture maskRt)
        {
            _hash = hash;
            _material = new Material(GetBaseMat());
            _material.SetHideAndDontSave();
            _shaderIndex = shaderIndex;
            _maskInteraction = maskInteraction;
            _maskRt = maskRt;
            _referenceCount = 1;
        }

        public bool Equals(ulong hash) => _hash == hash;

        public void Rent() => _referenceCount++;

        public void Release()
        {
            Assert.IsTrue(_material, "Material is destroyed or not initialized.");

            _referenceCount--;
            if (_referenceCount is 0)
            {
                Object.DestroyImmediate(_material);
                MaterialCache.Unregister(_hash);
            }
        }

        private static Material? _baseMat;
        private static Material GetBaseMat() => _baseMat ??= Resources.Load<Material>("SoftMaskable");

        private static ShaderID s_SoftMaskTexId = new("_SoftMaskTex");
        private static ShaderID s_MaskInteractionId = new("_MaskInteraction");

        public bool IsMaterialConfigured() => _material.HasFloat(s_MaskInteractionId.Val);

        public void ConfigureMaterial()
        {
            var (srcBlend, dstBlend, premult) = _shaderIndex switch
            {
                0 => (BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false), // default
                1 => (BlendMode.SrcAlpha, BlendMode.One, false), // additive
                2 => (BlendMode.One, BlendMode.OneMinusSrcAlpha, true), // premultiplied alpha
                _ => throw new Exception($"Shader index {_shaderIndex} not supported.")
            };

            _material.SetSrcBlend(srcBlend);
            _material.SetDstBlend(dstBlend);
            if (premult) _material.EnableKeyword("PREMULT");
            else _material.DisableKeyword("PREMULT");

            var mi = (byte) _maskInteraction;
            _material.SetTexture(s_SoftMaskTexId.Val, _maskRt);
            _material.SetFloat(s_MaskInteractionId.Val, mi & 0b11);
        }
    }

    internal static class MaterialCache
    {
        private static readonly Dictionary<ulong, MaterialLink> _cache = new();

        internal static bool TryResolveShaderIndex(string shaderName, out byte shaderIndex)
        {
            shaderIndex = shaderName switch
            {
                "UI/Default" => 0,
                "MeowTower/UI/UI-Additive" => 1,
                "MeowTower/UI/UI-PremultAlpha" => 2,
                _ => byte.MaxValue, // Not supported
            };

            return shaderIndex is not byte.MaxValue;
        }

        private static byte ResolveShaderIndex(string shaderName)
        {
            if (TryResolveShaderIndex(shaderName, out var shaderIndex))
                return shaderIndex;
            L.E($"[SoftMask.MaterialCache] Shader '{shaderName}' is not supported. Using default shader index 0.");
            return 0; // Fallback to default shader index.
        }

        private static ulong Hash(Material orgMat, MaskInteraction maskInteraction, RenderTexture maskRt, out byte shaderIndex)
        {
            shaderIndex = ResolveShaderIndex(orgMat.shader.name);
            return Numeric.PackU64(maskRt.GetInstanceID(), shaderIndex, (byte) maskInteraction);
        }

        public static void Rent(ref MaterialLink? link, Material orgMat, MaskInteraction maskInteraction, RenderTexture maskRt)
        {
            // L.I($"[SoftMask.MaterialCache] Registering material: {orgMat.name}, maskInteraction={maskInteraction}, depth={depth}, stencil={stencil}, mask={mask}");

            var hash = Hash(orgMat, maskInteraction, maskRt, out var shaderIndex);
            if (link is not null && link.Equals(hash)) return;

            // Release the old material link.
            link?.Release();

            if (_cache.TryGetValue(hash, out link))
            {
                link.Rent();
                return;
            }

            L.I($"[SoftMask.MaterialCache] Creating material: {orgMat.name}, hash={hash}");

            link = new MaterialLink(hash, shaderIndex, maskInteraction, maskRt);
            Assert.IsTrue(link.ReferenceCount is 1, "Reference count should be 1 after creation.");
            link.ConfigureMaterial();

            _cache.Add(hash, link);
#if DEBUG
            if (_cache.Count > 32)
                L.E("[SoftMask.MaterialCache] Material cache size exceeded 32. Consider optimizing material usage.");
#endif
        }

        internal static void Unregister(ulong hash)
        {
            // L.I($"[SoftMask.MaterialCache] Unregistering material, hash={hash}");
            _cache.Remove(hash);
        }
    }
}