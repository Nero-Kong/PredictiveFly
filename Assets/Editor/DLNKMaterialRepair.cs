using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class DLNKMaterialRepair
{
    private static readonly string[] Roots =
    {
        "Assets/_DLNK/Alien Terrain Pack",
        "Assets/_DLNK/Ancient Caverns",
        "Assets/_DLNK/Essential Terrain Pack",
        "Assets/_DLNK/Nature Pack - Mediterranean",
        "Assets/_DLNK/Neon City",
        "Assets/_DLNK/Space Base Pack",
        "Assets/_DLNK/Space Colony",
        "Assets/_DLNK/SS Heavy",
        "Assets/_DLNK/Winter Lands"
    };

    private static readonly HashSet<string> KnownLegacyShaderNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "DLNK Shaders/ASE/TopBottomSimple",
        "DLNK Shaders/ASE/TopBottomParallax",
        "DLNK Shaders/ASE/Nature/LeavesAnim",
        "DLNK Shaders/ASE/Nature/GrassAnim",
        "DLNK Shaders/ASE/Nature/WaterSimple",
        "DLNK Shaders/ASE/Nature/WaterRefractDepth",
        "DLNK Shaders/ASE/Nature/TreeAnim Leaves",
        "DLNK Shaders/ASE/Nature/TreeAnimTrunk",
        "DLNK Shaders/ASE/RefractionSimple"
    };

    private static readonly HashSet<string> KnownMissingShaderGuids = new HashSet<string>(StringComparer.Ordinal)
    {
        "933532a4fcc9baf4fa0491de14d08ed7",
        "0406db5a14f94604a8c57ccfbc9f3b46",
        "b2d82ac2afca0da42a8badfbe29100c3"
    };

    private const string SessionKey = "PredictiveFly.DLNKMaterialRepair.AutoRan";

    [InitializeOnLoadMethod]
    private static void RepairOnceAfterCompile()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        bool hasAnyRoot = false;
        foreach (var root in Roots)
        {
            if (AssetDatabase.IsValidFolder(root))
            {
                hasAnyRoot = true;
                break;
            }
        }

        if (!hasAnyRoot)
            return;

        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += () => RepairMaterialsInternal(false);
    }

    [MenuItem("Tools/Imported Packages/Repair DLNK Materials For URP")]
    public static void RepairMaterialsMenu()
    {
        RepairMaterialsInternal(true);
    }

    private static void RepairMaterialsInternal(bool verbose)
    {
        var validRoots = new List<string>();
        foreach (var root in Roots)
        {
            if (AssetDatabase.IsValidFolder(root))
                validRoots.Add(root);
        }

        if (validRoots.Count == 0)
        {
            if (verbose)
                Debug.LogWarning("DLNK material repair skipped: package roots not found.");
            return;
        }

        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (litShader == null)
        {
            Debug.LogError("DLNK material repair failed: 'Universal Render Pipeline/Lit' shader was not found.");
            return;
        }

        int repaired = 0;
        int normalMapsFixed = 0;

        var materialGuids = AssetDatabase.FindAssets("t:Material", validRoots.ToArray());
        foreach (var guid in materialGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                continue;

            var snapshot = MaterialSnapshot.Capture(material, path);
            if (ShouldSkip(snapshot))
                continue;

            if (!NeedsRepair(snapshot))
                continue;

            if (IsParticle(snapshot))
            {
                if (particleShader != null)
                {
                    RepairParticleMaterial(material, snapshot, particleShader);
                    repaired++;
                }

                continue;
            }

            normalMapsFixed += RepairLitMaterial(material, snapshot, litShader);
            repaired++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (verbose)
        {
            Debug.Log(
                $"DLNK material repair complete. Repaired {repaired} materials and fixed {normalMapsFixed} normal-map imports.");
        }
    }

    private static bool NeedsRepair(MaterialSnapshot snapshot)
    {
        if (snapshot.ShaderName.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
            return false;

        if (snapshot.ShaderGuid == "0000000000000000f000000000000000")
            return true;

        if (KnownMissingShaderGuids.Contains(snapshot.ShaderGuid))
            return true;

        if (KnownLegacyShaderNames.Contains(snapshot.ShaderName))
            return true;

        return snapshot.Shader == null && snapshot.HasSavedProperties;
    }

    private static bool ShouldSkip(MaterialSnapshot snapshot)
    {
        if (snapshot.Path.IndexOf("Neon High City/Source/Fog", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (snapshot.MaterialName.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return snapshot.ShaderFileId == "103";
    }

    private static bool IsParticle(MaterialSnapshot snapshot)
    {
        return snapshot.MaterialName.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               snapshot.ShaderName.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int RepairLitMaterial(Material material, MaterialSnapshot snapshot, Shader litShader)
    {
        var baseMap = snapshot.GetTexture("_BaseMap", "_MainTex", "_AlbedoTop");
        var bumpMap = snapshot.GetTexture("_BumpMap", "_NormalA", "_DetailNormalMap");
        var occlusionMap = snapshot.GetTexture("_OcclusionMap");
        var metallicGlossMap = snapshot.GetTexture("_MetallicGlossMap", "_MetalnessTop");
        var specGlossMap = snapshot.GetTexture("_SpecGlossMap");
        var emissionMap = snapshot.GetTexture("_EmissionMap");

        var baseMapScale = snapshot.GetTextureScale("_BaseMap", "_MainTex", "_AlbedoTop");
        var baseMapOffset = snapshot.GetTextureOffset("_BaseMap", "_MainTex", "_AlbedoTop");
        var bumpScale = snapshot.GetFloat(1f, "_BumpScale", "_NormalScale", "_BumpScaleTop");
        var occlusionStrength = snapshot.GetFloat(1f, "_OcclusionStrength", "_OcclusionTop");
        var smoothness = snapshot.GetFloat(0.5f, "_Smoothness", "_Glossiness", "_GlossinessTop");
        var metallic = snapshot.GetFloat(0f, "_Metallic", "_MetallicTop");
        var cutoff = snapshot.GetFloat(0.5f, "_Cutoff");

        var baseColor = snapshot.GetColor(Color.white, "_BaseColor", "_Color", "_ColorA", "_TintColor");
        var specColor = snapshot.GetColor(new Color(0.2f, 0.2f, 0.2f, 1f), "_SpecColor");
        var emissionColor = snapshot.GetColor(Color.black, "_EmissionColor", "_ColorB");

        int normalMapsFixed = 0;
        normalMapsFixed += EnsureNormalMapImport(bumpMap);

        bool transparent = snapshot.IsTransparent ||
                           snapshot.MaterialName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           snapshot.MaterialName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           snapshot.MaterialName.IndexOf("cristal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           snapshot.MaterialName.IndexOf("fog", StringComparison.OrdinalIgnoreCase) >= 0;
        bool alphaClip = snapshot.IsAlphaClip ||
                         snapshot.MaterialName.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         snapshot.MaterialName.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         snapshot.MaterialName.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0;
        bool premultiply = snapshot.IsPremultiply;
        float workflowMode = specGlossMap != null ? 0f : 1f;

        material.shader = litShader;

        material.SetColor("_BaseColor", baseColor);
        material.SetTexture("_BaseMap", baseMap);
        material.SetTextureScale("_BaseMap", baseMapScale);
        material.SetTextureOffset("_BaseMap", baseMapOffset);

        material.SetFloat("_WorkflowMode", workflowMode);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_SpecColor", specColor);
        material.SetTexture("_MetallicGlossMap", metallicGlossMap);
        material.SetTexture("_SpecGlossMap", specGlossMap);

        material.SetFloat("_BumpScale", bumpScale);
        material.SetTexture("_BumpMap", bumpMap);
        material.SetTexture("_OcclusionMap", occlusionMap);
        material.SetFloat("_OcclusionStrength", occlusionStrength);

        material.SetColor("_EmissionColor", emissionColor);
        material.SetTexture("_EmissionMap", emissionMap);

        material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        material.SetFloat("_Cutoff", cutoff);

        ApplySurfaceSettings(material, transparent, premultiply, alphaClip);

        SetKeyword(material, "_SPECULAR_SETUP", workflowMode == 0f);
        SetKeyword(material, "_NORMALMAP", bumpMap != null);
        SetKeyword(material, "_OCCLUSIONMAP", occlusionMap != null);
        SetKeyword(material, "_EMISSION", emissionMap != null || emissionColor.maxColorComponent > 0.001f);
        SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicGlossMap != null || specGlossMap != null);
        SetKeyword(material, "_ALPHATEST_ON", alphaClip);
        SetKeyword(material, "_ALPHAPREMULTIPLY_ON", transparent && premultiply);
        SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);

        EditorUtility.SetDirty(material);
        return normalMapsFixed;
    }

    private static void RepairParticleMaterial(Material material, MaterialSnapshot snapshot, Shader particleShader)
    {
        var mainTex = snapshot.GetTexture("_BaseMap", "_MainTex");
        var color = snapshot.GetColor(Color.white, "_BaseColor", "_Color", "_TintColor");

        material.shader = particleShader;
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", mainTex);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        EditorUtility.SetDirty(material);
    }

    private static void ApplySurfaceSettings(Material material, bool transparent, bool premultiply, bool alphaClip)
    {
        material.SetOverrideTag("RenderType", transparent ? "Transparent" : alphaClip ? "TransparentCutout" : "Opaque");
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_Blend", premultiply ? 1f : 0f);
        material.SetFloat("_Cull", 2f);
        material.SetFloat("_ZWrite", transparent ? 0f : 1f);

        if (transparent)
        {
            material.SetInt("_SrcBlend", premultiply ? (int)BlendMode.One : (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : -1;
        }
    }

    private static int EnsureNormalMapImport(Texture texture)
    {
        if (texture == null)
            return 0;

        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path))
            return 0;

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            return 0;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
        return 1;
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    private sealed class MaterialSnapshot
    {
        private readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector2> _scales = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector2> _offsets = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>(StringComparer.Ordinal);

        public string MaterialName { get; private set; }
        public string Path { get; private set; }
        public Shader Shader { get; private set; }
        public string ShaderName { get; private set; }
        public string ShaderGuid { get; private set; }
        public string ShaderFileId { get; private set; }
        public bool IsTransparent { get; private set; }
        public bool IsAlphaClip { get; private set; }
        public bool IsPremultiply { get; private set; }
        public bool HasSavedProperties => _textures.Count > 0 || _colors.Count > 0 || _floats.Count > 0;

        public static MaterialSnapshot Capture(Material material, string path)
        {
            var snapshot = new MaterialSnapshot
            {
                MaterialName = material.name,
                Path = path,
                Shader = material.shader,
                ShaderName = material.shader != null ? material.shader.name : string.Empty
            };

            string text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var shaderMatch = System.Text.RegularExpressions.Regex.Match(
                text,
                "m_Shader:\\s*\\{fileID:\\s*([0-9-]+), guid:\\s*([0-9a-f]{32}), type:\\s*([0-9]+)\\}");
            if (shaderMatch.Success)
            {
                snapshot.ShaderFileId = shaderMatch.Groups[1].Value;
                snapshot.ShaderGuid = shaderMatch.Groups[2].Value;
            }
            else
            {
                snapshot.ShaderFileId = string.Empty;
                snapshot.ShaderGuid = string.Empty;
            }

            snapshot.IsTransparent = text.IndexOf("RenderType: Transparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     text.IndexOf("_SURFACE_TYPE_TRANSPARENT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     text.IndexOf("_ALPHABLEND_ON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     text.IndexOf("_ALPHAPREMULTIPLY_ON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     text.IndexOf("TreeTransparent", StringComparison.OrdinalIgnoreCase) >= 0;
            snapshot.IsAlphaClip = text.IndexOf("TransparentCutout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   text.IndexOf("TreeTransparentCutout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   text.IndexOf("_ALPHATEST_ON", StringComparison.OrdinalIgnoreCase) >= 0;
            snapshot.IsPremultiply = text.IndexOf("_ALPHAPREMULTIPLY_ON", StringComparison.OrdinalIgnoreCase) >= 0;

            var serialized = new SerializedObject(material);
            snapshot.ReadTextureValues(serialized.FindProperty("m_SavedProperties.m_TexEnvs"));
            snapshot.ReadColorValues(serialized.FindProperty("m_SavedProperties.m_Colors"));
            snapshot.ReadFloatValues(serialized.FindProperty("m_SavedProperties.m_Floats"));
            snapshot.ReadFloatValues(serialized.FindProperty("m_SavedProperties.m_Ints"));

            return snapshot;
        }

        public Texture GetTexture(params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (_textures.TryGetValue(property, out var texture) && texture != null)
                    return texture;
            }

            return null;
        }

        public float GetFloat(float fallback, params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (_floats.TryGetValue(property, out var value))
                    return value;
            }

            return fallback;
        }

        public Color GetColor(Color fallback, params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (_colors.TryGetValue(property, out var value))
                    return value;
            }

            return fallback;
        }

        public Vector2 GetTextureScale(params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (_scales.TryGetValue(property, out var value))
                    return value;
            }

            return Vector2.one;
        }

        public Vector2 GetTextureOffset(params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (_offsets.TryGetValue(property, out var value))
                    return value;
            }

            return Vector2.zero;
        }

        private void ReadTextureValues(SerializedProperty array)
        {
            if (array == null || !array.isArray)
                return;

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("first");
                var valueProp = element.FindPropertyRelative("second");
                if (nameProp == null || valueProp == null)
                    continue;

                string name = nameProp.stringValue;
                var textureProp = valueProp.FindPropertyRelative("m_Texture");
                var scaleProp = valueProp.FindPropertyRelative("m_Scale");
                var offsetProp = valueProp.FindPropertyRelative("m_Offset");

                if (textureProp != null)
                    _textures[name] = textureProp.objectReferenceValue as Texture;
                if (scaleProp != null)
                    _scales[name] = scaleProp.vector2Value;
                if (offsetProp != null)
                    _offsets[name] = offsetProp.vector2Value;
            }
        }

        private void ReadColorValues(SerializedProperty array)
        {
            if (array == null || !array.isArray)
                return;

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("first");
                var valueProp = element.FindPropertyRelative("second");
                if (nameProp != null && valueProp != null)
                    _colors[nameProp.stringValue] = valueProp.colorValue;
            }
        }

        private void ReadFloatValues(SerializedProperty array)
        {
            if (array == null || !array.isArray)
                return;

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("first");
                var valueProp = element.FindPropertyRelative("second");
                if (nameProp != null && valueProp != null)
                    _floats[nameProp.stringValue] = valueProp.floatValue;
            }
        }
    }
}
