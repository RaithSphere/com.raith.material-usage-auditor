using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Raith.MaterialUsageAuditor
{
    internal sealed class MaterialAuditResult
    {
        public GameObject Root { get; set; }
        public List<MaterialAuditEntry> Materials { get; } = new List<MaterialAuditEntry>();
        public int RendererCount { get; set; }
        public int EmptyMaterialSlotCount { get; set; }
        public string PlatformName { get; set; }

        public int UniqueTextureCount => Materials
            .SelectMany(material => material.Textures)
            .Where(texture => texture.Texture != null)
            .Select(texture => texture.Texture.GetInstanceID())
            .Distinct()
            .Count();

        public long UniqueTextureMemoryBytes => Materials
            .SelectMany(material => material.Textures)
            .Where(texture => texture.Texture != null)
            .GroupBy(texture => texture.Texture.GetInstanceID())
            .Sum(group => group.First().MemoryBytes);

        public int LargeTextureCount(int threshold)
        {
            return Materials
                .SelectMany(material => material.Textures)
                .Where(texture => texture.Texture != null && texture.EffectiveMaxSize >= threshold)
                .Select(texture => texture.Texture.GetInstanceID())
                .Distinct()
                .Count();
        }
    }

    internal sealed class MaterialAuditEntry
    {
        public Material Material { get; set; }
        public string AssetPath { get; set; }
        public List<MaterialUsage> Usages { get; } = new List<MaterialUsage>();
        public List<TextureAuditEntry> Textures { get; } = new List<TextureAuditEntry>();

        public int LargestMaxSize => Textures.Count == 0 ? 0 : Textures.Max(texture => texture.EffectiveMaxSize);
        public long UniqueTextureMemoryBytes => Textures
            .Where(texture => texture.Texture != null)
            .GroupBy(texture => texture.Texture.GetInstanceID())
            .Sum(group => group.First().MemoryBytes);
    }

    internal sealed class MaterialUsage
    {
        public Renderer Renderer { get; set; }
        public int MaterialSlot { get; set; }
        public string HierarchyPath { get; set; }
    }

    internal sealed class TextureAuditEntry
    {
        public string PropertyName { get; set; }
        public string PropertyDescription { get; set; }
        public Texture Texture { get; set; }
        public string AssetPath { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public int ImportedWidth { get; set; }
        public int ImportedHeight { get; set; }
        public int DefaultMaxSize { get; set; }
        public int EffectiveMaxSize { get; set; }
        public string PlatformName { get; set; }
        public bool HasPlatformOverride { get; set; }
        public bool HasImporter { get; set; }
        public long MemoryBytes { get; set; }
    }

    internal static class MaterialAuditScanner
    {
        public static MaterialAuditResult Scan(GameObject root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            var result = new MaterialAuditResult
            {
                Root = root,
                PlatformName = GetCurrentTexturePlatformName()
            };
            var materialLookup = new Dictionary<Material, MaterialAuditEntry>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            result.RendererCount = renderers.Length;

            foreach (var renderer in renderers)
            {
                var sharedMaterials = renderer.sharedMaterials;
                for (var slot = 0; slot < sharedMaterials.Length; slot++)
                {
                    var material = sharedMaterials[slot];
                    if (material == null)
                    {
                        result.EmptyMaterialSlotCount++;
                        continue;
                    }

                    if (!materialLookup.TryGetValue(material, out var entry))
                    {
                        entry = BuildMaterialEntry(material);
                        materialLookup.Add(material, entry);
                    }

                    entry.Usages.Add(new MaterialUsage
                    {
                        Renderer = renderer,
                        MaterialSlot = slot,
                        HierarchyPath = GetHierarchyPath(root.transform, renderer.transform)
                    });
                }
            }

            result.Materials.AddRange(materialLookup.Values
                .OrderBy(entry => entry.Material.name, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        private static MaterialAuditEntry BuildMaterialEntry(Material material)
        {
            var entry = new MaterialAuditEntry
            {
                Material = material,
                AssetPath = AssetDatabase.GetAssetPath(material)
            };

            if (material.shader == null)
                return entry;

            var propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (var index = 0; index < propertyCount; index++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, index) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;

                var propertyName = ShaderUtil.GetPropertyName(material.shader, index);
                if (!material.HasProperty(propertyName))
                    continue;

                var texture = material.GetTexture(propertyName);
                if (texture == null)
                    continue;

                entry.Textures.Add(BuildTextureEntry(
                    propertyName,
                    CleanPropertyDescription(
                        ShaderUtil.GetPropertyDescription(material.shader, index),
                        propertyName),
                    texture));
            }

            entry.Textures.Sort((left, right) => string.Compare(
                left.PropertyDescription,
                right.PropertyDescription,
                StringComparison.OrdinalIgnoreCase));
            return entry;
        }

        private static string CleanPropertyDescription(string description, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(description))
                return ObjectNames.NicifyVariableName(propertyName.TrimStart('_'));

            // Poiyomi and some other shader editors append their inspector directives
            // to the visible label (for example: "Decal--{reference_properties:...}").
            // Those directives are useful to the custom inspector, but not to this audit.
            var metadataIndex = description.IndexOf("--{", StringComparison.Ordinal);
            if (metadataIndex > 0)
                description = description.Substring(0, metadataIndex);

            description = description.Trim();
            return string.IsNullOrEmpty(description)
                ? ObjectNames.NicifyVariableName(propertyName.TrimStart('_'))
                : description;
        }

        private static TextureAuditEntry BuildTextureEntry(string propertyName, string description, Texture texture)
        {
            var assetPath = AssetDatabase.GetAssetPath(texture);
            var importer = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetImporter.GetAtPath(assetPath) as TextureImporter;

            var entry = new TextureAuditEntry
            {
                PropertyName = propertyName,
                PropertyDescription = string.IsNullOrEmpty(description) ? propertyName : description,
                Texture = texture,
                MemoryBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture),
                AssetPath = assetPath,
                ImportedWidth = texture.width,
                ImportedHeight = texture.height,
                SourceWidth = texture.width,
                SourceHeight = texture.height,
                    PlatformName = GetCurrentTexturePlatformName(),
                HasImporter = importer != null
            };

            if (importer == null)
                return entry;

            importer.GetSourceTextureWidthAndHeight(out var sourceWidth, out var sourceHeight);
            entry.SourceWidth = sourceWidth;
            entry.SourceHeight = sourceHeight;
            entry.DefaultMaxSize = importer.maxTextureSize;
            entry.EffectiveMaxSize = importer.maxTextureSize;

            var platformSettings = importer.GetPlatformTextureSettings(entry.PlatformName);
            if (platformSettings.overridden)
            {
                entry.HasPlatformOverride = true;
                entry.EffectiveMaxSize = platformSettings.maxTextureSize;
            }

            return entry;
        }

        private static string GetCurrentTexturePlatformName()
        {
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iPhone";
                case BuildTarget.WebGL:
                    return "WebGL";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return "Standalone";
                default:
                    return EditorUserBuildSettings.activeBuildTarget.ToString();
            }
        }

        private static string GetHierarchyPath(Transform root, Transform target)
        {
            var names = new Stack<string>();
            var current = target;
            while (current != null)
            {
                names.Push(current.name);
                if (current == root)
                    break;
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }
    }
}
