using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class NeonCityMaterialRepair
{
    private static readonly string[] MaterialRoots =
    {
        "Assets/_DLNK/Neon City/Source/Materials",
        "Assets/_DLNK/Neon City/Scene/Neon High City/Source"
    };

    private const string SessionKey = "PredictiveFly.NeonCityMaterialRepair.AutoRan";

    [InitializeOnLoadMethod]
    private static void RepairOnceAfterCompile()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/_DLNK/Neon City"))
            return;

        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += () => RepairMaterialsInternal(false);
    }

    [MenuItem("Tools/Neon City/Repair URP Materials")]
    public static void RepairMaterialsMenu()
    {
        RepairMaterialsInternal(true);
    }

    private static void RepairMaterialsInternal(bool verbose)
    {
        var validRoots = new List<string>();
        foreach (var root in MaterialRoots)
        {
            if (AssetDatabase.IsValidFolder(root))
                validRoots.Add(root);
        }

        if (validRoots.Count == 0)
        {
            if (verbose)
                Debug.LogWarning("Neon City repair skipped: material folders not found.");
            return;
        }

        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (litShader == null)
        {
            Debug.LogError("Neon City repair failed: 'Universal Render Pipeline/Lit' shader was not found.");
            return;
        }

        int repairedMaterials = 0;
        int fixedNormalMaps = 0;

        var materialGuids = AssetDatabase.FindAssets("t:Material", validRoots.ToArray());

        foreach (var guid in materialGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || ShouldSkip(material))
                continue;

            if (IsParticleMaterial(material))
            {
                if (particleShader != null)
                {
                    RepairParticleMaterial(material, particleShader);
                    repairedMaterials++;
                }

                continue;
            }

            fixedNormalMaps += RepairLitMaterial(material, litShader);
            repairedMaterials++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (verbose)
        {
            Debug.Log(
                $"Neon City repair complete. Repaired {repairedMaterials} materials and fixed {fixedNormalMaps} normal-map imports.");
        }
    }

    private static bool ShouldSkip(Material material)
    {
        if (material == null)
            return true;

        if (material.shader != null && material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
            return true;

        return material.name.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsParticleMaterial(Material material)
    {
        return material.name.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int RepairLitMaterial(Material material, Shader litShader)
    {
        var baseMap = GetTexture(material, "_BaseMap", "_MainTex");
        var bumpMap = GetTexture(material, "_BumpMap");
        var detailNormalMap = GetTexture(material, "_DetailNormalMap");
        var occlusionMap = GetTexture(material, "_OcclusionMap");
        var metallicGlossMap = GetTexture(material, "_MetallicGlossMap");
        var specGlossMap = GetTexture(material, "_SpecGlossMap");
        var emissionMap = GetTexture(material, "_EmissionMap");

        var baseMapScale = GetTextureScale(material, "_BaseMap", "_MainTex");
        var baseMapOffset = GetTextureOffset(material, "_BaseMap", "_MainTex");
        var bumpScale = GetFloat(material, 1f, "_BumpScale");
        var occlusionStrength = GetFloat(material, 1f, "_OcclusionStrength");
        var smoothness = GetFloat(material, 0.5f, "_Smoothness", "_Glossiness");
        var metallic = GetFloat(material, 0f, "_Metallic");
        var cutoff = GetFloat(material, 0.5f, "_Cutoff");

        var baseColor = GetColor(material, Color.white, "_BaseColor", "_Color");
        var specColor = GetColor(material, new Color(0.2f, 0.2f, 0.2f, 1f), "_SpecColor");
        var emissionColor = GetColor(material, Color.black, "_EmissionColor");

        int normalMapsFixed = 0;
        normalMapsFixed += EnsureNormalMapImport(bumpMap);
        normalMapsFixed += EnsureNormalMapImport(detailNormalMap);

        bool transparent = IsTransparent(material);
        bool alphaClip = IsAlphaClip(material);
        bool premultiply = material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
        float workflowMode = specGlossMap != null ? 0f : 1f;

        material.shader = litShader;

        material.SetColor("_BaseColor", baseColor);
        material.SetTexture("_BaseMap", baseMap);
        material.SetTextureScale("_BaseMap", baseMapScale);
        material.SetTextureOffset("_BaseMap", baseMapOffset);

        material.SetFloat("_WorkflowMode", workflowMode);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
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

    private static void RepairParticleMaterial(Material material, Shader particleShader)
    {
        var mainTex = GetTexture(material, "_BaseMap", "_MainTex");
        var color = GetColor(material, Color.white, "_BaseColor", "_Color", "_TintColor");

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
        material.SetFloat("_Cull", GetFloat(material, 2f, "_Cull"));
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

    private static bool IsTransparent(Material material)
    {
        if (material.renderQueue >= (int)RenderQueue.Transparent)
            return true;

        if (material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
            return true;

        string renderType = material.GetTag("RenderType", false);
        if (string.Equals(renderType, "Transparent", StringComparison.OrdinalIgnoreCase))
            return true;

        float mode = GetFloat(material, -1f, "_Mode");
        return Mathf.Approximately(mode, 2f) || Mathf.Approximately(mode, 3f);
    }

    private static bool IsAlphaClip(Material material)
    {
        if (material.IsKeywordEnabled("_ALPHATEST_ON"))
            return true;

        string renderType = material.GetTag("RenderType", false);
        if (string.Equals(renderType, "TransparentCutout", StringComparison.OrdinalIgnoreCase))
            return true;

        float mode = GetFloat(material, -1f, "_Mode");
        return Mathf.Approximately(mode, 1f);
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

    private static Texture GetTexture(Material material, params string[] propertyNames)
    {
        foreach (var property in propertyNames)
        {
            if (material.HasProperty(property))
            {
                var texture = material.GetTexture(property);
                if (texture != null)
                    return texture;
            }
        }

        return null;
    }

    private static float GetFloat(Material material, float fallback, params string[] propertyNames)
    {
        foreach (var property in propertyNames)
        {
            if (material.HasProperty(property))
                return material.GetFloat(property);
        }

        return fallback;
    }

    private static Color GetColor(Material material, Color fallback, params string[] propertyNames)
    {
        foreach (var property in propertyNames)
        {
            if (material.HasProperty(property))
                return material.GetColor(property);
        }

        return fallback;
    }

    private static Vector2 GetTextureScale(Material material, params string[] propertyNames)
    {
        foreach (var property in propertyNames)
        {
            if (material.HasProperty(property))
                return material.GetTextureScale(property);
        }

        return Vector2.one;
    }

    private static Vector2 GetTextureOffset(Material material, params string[] propertyNames)
    {
        foreach (var property in propertyNames)
        {
            if (material.HasProperty(property))
                return material.GetTextureOffset(property);
        }

        return Vector2.zero;
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }
}
