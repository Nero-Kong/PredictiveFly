using System.IO;
using UnityEditor;
using UnityEngine;

public static class HighCityMaterialRepair
{
    private const string MaterialsRoot = "Assets/HighCity/Materials";
    private const string TexturesRoot = "Assets/HighCity/Textures";
    private const string SessionKey = "PredictiveFly.HighCityMaterialRepair.AutoRan";

    [InitializeOnLoadMethod]
    private static void RepairOnceAfterCompile()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/HighCity"))
            return;

        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += () => RepairMaterialsInternal(false);
    }

    [MenuItem("Tools/High City/Repair URP Materials")]
    public static void RepairMaterialsMenu()
    {
        RepairMaterialsInternal(true);
    }

    private static void RepairMaterialsInternal(bool verbose)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsRoot))
        {
            if (verbose)
                Debug.LogWarning("HighCity repair skipped: material folder not found.");
            return;
        }

        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (litShader == null)
        {
            Debug.LogError("HighCity repair failed: 'Universal Render Pipeline/Lit' shader was not found.");
            return;
        }

        int textureImportsFixed = FixNormalTextureImports();
        int repairedMaterials = 0;

        var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot });

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in materialGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;

                if (ShouldSkip(material))
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

                RepairLitMaterial(material, litShader);
                repairedMaterials++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (verbose)
        {
            Debug.Log(
                $"HighCity repair complete. Repaired {repairedMaterials} materials and fixed {textureImportsFixed} normal-map imports.");
        }
    }

    private static bool ShouldSkip(Material material)
    {
        return material.name == "ClearSky";
    }

    private static bool IsParticleMaterial(Material material)
    {
        return material.name.StartsWith("UnityParticle");
    }

    private static int FixNormalTextureImports()
    {
        if (!AssetDatabase.IsValidFolder(TexturesRoot))
            return 0;

        int fixedCount = 0;
        var textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { TexturesRoot });
        foreach (var guid in textureGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName == null || fileName.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap)
                continue;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
            fixedCount++;
        }

        return fixedCount;
    }

    private static void RepairLitMaterial(Material material, Shader litShader)
    {
        var baseMap = GetTexture(material, "_BaseMap", "_MainTex");
        var bumpMap = GetTexture(material, "_BumpMap");
        var occlusionMap = GetTexture(material, "_OcclusionMap");
        var specGlossMap = GetTexture(material, "_SpecGlossMap");
        var emissionMap = GetTexture(material, "_EmissionMap");

        var baseMapScale = GetTextureScale(material, "_BaseMap", "_MainTex");
        var baseMapOffset = GetTextureOffset(material, "_BaseMap", "_MainTex");
        var bumpScale = GetFloat(material, 1f, "_BumpScale");
        var occlusionStrength = GetFloat(material, 1f, "_OcclusionStrength");
        var smoothness = GetFloat(material, 0.5f, "_Smoothness", "_Glossiness");
        var metallic = GetFloat(material, 0f, "_Metallic");
        var alphaClip = GetFloat(material, 0f, "_AlphaClip");
        var cutoff = GetFloat(material, 0.5f, "_Cutoff");
        var surface = GetFloat(material, 0f, "_Surface");

        var baseColor = GetColor(material, Color.white, "_BaseColor", "_Color");
        var specColor = GetColor(material, new Color(0.2f, 0.2f, 0.2f, 1f), "_SpecColor");
        var emissionColor = GetColor(material, Color.black, "_EmissionColor");

        material.shader = litShader;

        material.SetColor("_BaseColor", baseColor);
        material.SetTexture("_BaseMap", baseMap);
        material.SetTextureScale("_BaseMap", baseMapScale);
        material.SetTextureOffset("_BaseMap", baseMapOffset);

        material.SetFloat("_WorkflowMode", 0f);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_SpecColor", specColor);

        material.SetFloat("_BumpScale", bumpScale);
        material.SetTexture("_BumpMap", bumpMap);
        material.SetFloat("_OcclusionStrength", occlusionStrength);
        material.SetTexture("_OcclusionMap", occlusionMap);
        material.SetTexture("_SpecGlossMap", specGlossMap);

        material.SetFloat("_AlphaClip", alphaClip);
        material.SetFloat("_Cutoff", cutoff);
        material.SetFloat("_Surface", surface);

        material.SetColor("_EmissionColor", emissionColor);
        material.SetTexture("_EmissionMap", emissionMap);

        SetKeyword(material, "_SPECULAR_SETUP", true);
        SetKeyword(material, "_NORMALMAP", bumpMap != null);
        SetKeyword(material, "_OCCLUSIONMAP", occlusionMap != null);
        SetKeyword(material, "_SPECGLOSSMAP", specGlossMap != null);
        SetKeyword(material, "_METALLICSPECGLOSSMAP", specGlossMap != null);
        SetKeyword(material, "_EMISSION", emissionMap != null || emissionColor.maxColorComponent > 0.001f);

        EditorUtility.SetDirty(material);
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
