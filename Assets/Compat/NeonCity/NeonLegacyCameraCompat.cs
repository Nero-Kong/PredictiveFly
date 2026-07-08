using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NeonLegacyCameraCompat : MonoBehaviour
{
    public Transform volumeTrigger;
    public LayerMask volumeLayer = 1;
    public bool stopNaNPropagation = true;
    public bool finalBlitToCameraTarget;
    public int antialiasingMode;
    public TemporalAntialiasingSettings temporalAntialiasing = new TemporalAntialiasingSettings();
    public SubpixelMorphologicalAntialiasingSettings subpixelMorphologicalAntialiasing =
        new SubpixelMorphologicalAntialiasingSettings();
    public FastApproximateAntialiasingSettings fastApproximateAntialiasing =
        new FastApproximateAntialiasingSettings();
    public FogSettings fog = new FogSettings();
    public DebugLayerSettings debugLayer = new DebugLayerSettings();
    public NeonLegacyRendererResourcesCompat m_Resources;
    public bool m_ShowToolkit;
    public bool m_ShowCustomSorter;
    public int breakBeforeColorGrading;
    public List<UnityEngine.Object> m_BeforeTransparentBundles = new List<UnityEngine.Object>();
    public List<UnityEngine.Object> m_BeforeStackBundles = new List<UnityEngine.Object>();
    public List<UnityEngine.Object> m_AfterStackBundles = new List<UnityEngine.Object>();
}

[Serializable]
public sealed class TemporalAntialiasingSettings
{
    public float jitterSpread = 0.75f;
    public float sharpness = 0.25f;
    public float stationaryBlending = 0.95f;
    public float motionBlending = 0.85f;
}

[Serializable]
public sealed class SubpixelMorphologicalAntialiasingSettings
{
    public int quality = 2;
}

[Serializable]
public sealed class FastApproximateAntialiasingSettings
{
    public bool fastMode;
    public bool keepAlpha;
}

[Serializable]
public sealed class FogSettings
{
    public bool enabled = true;
    public bool excludeSkybox = true;
}

[Serializable]
public sealed class DebugLayerSettings
{
    public LightMeterSettings lightMeter = new LightMeterSettings();
    public HistogramSettings histogram = new HistogramSettings();
    public WaveformSettings waveform = new WaveformSettings();
    public VectorscopeSettings vectorscope = new VectorscopeSettings();
    public OverlaySettings overlaySettings = new OverlaySettings();
}

[Serializable]
public sealed class LightMeterSettings
{
    public int width = 512;
    public int height = 256;
    public bool showCurves = true;
}

[Serializable]
public sealed class HistogramSettings
{
    public int width = 512;
    public int height = 256;
    public int channel = 3;
}

[Serializable]
public sealed class WaveformSettings
{
    public float exposure = 0.12f;
    public int height = 256;
}

[Serializable]
public sealed class VectorscopeSettings
{
    public int size = 256;
    public float exposure = 0.12f;
}

[Serializable]
public sealed class OverlaySettings
{
    public bool linearDepth;
    public int motionColorIntensity = 4;
    public int motionGridSize = 64;
    public int colorBlindnessType;
    public float colorBlindnessStrength = 1f;
}
