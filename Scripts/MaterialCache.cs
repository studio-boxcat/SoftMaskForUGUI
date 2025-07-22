// ReSharper disable InconsistentNaming

#nullable enable
using System.Collections.Generic;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Coffee.UISoftMask
{
    internal struct MaterialLink
    {
        private ulong _hash;
        public ulong Hash => _hash;
        private Material? _material;

        public MaterialLink(ulong hash, Material? material)
        {
            _hash = hash;
            _material = material;
        }

        public Material? Get() => _material;

        public void Release()
        {
            if (_material is null) return;

            MaterialCache.Unregister(_hash);
            _material = null;
            _hash = 0;
        }
    }

    internal static class MaterialCache
    {
        private class Entry
        {
            public readonly Material material;
            public int referenceCount;
            public Entry(Material material) => this.material = material;
        }

        private static readonly Dictionary<ulong, Entry> s_MaterialMap = new();

        private static ShaderID s_SoftMaskTexId = new("_SoftMaskTex");
        private static ShaderID s_MaskInteractionId = new("_MaskInteraction");

        public static int ResolveShaderIndex(string shaderName)
        {
            return shaderName switch
            {
                "UI/Default" => 0,
                "MeowTower/UI/UI-PremultAlpha" => 1,
                _ => throw new Exception($"Shader {shaderName} not supported.")
            };
        }

        public static void Register(
            ref MaterialLink link, Material baseMat, MaskInteraction maskInteraction, RenderTexture maskRt)
        {
            // L.I($"[SoftMark.MaterialCache] Registering material: {baseMat.name}, maskInteraction={maskInteraction}, depth={depth}, stencil={stencil}, mask={mask}");

            var shaderIndex = ResolveShaderIndex(baseMat.shader.name);
            var propField = (uint) shaderIndex | (uint) maskInteraction << 8;
            var hash = ((ulong) propField << 32) | (uint) maskRt.GetInstanceID();
            if (hash == link.Hash)
                return;

            // Release the old material link.
            link.Release();

            if (s_MaterialMap.TryGetValue(hash, out var entry))
            {
                entry.referenceCount++;
                link = new MaterialLink(hash, entry.material);
                return;
            }

            L.I($"[SoftMark.MaterialCache] Creating material: {baseMat.name}, hash={hash}");

            var shader = Shader.Find(shaderIndex switch
            {
                0 => "Hidden/UI/Default (SoftMaskable)",
                1 => "Hidden/UI/PremultAlpha (SoftMaskable)",
                _ => throw new Exception($"Shader {baseMat.shader.name} not supported.")
            });

            var mat = new Material(shader);
            ConfigureMaterial(mat, maskInteraction, maskRt);
            mat.SetHideAndDontSave();

            entry = new Entry(mat) { referenceCount = 1 };
            link = new MaterialLink(hash, mat);

            s_MaterialMap.Add(hash, entry);
#if DEBUG
            if (s_MaterialMap.Count > 32)
                L.E("[SoftMark.MaterialCache] Material cache size exceeded 32. Consider optimizing material usage.");
#endif
        }

        internal static void Unregister(ulong hash)
        {
            // L.I($"[SoftMark.MaterialCache] Unregistering material, hash={hash}");

            if (!s_MaterialMap.TryGetValue(hash, out var entry))
            {
                L.E($"[SoftMark.MaterialCache] Unregister: Material not found, hash={hash}");
                return;
            }

            if (--entry.referenceCount is 0)
            {
                L.I($"[SoftMark.MaterialCache] Destroying material: {entry.material.name}, hash={hash}");
                Object.DestroyImmediate(entry.material);
                s_MaterialMap.Remove(hash);
            }
        }

        public static bool IsMaterialConfigured(Material mat) => mat.HasVector(s_MaskInteractionId.Val);

        public static void ConfigureMaterial(Material mat, MaskInteraction maskInteraction, RenderTexture maskRt)
        {
            var mi = (byte) maskInteraction;
            mat.SetTexture(s_SoftMaskTexId.Val, maskRt);
            mat.SetVector(s_MaskInteractionId.Val, new Vector4(mi & 0b11, 0, 0, 0));
            mat.SetHideAndDontSave();
        }
    }
}