#if UNITY_EDITOR
#nullable enable

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;

namespace Coffee.UISoftMask
{
    internal static class SoftMaskSceneViewHandler
    {
        private static readonly List<SoftMaskable> _maskables = new();
        private static readonly int _gameVPId = Shader.PropertyToID("_GameVP");
        private static readonly int _gameTVPId = Shader.PropertyToID("_GameTVP");

        public static void Add(SoftMaskable maskable)
        {
            // Subscribe to the event when the first maskable is added.
            if (_maskables.IsEmpty())
                SceneView.beforeSceneGui += UpdateMaskables;

            _maskables.Add(maskable);
        }

        public static void Remove(SoftMaskable maskable)
        {
            _maskables.RemoveLast_RefEq(maskable);

            // Unsubscribe from the event when no maskables are left.
            if (_maskables.IsEmpty())
                SceneView.beforeSceneGui -= UpdateMaskables;
        }

        public static void SetUpMaterialProperties(Material mat, Camera cam)
        {
            Assert.IsTrue(cam, "Camera is null.");
            Assert.IsTrue(mat.shader.name.EndsWithOrdinal("(SoftMaskable)"), "Material shader is not SoftMaskable.");

            mat.EnableKeyword("SOFTMASK_EDITOR");

            // Set view and projection matrices.
            Profiler.BeginSample("Set view and projection matrices");
            var p = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
            var pv = p * cam.worldToCameraMatrix;
            mat.SetMatrix(_gameVPId, pv);
            mat.SetMatrix(_gameTVPId, pv);
            Profiler.EndSample();
        }

        private static void UpdateMaskables(SceneView sceneView)
        {
            Assert.IsTrue(_maskables.NotEmpty(), "There are no SoftMaskable components to update in the scene view.");

            foreach (var maskable in _maskables)
            {
                var mat = maskable.modifiedMaterial; // exists most cases.
                var cam = maskable.graphic.canvas.worldCamera;
                if (mat) SetUpMaterialProperties(mat!, cam);
            }
        }
    }
}
#endif