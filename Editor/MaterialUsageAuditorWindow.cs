using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Raith.MaterialUsageAuditor
{
    internal sealed class MaterialUsageAuditorWindow : EditorWindow
    {
        private static readonly int[] ThresholdValues = { 1024, 2048, 4096, 8192 };
        private static readonly string[] ThresholdLabels = { "1K", "2K", "4K", "8K" };
        private static readonly string[] SortOptions = { "Name", "Editor memory", "Max size", "Usage count" };
        private static readonly string[] FilterOptions = { "All", "At / above limit", "No importer", "No asset path", "No textures" };

        private GameObject root;
        private MaterialAuditResult result;
        private Vector2 scrollPosition;
        private string search = string.Empty;
        private int thresholdIndex = 2;
        private int sortIndex;
        private int filterIndex;
        private readonly Dictionary<int, bool> expandedMaterials = new Dictionary<int, bool>();

        private GUIStyle titleStyle;
        private GUIStyle mutedStyle;
        private GUIStyle pathStyle;
        private GUIStyle textureLinkStyle;
        private GUIStyle largeSizeStyle;
        private GUIStyle materialSummaryStyle;
        private GUIStyle materialSummaryLargeStyle;
        private GUIStyle rowStyle;
        private GUIStyle textureRowStyle;
        private GUIStyle metricLabelStyle;
        private GUIStyle metricValueStyle;

        [MenuItem("Tools/Material Usage Auditor")]
        private static void Open()
        {
            var window = GetWindow<MaterialUsageAuditorWindow>();
            window.titleContent = new GUIContent("Material Audit", EditorGUIUtility.IconContent("Material Icon").image);
            window.minSize = new Vector2(900f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            thresholdIndex = Mathf.Clamp(EditorPrefs.GetInt("Raith.MaterialUsageAuditor.Threshold", 2), 0, ThresholdValues.Length - 1);
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            DrawSourceControls();

            if (result == null)
            {
                DrawEmptyState();
                return;
            }

            DrawSummary();
            DrawToolbar();
            DrawResults();
        }

        private void DrawHeader()
        {
            GUILayout.Space(10f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Material Usage Auditor", titleStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("READ-ONLY", EditorStyles.miniLabel, GUILayout.Width(66f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "See every material and texture import limit used beneath one model.",
                mutedStyle);
            GUILayout.Space(8f);
        }

        private void DrawSourceControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Root GameObject", EditorStyles.boldLabel, GUILayout.Width(112f));
            var newRoot = (GameObject)EditorGUILayout.ObjectField(root, typeof(GameObject), true);
            if (newRoot != root)
            {
                root = newRoot;
                result = null;
                expandedMaterials.Clear();
            }

            using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
            {
                if (GUILayout.Button("Use Selection", GUILayout.Width(100f)))
                {
                    root = Selection.activeGameObject;
                    Scan();
                }
            }

            using (new EditorGUI.DisabledScope(root == null))
            {
                if (GUILayout.Button("Scan Model", GUILayout.Width(96f)))
                    Scan();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Inactive child objects are always included. Scanning never changes project assets.", mutedStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(390f));
            var icon = EditorGUIUtility.IconContent("d_SkinnedMeshRenderer Icon");
            var iconRect = GUILayoutUtility.GetRect(46f, 46f, GUILayout.ExpandWidth(true));
            if (icon.image != null)
                GUI.DrawTexture(new Rect(iconRect.center.x - 23f, iconRect.y, 46f, 46f), icon.image, ScaleMode.ScaleToFit);
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Choose the model root, then scan its renderer hierarchy.", new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                alignment = TextAnchor.MiddleCenter
            });
            EditorGUILayout.LabelField("Materials with no assigned textures remain visible in the report.", new GUIStyle(mutedStyle)
            {
                alignment = TextAnchor.MiddleCenter
            });
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private void DrawSummary()
        {
            GUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            DrawSummaryItem(result.Materials.Count.ToString(), "materials");
            DrawSummaryDivider();
            DrawSummaryItem(result.UniqueTextureCount.ToString(), "textures");
            DrawSummaryDivider();
            var totalEditorMemory = result.UniqueTextureCount == 0
                ? "—"
                : FormatMemory(result.UniqueTextureMemoryBytes, result.UnknownTextureMemoryCount > 0);
            DrawSummaryItem(totalEditorMemory, "Editor memory");
            DrawSummaryDivider();
            DrawSummaryItem(result.RendererCount.ToString(), "renderers");
            DrawSummaryDivider();
            DrawSummaryItem(result.LargeTextureCount(CurrentThreshold).ToString(), $"at least {ThresholdLabels[thresholdIndex]}");
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Build target: {result.PlatformName}", mutedStyle, GUILayout.Width(150f));
            EditorGUILayout.EndHorizontal();

            if (result.EmptyMaterialSlotCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Found {result.EmptyMaterialSlotCount} empty material slot{(result.EmptyMaterialSlotCount == 1 ? string.Empty : "s")}.",
                    MessageType.Info);
            }

            if (result.UnknownTextureMemoryCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Editor memory is unavailable for {result.UnknownTextureMemoryCount} unique texture{(result.UnknownTextureMemoryCount == 1 ? string.Empty : "s")}; the total excludes those textures.",
                    MessageType.Warning);
            }
        }

        private void DrawSummaryItem(string value, string label)
        {
            EditorGUILayout.LabelField($"{value} {label}", EditorStyles.miniBoldLabel, GUILayout.Width(94f));
        }

        private static void DrawSummaryDivider()
        {
            EditorGUILayout.LabelField("/", EditorStyles.miniLabel, GUILayout.Width(10f));
        }

        private void DrawToolbar()
        {
            GUILayout.Space(7f);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(180f));
            EditorGUILayout.LabelField("Sort", EditorStyles.miniLabel, GUILayout.Width(28f));
            sortIndex = EditorGUILayout.Popup(sortIndex, SortOptions, EditorStyles.toolbarPopup, GUILayout.Width(112f));
            EditorGUILayout.LabelField("Show", EditorStyles.miniLabel, GUILayout.Width(32f));
            filterIndex = EditorGUILayout.Popup(filterIndex, FilterOptions, EditorStyles.toolbarPopup, GUILayout.Width(132f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Flag at", EditorStyles.miniLabel, GUILayout.Width(42f));
            var newThreshold = EditorGUILayout.Popup(thresholdIndex, ThresholdLabels, EditorStyles.toolbarPopup, GUILayout.Width(54f));
            if (newThreshold != thresholdIndex)
            {
                thresholdIndex = newThreshold;
                EditorPrefs.SetInt("Raith.MaterialUsageAuditor.Threshold", thresholdIndex);
            }
            if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                SetAllExpanded(true);
            if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                SetAllExpanded(false);
            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                ExportCsv();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawResults()
        {
            var visibleMaterials = GetVisibleMaterials();
            if (visibleMaterials.Count == 0)
            {
                var message = result.Materials.Count == 0
                    ? "No assigned materials were found beneath this root. Check that its child objects have Renderer components with materials assigned."
                    : "No materials match the current filter. Clear the search field to see the full audit.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var material in visibleMaterials)
                DrawMaterial(material);
            EditorGUILayout.EndScrollView();

            if (AssetPreview.IsLoadingAssetPreviews())
                Repaint();
        }

        private void DrawMaterial(MaterialAuditEntry entry)
        {
            var materialId = entry.Material.GetInstanceID();
            expandedMaterials.TryGetValue(materialId, out var expanded);

            GUILayout.Space(3f);
            EditorGUILayout.BeginVertical(rowStyle);
            EditorGUILayout.BeginHorizontal();
            var preview = AssetPreview.GetAssetPreview(entry.Material) ?? AssetPreview.GetMiniThumbnail(entry.Material);
            GUILayout.Label(preview, GUILayout.Width(46f), GUILayout.Height(46f));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            var newExpanded = EditorGUILayout.Foldout(
                expanded,
                new GUIContent(entry.Material.name, entry.Material.name),
                true,
                EditorStyles.foldoutHeader);
            if (newExpanded != expanded)
                expandedMaterials[materialId] = newExpanded;

            GUILayout.FlexibleSpace();
            var unknownMemoryCount = entry.Textures
                .Where(texture => texture.Texture != null)
                .GroupBy(texture => texture.Texture.GetInstanceID())
                .Count(group => group.First().MemoryBytes <= 0);
            var materialMemory = entry.Textures.Count == 0
                ? "—"
                : FormatMemory(entry.UniqueTextureMemoryBytes, unknownMemoryCount > 0);
            var materialSummary = $"{entry.Textures.Count} texture{(entry.Textures.Count == 1 ? string.Empty : "s")}  /  {materialMemory}  /  max {FormatMaxSizeOrDash(entry.LargestMaxSize)}";
            EditorGUILayout.LabelField(
                materialSummary,
                entry.LargestMaxSize >= CurrentThreshold ? materialSummaryLargeStyle : materialSummaryStyle,
                GUILayout.Width(245f));
            if (GUILayout.Button("Select Material", EditorStyles.miniButton, GUILayout.Width(120f)))
                Ping(entry.Material);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            var shaderName = entry.Material.shader != null ? entry.Material.shader.name : "Missing shader";
            EditorGUILayout.LabelField(new GUIContent(shaderName, shaderName), pathStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Used by {entry.Usages.Count} renderer slot{(entry.Usages.Count == 1 ? string.Empty : "s")}", mutedStyle, GUILayout.Width(128f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (newExpanded)
            {
                DrawSeparator();
                DrawMaterialDetails(entry);
                expandedMaterials[materialId] = true;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawMaterialDetails(MaterialAuditEntry entry)
        {
            GUILayout.Space(5f);

            EditorGUILayout.LabelField("Used by", EditorStyles.boldLabel);
            foreach (var usage in entry.Usages)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8f);
                var usageLabel = $"{usage.HierarchyPath}  /  material slot {usage.MaterialSlot}";
                EditorGUILayout.LabelField(new GUIContent(usageLabel, usageLabel), pathStyle);
                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52f)))
                    Selection.activeObject = usage.Renderer.gameObject;
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Assigned textures", EditorStyles.boldLabel);
            if (entry.Textures.Count == 0)
            {
                EditorGUILayout.LabelField("This material has no assigned texture properties.", mutedStyle);
            }
            else
            {
                foreach (var texture in entry.Textures)
                    DrawTextureRow(texture);
            }
            GUILayout.Space(3f);
        }

        private void DrawTextureRow(TextureAuditEntry entry)
        {
            GUILayout.Space(2f);
            var backgroundRect = EditorGUILayout.BeginVertical(textureRowStyle);
            if (entry.EffectiveMaxSize >= CurrentThreshold && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(backgroundRect, LargeRowColor);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(AssetPreview.GetMiniThumbnail(entry.Texture), GUILayout.Width(36f), GUILayout.Height(36f));
            EditorGUILayout.BeginVertical(GUILayout.Height(38f));
            var propertyTooltip = $"{entry.PropertyDescription}\n{entry.Texture.name} ({entry.PropertyName})";
            EditorGUILayout.LabelField(new GUIContent(entry.PropertyDescription, propertyTooltip), textureLinkStyle);
            var textureLabel = $"{entry.Texture.name}  ({entry.PropertyName})";
            EditorGUILayout.LabelField(new GUIContent(textureLabel, textureLabel), pathStyle);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select Texture", GUILayout.Width(112f), GUILayout.Height(24f)))
                Ping(entry.Texture);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(42f);
            DrawTextureMetric("Source", FormatDimensions(entry.SourceWidth, entry.SourceHeight));
            DrawTextureMetric("Imported", FormatDimensions(entry.ImportedWidth, entry.ImportedHeight));
            DrawTextureMetric("Editor memory", FormatMemory(entry.MemoryBytes));
            DrawTextureMetric("Default max", entry.HasImporter ? FormatMaxSize(entry.DefaultMaxSize) : "Not imported");
            var effectiveValue = entry.HasImporter
                ? FormatMaxSize(entry.EffectiveMaxSize) + (entry.HasPlatformOverride ? " override" : " default")
                : "Not imported";
            DrawTextureMetric($"{entry.PlatformName} max", effectiveValue, entry.EffectiveMaxSize >= CurrentThreshold);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTextureMetric(string label, string value, bool highlight = false)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(96f));
            EditorGUILayout.LabelField(label, metricLabelStyle);
            EditorGUILayout.LabelField(value, highlight ? largeSizeStyle : metricValueStyle);
            EditorGUILayout.EndVertical();
        }

        private bool MatchesSearch(MaterialAuditEntry entry)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            var value = search.Trim();
            return entry.Material.name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0
                   || (entry.Material.shader != null && entry.Material.shader.name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                   || entry.Usages.Any(usage => usage.HierarchyPath.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                   || entry.Textures.Any(texture =>
                       (texture.Texture != null && texture.Texture.name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                       || texture.PropertyDescription.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0
                       || texture.PropertyName.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private List<MaterialAuditEntry> GetVisibleMaterials()
        {
            var materials = result.Materials
                .Where(MatchesSearch)
                .Where(MatchesFilter)
                .ToList();

            switch (sortIndex)
            {
                case 1:
                    return materials.OrderByDescending(entry => entry.UniqueTextureMemoryBytes)
                        .ThenBy(entry => entry.Material.name, StringComparer.OrdinalIgnoreCase).ToList();
                case 2:
                    return materials.OrderByDescending(entry => entry.LargestMaxSize)
                        .ThenBy(entry => entry.Material.name, StringComparer.OrdinalIgnoreCase).ToList();
                case 3:
                    return materials.OrderByDescending(entry => entry.Usages.Count)
                        .ThenBy(entry => entry.Material.name, StringComparer.OrdinalIgnoreCase).ToList();
                default:
                    return materials.OrderBy(entry => entry.Material.name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private bool MatchesFilter(MaterialAuditEntry entry)
        {
            switch (filterIndex)
            {
                case 1:
                    return entry.Textures.Any(texture => texture.EffectiveMaxSize >= CurrentThreshold);
                case 2:
                    return entry.Textures.Any(texture => !texture.HasImporter);
                case 3:
                    return entry.Textures.Any(texture => string.IsNullOrEmpty(texture.AssetPath));
                case 4:
                    return entry.Textures.Count == 0;
                default:
                    return true;
            }
        }

        private void Scan()
        {
            if (root == null)
                return;

            try
            {
                result = MaterialAuditScanner.Scan(root);
                expandedMaterials.Clear();
                search = string.Empty;
                Repaint();
            }
            catch (Exception exception)
            {
                result = null;
                Debug.LogException(exception);
                ShowNotification(new GUIContent("The scan failed. See the Console for details."));
            }
        }

        private void SetAllExpanded(bool expanded)
        {
            if (result == null)
                return;

            foreach (var entry in result.Materials)
                expandedMaterials[entry.Material.GetInstanceID()] = expanded;
        }

        private void ExportCsv()
        {
            if (result == null)
                return;

            var suggestedName = SanitizeFileName(result.Root.name) + "-material-audit.csv";
            var path = EditorUtility.SaveFilePanel("Export material audit", string.Empty, suggestedName, "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var csv = BuildCsv(result);

            try
            {
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ShowNotification(new GUIContent("CSV report exported."));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Could not export the audit",
                    "Unity could not write to that location. Check that the folder is writable and the CSV is not open in another application, then choose Export CSV again.",
                    "OK");
            }
        }

        internal static string BuildCsv(MaterialAuditResult audit)
        {
            var csv = new StringBuilder();
            AppendCsvRow(csv, "Scan timestamp (UTC)", audit.ScannedAtUtc.ToString("O"));
            AppendCsvRow(csv, "Texture memory scope", $"Unity Editor runtime memory; bytes; build target context: {audit.PlatformName}; blank means unavailable");
            csv.AppendLine();
            csv.AppendLine("Material,Shader,Material Asset,Renderer,Slot,Texture Property,Texture,Texture Asset,Source Size,Imported Size,Editor Memory (bytes),Default Max Size,Effective Max Size,Platform Override");

            foreach (var material in audit.Materials)
            {
                var textures = material.Textures.Count > 0
                    ? material.Textures.Cast<TextureAuditEntry>().ToArray()
                    : new TextureAuditEntry[] { null };

                foreach (var usage in material.Usages)
                foreach (var texture in textures)
                {
                    AppendCsvRow(csv,
                        material.Material.name,
                        material.Material.shader != null ? material.Material.shader.name : "Missing shader",
                        material.AssetPath,
                        usage.HierarchyPath,
                        usage.MaterialSlot.ToString(),
                        texture?.PropertyName ?? string.Empty,
                        texture?.Texture != null ? texture.Texture.name : string.Empty,
                        texture?.AssetPath ?? string.Empty,
                        texture != null ? FormatDimensions(texture.SourceWidth, texture.SourceHeight) : string.Empty,
                        texture != null ? FormatDimensions(texture.ImportedWidth, texture.ImportedHeight) : string.Empty,
                        texture != null && texture.MemoryBytes > 0 ? texture.MemoryBytes.ToString() : string.Empty,
                        texture != null && texture.HasImporter ? texture.DefaultMaxSize.ToString() : string.Empty,
                        texture != null && texture.HasImporter ? texture.EffectiveMaxSize.ToString() : string.Empty,
                        texture != null && texture.HasPlatformOverride ? texture.PlatformName : string.Empty);
                }
            }

            csv.AppendLine();
            AppendCsvRow(csv, "Unique Texture Summary");
            csv.AppendLine("Texture,Texture Asset,Imported Size,Editor Memory (bytes),Memory Status");
            var uniqueTextures = audit.Materials
                .SelectMany(material => material.Textures)
                .Where(texture => texture.Texture != null)
                .GroupBy(texture => texture.Texture.GetInstanceID())
                .Select(group => group.First())
                .OrderBy(texture => texture.Texture.name, StringComparer.OrdinalIgnoreCase);

            foreach (var texture in uniqueTextures)
            {
                AppendCsvRow(csv,
                    texture.Texture.name,
                    texture.AssetPath,
                    FormatDimensions(texture.ImportedWidth, texture.ImportedHeight),
                    texture.MemoryBytes > 0 ? texture.MemoryBytes.ToString() : string.Empty,
                    texture.MemoryBytes > 0 ? "Available" : "Unavailable");
            }

            return csv.ToString();
        }

        private static void AppendCsvRow(StringBuilder builder, params string[] values)
        {
            builder.AppendLine(string.Join(",", values.Select(EscapeCsv).ToArray()));
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidCharacter, '-');
            return value;
        }

        private static string FormatDimensions(int width, int height)
        {
            return width > 0 && height > 0 ? $"{width}×{height}" : "—";
        }

        private static string FormatMemory(long bytes, bool partial = false)
        {
            if (bytes <= 0)
                return "Unavailable";

            var megabytes = bytes / (1024d * 1024d);
            var formatted = megabytes >= 1d
                ? $"{megabytes:0.##} MB"
                : $"{bytes / 1024d:0.#} KB";
            return partial ? $"≥ {formatted}" : formatted;
        }

        private static string FormatMaxSize(int size)
        {
            if (size >= 1024 && size % 1024 == 0)
                return $"{size / 1024}K";
            return size.ToString();
        }

        private static string FormatMaxSizeOrDash(int size)
        {
            return size > 0 ? FormatMaxSize(size) : "none";
        }

        private static void Ping(UnityEngine.Object target)
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private int CurrentThreshold => ThresholdValues[thresholdIndex];

        private static Color LargeRowColor => EditorGUIUtility.isProSkin
            ? new Color(0.48f, 0.31f, 0.08f, 0.18f)
            : new Color(1f, 0.68f, 0.18f, 0.16f);

        private static Color SeparatorColor => EditorGUIUtility.isProSkin
            ? new Color(0.22f, 0.22f, 0.22f)
            : new Color(0.66f, 0.66f, 0.66f);

        private static void DrawSeparator()
        {
            GUILayout.Space(4f);
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1f), SeparatorColor);
            GUILayout.Space(4f);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null
                && mutedStyle != null
                && pathStyle != null
                && textureLinkStyle != null
                && largeSizeStyle != null
                && materialSummaryStyle != null
                && materialSummaryLargeStyle != null
                && rowStyle != null
                && textureRowStyle != null
                && metricLabelStyle != null
                && metricValueStyle != null)
                return;

            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                fixedHeight = 24f
            };
            mutedStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.67f, 0.67f, 0.67f)
                        : new Color(0.34f, 0.34f, 0.34f)
                }
            };
            pathStyle = new GUIStyle(mutedStyle)
            {
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            textureLinkStyle = new GUIStyle(EditorStyles.label)
            {
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold,
                stretchWidth = true
            };
            largeSizeStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 0.68f, 0.24f)
                        : new Color(0.68f, 0.31f, 0.02f)
                }
            };
            materialSummaryStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleRight,
                fixedHeight = 20f
            };
            materialSummaryLargeStyle = new GUIStyle(materialSummaryStyle)
            {
                normal = { textColor = largeSizeStyle.normal.textColor }
            };
            rowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 6, 7),
                margin = new RectOffset(0, 0, 3, 3)
            };
            textureRowStyle = new GUIStyle()
            {
                padding = new RectOffset(8, 8, 7, 7),
                margin = new RectOffset(0, 0, 0, 0)
            };
            metricLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = mutedStyle.normal.textColor },
                fixedHeight = 15f
            };
            metricValueStyle = new GUIStyle(EditorStyles.label)
            {
                fixedHeight = 17f
            };
        }
    }
}
