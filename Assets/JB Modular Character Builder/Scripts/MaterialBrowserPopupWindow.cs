using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JB.ModularCharacterBuilder
{
    public class MaterialBrowserPopupWindow : EditorWindow
    {
        private const float Padding = 8f;
        private const float SearchHeight = 22f;
        private const float ThumbSize = 72f;
        private const float TileWidth = 84f;
        private const float TileHeight = 112f;
        private const float HeaderHeight = 28f;
        private const float TabsHeight = 24f;
        private const int MaxRecentItems = 18;

        private const string FavoritesPrefsKey = "JB.ModularCharacterBuilder.MaterialFavorites";
        private const string RecentPrefsKey = "JB.ModularCharacterBuilder.MaterialRecent";

        private readonly List<Material> filteredMaterials = new();
        private readonly HashSet<string> favoriteGuids = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> recentGuids = new();

        private Vector2 materialsScroll;
        private Vector2 recentScroll;
        private string searchText = string.Empty;

        private Material currentMaterial;
        private Material hoveredMaterial;
        private ModularCharacterBuildPreset.MaterialFamily baseFamily;
        private ModularCharacterBuildPreset.MaterialFamily browsingFamily;
        private Action<Material> onPick;

        private Editor previewEditor;
        private string folderFilterPath = string.Empty;
        private DefaultAsset selectedFolderAsset;

        private GUIStyle headerStyle;
        private GUIStyle miniLabelStyle;
        private GUIStyle tileStyle;
        private GUIStyle tileSelectedStyle;
        private GUIStyle tileLabelStyle;
        private GUIStyle titleBarStyle;
        private GUIStyle familySectionStyle;
        private GUIStyle recentCardStyle;

        private bool isDraggingWindow;
        private Vector2 dragMouseStart;
        private Rect dragWindowStart;

        private enum ViewMode
        {
            All = 0,
            Favorites = 1
        }

        private ViewMode currentViewMode = ViewMode.All;

        private static readonly ModularCharacterBuildPreset.MaterialFamily[] FamilyTabs =
        {
            ModularCharacterBuildPreset.MaterialFamily.Opaque,
            ModularCharacterBuildPreset.MaterialFamily.Transparent,
            ModularCharacterBuildPreset.MaterialFamily.Metallic,
            ModularCharacterBuildPreset.MaterialFamily.Emissive
        };

        public static void ShowWindow(
            Rect activatorRect,
            ModularCharacterBuildPreset.MaterialFamily family,
            Material currentMaterial,
            Action<Material> onPick,
            string folderFilterPath = null)
        {
            MaterialBrowserPopupWindow window = CreateInstance<MaterialBrowserPopupWindow>();
            window.baseFamily = NormalizeBrowsableFamily(family);
            window.browsingFamily = NormalizeBrowsableFamily(family);
            window.currentMaterial = currentMaterial;
            window.onPick = onPick;
            window.folderFilterPath = NormalizeFolderPath(folderFilterPath);
            window.selectedFolderAsset = !string.IsNullOrWhiteSpace(window.folderFilterPath)
                ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(window.folderFilterPath)
                : null;

            window.LoadPrefs();
            window.titleContent = new GUIContent("Material Browser");
            window.minSize = new Vector2(820f, 700f);
            window.RefreshFilteredMaterials();

            Vector2 size = new Vector2(900f, 760f);

            if (activatorRect.width > 0f || activatorRect.height > 0f)
            {
                window.ShowAsDropDown(activatorRect, size);
            }
            else
            {
                Rect startRect = new Rect(
                    Mathf.Max(100f, Screen.currentResolution.width * 0.2f),
                    Mathf.Max(100f, Screen.currentResolution.height * 0.15f),
                    size.x,
                    size.y);

                window.position = startRect;
                window.ShowUtility();
            }
        }

        private static ModularCharacterBuildPreset.MaterialFamily NormalizeBrowsableFamily(ModularCharacterBuildPreset.MaterialFamily family)
        {
            return family switch
            {
                ModularCharacterBuildPreset.MaterialFamily.Glass => ModularCharacterBuildPreset.MaterialFamily.Transparent,
                ModularCharacterBuildPreset.MaterialFamily.Metal => ModularCharacterBuildPreset.MaterialFamily.Metallic,
                ModularCharacterBuildPreset.MaterialFamily.Unknown => ModularCharacterBuildPreset.MaterialFamily.Opaque,
                _ => family
            };
        }

        private void OnDisable()
        {
            if (previewEditor != null)
                DestroyImmediate(previewEditor);
        }

        private void OnGUI()
        {
            EnsureStyles();
            HandleWindowDragging();

            DrawHeader();
            DrawTopTabs();

            EditorGUILayout.Space(4f);
            DrawSearchBar();
            EditorGUILayout.Space(4f);

            float contentWidth = Mathf.Max(400f, position.width - Padding * 3f);
            float previewPanelWidth = Mathf.Max(250f, contentWidth * 0.32f);
            float materialsPanelWidth = Mathf.Max(420f, contentWidth - previewPanelWidth - 6f);
            float availableMainHeight = Mathf.Max(240f, Mathf.Min(360f, position.height - 300f));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Padding);

                using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(materialsPanelWidth), GUILayout.Height(availableMainHeight)))
                    DrawGridPanel(materialsPanelWidth - 12f, availableMainHeight - 12f);

                GUILayout.Space(6f);

                using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(previewPanelWidth), GUILayout.Height(availableMainHeight)))
                    DrawPreviewPanel(availableMainHeight - 12f);

                GUILayout.Space(Padding);
            }

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Padding);
                using (new EditorGUILayout.VerticalScope("box", GUILayout.Height(132f), GUILayout.ExpandWidth(true)))
                    DrawRecentPanel();
                GUILayout.Space(Padding);
            }

            GUILayout.FlexibleSpace();
            DrawFooter();
        }

        private void EnsureStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                headerStyle.normal.textColor = Color.white;
            }

            if (miniLabelStyle == null)
            {
                miniLabelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, clipping = TextClipping.Clip };
            }

            if (tileStyle == null)
            {
                tileStyle = new GUIStyle("box")
                {
                    alignment = TextAnchor.UpperCenter,
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(3, 3, 3, 3)
                };
            }

            if (tileSelectedStyle == null)
            {
                tileSelectedStyle = new GUIStyle(tileStyle);
                tileSelectedStyle.normal.background = MakeTex(new Color(0.24f, 0.52f, 0.90f, 0.40f));
            }

            if (tileLabelStyle == null)
            {
                tileLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
            }

            if (titleBarStyle == null)
            {
                titleBarStyle = new GUIStyle(EditorStyles.toolbar)
                {
                    fixedHeight = HeaderHeight,
                    normal = { background = MakeTex(EditorGUIUtility.isProSkin
                        ? new Color(0.10f, 0.10f, 0.10f, 1f)
                        : new Color(0.22f, 0.22f, 0.22f, 1f)) }
                };
            }

            if (familySectionStyle == null)
            {
                familySectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            }

            if (recentCardStyle == null)
            {
                recentCardStyle = new GUIStyle("box")
                {
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(2, 2, 2, 2)
                };
            }
        }

        private void DrawHeader()
        {
            Rect headerRect = GUILayoutUtility.GetRect(1f, HeaderHeight, GUILayout.ExpandWidth(true));
            GUI.Box(headerRect, GUIContent.none, titleBarStyle);

            Rect titleRect = new Rect(headerRect.x + 10f, headerRect.y + 5f, headerRect.width - 20f, 18f);
            GUI.Label(titleRect, "Material Browser • Base Family: " + GetFamilyDisplayName(baseFamily), headerStyle);
            EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.MoveArrow);
        }

        private void DrawTopTabs()
        {
            Rect barRect = GUILayoutUtility.GetRect(1f, TabsHeight * 2f + 4f, GUILayout.ExpandWidth(true));

            Rect modeRect = new Rect(barRect.x + 8f, barRect.y, barRect.width - 16f, TabsHeight);
            int modeIndex = (int)currentViewMode;
            int newModeIndex = GUI.Toolbar(modeRect, modeIndex, new[] { "All", "Favorites" });
            if (newModeIndex != modeIndex)
            {
                currentViewMode = (ViewMode)newModeIndex;
                RefreshFilteredMaterials();
                Repaint();
            }

            Rect familyRect = new Rect(barRect.x + 8f, barRect.y + TabsHeight + 4f, barRect.width - 16f, TabsHeight);
            int currentFamilyIndex = 0;
            string[] tabNames = new string[FamilyTabs.Length];
            for (int i = 0; i < FamilyTabs.Length; i++)
            {
                tabNames[i] = GetFamilyDisplayName(FamilyTabs[i]);
                if (FamilyTabs[i] == browsingFamily)
                    currentFamilyIndex = i;
            }

            GUI.enabled = currentViewMode != ViewMode.Favorites;
            int newFamilyIndex = GUI.Toolbar(familyRect, currentFamilyIndex, tabNames);
            GUI.enabled = true;
            if (newFamilyIndex != currentFamilyIndex)
            {
                browsingFamily = FamilyTabs[newFamilyIndex];
                RefreshFilteredMaterials();
                Repaint();
            }
        }

        private void HandleWindowDragging()
        {
            Rect dragRect = new Rect(0f, 0f, position.width, HeaderHeight);
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && dragRect.Contains(e.mousePosition))
                    {
                        isDraggingWindow = true;
                        dragMouseStart = GUIUtility.GUIToScreenPoint(e.mousePosition);
                        dragWindowStart = position;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (isDraggingWindow)
                    {
                        Vector2 currentMouse = GUIUtility.GUIToScreenPoint(e.mousePosition);
                        Vector2 delta = currentMouse - dragMouseStart;
                        position = new Rect(dragWindowStart.x + delta.x, dragWindowStart.y + delta.y, dragWindowStart.width, dragWindowStart.height);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (isDraggingWindow)
                    {
                        isDraggingWindow = false;
                        e.Use();
                    }
                    break;
            }
        }

        private void DrawSearchBar()
        {
            GUIStyle textFieldStyle =
                GUI.skin.FindStyle("ToolbarSearchTextField") ??
                GUI.skin.FindStyle("ToolbarSeachTextField") ??
                EditorStyles.toolbarSearchField ??
                EditorStyles.textField;

            GUIStyle cancelButtonStyle =
                GUI.skin.FindStyle("ToolbarSearchCancelButton") ??
                GUI.skin.FindStyle("ToolbarSeachCancelButton") ??
                GUI.skin.button;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Padding);
                GUI.SetNextControlName("MaterialBrowserSearchField");
                string newSearch = EditorGUILayout.TextField(searchText, textFieldStyle, GUILayout.Height(SearchHeight));
                if (newSearch != searchText)
                {
                    searchText = newSearch;
                    RefreshFilteredMaterials();
                }

                if (GUILayout.Button(string.Empty, cancelButtonStyle, GUILayout.Width(18f), GUILayout.Height(SearchHeight)))
                {
                    searchText = string.Empty;
                    GUI.FocusControl(null);
                    RefreshFilteredMaterials();
                }
                GUILayout.Space(Padding);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Padding);
                EditorGUI.BeginChangeCheck();
                DefaultAsset newFolder = (DefaultAsset)EditorGUILayout.ObjectField("Folder", selectedFolderAsset, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newFolder == null)
                    {
                        selectedFolderAsset = null;
                        folderFilterPath = string.Empty;
                    }
                    else
                    {
                        string folderPath = AssetDatabase.GetAssetPath(newFolder);
                        if (!string.IsNullOrWhiteSpace(folderPath) && AssetDatabase.IsValidFolder(folderPath))
                        {
                            selectedFolderAsset = newFolder;
                            folderFilterPath = NormalizeFolderPath(folderPath);
                        }
                    }
                    RefreshFilteredMaterials();
                    GUI.FocusControl(null);
                    Repaint();
                }
                GUILayout.Space(Padding);
            }

            if (!string.IsNullOrWhiteSpace(folderFilterPath))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(Padding);
                    EditorGUILayout.LabelField("Showing folder: " + folderFilterPath, miniLabelStyle);
                    GUILayout.Space(Padding);
                }
            }
        }

        private void DrawGridPanel(float availableWidth, float availableHeight)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Materials (" + filteredMaterials.Count + ")", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (currentViewMode == ViewMode.Favorites)
                {
                    GUI.enabled = favoriteGuids.Count > 0;
                    if (GUILayout.Button("Clear Favorites", GUILayout.Width(108f), GUILayout.Height(22f)))
                    {
                        favoriteGuids.Clear();
                        SavePrefs();
                        RefreshFilteredMaterials();
                        Repaint();
                    }
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.Space(4f);
            if (filteredMaterials.Count == 0)
            {
                EditorGUILayout.HelpBox("No materials found for this family/search/folder.", MessageType.Info);
                return;
            }

            if (currentViewMode == ViewMode.Favorites)
                DrawFavoritesGroupedByFamily(availableWidth, availableHeight);
            else
                DrawFlatGrid(filteredMaterials, availableWidth, availableHeight);
        }

        private void DrawFlatGrid(List<Material> materials, float availableWidth, float availableHeight)
        {
            int columns = Mathf.Max(2, Mathf.FloorToInt(availableWidth / (TileWidth + 12f)));
            int index = 0;
            materialsScroll = EditorGUILayout.BeginScrollView(materialsScroll, GUILayout.Height(Mathf.Max(120f, availableHeight - 28f)));
            while (index < materials.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns; c++)
                    {
                        if (index >= materials.Count)
                        {
                            GUILayout.FlexibleSpace();
                            continue;
                        }
                        DrawMaterialTile(materials[index]);
                        index++;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawFavoritesGroupedByFamily(float availableWidth, float availableHeight)
        {
            int columns = Mathf.Max(2, Mathf.FloorToInt(availableWidth / (TileWidth + 12f)));
            materialsScroll = EditorGUILayout.BeginScrollView(materialsScroll, GUILayout.Height(Mathf.Max(120f, availableHeight - 28f)));
            foreach (ModularCharacterBuildPreset.MaterialFamily family in FamilyTabs)
            {
                List<Material> group = filteredMaterials.Where(m => NormalizeBrowsableFamily(DetectMaterialFamily(m)) == family).ToList();
                if (group.Count == 0)
                    continue;

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(GetFamilyDisplayName(family), familySectionStyle);
                Rect sep = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(sep, EditorGUIUtility.isProSkin ? new Color(1f,1f,1f,0.12f) : new Color(0f,0f,0f,0.12f));
                EditorGUILayout.Space(4f);

                int index = 0;
                while (index < group.Count)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int c = 0; c < columns; c++)
                        {
                            if (index >= group.Count)
                            {
                                GUILayout.FlexibleSpace();
                                continue;
                            }
                            DrawMaterialTile(group[index]);
                            index++;
                        }
                    }
                }
                EditorGUILayout.Space(6f);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawMaterialTile(Material material)
        {
            bool isSelected = material == currentMaterial;
            GUIStyle style = isSelected ? tileSelectedStyle : tileStyle;
            Rect tileRect = GUILayoutUtility.GetRect(TileWidth, TileHeight, GUILayout.Width(TileWidth), GUILayout.Height(TileHeight));
            GUI.Box(tileRect, GUIContent.none, style);

            Rect previewRect = new Rect(tileRect.x + 6f, tileRect.y + 24f, ThumbSize, ThumbSize);
            Rect starRect = new Rect(tileRect.x + tileRect.width - 22f, tileRect.y + 4f, 18f, 18f);
            bool isFavorite = IsFavorite(material);
            if (GUI.Button(starRect, isFavorite ? "★" : "☆", EditorStyles.miniButton))
            {
                ToggleFavorite(material);
                Repaint();
                return;
            }

            Texture preview = AssetPreview.GetAssetPreview(material) ?? AssetPreview.GetMiniThumbnail(material);
            if (preview != null)
                GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);
            else
                EditorGUI.DrawRect(previewRect, new Color(0f,0f,0f,0.15f));

            Rect labelRect = new Rect(tileRect.x + 4f, tileRect.y + 24f + ThumbSize + 6f, tileRect.width - 8f, 18f);
            GUI.Label(labelRect, material.name, tileLabelStyle);

            Event e = Event.current;
            if (tileRect.Contains(e.mousePosition))
                hoveredMaterial = material;

            if (e.type == EventType.MouseDown && tileRect.Contains(e.mousePosition))
            {
                currentMaterial = material;
                RegisterRecent(material);
                ApplyLive(material);
                Repaint();
                if (e.clickCount >= 2)
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                e.Use();
            }
        }

        private void DrawPreviewPanel(float availableHeight)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            Material displayMaterial = currentMaterial != null ? currentMaterial : hoveredMaterial;
            if (displayMaterial == null)
            {
                EditorGUILayout.HelpBox("Select a material to preview.", MessageType.Info);
                GUILayout.FlexibleSpace();
                return;
            }

            float previewSize = Mathf.Clamp(availableHeight - 170f, 180f, 240f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.Width(previewSize), GUILayout.Height(previewSize));
                GUILayout.FlexibleSpace();
                if (previewEditor == null || previewEditor.target != displayMaterial)
                {
                    if (previewEditor != null)
                        DestroyImmediate(previewEditor);
                    previewEditor = Editor.CreateEditor(displayMaterial);
                }
                if (previewEditor != null)
                    previewEditor.OnInteractivePreviewGUI(previewRect, EditorStyles.helpBox);
                else
                    EditorGUI.DrawRect(previewRect, new Color(0f,0f,0f,0.12f));
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Material", displayMaterial, typeof(Material), false);

            EditorGUILayout.LabelField("Detected Family", GetFamilyDisplayName(DetectMaterialFamily(displayMaterial)), miniLabelStyle);
            EditorGUILayout.LabelField("Browsing Family", GetFamilyDisplayName(browsingFamily), miniLabelStyle);
            EditorGUILayout.LabelField("Base Slot Family", GetFamilyDisplayName(baseFamily), miniLabelStyle);
            string path = AssetDatabase.GetAssetPath(displayMaterial);
            if (!string.IsNullOrWhiteSpace(path))
                EditorGUILayout.LabelField(path, miniLabelStyle);
            GUILayout.FlexibleSpace();
        }

        private void DrawRecentPanel()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Recent", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUI.enabled = recentGuids.Count > 0;
                if (GUILayout.Button("Clear Recent", GUILayout.Width(96f), GUILayout.Height(22f)))
                {
                    recentGuids.Clear();
                    SavePrefs();
                    Repaint();
                }
                GUI.enabled = true;
            }

            EditorGUILayout.Space(4f);
            List<Material> recentMaterials = GetRecentMaterials();
            if (recentMaterials.Count == 0)
            {
                EditorGUILayout.HelpBox("Recently applied materials will appear here.", MessageType.None);
                return;
            }

            float viewHeight = 94f;
            Rect outerRect = GUILayoutUtility.GetRect(10f, 2000f, viewHeight, viewHeight + 16f, GUILayout.ExpandWidth(true));
            float contentWidth = recentMaterials.Count * 72f + 8f;
            Rect viewRect = new Rect(0f, 0f, contentWidth, viewHeight);
            recentScroll = GUI.BeginScrollView(outerRect, recentScroll, viewRect, false, true);

            float x = 4f;
            foreach (Material mat in recentMaterials)
            {
                if (mat == null)
                    continue;
                Rect cardRect = new Rect(x, 2f, 64f, 88f);
                GUI.Box(cardRect, GUIContent.none, recentCardStyle);
                Texture preview = AssetPreview.GetAssetPreview(mat) ?? AssetPreview.GetMiniThumbnail(mat);
                Rect thumbRect = new Rect(cardRect.x + 4f, cardRect.y + 4f, 56f, 56f);
                if (preview != null)
                    GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit, true);
                string label = mat.name.Length > 10 ? mat.name.Substring(0,9) + "…" : mat.name;
                Rect buttonRect = new Rect(cardRect.x + 3f, cardRect.y + 64f, 58f, 18f);
                GUI.Label(buttonRect, label, EditorStyles.miniLabel);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && cardRect.Contains(e.mousePosition))
                {
                    currentMaterial = mat;
                    RegisterRecent(mat);
                    ApplyLive(mat);
                    Repaint();
                    if (e.clickCount >= 2)
                    {
                        Close();
                        GUIUtility.ExitGUI();
                    }
                    e.Use();
                }
                x += 68f;
            }
            GUI.EndScrollView();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Padding);
                if (GUILayout.Button("None", GUILayout.Height(24f), GUILayout.Width(72f)))
                {
                    currentMaterial = null;
                    ApplyLive(null);
                }
                GUILayout.FlexibleSpace();
                GUI.enabled = currentMaterial != null;
                if (GUILayout.Button("Apply Material", GUILayout.Height(26f), GUILayout.Width(120f)))
                {
                    ApplyLive(currentMaterial);
                    Close();
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = true;
                if (GUILayout.Button("Close", GUILayout.Height(26f), GUILayout.Width(72f)))
                    Close();
                GUILayout.Space(Padding);
            }
            EditorGUILayout.Space(6f);
        }

        private void ApplyLive(Material material) => onPick?.Invoke(material);

        private void ToggleFavorite(Material material)
        {
            string guid = GetMaterialGuid(material);
            if (string.IsNullOrWhiteSpace(guid))
                return;
            if (favoriteGuids.Contains(guid)) favoriteGuids.Remove(guid); else favoriteGuids.Add(guid);
            SavePrefs();
            RefreshFilteredMaterials();
        }

        private bool IsFavorite(Material material)
        {
            string guid = GetMaterialGuid(material);
            return !string.IsNullOrWhiteSpace(guid) && favoriteGuids.Contains(guid);
        }

        private void RegisterRecent(Material material)
        {
            string guid = GetMaterialGuid(material);
            if (string.IsNullOrWhiteSpace(guid))
                return;
            recentGuids.RemoveAll(x => string.Equals(x, guid, StringComparison.OrdinalIgnoreCase));
            recentGuids.Insert(0, guid);
            if (recentGuids.Count > MaxRecentItems)
                recentGuids.RemoveRange(MaxRecentItems, recentGuids.Count - MaxRecentItems);
            SavePrefs();
        }

        private List<Material> GetRecentMaterials()
        {
            List<Material> result = new();
            foreach (string guid in recentGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    result.Add(mat);
            }
            return result;
        }

        private void LoadPrefs()
        {
            favoriteGuids.Clear();
            recentGuids.Clear();
            string favoritesRaw = EditorPrefs.GetString(FavoritesPrefsKey, string.Empty);
            foreach (string guid in favoritesRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                favoriteGuids.Add(guid);
            string recentRaw = EditorPrefs.GetString(RecentPrefsKey, string.Empty);
            foreach (string guid in recentRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                recentGuids.Add(guid);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(FavoritesPrefsKey, string.Join(";", favoriteGuids.ToArray()));
            EditorPrefs.SetString(RecentPrefsKey, string.Join(";", recentGuids.ToArray()));
        }

        private static string GetMaterialGuid(Material mat)
        {
            if (mat == null) return null;
            string path = AssetDatabase.GetAssetPath(mat);
            return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        private void RefreshFilteredMaterials()
        {
            filteredMaterials.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Material");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || !IsValidUserMaterial(mat) || !MatchesFolder(path, folderFilterPath))
                    continue;
                if (currentViewMode == ViewMode.Favorites && !favoriteGuids.Contains(guid))
                    continue;

                ModularCharacterBuildPreset.MaterialFamily detectedFamily = NormalizeBrowsableFamily(DetectMaterialFamily(mat));
                if (currentViewMode != ViewMode.Favorites && browsingFamily != ModularCharacterBuildPreset.MaterialFamily.Unknown && detectedFamily != browsingFamily)
                    continue;
                if (!MatchesSearch(mat, searchText))
                    continue;
                filteredMaterials.Add(mat);
            }
            filteredMaterials.Sort(CompareMaterialsByColorThenName);
        }

        private static int CompareMaterialsByColorThenName(Material a, Material b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            Color ca = GetMaterialBaseColor(a);
            Color cb = GetMaterialBaseColor(b);
            Color.RGBToHSV(ca, out float ha, out float sa, out float va);
            Color.RGBToHSV(cb, out float hb, out float sb, out float vb);
            int satBucketA = sa < 0.08f ? 0 : 1;
            int satBucketB = sb < 0.08f ? 0 : 1;
            int satBucketCompare = satBucketA.CompareTo(satBucketB);
            if (satBucketCompare != 0) return satBucketCompare;
            int hueCompare = ha.CompareTo(hb);
            if (hueCompare != 0) return hueCompare;
            int valueCompare = vb.CompareTo(va);
            if (valueCompare != 0) return valueCompare;
            int satCompare = sb.CompareTo(sa);
            if (satCompare != 0) return satCompare;
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        }

        private static Color GetMaterialBaseColor(Material mat)
        {
            if (mat == null) return Color.white;
            try
            {
                if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
                if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            }
            catch { }
            return Color.white;
        }

        private static bool IsValidUserMaterial(Material mat)
        {
            if (mat == null) return false;
            string path = AssetDatabase.GetAssetPath(mat);
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || mat.shader == null)
                return false;
            string shaderName = mat.shader.name ?? string.Empty;
            return !shaderName.StartsWith("Hidden/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesFolder(string assetPath, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return true;
            if (string.IsNullOrWhiteSpace(assetPath)) return false;
            string normalizedAsset = assetPath.Replace("\\", "/");
            string normalizedFolder = folderPath.Replace("\\", "/").TrimEnd('/');
            return normalizedAsset.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(System.IO.Path.GetDirectoryName(normalizedAsset)?.Replace("\\", "/"), normalizedFolder, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesSearch(Material mat, string search)
        {
            if (mat == null) return false;
            if (string.IsNullOrWhiteSpace(search)) return true;
            string s = search.Trim();
            string shaderName = mat.shader != null ? mat.shader.name : string.Empty;
            string path = AssetDatabase.GetAssetPath(mat);
            return mat.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   shaderName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace("\\", "/").TrimEnd('/');
        }

        private static Texture2D MakeTex(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static string GetFamilyDisplayName(ModularCharacterBuildPreset.MaterialFamily family)
        {
            family = NormalizeBrowsableFamily(family);
            return family switch
            {
                ModularCharacterBuildPreset.MaterialFamily.Opaque => "Opaque",
                ModularCharacterBuildPreset.MaterialFamily.Transparent => "Transparent",
                ModularCharacterBuildPreset.MaterialFamily.Metallic => "Metallic",
                ModularCharacterBuildPreset.MaterialFamily.Emissive => "Emissive",
                _ => "Unknown"
            };
        }

        private static ModularCharacterBuildPreset.MaterialFamily DetectMaterialFamily(Material mat)
        {
            if (mat == null)
                return ModularCharacterBuildPreset.MaterialFamily.Unknown;
            string name = (mat.name ?? string.Empty).ToLowerInvariant();
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;
            if (name.Contains("transparent") || shaderName.Contains("transparent") || name.Contains("glass") || shaderName.Contains("glass") || IsMaterialTransparent(mat))
                return ModularCharacterBuildPreset.MaterialFamily.Transparent;
            if (name.Contains("emissive") || shaderName.Contains("emissive") || IsMaterialEmissive(mat))
                return ModularCharacterBuildPreset.MaterialFamily.Emissive;
            if (name.Contains("metallic") || shaderName.Contains("metallic") || name.Contains("metal") || shaderName.Contains("metal") || IsMaterialMetallic(mat))
                return ModularCharacterBuildPreset.MaterialFamily.Metallic;
            return ModularCharacterBuildPreset.MaterialFamily.Opaque;
        }

        private static bool IsMaterialTransparent(Material mat)
        {
            if (mat == null) return false;
            try
            {
                if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f) return true;
                if (mat.HasProperty("_SurfaceType") && mat.GetFloat("_SurfaceType") > 0.5f) return true;
                if (mat.renderQueue >= 3000) return true;
                if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || mat.IsKeywordEnabled("_ALPHABLEND_ON")) return true;
                string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;
                if (shaderName.Contains("transparent") || shaderName.Contains("fade")) return true;
                if (mat.HasProperty("_BaseColor") && mat.GetColor("_BaseColor").a < 0.999f) return true;
                if (mat.HasProperty("_Color") && mat.GetColor("_Color").a < 0.999f) return true;
            }
            catch { return false; }
            return false;
        }

        private static bool IsMaterialEmissive(Material mat)
        {
            if (mat == null) return false;
            try
            {
                if (mat.IsKeywordEnabled("_EMISSION")) return true;
                if (!mat.HasProperty("_EmissionColor")) return false;
                Color c = mat.GetColor("_EmissionColor");
                return Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.001f;
            }
            catch { return false; }
        }

        private static bool IsMaterialMetallic(Material mat)
        {
            if (mat == null) return false;
            try
            {
                if (mat.HasProperty("_Metallic") && mat.GetFloat("_Metallic") > 0.35f) return true;
                if (mat.HasProperty("_Metalness") && mat.GetFloat("_Metalness") > 0.35f) return true;
                if (mat.HasProperty("_MetallicScale") && mat.GetFloat("_MetallicScale") > 0.35f) return true;
            }
            catch { return false; }
            return false;
        }
    }
}
