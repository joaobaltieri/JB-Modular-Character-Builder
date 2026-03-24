using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JB.ModularCharacterBuilder
{
    public class ModularCharacterPrefabBuilderWindow : EditorWindow
    {
        private const string TempPreviewName = "__JBModularCharacterBuilderPreview__";
        private const string EmbeddedBannerFileName = "jb-modular-prefab-builder-header";
        private const float CompactButtonWidth = 20f;
        private const float DiceButtonWidth = 24f;
        private const float LockButtonWidth = 24f;
        private const float CompactActionButtonWidth = 84f;
        private const float CompactToolbarButtonWidth = 96f;
        private const float CompactMiniButtonWidth = 74f;
        private const float CompactNoneButtonWidth = 64f;
        private const float CompactRandomButtonWidth = 92f;
        private const float CompactBrowseButtonWidth = 76f;
        private const float MinBannerHeight = 40f;
        private const string GeneratedMaterialsFolderName = "JB Materials";
        private const string GeneratedPrefabsFolderName = "JB Prefabs";
        private const string GeneratedPresetsFolderName = "JB Presets";

        private static readonly string[] CoreCategoryOrder =
        {
            "Body",
            "HeadTop",
            "Hair",
            "Face",
            "Torso",
            "TorsoOuter",
            "TorsoGear",
            "Arms",
            "ArmsOuter",
            "ArmsGear",
            "Legs",
            "LegsOuter",
            "LegsGear",
            "Feet",
        };

        private static readonly HashSet<string> BuiltInMultiSelectCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "Face",
            "TorsoGear",
            "ArmsGear",
            "LegsGear",
        };

        private static readonly string[] BaseColorPropertyCandidates = { "_BaseColor", "_Color" };
        private static readonly string[] EmissionColorPropertyCandidates =
        {
            "_EmissiveColor",   // HDRP
            "_EmissionColor"    // URP / Standard-like
        };
        private static readonly string[] SurfaceTypePropertyCandidates = { "_Surface", "_SurfaceType" };
        private static readonly string[] MetallicPropertyCandidates = { "_Metallic", "_Metalness", "_MetallicScale", "_MetallicRemapMax" };
        private static readonly string[] SmoothnessPropertyCandidates = { "_Smoothness", "_Glossiness" };
        private static readonly string[] BaseTexturePropertyCandidates = { "_BaseMap", "_BaseColorMap", "_MainTex" };

        [Serializable]
        private class PieceData
        {
            public string pieceKey;
            public string sourceName;
            public string categoryName;
            public string categoryDisplayName;
            public string objectName;
            public string variantDisplayName;
            public string displayName;
            public string colorGroupDisplayName;
            public GameObject go;
            public List<Renderer> renderers = new();
            public bool isBodyPiece;
        }

        [Serializable]
        private class CategoryData
        {
            public string name;
            public string displayName;
            public bool allowMultiple;
            public bool disallowNone;
            public bool foldout = true;
            public bool isCoreCategory;
            public bool isLocked;
            public List<PieceData> pieces = new();
            public int selectedIndex = -1;
            public List<int> selectedIndices = new();
        }

        [Serializable]
        private class SlotRuntimeState
        {
            public string slotName;
            public string rendererPath;
            public int slotIndex = -1;
            public ModularCharacterBuildPreset.MaterialFamily family;
            public ModularCharacterBuildPreset.MaterialFamily baseFamily;
            public Material familyBaseMaterial;
            public bool useCustom;
            public Material existingMaterial;
            public Color baseColor = Color.white;
            public Color emissionColor = Color.black;
            public bool isLocked;
        }

        [Serializable]
        private class SlotBinding
        {
            public string slotKey;
            public string slotName;
            public string rendererPath;
            public Renderer targetRenderer;
            public int slotIndex;
            public ModularCharacterBuildPreset.MaterialFamily family;
            public ModularCharacterBuildPreset.MaterialFamily baseFamily;
            public Material familyBaseMaterial;
            public bool useCustom;
            public Material existingMaterial;
            public Color baseColor = Color.white;
            public Color emissionColor = Color.black;
            public bool isLocked;
        }

        [Serializable]
        private class PieceMaterialGroup
        {
            public string pieceKey;
            public string pieceObjectName;
            public string displayName;
            public bool isBodyGroup;
            public bool foldout = true;
            public List<SlotBinding> slots = new();
        }

        [Serializable]
        private class CopiedSlotState
        {
            public Color baseColor;
            public Color emissionColor;
        }

        private readonly struct DerivedMaterialKey : IEquatable<DerivedMaterialKey>
        {
            public readonly ModularCharacterBuildPreset.MaterialFamily Family;
            public readonly Material BaseMaterial;
            public readonly int BaseR;
            public readonly int BaseG;
            public readonly int BaseB;
            public readonly int BaseA;
            public readonly int EmissionR;
            public readonly int EmissionG;
            public readonly int EmissionB;
            public readonly int EmissionA;

            public DerivedMaterialKey(ModularCharacterBuildPreset.MaterialFamily family, Material baseMaterial, Color baseColor, Color emissionColor)
            {
                Family = family;
                BaseMaterial = baseMaterial;
                BaseR = QuantizeColorChannel(baseColor.r);
                BaseG = QuantizeColorChannel(baseColor.g);
                BaseB = QuantizeColorChannel(baseColor.b);
                BaseA = QuantizeColorChannel(baseColor.a);
                EmissionR = QuantizeColorChannel(emissionColor.r);
                EmissionG = QuantizeColorChannel(emissionColor.g);
                EmissionB = QuantizeColorChannel(emissionColor.b);
                EmissionA = QuantizeColorChannel(emissionColor.a);
            }

            public bool Equals(DerivedMaterialKey other)
            {
                return Family == other.Family &&
                       BaseMaterial == other.BaseMaterial &&
                       BaseR == other.BaseR && BaseG == other.BaseG && BaseB == other.BaseB && BaseA == other.BaseA &&
                       EmissionR == other.EmissionR && EmissionG == other.EmissionG && EmissionB == other.EmissionB && EmissionA == other.EmissionA;
            }

            public override bool Equals(object obj) => obj is DerivedMaterialKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)Family;
                    hash = (hash * 397) ^ (BaseMaterial != null ? BaseMaterial.GetHashCode() : 0);
                    hash = (hash * 397) ^ BaseR;
                    hash = (hash * 397) ^ BaseG;
                    hash = (hash * 397) ^ BaseB;
                    hash = (hash * 397) ^ BaseA;
                    hash = (hash * 397) ^ EmissionR;
                    hash = (hash * 397) ^ EmissionG;
                    hash = (hash * 397) ^ EmissionB;
                    hash = (hash * 397) ^ EmissionA;
                    return hash;
                }
            }
        }

        private readonly List<GameObject> sourceCharacters = new() { null };
        private GameObject previewInstance;
        private Transform previewRoot;
        private string extraMultiSelectCategoriesCsv = string.Empty;

        private readonly List<CategoryData> categories = new();
        private readonly List<PieceMaterialGroup> materialGroups = new();
        private readonly HashSet<string> lockedMaterialSlots = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> lockedMultiCategoryPieces = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SlotRuntimeState> slotRuntimeStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DerivedMaterialKey, Material> previewDerivedMaterials = new();
        private readonly Dictionary<DerivedMaterialKey, string> exportMaterialPaths = new();

        private ModularCharacterBuildPreset presetAsset;
        private string prefabName = "Character_Custom";
        private DefaultAsset saveFolder;
        private Texture2D heroBannerTexture;
        private bool attemptedBannerLoad;
        private static bool hasCopiedSlotState;
        private static CopiedSlotState copiedSlotState;
        private bool syncBodyColors;

        private string lastHoveredUiSlotKey = string.Empty;
        private bool slotHoverPulseActive;
        private Renderer slotHoverPulseRenderer;
        private int slotHoverPulseSlotIndex = -1;
        private double slotHoverPulseStartTime;
        private const float SlotHoverPulseDuration = 1f;
        private Material slotHoverPulseMaterial;
        private Mesh slotHoverPulseBakedMesh;

        private bool isPresetModified;
        private string lastLoadedOrSavedPresetSignature = string.Empty;
        private int lastExportGeneratedMaterialsCount;
        private int lastExportReusedMaterialsCount;

        private GUIStyle sectionHeaderStyle;
        private GUIStyle sectionHeaderMiniStyle;
        private GUIStyle evenBG;
        private GUIStyle oddBG;
        private GUIStyle compactPopupStyle;
        private GUIStyle compactMiniButtonStyle;
        private GUIStyle compactToolbarButtonStyle;
        private GUIStyle compactFoldoutStyle;
        private GUIStyle compactLabelStyle;

        private ScrollView mainScrollView;
        private IMGUIContainer sourceImguiContainer;
        private IMGUIContainer bodyImguiContainer;
        private IMGUIContainer presetImguiContainer;
        private TextField extraMultiCategoriesField;
        private TextField prefabNameField;

        [MenuItem("Tools/João Baltieri 3D/JB Modular Character Builder 🛠️")]
        public static void Open()
        {
            var window = GetWindow<ModularCharacterPrefabBuilderWindow>();
            window.titleContent = new GUIContent("JB Modular Character Builder 🛠️");
            window.minSize = new Vector2(460f, 560f);
        }

        private static GUIContent GC(string text, string tooltip = null) => new(text, tooltip);

        private void OnEnable()
        {
            titleContent = new GUIContent("JB Modular Character Builder 🛠️");
            EnsureGeneratedFoldersExist();
            if (saveFolder == null)
                saveFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(GetGeneratedPrefabsFolderPath());

            SceneView.duringSceneGui -= OnSceneViewDuringGui;
            SceneView.duringSceneGui += OnSceneViewDuringGui;

            if (rootVisualElement != null && rootVisualElement.childCount == 0)
                BuildUi();
        }

        public void CreateGUI() => BuildUi();

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewDuringGui;

            if (slotHoverPulseMaterial != null)
                DestroyImmediate(slotHoverPulseMaterial);

            if (slotHoverPulseBakedMesh != null)
                DestroyImmediate(slotHoverPulseBakedMesh);

            ClearPreviewInstance();
            DestroyPreviewDerivedMaterials();
        }

        private void BuildUi()
        {
            rootVisualElement.Clear();
            mainScrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(mainScrollView);

            sourceImguiContainer = new IMGUIContainer(DrawSourceImgui) { style = { marginBottom = 6 } };
            var sourceFieldsSection = CreateToolkitFieldSection("SOURCE SETTINGS");

            extraMultiCategoriesField = CreateTextField(
                "Extra Multi Categories",
                "Optional comma-separated category names that should behave as multi-select. Example: HeadGear, BackGear, Accessories");
            extraMultiCategoriesField.value = extraMultiSelectCategoriesCsv;
            extraMultiCategoriesField.RegisterValueChangedCallback(evt =>
            {
                extraMultiSelectCategoriesCsv = evt.newValue ?? string.Empty;
                MarkAllGuiDirty();
            });
            sourceFieldsSection.Add(extraMultiCategoriesField);

            bodyImguiContainer = new IMGUIContainer(DrawBodyImgui) { style = { marginBottom = 6 } };

            prefabNameField = CreateTextField("Prefab Name", "Name used for the exported prefab asset.");
            prefabNameField.value = prefabName;
            prefabNameField.RegisterValueChangedCallback(evt =>
            {
                prefabName = evt.newValue ?? string.Empty;
                MarkAllGuiDirty();
            });

            presetImguiContainer = new IMGUIContainer(DrawPresetImgui);
            mainScrollView.Add(sourceImguiContainer);
            mainScrollView.Add(sourceFieldsSection);
            mainScrollView.Add(bodyImguiContainer);
            mainScrollView.Add(presetImguiContainer);
            SyncToolkitFieldsFromState();
        }

        private VisualElement CreateToolkitFieldSection(string title)
        {
            var section = new VisualElement();
            section.style.marginLeft = 4;
            section.style.marginRight = 4;
            section.style.marginBottom = 6;
            section.style.paddingLeft = 8;
            section.style.paddingRight = 8;
            section.style.paddingTop = 8;
            section.style.paddingBottom = 8;
            section.style.borderTopWidth = 1;
            section.style.borderBottomWidth = 1;
            section.style.borderLeftWidth = 1;
            section.style.borderRightWidth = 1;

            Color borderColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.10f);
            section.style.borderTopColor = borderColor;
            section.style.borderBottomColor = borderColor;
            section.style.borderLeftColor = borderColor;
            section.style.borderRightColor = borderColor;
            section.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.025f);

            var titleLabel = new Label(title);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 11;
            titleLabel.style.marginBottom = 6;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            section.Add(titleLabel);
            return section;
        }

        private TextField CreateTextField(string label, string tooltip)
        {
            var field = new TextField(label);
            field.tooltip = tooltip;
            field.style.marginBottom = 6;
            field.style.flexGrow = 1;
            field.labelElement.style.minWidth = 140;
            field.labelElement.style.unityFontStyleAndWeight = FontStyle.Normal;
            return field;
        }

        private void SyncToolkitFieldsFromState()
        {
            if (extraMultiCategoriesField != null && extraMultiCategoriesField.value != (extraMultiSelectCategoriesCsv ?? string.Empty))
                extraMultiCategoriesField.SetValueWithoutNotify(extraMultiSelectCategoriesCsv ?? string.Empty);
            if (prefabNameField != null && prefabNameField.value != (prefabName ?? string.Empty))
                prefabNameField.SetValueWithoutNotify(prefabName ?? string.Empty);
        }

        private void MarkAllGuiDirty()
        {
            sourceImguiContainer?.MarkDirtyRepaint();
            bodyImguiContainer?.MarkDirtyRepaint();
            presetImguiContainer?.MarkDirtyRepaint();
            Repaint();
        }

        private void EnsureStyles()
        {
            if (sectionHeaderStyle == null)
            {
                sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = EditorStyles.boldLabel.fontSize + 1,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                sectionHeaderStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.08f, 0.08f, 0.08f);
            }

            if (sectionHeaderMiniStyle == null)
            {
                sectionHeaderMiniStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = Mathf.Max(10, EditorStyles.boldLabel.fontSize),
                    fontStyle = FontStyle.Bold
                };
            }

            if (evenBG == null)
            {
                evenBG = new GUIStyle("box");
                evenBG.normal.background = MakeTex(new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.03f : 0.08f));
                evenBG.padding = new RectOffset(5, 5, 5, 5);
                evenBG.margin = new RectOffset(0, 0, 1, 1);
            }

            if (oddBG == null)
            {
                oddBG = new GUIStyle("box");
                oddBG.normal.background = MakeTex(new Color(0f, 0f, 0f, EditorGUIUtility.isProSkin ? 0.2f : 0.035f));
                oddBG.padding = new RectOffset(5, 5, 5, 5);
                oddBG.margin = new RectOffset(0, 0, 1, 1);
            }

            if (compactPopupStyle == null)
            {
                compactPopupStyle = new GUIStyle(EditorStyles.popup) { fixedHeight = 18 };
                compactPopupStyle.padding = new RectOffset(6, 18, 2, 2);
            }

            if (compactMiniButtonStyle == null)
            {
                compactMiniButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 18,
                    fontSize = 10,
                    padding = new RectOffset(2, 2, 1, 1),
                    margin = new RectOffset(1, 1, 1, 1)
                };
            }

            if (compactToolbarButtonStyle == null)
            {
                compactToolbarButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 20,
                    fontSize = 10,
                    padding = new RectOffset(5, 5, 2, 2),
                    margin = new RectOffset(1, 1, 1, 1)
                };
            }

            if (compactFoldoutStyle == null)
                compactFoldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

            if (compactLabelStyle == null)
                compactLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 10 };
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void DrawSectionTitle(string title)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 28f);
            Color bg = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
            Color topLine = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.55f);
            Color bottomLine = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.22f) : new Color(0f, 0f, 0f, 0.18f);
            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), topLine);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), bottomLine);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, rect.height - 8f), "🧩 " + title, sectionHeaderStyle);
            EditorGUILayout.Space(4f);
        }

        private static string GetFriendlySlotName(int slotIndex)
        {
            return slotIndex switch
            {
                0 => "Primary",
                1 => "Secondary",
                2 => "Tertiary",
                3 => "Quaternary",
                4 => "Quinary",
                5 => "Senary",
                _ => "Slot " + (slotIndex + 1)
            };
        }

        private string CaptureCurrentStateSignature()
        {
            var snapshot = ScriptableObject.CreateInstance<ModularCharacterBuildPreset>();
            FillPresetData(snapshot);
            string json = JsonUtility.ToJson(snapshot);
            DestroyImmediate(snapshot);
            return json;
        }

        private void RefreshPresetModifiedFlag()
        {
            string current = CaptureCurrentStateSignature();
            isPresetModified = !string.Equals(current, lastLoadedOrSavedPresetSignature, StringComparison.Ordinal);
        }

        private void MarkPresetAsSavedOrLoaded()
        {
            lastLoadedOrSavedPresetSignature = CaptureCurrentStateSignature();
            isPresetModified = false;
        }

        private void ApplySlotStateToAllBodyParts(SlotBinding sourceSlot)
        {
            if (sourceSlot == null)
                return;

            foreach (var group in materialGroups)
            {
                if (group == null || !group.isBodyGroup)
                    continue;

                foreach (var slot in group.slots)
                {
                    if (slot == null)
                        continue;

                    slot.baseColor = sourceSlot.baseColor;
                    slot.emissionColor = sourceSlot.emissionColor;
                    slot.useCustom = true;
                    SyncSlotRuntimeFromBinding(slot);
                    ApplySlotStateToRenderer(slot);
                }
            }

            RefreshPresetModifiedFlag();
            MarkAllGuiDirty();
        }

        private void EnsureEmbeddedBannerLoaded()
        {
            if (attemptedBannerLoad)
                return;
            attemptedBannerLoad = true;
            string[] guids = AssetDatabase.FindAssets($"{EmbeddedBannerFileName} t:Texture2D");
            if (guids == null || guids.Length == 0)
                return;
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            heroBannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void DrawHeroBanner()
        {
            if (heroBannerTexture == null)
                return;
            float aspect = heroBannerTexture.height > 0 ? (float)heroBannerTexture.width / heroBannerTexture.height : 1f;
            float availableWidth = Mathf.Max(100f, position.width - 24f);
            float bannerHeight = Mathf.Max(MinBannerHeight, availableWidth / Mathf.Max(0.01f, aspect));

            using (new EditorGUILayout.VerticalScope("box"))
            {
                Rect bannerRect = GUILayoutUtility.GetRect(availableWidth, bannerHeight, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.BeginGroup(bannerRect);
                    Rect localRect = new Rect(0f, 0f, bannerRect.width, bannerRect.height);
                    GUI.DrawTexture(localRect, heroBannerTexture, ScaleMode.ScaleAndCrop, true);
                    GUI.EndGroup();
                }
            }
        }

        private void DrawSourceImgui()
        {
            EnsureStyles();
            EnsureEmbeddedBannerLoaded();
            DrawHeroBanner();
            EditorGUILayout.Space(4);
            DrawSectionTitle("1. Source");

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField(GC("Source Characters", "You can load multiple source characters. Their modular parts will be combined in the builder."), compactLabelStyle);
                for (int i = 0; i < sourceCharacters.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        sourceCharacters[i] = (GameObject)EditorGUILayout.ObjectField(GC($"Source {i + 1}", "Character root prefab or scene object used as one of the modular sources."), sourceCharacters[i], typeof(GameObject), true);
                        GUI.enabled = sourceCharacters.Count > 1;
                        if (GUILayout.Button(GC("−", "Remove this source slot."), compactMiniButtonStyle, GUILayout.Width(24f)))
                        {
                            sourceCharacters.RemoveAt(i);
                            GUI.enabled = true;
                            MarkAllGuiDirty();
                            GUIUtility.ExitGUI();
                            return;
                        }
                        GUI.enabled = true;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(GC("+ Add Source", "Add another source character slot."), compactToolbarButtonStyle, GUILayout.Width(96f)))
                    {
                        sourceCharacters.Add(null);
                        MarkAllGuiDirty();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = sourceCharacters.Any(x => x != null);
                    if (GUILayout.Button(GC("📥 Load", "Create a fresh preview from all assigned source characters."), compactToolbarButtonStyle, GUILayout.Width(CompactActionButtonWidth)))
                        LoadSourceCharacters();

                    GUI.enabled = previewInstance != null;
                    if (GUILayout.Button(GC("🔁 Rebuild", "Rebuild the preview from all current sources while preserving selections and materials."), compactToolbarButtonStyle, GUILayout.Width(CompactActionButtonWidth)))
                        RebuildFromCurrentSource();

                    GUI.enabled = previewInstance != null;
                    if (GUILayout.Button(GC("🧩 Restore Base", "Restore all slots to their original base family materials from the preview source."), compactToolbarButtonStyle, GUILayout.Width(110f)))
                        RestoreAllBaseMaterials();

                    GUI.enabled = previewInstance != null;
                    if (GUILayout.Button(GC("🎯 Select", "Select the current preview instance in the Hierarchy."), compactToolbarButtonStyle, GUILayout.Width(68f)))
                        SelectPreviewInstance();

                    GUI.enabled = true;
                    if (GUILayout.Button(GC("🧹 Clear", "Remove the current preview and clear scanned categories and materials."), compactToolbarButtonStyle, GUILayout.Width(68f)))
                        ClearAll();
                    GUI.enabled = true;
                }
            }
        }

        private void DrawBodyImgui()
        {
            EnsureStyles();
            if (previewInstance != null)
            {
                DrawCategoriesSection();
                EditorGUILayout.Space(6);
                DrawMaterialsSection();
            }
            else
            {
                EditorGUILayout.HelpBox("Load one or more source characters from the Project or Hierarchy to begin.", MessageType.Info);
            }
        }

        private bool EnsureSlotHoverPulseMaterial()
        {
            if (slotHoverPulseMaterial != null)
                return true;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return false;

            slotHoverPulseMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            slotHoverPulseMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            slotHoverPulseMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            slotHoverPulseMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            slotHoverPulseMaterial.SetInt("_ZWrite", 0);

            return true;
        }

        private void TriggerSlotHoverPulse(SlotBinding slot)
        {
            if (slot == null || slot.targetRenderer == null || slot.slotIndex < 0)
                return;

            bool valid = false;

            if (slot.targetRenderer is SkinnedMeshRenderer skinned)
            {
                valid = skinned.sharedMesh != null && slot.slotIndex < skinned.sharedMesh.subMeshCount;
            }
            else
            {
                MeshFilter mf = slot.targetRenderer.GetComponent<MeshFilter>();
                valid = mf != null && mf.sharedMesh != null && slot.slotIndex < mf.sharedMesh.subMeshCount;
            }

            if (!valid)
                return;

            slotHoverPulseRenderer = slot.targetRenderer;
            slotHoverPulseSlotIndex = slot.slotIndex;
            slotHoverPulseStartTime = EditorApplication.timeSinceStartup;
            slotHoverPulseActive = true;
            SceneView.RepaintAll();
        }

        private void ProcessSlotHover(Rect rect, SlotBinding slot)
        {
            Event e = Event.current;
            if (slot == null || e == null)
                return;

            if (!rect.Contains(e.mousePosition))
                return;

            if (string.Equals(lastHoveredUiSlotKey, slot.slotKey, StringComparison.Ordinal))
                return;

            lastHoveredUiSlotKey = slot.slotKey;
            TriggerSlotHoverPulse(slot);
        }

        private void OnSceneViewDuringGui(SceneView sceneView)
        {
            if (!slotHoverPulseActive || slotHoverPulseRenderer == null || slotHoverPulseSlotIndex < 0)
                return;

            float elapsed = (float)(EditorApplication.timeSinceStartup - slotHoverPulseStartTime);
            if (elapsed >= SlotHoverPulseDuration)
            {
                slotHoverPulseActive = false;
                slotHoverPulseRenderer = null;
                slotHoverPulseSlotIndex = -1;
                return;
            }

            if (!EnsureSlotHoverPulseMaterial())
                return;

            float t = 1f - Mathf.Clamp01(elapsed / SlotHoverPulseDuration);
            float alpha = 0.6f * t;
            slotHoverPulseMaterial.SetColor("_Color", new Color(1f, 0.85f, 0.2f, alpha));

            Mesh mesh = null;
            Matrix4x4 matrix;

            if (slotHoverPulseRenderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null || slotHoverPulseSlotIndex >= skinned.sharedMesh.subMeshCount)
                    return;

                if (slotHoverPulseBakedMesh == null)
                    slotHoverPulseBakedMesh = new Mesh();

                skinned.BakeMesh(slotHoverPulseBakedMesh);
                mesh = slotHoverPulseBakedMesh;
                matrix = skinned.transform.localToWorldMatrix;
            }
            else
            {
                MeshFilter mf = slotHoverPulseRenderer.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || slotHoverPulseSlotIndex >= mf.sharedMesh.subMeshCount)
                    return;

                mesh = mf.sharedMesh;
                matrix = slotHoverPulseRenderer.transform.localToWorldMatrix;
            }

            if (mesh == null)
                return;

            slotHoverPulseMaterial.SetPass(0);
            Graphics.DrawMeshNow(mesh, matrix, slotHoverPulseSlotIndex);
            sceneView.Repaint();
        }

        private void DrawPresetImgui()
        {
            EnsureStyles();
            if (previewInstance == null)
                return;

            bool requestLoadPreset = false;
            bool requestSavePreset = false;
            bool requestSavePresetAs = false;
            bool requestExportPrefab = false;
            ModularCharacterBuildPreset selectedPreset = presetAsset;

            DrawSectionTitle("4. Presets / Save");
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.VerticalScope(evenBG))
                {
                    selectedPreset = (ModularCharacterBuildPreset)EditorGUILayout.ObjectField(
                        GC("Preset", "Selecting a preset automatically loads character selections, locks and slot material states."),
                        presetAsset,
                        typeof(ModularCharacterBuildPreset),
                        false);

                    if (selectedPreset != presetAsset)
                    {
                        presetAsset = selectedPreset;
                        requestLoadPreset = presetAsset != null;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = presetAsset != null;
                        if (GUILayout.Button(GC("💾 Save Preset", "Overwrite the assigned preset with the current build state."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                            requestSavePreset = true;
                        GUI.enabled = true;
                        if (GUILayout.Button(GC("🆕 Save Preset As", "Create a new preset asset from the current build state."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                            requestSavePresetAs = true;
                        GUILayout.Space(6f);
                        GUI.contentColor = isPresetModified ? new Color(1f, 0.9f, 0.3f) : Color.white;
                        EditorGUILayout.LabelField(isPresetModified ? "⚠️ Preset Modified" : string.Empty, EditorStyles.boldLabel, GUILayout.Width(120f));
                        GUI.contentColor = Color.white;
                    }
                }

                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.VerticalScope(oddBG))
                {
                    prefabName = EditorGUILayout.TextField(GC("Prefab Name", "Name used for the exported prefab asset."), prefabName ?? string.Empty);
                    if (prefabNameField != null && prefabNameField.value != (prefabName ?? string.Empty))
                        prefabNameField.SetValueWithoutNotify(prefabName ?? string.Empty);

                    saveFolder = (DefaultAsset)EditorGUILayout.ObjectField(GC("Save Folder", "Folder inside Assets where the prefab and derived materials will be saved."), saveFolder, typeof(DefaultAsset), false);
                    EditorGUILayout.Space(4f);
                    if (GUILayout.Button(GC("📦 Export Character Prefab", "Export the current visible build as a clean prefab with deduplicated derived materials."), GUILayout.Height(28)))
                        requestExportPrefab = true;
                }
            }

            if (requestLoadPreset)
            {
                EditorApplication.delayCall += () => { if (presetAsset != null) LoadPreset(presetAsset); };
                GUIUtility.ExitGUI();
            }
            if (requestSavePreset)
            {
                EditorApplication.delayCall += SavePresetOverwrite;
                GUIUtility.ExitGUI();
            }
            if (requestSavePresetAs)
            {
                EditorApplication.delayCall += SavePresetAs;
                GUIUtility.ExitGUI();
            }
            if (requestExportPrefab)
            {
                EditorApplication.delayCall += SavePrefab;
                GUIUtility.ExitGUI();
            }
        }

        private List<int> GetDefaultMultiSelectionIndices(CategoryData category)
        {
            List<int> result = new();
            if (category == null || category.pieces == null || category.pieces.Count == 0)
                return result;

            IEnumerable<IGrouping<string, (PieceData piece, int index)>> grouped = category.pieces
                .Select((piece, index) => (piece, index))
                .GroupBy(x => GetConflictKey(x.piece.objectName, category.name), StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
                result.Add(group.First().index);

            return result.Distinct().OrderBy(x => x).ToList();
        }

        private void DrawCategoriesSection()
        {
            DrawSectionTitle("2. Character Parts");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(GC("🎲 Randomize Character", "Randomize all unlocked categories. Locked categories are preserved. Required body categories never become None. Multi-select categories respect locked pieces."), compactToolbarButtonStyle, GUILayout.Width(144f)))
                    RandomizeCharacter();
                if (GUILayout.Button(GC("⬇ Expand All", "Expand all category groups."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllCategoryFoldouts(true);
                if (GUILayout.Button(GC("⬆ Collapse All", "Collapse all category groups."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllCategoryFoldouts(false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(GC("🔒 Lock All", "Lock all single-select categories and all pieces inside multi-select categories."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllCategoryLocks(true);
                if (GUILayout.Button(GC("🔓 Unlock All", "Unlock all single-select categories and all pieces inside multi-select categories."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllCategoryLocks(false);
            }
            DrawCategoriesBlock();
        }

        private void DrawCategoriesBlock()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                int visualIndex = 0;
                foreach (var category in categories)
                {
                    if (category.pieces.Count == 0)
                        continue;
                    GUIStyle bgStyle = (visualIndex % 2 == 0) ? evenBG : oddBG;
                    using (new EditorGUILayout.VerticalScope(bgStyle))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            category.foldout = EditorGUILayout.Foldout(category.foldout, GC($"{category.displayName} ({category.pieces.Count})", category.allowMultiple ? "Multi-select category." : "Single-select category."), true, compactFoldoutStyle);
                            if (!category.allowMultiple)
                            {
                                bool newLocked = GUILayout.Toggle(category.isLocked, GC(category.isLocked ? "🔒" : "🔓", category.isLocked ? "Locked: Randomize Character will ignore this category." : "Unlocked: Randomize Character can modify this category."), compactMiniButtonStyle, GUILayout.Width(LockButtonWidth));
                                if (newLocked != category.isLocked)
                                    category.isLocked = newLocked;
                            }
                        }

                        if (category.foldout)
                        {
                            EditorGUI.indentLevel++;
                            if (category.allowMultiple) DrawMultiCategory(category); else DrawSingleCategory(category);
                            EditorGUI.indentLevel--;
                        }
                    }
                    visualIndex++;
                }
            }
        }

        private void DrawSingleCategory(CategoryData category)
        {
            bool allowNone = !category.disallowNone;
            var options = new List<string>();
            if (allowNone) options.Add("None");
            options.AddRange(category.pieces.Select(p => p.displayName));

            int uiIndex = allowNone ? (category.selectedIndex >= 0 ? category.selectedIndex + 1 : 0) : Mathf.Clamp(category.selectedIndex, 0, Mathf.Max(0, category.pieces.Count - 1));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(GC("Selection", "Choose the active piece for this category."));

            if (GUILayout.Button(GC("<", "Previous piece."), compactMiniButtonStyle, GUILayout.Width(CompactButtonWidth)))
                CycleSingleCategory(category, -1);

            GUI.enabled = allowNone;
            if (GUILayout.Button(GC("X", allowNone ? "Set this category to None." : "Required category. None is not allowed."), compactMiniButtonStyle, GUILayout.Width(CompactButtonWidth)))
            {
                if (allowNone && category.selectedIndex != -1)
                {
                    category.selectedIndex = -1;
                    ApplySelectionsToPreview();
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                    return;
                }
            }
            GUI.enabled = true;

            if (GUILayout.Button(GC(">", "Next piece."), compactMiniButtonStyle, GUILayout.Width(CompactButtonWidth)))
                CycleSingleCategory(category, 1);

            if (GUILayout.Button(GC("🎲", allowNone ? "Choose a random piece, including None." : "Choose a random piece. Required category cannot become None."), compactMiniButtonStyle, GUILayout.Width(DiceButtonWidth)))
            {
                category.selectedIndex = allowNone ? UnityEngine.Random.Range(-1, category.pieces.Count) : UnityEngine.Random.Range(0, category.pieces.Count);
                ApplySelectionsToPreview();
                EditorGUILayout.EndHorizontal();
                GUIUtility.ExitGUI();
                return;
            }

            int newUiIndex = EditorGUILayout.Popup(uiIndex, options.ToArray(), compactPopupStyle, GUILayout.MaxWidth(220f));
            int newSelectedIndex = allowNone ? newUiIndex - 1 : newUiIndex;
            if (newSelectedIndex != category.selectedIndex)
            {
                category.selectedIndex = newSelectedIndex;
                ApplySelectionsToPreview();
                EditorGUILayout.EndHorizontal();
                GUIUtility.ExitGUI();
                return;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void CycleSingleCategory(CategoryData category, int direction)
        {
            if (category == null || category.pieces.Count == 0)
                return;
            bool allowNone = !category.disallowNone;
            if (allowNone)
                category.selectedIndex = category.selectedIndex < 0 ? (direction >= 0 ? 0 : category.pieces.Count - 1) : (category.selectedIndex + direction + category.pieces.Count) % category.pieces.Count;
            else
                category.selectedIndex = category.selectedIndex < 0 ? 0 : (category.selectedIndex + direction + category.pieces.Count) % category.pieces.Count;
            ApplySelectionsToPreview();
            GUIUtility.ExitGUI();
        }

        private void RandomizeMultiCategory(CategoryData category)
        {
            if (category == null)
                return;
            var lockedSelectedIndices = category.selectedIndices.Where(i => i >= 0 && i < category.pieces.Count && IsMultiCategoryPieceLocked(category.pieces[i])).ToHashSet();
            var result = new HashSet<int>(lockedSelectedIndices);
            var grouped = category.pieces.Select((piece, index) => new { piece, index, key = GetConflictKey(piece.objectName, category.name) }).GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var group in grouped)
            {
                if (group == null) continue;
                var groupItems = group.ToList();
                bool hasLockedSelectedInGroup = groupItems.Any(x => IsMultiCategoryPieceLocked(x.piece) && category.selectedIndices.Contains(x.index));
                if (hasLockedSelectedInGroup)
                {
                    foreach (var lockedSelected in groupItems.Where(x => IsMultiCategoryPieceLocked(x.piece) && category.selectedIndices.Contains(x.index)))
                        result.Add(lockedSelected.index);
                    continue;
                }

                var unlockedCandidates = groupItems.Where(x => !IsMultiCategoryPieceLocked(x.piece)).ToList();
                if (unlockedCandidates.Count == 0) continue;
                if (UnityEngine.Random.Range(0f, 1f) < 0.5f) continue;
                result.Add(unlockedCandidates[UnityEngine.Random.Range(0, unlockedCandidates.Count)].index);
            }
            category.selectedIndices = result.Distinct().OrderBy(x => x).ToList();
        }

        private static int CompareCategoryOrder(CategoryData a, CategoryData b)
        {
            int ia = Array.FindIndex(CoreCategoryOrder, x => string.Equals(x, a.name, StringComparison.OrdinalIgnoreCase));
            int ib = Array.FindIndex(CoreCategoryOrder, x => string.Equals(x, b.name, StringComparison.OrdinalIgnoreCase));
            bool aCore = ia >= 0;
            bool bCore = ib >= 0;
            if (aCore && bCore) return ia.CompareTo(ib);
            if (aCore && !bCore) return -1;
            if (!aCore && bCore) return 1;
            if (a.name.StartsWith("Body_", StringComparison.OrdinalIgnoreCase) && b.name.StartsWith("Body_", StringComparison.OrdinalIgnoreCase))
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            if (a.name.StartsWith("Body_", StringComparison.OrdinalIgnoreCase)) return -1;
            if (b.name.StartsWith("Body_", StringComparison.OrdinalIgnoreCase)) return 1;
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetConflictKey(string objectName, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return string.Empty;
            if (string.IsNullOrWhiteSpace(categoryName)) return objectName;
            string[] parts = objectName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return objectName;

            if (categoryName.StartsWith("Body_", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length <= 2) return objectName;
                string[] variantParts = parts.Skip(2).ToArray();
                if (variantParts.Length >= 2 && int.TryParse(variantParts[^1], out _))
                    return string.Join("_", variantParts.Take(variantParts.Length - 1));
                return string.Join("_", variantParts);
            }

            string prefix = categoryName + "_";
            string raw = objectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? objectName.Substring(prefix.Length) : objectName;
            string[] rawParts = raw.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (rawParts.Length == 0) return raw;
            if (rawParts.Length >= 2 && int.TryParse(rawParts[^1], out _))
                return string.Join("_", rawParts.Take(rawParts.Length - 1));
            return raw;
        }

        private void DrawMultiCategory(CategoryData category)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(GC("None", "Disable all unlocked pieces in this multi-select category. Locked selected pieces remain selected."), compactToolbarButtonStyle, GUILayout.Width(CompactNoneButtonWidth)))
                {
                    category.selectedIndices = category.selectedIndices.Where(i => i >= 0 && i < category.pieces.Count && IsMultiCategoryPieceLocked(category.pieces[i])).Distinct().OrderBy(x => x).ToList();
                    ApplySelectionsToPreview();
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(GC("🎲 Randomize", "Randomize this multi-select category while avoiding conflicting variants and respecting locked pieces."), compactToolbarButtonStyle, GUILayout.Width(CompactRandomButtonWidth)))
                {
                    RandomizeMultiCategory(category);
                    ApplySelectionsToPreview();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(GC("🔒 Lock All", "Lock all pieces in this multi-select category."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                {
                    SetAllMultiCategoryPieceLocks(category, true);
                    MarkAllGuiDirty();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(GC("🔓 Unlock All", "Unlock all pieces in this multi-select category."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                {
                    SetAllMultiCategoryPieceLocks(category, false);
                    MarkAllGuiDirty();
                    GUIUtility.ExitGUI();
                }
            }

            for (int i = 0; i < category.pieces.Count; i++)
            {
                PieceData piece = category.pieces[i];
                bool current = category.selectedIndices.Contains(i);
                bool locked = IsMultiCategoryPieceLocked(piece);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool newLocked = GUILayout.Toggle(locked, GC(locked ? "🔒" : "🔓", locked ? "Locked: Randomize Character and Randomize this category will preserve this piece state." : "Unlocked: Randomization can modify this piece."), compactMiniButtonStyle, GUILayout.Width(LockButtonWidth));
                    bool next = EditorGUILayout.ToggleLeft(GC(piece.displayName, "Toggle this piece on or off."), current);
                    if (newLocked != locked)
                    {
                        SetMultiCategoryPieceLocked(piece, newLocked);
                        MarkAllGuiDirty();
                    }
                    if (next == current)
                        continue;
                    if (next) category.selectedIndices.Add(i); else category.selectedIndices.Remove(i);
                    category.selectedIndices = category.selectedIndices.Distinct().OrderBy(x => x).ToList();
                    ApplySelectionsToPreview();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private bool AreAllBodyMaterialSlotsLocked()
        {
            bool foundAny = false;
            foreach (var group in materialGroups)
            {
                if (group == null || !group.isBodyGroup) continue;
                foreach (var slot in group.slots)
                {
                    if (slot == null) continue;
                    foundAny = true;
                    if (!slot.isLocked) return false;
                }
            }
            return foundAny;
        }

        private void SetAllBodyMaterialSlotLocks(bool isLocked)
        {
            foreach (var group in materialGroups)
            {
                if (group == null || !group.isBodyGroup) continue;
                foreach (var slot in group.slots)
                {
                    if (slot == null) continue;
                    slot.isLocked = isLocked;
                    SetMaterialSlotLocked(group, slot, isLocked);
                    SyncSlotRuntimeFromBinding(slot);
                }
            }
            MarkAllGuiDirty();
        }

        private void DrawMaterialsSection()
        {
            DrawSectionTitle("3. Materials");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUI.enabled = previewInstance != null;
                if (GUILayout.Button(GC("🔄 Refresh Slots", "Re-scan active pieces and rebuild the material groups list."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                {
                    RebuildMaterialGroups();
                    MarkAllGuiDirty();
                }
                if (GUILayout.Button(GC("🎲 Randomize Colors", "Randomize visible unlocked slots using family-aware rules."), compactToolbarButtonStyle, GUILayout.Width(140f)))
                    RandomizeVisibleMaterials();
                if (GUILayout.Button(GC("⬇ Expand All", "Expand all material groups."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllMaterialFoldouts(true);
                if (GUILayout.Button(GC("⬆ Collapse All", "Collapse all material groups."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllMaterialFoldouts(false);
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(GC("♻ Reset All", "Reset all visible unlocked slots to their original base family materials."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    ResetAllMaterialSlots();
                bool allBodyLocked = AreAllBodyMaterialSlotsLocked();
                if (GUILayout.Button(GC(allBodyLocked ? "🔒 Unlock Body" : "🔓 Lock Body", allBodyLocked ? "Unlock all body slots so randomization can affect them." : "Lock all body slots so randomization will preserve them."), compactToolbarButtonStyle, GUILayout.Width(140f)))
                    SetAllBodyMaterialSlotLocks(!allBodyLocked);
                if (GUILayout.Button(GC("🔒 Lock All", "Lock all visible material slots so Randomize Colors will ignore them."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllMaterialSlotLocks(true);
                if (GUILayout.Button(GC("🔓 Unlock All", "Unlock all visible material slots so Randomize Colors can affect them."), compactToolbarButtonStyle, GUILayout.Width(CompactToolbarButtonWidth)))
                    SetAllMaterialSlotLocks(false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        GC(syncBodyColors ? "🔓 Free Body Colors" : "🔗 Sync Body Colors",
                           syncBodyColors
                               ? "Body colors are currently synchronized. Click to randomize body slots independently."
                               : "Body colors are currently independent. Click to synchronize body slots during randomization."),
                        compactToolbarButtonStyle,
                        GUILayout.Width(140f)))
                {
                    syncBodyColors = !syncBodyColors;
                    MarkAllGuiDirty();
                }
            }

            DrawMaterialsBlock();
        }

        private void DrawMaterialsBlock()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (materialGroups.Count == 0)
                {
                    EditorGUILayout.HelpBox("No active material slots were found.", MessageType.Info);
                    return;
                }

                int visualIndex = 0;
                var groupsSnapshot = materialGroups.ToList();
                foreach (var pieceGroup in groupsSnapshot)
                {
                    if (pieceGroup == null) continue;
                    GUIStyle bgStyle = (visualIndex % 2 == 0) ? evenBG : oddBG;
                    using (new EditorGUILayout.VerticalScope(bgStyle))
                    {
                        pieceGroup.foldout = EditorGUILayout.Foldout(pieceGroup.foldout, GC("🔹 " + pieceGroup.displayName, "Material controls for this active piece."), true, compactFoldoutStyle);
                        if (pieceGroup.foldout)
                        {
                            EditorGUI.indentLevel++;
                            var slotsSnapshot = pieceGroup.slots.ToList();
                            foreach (var slot in slotsSnapshot)
                            {
                                if (slot == null) continue;
                                Rect slotBlockRect = EditorGUILayout.BeginVertical("box");
                                {
                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.LabelField(GC($"{slot.slotName}  •  {GetFamilyDisplayName(slot.family)}", "Per-slot material controls."), sectionHeaderMiniStyle);
                                        bool newLocked = GUILayout.Toggle(slot.isLocked, GC(slot.isLocked ? "🔒" : "🔓", slot.isLocked ? "Locked: Randomize Colors will ignore this slot." : "Unlocked: Randomize Colors can affect this slot."), compactMiniButtonStyle, GUILayout.Width(LockButtonWidth));
                                        if (newLocked != slot.isLocked)
                                        {
                                            slot.isLocked = newLocked;
                                            SetMaterialSlotLocked(pieceGroup, slot, newLocked);
                                            SyncSlotRuntimeFromBinding(slot);
                                        }
                                        if (GUILayout.Button(GC("♻ Reset", "Reset this slot to its original family base material."), compactMiniButtonStyle, GUILayout.Width(CompactMiniButtonWidth)))
                                        {
                                            ResetSingleSlot(slot);
                                            ApplySlotStateToRenderer(slot);
                                            RefreshPresetModifiedFlag();
                                            MarkAllGuiDirty();
                                        }
                                    }

                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        if (GUILayout.Button(GC("📋 Copy", "Copy this slot color state."), compactMiniButtonStyle, GUILayout.Width(CompactMiniButtonWidth)))
                                        {
                                            copiedSlotState = new CopiedSlotState
                                            {
                                                baseColor = slot.baseColor,
                                                emissionColor = slot.emissionColor
                                            };
                                            hasCopiedSlotState = true;
                                        }

                                        GUI.enabled = hasCopiedSlotState;
                                        if (GUILayout.Button(GC("📥 Paste", "Paste the last copied colors into this slot."), compactMiniButtonStyle, GUILayout.Width(CompactMiniButtonWidth)))
                                        {
                                            slot.baseColor = copiedSlotState.baseColor;
                                            slot.emissionColor = copiedSlotState.emissionColor;
                                            slot.useCustom = true;
                                            SyncSlotRuntimeFromBinding(slot);
                                            ApplySlotStateToRenderer(slot);
                                            RefreshPresetModifiedFlag();
                                            MarkAllGuiDirty();
                                        }
                                        GUI.enabled = true;

                                        if (pieceGroup.isBodyGroup && GUILayout.Button(GC("🔗 Apply to All Body Parts", "Apply this slot colors to all body part slots."), compactMiniButtonStyle, GUILayout.Width(132f)))
                                        {
                                            ApplySlotStateToAllBodyParts(slot);
                                            GUIUtility.ExitGUI();
                                        }
                                    }

                                    int materialMode = slot.useCustom ? 0 : 1;
                                    int newMaterialMode = GUILayout.Toolbar(materialMode, new[] { "Custom Color", "Material Asset" });
                                    bool newUseCustom = newMaterialMode == 0;
                                    if (newUseCustom != slot.useCustom)
                                    {
                                        slot.useCustom = newUseCustom;
                                        SyncSlotRuntimeFromBinding(slot);
                                        ApplySlotStateToRenderer(slot);
                                        MarkAllGuiDirty();
                                    }

                                    EditorGUILayout.Space(4f);
                                    if (!slot.useCustom)
                                    {
                                        using (new EditorGUILayout.HorizontalScope())
                                        {
                                            Material newExistingMaterial = (Material)EditorGUILayout.ObjectField(GC("Material Asset", "Material used directly by this slot in Material Asset mode."), slot.existingMaterial, typeof(Material), false);
                                            if (newExistingMaterial != slot.existingMaterial)
                                            {
                                                slot.existingMaterial = newExistingMaterial;
                                                slot.family = newExistingMaterial != null ? DetectMaterialFamily(newExistingMaterial) : slot.baseFamily;
                                                slot.useCustom = false;
                                                SyncSlotRuntimeFromBinding(slot);
                                                ApplySlotStateToRenderer(slot);
                                                RefreshPresetModifiedFlag();
                                                MarkAllGuiDirty();
                                            }

                                            Rect browseRect = GUILayoutUtility.GetRect(CompactBrowseButtonWidth, 18f, GUILayout.Width(CompactBrowseButtonWidth));
                                            if (GUI.Button(browseRect, GC("Browse...", "Open material browser filtered by this slot family.")))
                                            {
                                                MaterialBrowserPopupWindow.ShowWindow(
                                                    browseRect,
                                                    slot.family,
                                                    slot.existingMaterial,
                                                    selectedMaterial =>
                                                    {
                                                        slot.existingMaterial = selectedMaterial;
                                                        slot.family = selectedMaterial != null ? DetectMaterialFamily(selectedMaterial) : slot.baseFamily;
                                                        slot.useCustom = false;
                                                        SyncSlotRuntimeFromBinding(slot);
                                                        ApplySlotStateToRenderer(slot);
                                                        RefreshPresetModifiedFlag();
                                                        MarkAllGuiDirty();
                                                    },
                                                    GetGeneratedMaterialsFolderPath(false));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Color newBaseColor = EditorGUILayout.ColorField(GC("Base Color", "Base color used to derive the slot material."), slot.baseColor);
                                        if (newBaseColor != slot.baseColor)
                                        {
                                            slot.baseColor = newBaseColor;
                                            slot.useCustom = true;
                                            SyncSlotRuntimeFromBinding(slot);
                                            ApplySlotStateToRenderer(slot);
                                            RefreshPresetModifiedFlag();
                                            MarkAllGuiDirty();
                                        }

                                        if (slot.family == ModularCharacterBuildPreset.MaterialFamily.Emissive)
                                        {
                                            Color newEmissionColor = EditorGUILayout.ColorField(GC("Emission Color", "HDR emission color used to derive the emissive material."), slot.emissionColor, true, true, true);
                                            if (newEmissionColor != slot.emissionColor)
                                            {
                                                slot.emissionColor = newEmissionColor;
                                                slot.useCustom = true;
                                                SyncSlotRuntimeFromBinding(slot);
                                                ApplySlotStateToRenderer(slot);
                                                RefreshPresetModifiedFlag();
                                                MarkAllGuiDirty();
                                            }
                                        }
                                    }

                                    ProcessSlotHover(slotBlockRect, slot);
                                }
                                EditorGUILayout.EndVertical();
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                    visualIndex++;
                }
            }
        }

        private void ResetAllMaterialSlots()
        {
            if (previewInstance == null)
                return;
            foreach (var state in slotRuntimeStates.Values)
            {
                if (state == null || state.isLocked) continue;
                state.family = state.baseFamily;
                state.useCustom = false;
                state.existingMaterial = state.familyBaseMaterial;
                state.baseColor = ExtractBaseColor(state.familyBaseMaterial);
                state.emissionColor = ExtractEmissionColor(state.familyBaseMaterial);
            }
            ApplyAllRuntimeStatesToPreview();
            RebuildMaterialGroups();
            RefreshPresetModifiedFlag();
            MarkAllGuiDirty();
        }

        private void RandomizeVisibleMaterials()
        {
            if (previewInstance == null)
                return;
            Color sharedBodyColor = GenerateBodyColor();
            foreach (var pieceGroup in materialGroups)
            {
                if (pieceGroup == null) continue;
                foreach (var slot in pieceGroup.slots)
                {
                    if (slot == null || slot.isLocked) continue;
                    slot.useCustom = true;
                    if (pieceGroup.isBodyGroup && syncBodyColors)
                    {
                        slot.baseColor = sharedBodyColor;
                        if (slot.family == ModularCharacterBuildPreset.MaterialFamily.Emissive)
                            slot.emissionColor = GenerateControlledEmissionColor(sharedBodyColor);
                    }
                    else
                    {
                        slot.baseColor = GenerateRandomBaseColorForFamily(slot.family);
                        if (slot.family == ModularCharacterBuildPreset.MaterialFamily.Emissive)
                            slot.emissionColor = GenerateControlledEmissionColor(slot.baseColor);
                    }
                    SyncSlotRuntimeFromBinding(slot);
                    ApplySlotStateToRenderer(slot);
                }
            }
            RefreshPresetModifiedFlag();
            MarkAllGuiDirty();
        }

        private Color GenerateBodyColor() => RandomColorHSV(0f, 1f, 0.15f, 0.45f, 0.50f, 0.95f);

        private Color GenerateRandomBaseColorForFamily(ModularCharacterBuildPreset.MaterialFamily family)
        {
            return family switch
            {
                ModularCharacterBuildPreset.MaterialFamily.Metal => RandomColorHSV(0f, 1f, 0.05f, 0.25f, 0.45f, 0.90f),
                ModularCharacterBuildPreset.MaterialFamily.Emissive => RandomColorHSV(0f, 1f, 0.45f, 0.90f, 0.50f, 1.00f),
                ModularCharacterBuildPreset.MaterialFamily.Glass => RandomColorHSV(0f, 1f, 0.20f, 0.80f, 0.55f, 1.00f),
                _ => RandomColorHSV(0f, 1f, 0.25f, 0.85f, 0.45f, 1.00f)
            };
        }

        private Color GenerateControlledEmissionColor(Color baseColor)
        {
            Color.RGBToHSV(baseColor, out float h, out _, out _);
            return Color.HSVToRGB(h, UnityEngine.Random.Range(0.60f, 1.00f), UnityEngine.Random.Range(0.75f, 1.00f));
        }

        private static Color RandomColorHSV(float hMin, float hMax, float sMin, float sMax, float vMin, float vMax)
        {
            return Color.HSVToRGB(UnityEngine.Random.Range(hMin, hMax), UnityEngine.Random.Range(sMin, sMax), UnityEngine.Random.Range(vMin, vMax));
        }

        private void LoadSourceCharacters()
        {
            var validSources = sourceCharacters.Where(x => x != null).ToList();
            if (validSources.Count == 0)
            {
                EditorUtility.DisplayDialog("João Baltieri 3D", "Assign at least one source character first.", "OK");
                return;
            }

            ClearPreviewInstance();
            DestroyPreviewDerivedMaterials();
            slotRuntimeStates.Clear();
            lockedMaterialSlots.Clear();
            lockedMultiCategoryPieces.Clear();
            exportMaterialPaths.Clear();

            previewInstance = new GameObject(TempPreviewName) { hideFlags = HideFlags.DontSave };
            previewRoot = previewInstance.transform;
            Undo.RegisterCreatedObjectUndo(previewInstance, "Create Character Builder Preview");
            Selection.activeGameObject = previewInstance;
            EditorGUIUtility.PingObject(previewInstance);

            GameObject masterSource = ResolveSourceRoot(validSources[0]);
            if (masterSource == null)
                return;

            GameObject masterInstance = masterSource.scene.IsValid()
                ? Instantiate(masterSource, previewRoot)
                : (GameObject)PrefabUtility.InstantiatePrefab(masterSource, previewRoot) ?? Instantiate(masterSource, previewRoot);

            masterInstance.name = masterSource.name;
            masterInstance.hideFlags = HideFlags.DontSave;

            Dictionary<string, Transform> masterBoneMap = BuildBoneMap(masterInstance.transform);

            for (int i = 1; i < validSources.Count; i++)
            {
                GameObject extraSource = ResolveSourceRoot(validSources[i]);
                if (extraSource == null)
                    continue;

                MergeAdditionalSourcePiecesIntoMaster(extraSource, masterBoneMap);
            }

            RemoveAtlasControllersFromPreview();
            ScanPreview();
            CaptureInitialSlotStates();
            ApplySelectionsToPreview();

            if (string.IsNullOrWhiteSpace(prefabName))
                prefabName = masterSource.name + "_Custom";

            SyncToolkitFieldsFromState();
            MarkPresetAsSavedOrLoaded();
            MarkAllGuiDirty();
        }

        private Dictionary<string, Transform> BuildBoneMap(Transform root)
        {
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            if (root == null)
                return map;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                string path = AnimationUtility.CalculateTransformPath(t, root);
                if (!map.ContainsKey(path))
                    map.Add(path, t);
            }

            return map;
        }

        private void MergeAdditionalSourcePiecesIntoMaster(GameObject sourcePrefab, Dictionary<string, Transform> masterBoneMap)
        {
            GameObject sourceInstance = null;

            try
            {
                sourceInstance = sourcePrefab.scene.IsValid()
                    ? Instantiate(sourcePrefab, previewRoot)
                    : (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, previewRoot) ?? Instantiate(sourcePrefab, previewRoot);

                if (sourceInstance == null)
                    return;

                sourceInstance.name = sourcePrefab.name;
                sourceInstance.hideFlags = HideFlags.DontSave;

                var renderers = sourceInstance.GetComponentsInChildren<Renderer>(true);
                var pieceRoots = new HashSet<Transform>();

                foreach (var renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    Transform pieceRoot = FindPieceRoot(renderer.transform, sourceInstance.transform);
                    if (pieceRoot == null)
                        continue;

                    pieceRoots.Add(pieceRoot);

                    if (renderer is SkinnedMeshRenderer smr)
                        RebindSkinnedMeshRendererToMaster(smr, sourceInstance.transform, masterBoneMap);
                }

                var topLevelPieceRoots = pieceRoots
                    .Where(root => root != null && !pieceRoots.Any(other => other != null && other != root && root.IsChildOf(other)))
                    .ToList();

                foreach (Transform pieceRoot in topLevelPieceRoots)
                {
                    if (pieceRoot != null && pieceRoot.parent != sourceInstance.transform)
                        pieceRoot.SetParent(sourceInstance.transform, true);
                }

                PruneNonPieceChildren(sourceInstance.transform, new HashSet<Transform>(topLevelPieceRoots));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JB Modular Character Builder] Failed to merge source '{sourcePrefab.name}'.\n{ex}");
                if (sourceInstance != null)
                    DestroyImmediate(sourceInstance);
            }
        }

        private void PruneNonPieceChildren(Transform containerRoot, HashSet<Transform> keptPieceRoots)
        {
            if (containerRoot == null)
                return;

            for (int i = containerRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = containerRoot.GetChild(i);
                if (child == null)
                    continue;

                if (!keptPieceRoots.Contains(child))
                    DestroyImmediate(child.gameObject);
            }
        }

        private Transform FindPieceRoot(Transform current, Transform sourceRoot)
        {
            if (current == null || sourceRoot == null)
                return null;

            Transform t = current;
            while (t != null && t != sourceRoot)
            {
                if (TryParseObjectName(t.name, out _, out _, out _))
                    return t;

                t = t.parent;
            }

            return null;
        }

        private void RebindSkinnedMeshRendererToMaster(
            SkinnedMeshRenderer smr,
            Transform sourceCharacterRoot,
            Dictionary<string, Transform> masterBoneMap)
        {
            if (smr == null || sourceCharacterRoot == null || masterBoneMap == null)
                return;

            Transform[] originalBones = smr.bones ?? Array.Empty<Transform>();
            Transform[] reboundBones = new Transform[originalBones.Length];

            for (int i = 0; i < originalBones.Length; i++)
            {
                Transform sourceBone = originalBones[i];
                if (sourceBone == null)
                    continue;

                string sourcePath = AnimationUtility.CalculateTransformPath(sourceBone, sourceCharacterRoot);
                if (!masterBoneMap.TryGetValue(sourcePath, out Transform rebound))
                {
                    Debug.LogWarning($"[JB Modular Character Builder] Missing matching bone '{sourcePath}' while merging piece '{smr.name}'.");
                    rebound = sourceBone;
                }

                reboundBones[i] = rebound;
            }

            smr.bones = reboundBones;

            if (smr.rootBone != null)
            {
                string rootBonePath = AnimationUtility.CalculateTransformPath(smr.rootBone, sourceCharacterRoot);
                if (masterBoneMap.TryGetValue(rootBonePath, out Transform reboundRootBone))
                    smr.rootBone = reboundRootBone;
            }
        }

        private void RebuildFromCurrentSource()
        {
            if (!sourceCharacters.Any(x => x != null)) return;
            var tempPreset = CreateInstance<ModularCharacterBuildPreset>();
            FillPresetData(tempPreset);
            LoadSourceCharacters();
            LoadPreset(tempPreset);
            DestroyImmediate(tempPreset);
        }

        private static GameObject ResolveSourceRoot(GameObject input) => input;

        private void RandomizeCharacter()
        {
            if (previewInstance == null) return;
            foreach (var category in categories)
            {
                if (!category.allowMultiple && category.isLocked) continue;
                if (category.pieces.Count == 0)
                {
                    category.selectedIndex = category.disallowNone ? 0 : -1;
                    category.selectedIndices.Clear();
                    continue;
                }
                if (!category.allowMultiple)
                    category.selectedIndex = category.disallowNone ? UnityEngine.Random.Range(0, category.pieces.Count) : UnityEngine.Random.Range(-1, category.pieces.Count);
                else
                    RandomizeMultiCategory(category);
            }
            ApplySelectionsToPreview();
        }

        private void RestoreAllBaseMaterials()
        {
            if (previewRoot == null) return;
            foreach (var state in slotRuntimeStates.Values)
            {
                if (state == null) continue;
                state.useCustom = false;
                state.family = state.baseFamily;
                state.existingMaterial = state.familyBaseMaterial;
                state.baseColor = ExtractBaseColor(state.familyBaseMaterial);
                state.emissionColor = ExtractEmissionColor(state.familyBaseMaterial);
            }
            ApplyAllRuntimeStatesToPreview();
            RebuildMaterialGroups();
            RefreshPresetModifiedFlag();
            MarkAllGuiDirty();
        }

        private void RemoveAtlasControllersFromPreview()
        {
            if (previewRoot == null) return;
            var legacyComponents = previewRoot.GetComponentsInChildren<MonoBehaviour>(true).Where(x => x != null && x.GetType().Name == "AtlasColorController").ToList();
            foreach (var comp in legacyComponents)
                DestroyImmediate(comp);
        }

        private void ScanPreview()
        {
            var existingCategoryFoldouts = categories.ToDictionary(x => x.name, x => x.foldout, StringComparer.OrdinalIgnoreCase);
            var existingCategoryLocks = categories.ToDictionary(x => x.name, x => x.isLocked, StringComparer.OrdinalIgnoreCase);
            categories.Clear();
            materialGroups.Clear();
            var categoryMap = new Dictionary<string, CategoryData>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in CoreCategoryOrder)
            {
                categoryMap[name] = new CategoryData
                {
                    name = name,
                    displayName = GetCategoryDisplayName(name),
                    allowMultiple = IsMultiSelectCategory(name),
                    disallowNone = IsRequiredCategory(name),
                    isCoreCategory = true,
                    foldout = existingCategoryFoldouts.TryGetValue(name, out bool foldout) ? foldout : true,
                    isLocked = existingCategoryLocks.TryGetValue(name, out bool isLocked) && isLocked
                };
            }

            var renderers = previewRoot.GetComponentsInChildren<Renderer>(true);
            var uniqueObjects = new HashSet<GameObject>();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var go = renderer.gameObject;
                if (go == null || !uniqueObjects.Add(go)) continue;
                Transform sourceRoot = GetDirectChildUnderPreviewRoot(go.transform);
                if (sourceRoot == null) continue;
                string objectName = go.name;
                if (!TryParseObjectName(objectName, out string categoryName, out string variantName, out bool isBodyPiece)) continue;

                if (!categoryMap.TryGetValue(categoryName, out var category))
                {
                    category = new CategoryData
                    {
                        name = categoryName,
                        displayName = GetCategoryDisplayName(categoryName),
                        allowMultiple = IsMultiSelectCategory(categoryName),
                        disallowNone = IsRequiredCategory(categoryName),
                        isCoreCategory = false,
                        foldout = existingCategoryFoldouts.TryGetValue(categoryName, out bool foldoutExtra) ? foldoutExtra : true,
                        isLocked = existingCategoryLocks.TryGetValue(categoryName, out bool isLockedExtra) && isLockedExtra
                    };
                    categoryMap[categoryName] = category;
                }

                string relativePath = GetRelativePath(sourceRoot, go.transform);
                string pieceKey = $"{sourceRoot.name}::{relativePath}";
                string variantDisplay = BeautifyVariantName(variantName);
                category.pieces.Add(new PieceData
                {
                    pieceKey = pieceKey,
                    sourceName = sourceRoot.name,
                    categoryName = categoryName,
                    categoryDisplayName = category.displayName,
                    objectName = go.name,
                    variantDisplayName = variantDisplay,
                    displayName = variantDisplay,
                    colorGroupDisplayName = isBodyPiece ? category.displayName : variantDisplay,
                    go = go,
                    renderers = go.GetComponentsInChildren<Renderer>(true).ToList(),
                    isBodyPiece = isBodyPiece
                });
            }

            foreach (var entry in categoryMap.Values)
            {
                entry.pieces = entry.pieces.OrderBy(p => p.objectName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.sourceName, StringComparer.OrdinalIgnoreCase).ToList();
                ApplyDuplicateDisplayNameDisambiguation(entry);
                if (!entry.allowMultiple)
                    entry.selectedIndex = entry.pieces.Count > 0 ? 0 : (entry.disallowNone ? 0 : -1);
                else
                    entry.selectedIndices = GetDefaultMultiSelectionIndices(entry);
                categories.Add(entry);
            }
            categories.Sort(CompareCategoryOrder);
        }

        private void CaptureInitialSlotStates()
        {
            slotRuntimeStates.Clear();
            var allRenderers = previewRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                if (renderer == null) continue;
                string rendererPath = CalculateRendererPath(renderer);
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    string slotKey = GetSlotKey(rendererPath, i);
                    slotRuntimeStates[slotKey] = new SlotRuntimeState
                    {
                        slotName = GetFriendlySlotName(i),
                        rendererPath = rendererPath,
                        slotIndex = i,
                        family = DetectMaterialFamily(mat),
                        baseFamily = DetectMaterialFamily(mat),
                        familyBaseMaterial = mat,
                        useCustom = false,
                        existingMaterial = mat,
                        baseColor = ExtractBaseColor(mat),
                        emissionColor = ExtractEmissionColor(mat),
                        isLocked = lockedMaterialSlots.Contains(slotKey)
                    };
                }
            }
        }

        private static Transform GetDirectChildUnderPreviewRoot(Transform current)
        {
            if (current == null) return null;
            Transform t = current;
            while (t.parent != null)
            {
                if (t.parent.name == TempPreviewName) return t;
                t = t.parent;
            }
            return null;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return string.Empty;
            if (root == target) return root.name;
            var stack = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }
            if (current == root) stack.Push(root.name);
            return string.Join("/", stack);
        }

        private static void ApplyDuplicateDisplayNameDisambiguation(CategoryData category)
        {
            var duplicates = category.pieces.GroupBy(p => p.displayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
            foreach (var group in duplicates)
            {
                foreach (var piece in group)
                {
                    piece.displayName = $"{piece.variantDisplayName} — {piece.sourceName}";
                    if (!piece.isBodyPiece)
                        piece.colorGroupDisplayName = piece.displayName;
                }
            }

            if (category.name.StartsWith("Body_", StringComparison.OrdinalIgnoreCase) || string.Equals(category.name, "Body", StringComparison.OrdinalIgnoreCase))
            {
                bool multiSource = category.pieces.Select(p => p.sourceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                foreach (var piece in category.pieces)
                    piece.colorGroupDisplayName = multiSource ? $"{category.displayName} — {piece.sourceName}" : category.displayName;
            }
        }

        private static bool IsRequiredCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return false;
            return string.Equals(categoryName, "Body", StringComparison.OrdinalIgnoreCase) || categoryName.StartsWith("Body_", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryParseObjectName(string objectName, out string categoryName, out string variantName, out bool isBodyPiece)
        {
            categoryName = string.Empty;
            variantName = string.Empty;
            isBodyPiece = false;
            if (string.IsNullOrWhiteSpace(objectName)) return false;
            string[] parts = objectName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            if (string.Equals(parts[0], "Body", StringComparison.OrdinalIgnoreCase))
            {
                isBodyPiece = true;
                if (parts.Length == 2)
                {
                    categoryName = "Body";
                    variantName = parts[1];
                    return true;
                }
                categoryName = $"Body_{parts[1]}";
                variantName = string.Join("_", parts.Skip(2));
                return !string.IsNullOrWhiteSpace(variantName);
            }

            categoryName = parts[0];
            variantName = string.Join("_", parts.Skip(1));
            return !string.IsNullOrWhiteSpace(categoryName) && !string.IsNullOrWhiteSpace(variantName);
        }

        private bool IsMultiSelectCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return false;
            if (BuiltInMultiSelectCategories.Contains(categoryName)) return true;
            foreach (string extra in ParseExtraMultiSelectCategories())
                if (string.Equals(extra, categoryName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private IEnumerable<string> ParseExtraMultiSelectCategories()
        {
            if (string.IsNullOrWhiteSpace(extraMultiSelectCategoriesCsv)) yield break;
            string[] parts = extraMultiSelectCategoriesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }

        private void ApplySelectionsToPreview()
        {
            if (previewInstance == null) return;
            foreach (var category in categories)
            {
                if (!category.allowMultiple)
                {
                    if (category.disallowNone && category.selectedIndex < 0 && category.pieces.Count > 0)
                        category.selectedIndex = 0;
                    for (int i = 0; i < category.pieces.Count; i++)
                        SetPieceActive(category.pieces[i], i == category.selectedIndex);
                }
                else
                {
                    var selected = new HashSet<int>(category.selectedIndices);
                    for (int i = 0; i < category.pieces.Count; i++)
                        SetPieceActive(category.pieces[i], selected.Contains(i));
                }
            }
            RebuildMaterialGroups();
            RefreshPresetModifiedFlag();
            MarkAllGuiDirty();
        }

        private void SetPieceActive(PieceData piece, bool active)
        {
            if (piece?.go == null) return;
            piece.go.SetActive(active);
        }

        private void RebuildMaterialGroups()
        {
            var existingFoldouts = materialGroups.ToDictionary(GetPieceGroupKey, x => x.foldout, StringComparer.OrdinalIgnoreCase);
            materialGroups.Clear();
            foreach (var category in categories)
            {
                IEnumerable<PieceData> activePieces = !category.allowMultiple
                    ? (category.selectedIndex >= 0 && category.selectedIndex < category.pieces.Count ? new[] { category.pieces[category.selectedIndex] } : Array.Empty<PieceData>())
                    : category.selectedIndices.Where(i => i >= 0 && i < category.pieces.Count).Select(i => category.pieces[i]);

                foreach (var piece in activePieces)
                {
                    var group = BuildPieceMaterialGroup(piece);
                    if (group != null && group.slots.Count > 0)
                    {
                        if (existingFoldouts.TryGetValue(GetPieceGroupKey(group), out bool foldout))
                            group.foldout = foldout;
                        materialGroups.Add(group);
                    }
                }
            }
        }

        private PieceMaterialGroup BuildPieceMaterialGroup(PieceData piece)
        {
            var group = new PieceMaterialGroup
            {
                pieceKey = piece.pieceKey,
                pieceObjectName = piece.objectName,
                displayName = piece.colorGroupDisplayName,
                isBodyGroup = piece.isBodyPiece
            };

            foreach (var renderer in piece.renderers)
            {
                if (renderer == null) continue;
                string rendererPath = CalculateRendererPath(renderer);
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slotKey = GetSlotKey(rendererPath, i);
                    if (!slotRuntimeStates.TryGetValue(slotKey, out var runtime))
                    {
                        Material currentMat = mats[i];
                        runtime = new SlotRuntimeState
                        {
                            slotName = GetFriendlySlotName(i),
                            rendererPath = rendererPath,
                            slotIndex = i,
                            family = DetectMaterialFamily(currentMat),
                            baseFamily = DetectMaterialFamily(currentMat),
                            familyBaseMaterial = currentMat,
                            useCustom = false,
                            existingMaterial = currentMat,
                            baseColor = ExtractBaseColor(currentMat),
                            emissionColor = ExtractEmissionColor(currentMat),
                            isLocked = lockedMaterialSlots.Contains(slotKey)
                        };
                        slotRuntimeStates[slotKey] = runtime;
                    }

                    group.slots.Add(new SlotBinding
                    {
                        slotKey = slotKey,
                        slotName = runtime.slotName,
                        rendererPath = rendererPath,
                        targetRenderer = renderer,
                        slotIndex = i,
                        family = runtime.family,
                        baseFamily = runtime.baseFamily,
                        familyBaseMaterial = runtime.familyBaseMaterial,
                        useCustom = runtime.useCustom,
                        existingMaterial = runtime.existingMaterial,
                        baseColor = runtime.baseColor,
                        emissionColor = runtime.emissionColor,
                        isLocked = runtime.isLocked
                    });
                }
            }
            return group;
        }

        private void SyncSlotRuntimeFromBinding(SlotBinding slot)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.slotKey)) return;
            if (!slotRuntimeStates.TryGetValue(slot.slotKey, out var runtime))
            {
                runtime = new SlotRuntimeState();
                slotRuntimeStates[slot.slotKey] = runtime;
            }
            runtime.slotName = slot.slotName;
            runtime.rendererPath = slot.rendererPath;
            runtime.slotIndex = slot.slotIndex;
            runtime.family = slot.family;
            runtime.baseFamily = slot.baseFamily;
            runtime.familyBaseMaterial = slot.familyBaseMaterial;
            runtime.useCustom = slot.useCustom;
            runtime.existingMaterial = slot.existingMaterial;
            runtime.baseColor = slot.baseColor;
            runtime.emissionColor = slot.emissionColor;
            runtime.isLocked = slot.isLocked;
        }

        private void ResetSingleSlot(SlotBinding slot)
        {
            if (slot == null) return;
            slot.useCustom = false;
            slot.family = slot.baseFamily;
            slot.existingMaterial = slot.familyBaseMaterial;
            slot.baseColor = ExtractBaseColor(slot.familyBaseMaterial);
            slot.emissionColor = ExtractEmissionColor(slot.familyBaseMaterial);
            SyncSlotRuntimeFromBinding(slot);
        }

        private void ApplyAllRuntimeStatesToPreview()
        {
            if (previewRoot == null) return;
            var renderers = previewRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                string rendererPath = CalculateRendererPath(renderer);
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slotKey = GetSlotKey(rendererPath, i);
                    if (!slotRuntimeStates.TryGetValue(slotKey, out var runtime)) continue;
                    Material desired = ResolvePreviewMaterial(runtime);
                    if (mats[i] != desired)
                    {
                        mats[i] = desired;
                        changed = true;
                    }
                }
                if (changed)
                {
                    Undo.RecordObject(renderer, "Apply Slot Materials");
                    renderer.sharedMaterials = mats;
                    EditorUtility.SetDirty(renderer);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
        }

        private void ApplySlotStateToRenderer(SlotBinding slot)
        {
            if (slot == null || slot.targetRenderer == null) return;
            SyncSlotRuntimeFromBinding(slot);
            var mats = slot.targetRenderer.sharedMaterials;
            if (mats == null || slot.slotIndex < 0 || slot.slotIndex >= mats.Length) return;
            Material targetMaterial = ResolvePreviewMaterial(slotRuntimeStates[slot.slotKey]);
            Undo.RecordObject(slot.targetRenderer, "Change Slot Material");
            mats[slot.slotIndex] = targetMaterial;
            slot.targetRenderer.sharedMaterials = mats;
            EditorUtility.SetDirty(slot.targetRenderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(slot.targetRenderer);
        }

        private Material ResolvePreviewMaterial(SlotRuntimeState state)
        {
            if (state == null) return null;
            if (!state.useCustom)
                return state.existingMaterial != null ? state.existingMaterial : state.familyBaseMaterial;
            Material baseMaterial = state.existingMaterial != null ? state.existingMaterial : state.familyBaseMaterial;
            if (baseMaterial == null) return state.existingMaterial;
            var key = new DerivedMaterialKey(state.family, baseMaterial, state.baseColor, state.family == ModularCharacterBuildPreset.MaterialFamily.Emissive ? state.emissionColor : Color.black);
            if (previewDerivedMaterials.TryGetValue(key, out var cached) && cached != null)
                return cached;
            Material derived = CreateDerivedMaterial(baseMaterial, state.family, state.baseColor, state.emissionColor);
            derived.hideFlags = HideFlags.HideAndDontSave;
            previewDerivedMaterials[key] = derived;
            return derived;
        }

        private Material CreateDerivedMaterial(Material baseMaterial, ModularCharacterBuildPreset.MaterialFamily family, Color baseColor, Color emissionColor)
        {
            var mat = new Material(baseMaterial);
            mat.name = GeneratePrettyMaterialName(family, baseColor, family == ModularCharacterBuildPreset.MaterialFamily.Emissive ? emissionColor : Color.black);
            foreach (string textureProp in BaseTexturePropertyCandidates)
                if (mat.HasProperty(textureProp)) mat.SetTexture(textureProp, null);

            string baseColorProp = GetFirstExistingProperty(mat, BaseColorPropertyCandidates);
            if (!string.IsNullOrEmpty(baseColorProp))
            {
                Color original = mat.GetColor(baseColorProp);
                Color finalBase = baseColor;
                finalBase.a = original.a;
                mat.SetColor(baseColorProp, finalBase);
            }
            if (family == ModularCharacterBuildPreset.MaterialFamily.Emissive)
            {
                string emissionProp = GetFirstExistingProperty(mat, EmissionColorPropertyCandidates);
                if (!string.IsNullOrEmpty(emissionProp))
                {
                    Color finalEmission = emissionColor;

                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(emissionProp, finalEmission);

                    // HDRP serialized/UI-facing emissive fields
                    if (mat.HasProperty("_UseEmissiveIntensity"))
                        mat.SetFloat("_UseEmissiveIntensity", 1f);

                    if (mat.HasProperty("_EmissiveIntensity"))
                        mat.SetFloat("_EmissiveIntensity", Mathf.Max(finalEmission.maxColorComponent, 1f));

                    if (mat.HasProperty("_EmissiveColorLDR"))
                    {
                        float max = Mathf.Max(finalEmission.r, Mathf.Max(finalEmission.g, finalEmission.b));
                        Color ldr = max > 0.0001f
                            ? new Color(finalEmission.r / max, finalEmission.g / max, finalEmission.b / max, 1f)
                            : Color.black;

                        mat.SetColor("_EmissiveColorLDR", ldr);
                    }

                    if (mat.HasProperty("_EmissiveExposureWeight"))
                        mat.SetFloat("_EmissiveExposureWeight", 1f);

                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                    // HDRP validation through reflection, so this still compiles in URP-only projects
                    TryApplyHdrpEmissiveState(mat, finalEmission);
                }
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return mat;
        }

        private static void TryApplyHdrpEmissiveState(Material mat, Color finalEmission)
        {
            if (mat == null || mat.shader == null)
                return;

            string shaderName = mat.shader.name ?? string.Empty;
            if (!shaderName.Contains("HDRP", StringComparison.OrdinalIgnoreCase) &&
                !shaderName.Contains("High Definition", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var hdMaterialType = Type.GetType(
                    "UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");

                if (hdMaterialType == null)
                    return;

                var setUseEmissiveIntensity = hdMaterialType.GetMethod(
                    "SetUseEmissiveIntensity",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var setEmissiveColor = hdMaterialType.GetMethod(
                    "SetEmissiveColor",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var validateMaterial = hdMaterialType.GetMethod(
                    "ValidateMaterial",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                // Keep separate HDRP UI/intensity workflow enabled
                setUseEmissiveIntensity?.Invoke(null, new object[] { mat, true });

                // Writes the HDRP emissive state in a valid way
                setEmissiveColor?.Invoke(null, new object[] { mat, finalEmission });

                // Ensures keywords / passes / internal HDRP state are valid
                validateMaterial?.Invoke(null, new object[] { mat });
            }
            catch
            {
                // Silent fallback: URP and non-HDRP paths already handled above
            }
        }

        private string CalculateRendererPath(Renderer renderer)
        {
            if (renderer == null || previewRoot == null) return string.Empty;
            return AnimationUtility.CalculateTransformPath(renderer.transform, previewRoot);
        }

        private static string GetSlotKey(string rendererPath, int slotIndex) => $"{rendererPath}|{slotIndex}";
        private static string GetMaterialSlotLockKey(string pieceKey, string rendererPath, int slotIndex) => $"{pieceKey}|{rendererPath}|{slotIndex}";
        private bool IsMaterialSlotLocked(string pieceKey, string rendererPath, int slotIndex) => lockedMaterialSlots.Contains(GetMaterialSlotLockKey(pieceKey, rendererPath, slotIndex));

        private void SetMaterialSlotLocked(PieceMaterialGroup group, SlotBinding slot, bool isLocked)
        {
            if (group == null || slot == null) return;
            string key = GetMaterialSlotLockKey(group.pieceKey, slot.rendererPath, slot.slotIndex);
            if (string.IsNullOrWhiteSpace(key)) return;
            if (isLocked) lockedMaterialSlots.Add(key); else lockedMaterialSlots.Remove(key);
            if (slotRuntimeStates.TryGetValue(slot.slotKey, out var runtime)) runtime.isLocked = isLocked;
        }

        private bool IsMultiCategoryPieceLocked(PieceData piece)
        {
            if (piece == null || string.IsNullOrWhiteSpace(piece.pieceKey)) return false;
            return lockedMultiCategoryPieces.Contains(piece.pieceKey);
        }

        private void SetMultiCategoryPieceLocked(PieceData piece, bool isLocked)
        {
            if (piece == null || string.IsNullOrWhiteSpace(piece.pieceKey)) return;
            if (isLocked) lockedMultiCategoryPieces.Add(piece.pieceKey); else lockedMultiCategoryPieces.Remove(piece.pieceKey);
        }

        private void SetAllMultiCategoryPieceLocks(CategoryData category, bool isLocked)
        {
            if (category == null) return;
            foreach (var piece in category.pieces)
                SetMultiCategoryPieceLocked(piece, isLocked);
        }

        private static string GetPieceGroupKey(PieceMaterialGroup group) => group == null ? string.Empty : $"{group.pieceKey}|{group.displayName}";

        private void SetAllCategoryLocks(bool isLocked)
        {
            foreach (var category in categories)
            {
                if (!category.allowMultiple)
                {
                    category.isLocked = isLocked;
                    continue;
                }
                foreach (var piece in category.pieces)
                    SetMultiCategoryPieceLocked(piece, isLocked);
            }
            MarkAllGuiDirty();
        }

        private void SetAllMaterialSlotLocks(bool isLocked)
        {
            foreach (var group in materialGroups)
            {
                if (group == null) continue;
                foreach (var slot in group.slots)
                {
                    if (slot == null) continue;
                    slot.isLocked = isLocked;
                    SetMaterialSlotLocked(group, slot, isLocked);
                    SyncSlotRuntimeFromBinding(slot);
                }
            }
            MarkAllGuiDirty();
        }

        private void SavePresetOverwrite()
        {
            if (presetAsset == null) return;
            FillPresetData(presetAsset);
            EditorUtility.SetDirty(presetAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(presetAsset);
            MarkPresetAsSavedOrLoaded();
        }

        private void SavePresetAs()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Character Build Preset", "CharacterBuildPreset", "asset", "Choose where to save the preset asset.", GetGeneratedPresetsFolderPath());
            if (string.IsNullOrWhiteSpace(path)) return;
            var preset = CreateInstance<ModularCharacterBuildPreset>();
            FillPresetData(preset);
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            presetAsset = preset;
            EditorGUIUtility.PingObject(preset);
            Selection.activeObject = preset;
            MarkPresetAsSavedOrLoaded();
        }

        private void FillPresetData(ModularCharacterBuildPreset preset)
        {
            preset.sourceCharacterNames = sourceCharacters.Where(x => x != null).Select(x => x.name).ToList();
            preset.categorySelections.Clear();
            preset.pieceMaterials.Clear();

            foreach (var category in categories)
            {
                var entry = new ModularCharacterBuildPreset.CategorySelection
                {
                    categoryName = category.name,
                    allowMultiple = category.allowMultiple,
                    isLocked = category.isLocked
                };
                if (!category.allowMultiple)
                {
                    entry.selectedPieceKey = (category.selectedIndex >= 0 && category.selectedIndex < category.pieces.Count) ? category.pieces[category.selectedIndex].pieceKey : string.Empty;
                }
                else
                {
                    entry.selectedPieceKeys = category.selectedIndices.Where(i => i >= 0 && i < category.pieces.Count).Select(i => category.pieces[i].pieceKey).ToList();
                    entry.lockedPieceKeys = category.pieces.Where(IsMultiCategoryPieceLocked).Select(p => p.pieceKey).ToList();
                }
                preset.categorySelections.Add(entry);
            }

            foreach (var group in materialGroups)
            {
                var pieceMaterial = new ModularCharacterBuildPreset.PieceMaterialState { pieceKey = group.pieceKey, isBodyGroup = group.isBodyGroup };
                foreach (var slot in group.slots)
                {
                    pieceMaterial.slots.Add(new ModularCharacterBuildPreset.SlotMaterialState
                    {
                        slotName = slot.slotName,
                        rendererPath = slot.rendererPath,
                        slotIndex = slot.slotIndex,
                        family = slot.family,
                        isLocked = slot.isLocked,
                        useCustom = slot.useCustom,
                        existingMaterial = slot.existingMaterial,
                        baseColor = slot.baseColor,
                        emissionColor = slot.emissionColor
                    });
                }
                preset.pieceMaterials.Add(pieceMaterial);
            }
        }

        private void LoadPreset(ModularCharacterBuildPreset preset)
        {
            if (preset == null || previewInstance == null) return;
            lockedMultiCategoryPieces.Clear();
            lockedMaterialSlots.Clear();

            foreach (var cat in categories)
            {
                var presetCat = preset.categorySelections.FirstOrDefault(x => string.Equals(x.categoryName, cat.name, StringComparison.OrdinalIgnoreCase));
                if (presetCat == null) continue;
                if (!cat.allowMultiple)
                {
                    cat.isLocked = presetCat.isLocked;
                    int index = cat.pieces.FindIndex(p => string.Equals(p.pieceKey, presetCat.selectedPieceKey, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) cat.selectedIndex = index;
                    else if (cat.disallowNone && cat.pieces.Count > 0) cat.selectedIndex = 0;
                    else cat.selectedIndex = -1;
                }
                else
                {
                    cat.selectedIndices = cat.pieces.Select((piece, index) => new { piece, index })
                        .Where(x => presetCat.selectedPieceKeys.Any(key => string.Equals(key, x.piece.pieceKey, StringComparison.OrdinalIgnoreCase)))
                        .Select(x => x.index)
                        .ToList();

                    foreach (var piece in cat.pieces)
                    {
                        bool locked = presetCat.lockedPieceKeys.Any(key => string.Equals(key, piece.pieceKey, StringComparison.OrdinalIgnoreCase));
                        SetMultiCategoryPieceLocked(piece, locked);
                    }
                }
            }

            ApplySelectionsToPreview();

            foreach (var pieceMaterial in preset.pieceMaterials)
            {
                var targetGroup = materialGroups.FirstOrDefault(x => string.Equals(x.pieceKey, pieceMaterial.pieceKey, StringComparison.OrdinalIgnoreCase) && x.isBodyGroup == pieceMaterial.isBodyGroup);
                if (targetGroup == null) continue;
                foreach (var slot in targetGroup.slots)
                {
                    ModularCharacterBuildPreset.SlotMaterialState presetSlot = null;
                    if (!string.IsNullOrWhiteSpace(slot.rendererPath))
                    {
                        presetSlot = pieceMaterial.slots.FirstOrDefault(x => string.Equals(x.rendererPath, slot.rendererPath, StringComparison.OrdinalIgnoreCase) && x.slotIndex == slot.slotIndex);
                    }
                    if (presetSlot == null)
                    {
                        presetSlot = pieceMaterial.slots.FirstOrDefault(x => string.Equals(x.slotName, slot.slotName, StringComparison.OrdinalIgnoreCase));
                    }
                    if (presetSlot == null) continue;

                    slot.family = presetSlot.family;
                    slot.baseFamily = DetectMaterialFamily(slot.familyBaseMaterial);
                    slot.isLocked = presetSlot.isLocked;
                    SetMaterialSlotLocked(targetGroup, slot, slot.isLocked);
                    slot.useCustom = presetSlot.useCustom;
                    slot.existingMaterial = presetSlot.existingMaterial != null ? presetSlot.existingMaterial : slot.familyBaseMaterial;
                    slot.baseColor = presetSlot.baseColor;
                    slot.emissionColor = presetSlot.emissionColor;
                    SyncSlotRuntimeFromBinding(slot);
                    ApplySlotStateToRenderer(slot);
                }
            }

            MarkPresetAsSavedOrLoaded();
            MarkAllGuiDirty();
        }

        private void SavePrefab()
        {
            if (previewInstance == null)
            {
                EditorUtility.DisplayDialog("João Baltieri 3D", "There is no preview instance to save.", "OK");
                return;
            }

            string folderPath = GetGeneratedPrefabsFolderPath();
            if (saveFolder != null)
            {
                folderPath = AssetDatabase.GetAssetPath(saveFolder);
                if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                {
                    EditorUtility.DisplayDialog("João Baltieri 3D", "Choose a valid folder inside Assets.", "OK");
                    return;
                }
            }

            lastExportGeneratedMaterialsCount = 0;
            lastExportReusedMaterialsCount = 0;
            string materialsFolderPath = GetGeneratedMaterialsFolderPath(true);
            if (string.IsNullOrWhiteSpace(materialsFolderPath) || !AssetDatabase.IsValidFolder(materialsFolderPath))
            {
                EditorUtility.DisplayDialog("João Baltieri 3D", "Failed to create the JB Materials folder.", "OK");
                return;
            }

            string safeName = string.IsNullOrWhiteSpace(prefabName) ? "Character_Custom" : prefabName.Trim();
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{safeName}.prefab");
            ApplySelectionsToPreview();

            GameObject exportInstance = null;
            UnityEngine.Object savedPrefab = null;
            try
            {
                exportInstance = Instantiate(previewInstance);
                exportInstance.name = safeName;
                exportInstance.hideFlags = HideFlags.HideAndDontSave;
                ApplyExportMaterials(exportInstance, materialsFolderPath, safeName);
                CleanupPreviewForSave(exportInstance);
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(exportInstance, prefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                if (exportInstance != null)
                    DestroyImmediate(exportInstance);
            }

            if (savedPrefab != null)
            {
                EditorGUIUtility.PingObject(savedPrefab);
                Selection.activeObject = savedPrefab;
                JBBuilderSuccessDialogWindow.ShowWindow(
                    "Export Completed",
                    "✅ Character prefab exported successfully",
                    prefabPath,
                    GetGeneratedMaterialsFolderPath(false),
                    lastExportGeneratedMaterialsCount,
                    lastExportReusedMaterialsCount);
            }
            else
            {
                EditorUtility.DisplayDialog("João Baltieri 3D", "Failed to save prefab.", "OK");
            }
        }

        private string GetGeneratedMaterialsFolderPath(bool createIfMissing)
        {
            string generatedPath = GetToolRootFolderPath() + "/" + GeneratedMaterialsFolderName;
            if (AssetDatabase.IsValidFolder(generatedPath)) return generatedPath;
            if (!createIfMissing) return generatedPath;
            EnsureFolderExists(generatedPath);
            AssetDatabase.Refresh();
            return generatedPath;
        }

        private string GetGeneratedPrefabsFolderPath()
        {
            string folderPath = GetToolRootFolderPath() + "/" + GeneratedPrefabsFolderName;
            EnsureFolderExists(folderPath);
            return folderPath.Replace("\\", "/");
        }

        private string GetGeneratedPresetsFolderPath()
        {
            string folderPath = GetToolRootFolderPath() + "/" + GeneratedPresetsFolderName;
            EnsureFolderExists(folderPath);
            return folderPath.Replace("\\", "/");
        }

        private string GetToolRootFolderPath()
        {
            MonoScript script = MonoScript.FromScriptableObject(this);
            if (script != null)
            {
                string scriptPath = AssetDatabase.GetAssetPath(script).Replace("\\", "/");
                string directory = System.IO.Path.GetDirectoryName(scriptPath)?.Replace("\\", "/") ?? "Assets";
                if (directory.EndsWith("/Editor", StringComparison.OrdinalIgnoreCase))
                    directory = directory.Substring(0, directory.Length - "/Editor".Length);
                if (directory.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                    return directory;
            }
            return "Assets/JB Modular Character Builder";
        }

        private void EnsureGeneratedFoldersExist()
        {
            EnsureFolderExists(GetToolRootFolderPath());
            EnsureFolderExists(GetGeneratedPrefabsFolderPath());
            EnsureFolderExists(GetGeneratedPresetsFolderPath());
            EnsureFolderExists(GetToolRootFolderPath() + "/" + GeneratedMaterialsFolderName);
            AssetDatabase.Refresh();
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            folderPath = folderPath.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string[] parts = folderPath.Split('/');
            if (parts.Length == 0) return;
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private void ApplyExportMaterials(GameObject exportRoot, string materialsFolderPath, string safeName)
        {
            if (exportRoot == null) return;
            var renderers = exportRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, exportRoot.transform);
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slotKey = GetSlotKey(rendererPath, i);
                    if (!slotRuntimeStates.TryGetValue(slotKey, out var runtime)) continue;
                    Material exportMat = ResolveExportMaterial(runtime, materialsFolderPath, safeName);
                    if (mats[i] != exportMat)
                    {
                        mats[i] = exportMat;
                        changed = true;
                    }
                }
                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        private Material ResolveExportMaterial(SlotRuntimeState state, string materialsFolderPath, string safeName)
        {
            if (state == null) return null;
            if (!state.useCustom)
                return state.existingMaterial != null ? state.existingMaterial : state.familyBaseMaterial;
            Material baseMaterial = state.existingMaterial != null ? state.existingMaterial : state.familyBaseMaterial;
            if (baseMaterial == null) return state.existingMaterial;

            var key = new DerivedMaterialKey(state.family, baseMaterial, state.baseColor, state.family == ModularCharacterBuildPreset.MaterialFamily.Emissive ? state.emissionColor : Color.black);
            if (exportMaterialPaths.TryGetValue(key, out var existingPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<Material>(existingPath);
                if (existing != null)
                {
                    lastExportReusedMaterialsCount++;
                    return existing;
                }
            }

            string fileName = GeneratePrettyMaterialName(state.family, state.baseColor, state.family == ModularCharacterBuildPreset.MaterialFamily.Emissive ? state.emissionColor : Color.black);
            string materialPath = AssetDatabase.GenerateUniqueAssetPath($"{materialsFolderPath}/{safeName}_{fileName}.mat");
            var derived = CreateDerivedMaterial(baseMaterial, state.family, state.baseColor, state.emissionColor);
            derived.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(derived, materialPath);
            exportMaterialPaths[key] = materialPath;
            lastExportGeneratedMaterialsCount++;
            return derived;
        }

        private void NormalizeExportHierarchyToSingleRig(Transform root, string rigName)
        {
            if (root == null)
                return;

            var topLevelChildren = new List<Transform>();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null)
                    topLevelChildren.Add(child);
            }

            if (topLevelChildren.Count == 0)
                return;

            Transform rigRoot = topLevelChildren[0];
            if (rigRoot == null)
                return;

            rigRoot.name = string.IsNullOrWhiteSpace(rigName) ? "Rig" : rigName;
            rigRoot.SetParent(root, true);

            for (int i = 1; i < topLevelChildren.Count; i++)
            {
                Transform child = topLevelChildren[i];
                if (child == null)
                    continue;

                while (child.childCount > 0)
                {
                    Transform grandChild = child.GetChild(0);
                    if (grandChild == null)
                        break;

                    grandChild.SetParent(rigRoot, true);
                }

                DestroyImmediate(child.gameObject);
            }
        }

        private void CleanupPreviewForSave(GameObject root)
        {
            if (root == null) return;
            root.name = string.IsNullOrWhiteSpace(prefabName) ? root.name : prefabName.Trim();
            root.hideFlags = HideFlags.None;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.None;
            RemoveInactiveChildrenRecursive(root.transform, true);
            RemoveLegacyBuilderComponents(root);
            string safeRootName = string.IsNullOrWhiteSpace(root.name) ? "Character_Custom" : root.name.Trim();
            NormalizeExportHierarchyToSingleRig(root.transform, safeRootName + "_Rig");
            ZeroMeshTransforms(root.transform, true);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
        }

        private void RemoveLegacyBuilderComponents(GameObject root)
        {
            if (root == null) return;
            var monoBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true).Where(x => x != null && x.GetType().Name == "AtlasColorController").ToList();
            foreach (var mb in monoBehaviours)
                DestroyImmediate(mb);
        }

        private void RemoveInactiveChildrenRecursive(Transform parent, bool isRoot = false)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child == null) continue;
                if (!child.gameObject.activeSelf)
                {
                    DestroyImmediate(child.gameObject);
                    continue;
                }
                RemoveInactiveChildrenRecursive(child, false);
            }
        }

        private void ZeroMeshTransforms(Transform current, bool isRoot = false)
        {
            if (current == null) return;
            if (!isRoot && ShouldZeroTransform(current))
            {
                current.localPosition = Vector3.zero;
                current.localRotation = Quaternion.identity;
                current.localScale = Vector3.one;
            }
            for (int i = 0; i < current.childCount; i++)
                ZeroMeshTransforms(current.GetChild(i), false);
        }

        private bool ShouldZeroTransform(Transform t)
        {
            if (t == null) return false;
            if (t.GetComponent<Renderer>() != null) return true;
            if (t.GetComponent<MeshFilter>() != null) return true;
            if (t.GetComponent<SkinnedMeshRenderer>() != null) return true;
            return false;
        }

        private void SelectPreviewInstance()
        {
            if (previewInstance == null) return;
            Selection.activeGameObject = previewInstance;
            EditorGUIUtility.PingObject(previewInstance);
        }

        private void SetAllCategoryFoldouts(bool expanded)
        {
            foreach (var category in categories)
                category.foldout = expanded;
        }

        private void SetAllMaterialFoldouts(bool expanded)
        {
            foreach (var group in materialGroups)
                group.foldout = expanded;
        }

        private void ClearPreviewInstance()
        {
            if (previewInstance != null)
            {
                DestroyImmediate(previewInstance);
                previewInstance = null;
                previewRoot = null;
            }
        }

        private void DestroyPreviewDerivedMaterials()
        {
            foreach (var kv in previewDerivedMaterials)
                if (kv.Value != null) DestroyImmediate(kv.Value);
            previewDerivedMaterials.Clear();
        }

        private void ClearAll()
        {
            ClearPreviewInstance();
            DestroyPreviewDerivedMaterials();
            categories.Clear();
            materialGroups.Clear();
            slotRuntimeStates.Clear();
            lockedMaterialSlots.Clear();
            lockedMultiCategoryPieces.Clear();
            exportMaterialPaths.Clear();
            if (sourceCharacters.Count == 0) sourceCharacters.Add(null);
            SyncToolkitFieldsFromState();
            MarkAllGuiDirty();
        }

        private static ModularCharacterBuildPreset.MaterialFamily DetectMaterialFamily(Material mat)
        {
            if (mat == null)
                return ModularCharacterBuildPreset.MaterialFamily.Unknown;

            string name = (mat.name ?? string.Empty).ToLowerInvariant();
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;

            // 1. Transparent first
            if (name.Contains("glass") || shaderName.Contains("glass"))
                return ModularCharacterBuildPreset.MaterialFamily.Glass;

            if (name.Contains("transparent") || shaderName.Contains("transparent"))
                return ModularCharacterBuildPreset.MaterialFamily.Glass;

            if (IsMaterialTransparent(mat))
                return ModularCharacterBuildPreset.MaterialFamily.Glass;

            // 2. Metallic second
            if (name.Contains("metallic") || shaderName.Contains("metallic"))
                return ModularCharacterBuildPreset.MaterialFamily.Metal;

            if (name.Contains("metal") || shaderName.Contains("metal"))
                return ModularCharacterBuildPreset.MaterialFamily.Metal;

            if (IsMaterialMetallic(mat))
                return ModularCharacterBuildPreset.MaterialFamily.Metal;

            // 3. Emissive third
            if (IsMaterialEmissive(mat))
                return ModularCharacterBuildPreset.MaterialFamily.Emissive;

            // 4. Fallback
            return ModularCharacterBuildPreset.MaterialFamily.Opaque;
        }

        private static bool IsMaterialTransparent(Material mat)
        {
            if (mat == null) return false;
            string surfaceProp = GetFirstExistingProperty(mat, SurfaceTypePropertyCandidates);
            if (!string.IsNullOrEmpty(surfaceProp))
            {
                float value = mat.GetFloat(surfaceProp);
                if (value > 0.5f) return true;
            }
            if (mat.renderQueue >= 3000) return true;
            if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || mat.IsKeywordEnabled("_ALPHABLEND_ON")) return true;
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;
            if (shaderName.Contains("transparent")) return true;
            string baseColorProp = GetFirstExistingProperty(mat, BaseColorPropertyCandidates);
            if (!string.IsNullOrEmpty(baseColorProp))
            {
                Color c = mat.GetColor(baseColorProp);
                if (c.a < 0.999f) return true;
            }
            return false;
        }

        private static bool IsMaterialEmissive(Material mat)
        {
            if (mat == null)
                return false;

            Color emissionColor = Color.black;
            bool hasEmission = false;

            if (mat.HasProperty("_EmissionColor"))
            {
                emissionColor = mat.GetColor("_EmissionColor");
                hasEmission = true;
            }

            if (!hasEmission)
                return false;

            float max = Mathf.Max(emissionColor.r, Mathf.Max(emissionColor.g, emissionColor.b));

            // Needs a real visible emission value
            if (max <= 0.2f)
                return false;

            string name = (mat.name ?? string.Empty).ToLowerInvariant();
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;

            // Strong explicit hint = emissive
            if (name.Contains("emissive") || shaderName.Contains("emissive"))
                return true;

            // Avoid false positives on obvious opaque/metal/glass materials
            if (name.Contains("opaque") || shaderName.Contains("opaque"))
                return false;

            if (name.Contains("metal") || shaderName.Contains("metal"))
                return false;

            if (name.Contains("glass") || shaderName.Contains("glass"))
                return false;

            if (name.Contains("transparent") || shaderName.Contains("transparent"))
                return false;

            // If it has real emission and does not look like a standard opaque/metal/glass material,
            // treat it as emissive.
            return true;
        }

        private static bool IsMaterialMetallic(Material mat)
        {
            if (mat == null) return false;
            string metallicProp = GetFirstExistingProperty(mat, MetallicPropertyCandidates);
            if (!string.IsNullOrEmpty(metallicProp) && mat.GetFloat(metallicProp) > 0.35f) return true;
            string smoothnessProp = GetFirstExistingProperty(mat, SmoothnessPropertyCandidates);
            if (!string.IsNullOrEmpty(smoothnessProp) && mat.GetFloat(smoothnessProp) > 0.8f)
            {
                string name = mat.name.ToLowerInvariant();
                string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;
                if (name.Contains("metal") || shaderName.Contains("metal")) return true;
            }
            return false;
        }

        private static Color ExtractBaseColor(Material mat)
        {
            if (mat == null) return Color.white;
            string prop = GetFirstExistingProperty(mat, BaseColorPropertyCandidates);
            return !string.IsNullOrEmpty(prop) ? mat.GetColor(prop) : Color.white;
        }

        private static Color ExtractEmissionColor(Material mat)
        {
            if (mat == null) return Color.black;
            string prop = GetFirstExistingProperty(mat, EmissionColorPropertyCandidates);
            return !string.IsNullOrEmpty(prop) ? mat.GetColor(prop) : Color.black;
        }

        private static string GetFirstExistingProperty(Material mat, string[] candidates)
        {
            if (mat == null || candidates == null) return null;
            for (int i = 0; i < candidates.Length; i++)
                if (mat.HasProperty(candidates[i]))
                    return candidates[i];
            return null;
        }

        private static int QuantizeColorChannel(float value) => Mathf.RoundToInt(value * 1000f);
        private static float MaxRgb(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        private static Color PreserveEmissionIntensity(Color baseEmission, Color requestedEmission)
        {
            if (MaxRgb(requestedEmission) <= 0.0001f) return Color.black;
            return requestedEmission;
        }

        private static string GetFamilyDisplayName(ModularCharacterBuildPreset.MaterialFamily family)
        {
            return family switch
            {
                ModularCharacterBuildPreset.MaterialFamily.Opaque => "Opaque",
                ModularCharacterBuildPreset.MaterialFamily.Metal => "Metal",
                ModularCharacterBuildPreset.MaterialFamily.Glass => "Glass",
                ModularCharacterBuildPreset.MaterialFamily.Emissive => "Emissive",
                _ => "Unknown"
            };
        }

        private static string GeneratePrettyMaterialName(ModularCharacterBuildPreset.MaterialFamily family, Color baseColor, Color emissionColor)
        {
            string familyName = GetFamilyDisplayName(family);
            string baseName = DescribeColor(baseColor);
            return family == ModularCharacterBuildPreset.MaterialFamily.Emissive
                ? $"JB_{familyName}_{baseName}_Em_{DescribeColor(emissionColor)}"
                : $"JB_{familyName}_{baseName}";
        }

        private static string DescribeColor(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            string hueName;
            if (s < 0.08f)
            {
                hueName = v < 0.2f ? "Black" : v < 0.45f ? "Gray" : v < 0.85f ? "Silver" : "White";
            }
            else
            {
                float degrees = h * 360f;
                if (degrees < 15f || degrees >= 345f) hueName = "Red";
                else if (degrees < 40f) hueName = "Orange";
                else if (degrees < 65f) hueName = "Yellow";
                else if (degrees < 150f) hueName = "Green";
                else if (degrees < 200f) hueName = "Cyan";
                else if (degrees < 255f) hueName = "Blue";
                else if (degrees < 290f) hueName = "Purple";
                else if (degrees < 345f) hueName = "Pink";
                else hueName = "Red";
            }

            string tone = v < 0.22f ? "Deep" : v < 0.45f ? "Dark" : s < 0.15f ? "Soft" : v > 0.85f ? "Bright" : "Mid";
            return $"{tone}_{hueName}";
        }

        private static string GetCategoryDisplayName(string categoryName)
        {
            if (string.Equals(categoryName, "Body", StringComparison.OrdinalIgnoreCase)) return "Body";
            if (string.Equals(categoryName, "HeadTop", StringComparison.OrdinalIgnoreCase)) return "Head";
            if (string.Equals(categoryName, "Hair", StringComparison.OrdinalIgnoreCase)) return "Hair";
            if (string.Equals(categoryName, "Face", StringComparison.OrdinalIgnoreCase)) return "Face";
            if (string.Equals(categoryName, "Torso", StringComparison.OrdinalIgnoreCase)) return "Torso";
            if (string.Equals(categoryName, "TorsoOuter", StringComparison.OrdinalIgnoreCase)) return "Torso Outer";
            if (string.Equals(categoryName, "TorsoGear", StringComparison.OrdinalIgnoreCase)) return "Torso Gear";
            if (string.Equals(categoryName, "Arms", StringComparison.OrdinalIgnoreCase)) return "Arms";
            if (string.Equals(categoryName, "ArmsOuter", StringComparison.OrdinalIgnoreCase)) return "Arms Outer";
            if (string.Equals(categoryName, "ArmsGear", StringComparison.OrdinalIgnoreCase)) return "Arms Gear";
            if (string.Equals(categoryName, "Legs", StringComparison.OrdinalIgnoreCase)) return "Legs";
            if (string.Equals(categoryName, "LegsOuter", StringComparison.OrdinalIgnoreCase)) return "Legs Outer";
            if (string.Equals(categoryName, "LegsGear", StringComparison.OrdinalIgnoreCase)) return "Legs Gear";
            if (string.Equals(categoryName, "Feet", StringComparison.OrdinalIgnoreCase)) return "Feet";
            if (categoryName.StartsWith("Body_", StringComparison.OrdinalIgnoreCase))
                return "Body " + BeautifyVariantName(categoryName.Substring("Body_".Length));
            return BeautifyCategoryName(categoryName);
        }

        private static string BeautifyCategoryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string result = value.Trim();
            var chars = new List<char>(result.Length * 2);
            for (int i = 0; i < result.Length; i++)
            {
                char c = result[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(result[i - 1]))
                    chars.Add(' ');
                chars.Add(c);
            }
            return BeautifyVariantName(new string(chars.ToArray()));
        }

        private static string BeautifyVariantName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string result = value.Replace("_", " ").Trim();
            while (result.Contains("  ")) result = result.Replace("  ", " ");
            var words = result.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(words[i])) continue;
                if (int.TryParse(words[i], out _)) continue;
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }
    }

    internal class JBBuilderSuccessDialogWindow : EditorWindow
    {
        private string dialogTitle;
        private string headline;
        private string prefabPath;
        private string materialsFolder;
        private int generatedCount;
        private int reusedCount;
        private Vector2 scroll;

        public static void ShowWindow(string dialogTitle, string headline, string prefabPath, string materialsFolder, int generatedCount, int reusedCount)
        {
            var window = CreateInstance<JBBuilderSuccessDialogWindow>();
            window.dialogTitle = dialogTitle ?? "Success";
            window.headline = headline ?? "✅ Done";
            window.prefabPath = prefabPath ?? string.Empty;
            window.materialsFolder = materialsFolder ?? string.Empty;
            window.generatedCount = generatedCount;
            window.reusedCount = reusedCount;
            window.titleContent = new GUIContent("✅ Success");
            window.minSize = new Vector2(520f, 320f);
            window.maxSize = new Vector2(800f, 520f);
            window.position = new Rect(Mathf.Max(100f, Screen.currentResolution.width * 0.35f), Mathf.Max(100f, Screen.currentResolution.height * 0.25f), 560f, 360f);
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField(dialogTitle, EditorStyles.boldLabel);
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(headline, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.Space(6f);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(prefabPath, EditorStyles.textField, GUILayout.Height(36f));
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Materials Folder", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(materialsFolder, EditorStyles.textField, GUILayout.Height(36f));
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• Generated Derived Materials: " + generatedCount, EditorStyles.label);
                EditorGUILayout.LabelField("• Reused Derived Materials: " + reusedCount, EditorStyles.label);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy Prefab Path", GUILayout.Height(24f), GUILayout.Width(110f)))
                    EditorGUIUtility.systemCopyBuffer = prefabPath ?? string.Empty;
                if (GUILayout.Button("Copy Materials Folder", GUILayout.Height(24f), GUILayout.Width(130f)))
                    EditorGUIUtility.systemCopyBuffer = materialsFolder ?? string.Empty;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("OK", GUILayout.Height(26f), GUILayout.Width(80f)))
                    Close();
            }
            EditorGUILayout.Space(8f);
        }
    }
}
