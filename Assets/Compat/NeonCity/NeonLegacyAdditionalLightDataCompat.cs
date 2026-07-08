using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class NeonLegacyAdditionalLightDataCompat : MonoBehaviour
{
    public bool m_UsePipelineSettings = true;
    public int m_AdditionalLightsShadowResolutionTier = 2;
    public bool m_CustomShadowLayers;
    public Vector2 m_LightCookieSize = Vector2.one;
    public Vector2 m_LightCookieOffset = Vector2.zero;
    public int m_SoftShadowQuality;
    public RenderingLayerMask m_RenderingLayersMask = 1u;
    public RenderingLayerMask m_ShadowRenderingLayersMask = 1u;
    public int m_Version = 4;
}
