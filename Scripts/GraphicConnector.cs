using UnityEngine;

namespace Coffee.UISoftMask
{
    internal static class GraphicConnectorExtension
    {
        public static T GetComponentInParentEx<T>(this Component component, bool includeInactive = false) where T : MonoBehaviour
        {
            if (!component) return null;
            var trans = component.transform;

            while (trans)
            {
                var c = trans.GetComponent<T>();
                if (c && (includeInactive || c.isActiveAndEnabled)) return c;

                trans = trans.parent;
            }

            return null;
        }
    }
}