using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ComfortView
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ComfortViewPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "mishka.valheim.comfortview";
        public const string PluginName = "ComfortView";
        public const string PluginVersion = "1.0.0";

        private const float ComfortRadius = 10f;
        private const float ScanRadius = 20f;
        private const float RefreshInterval = 1f;
        private const int Segments = 48;
        private const float HeightOffset = 0.05f;
        private const float DimWidth = 0.04f;
        private const float HoverWidth = 0.12f;
        private const float DimAlpha = 0.35f;
        private const float PostHeight = 2.5f;
        private const float HoverRayDistance = 50f;
        private const float SoloAimTolerance = 1.2f;
        private const float SpokeAlpha = 0.6f;
        private const float SpokeWidth = 0.05f;
        private const float SpokeDashesPerRadius = 10f;
        private const float SphereFillAlpha = 0.12f;

        private static Color CategoryColor(Piece.ComfortGroup group)
        {
            switch (group)
            {
                case Piece.ComfortGroup.Fire:
                    return new Color(0.9f, 0.25f, 0.2f);
                case Piece.ComfortGroup.Chair:
                    return new Color(0.95f, 0.6f, 0.15f);
                case Piece.ComfortGroup.Table:
                    return new Color(0.55f, 0.35f, 0.15f);
                case Piece.ComfortGroup.Bed:
                    return new Color(0.85f, 0.4f, 0.8f);
                case Piece.ComfortGroup.Banner:
                    return new Color(0.3f, 0.5f, 0.95f);
                case Piece.ComfortGroup.Carpet:
                    return new Color(0.35f, 0.8f, 0.35f);
                default:
                    return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        private enum DisplayMode
        {
            All,
            ActiveOnly,
            Picker,
        }

        private enum MenuTab
        {
            Items,
            Nearby,
        }

        private static Material s_ringMaterial;
        private static Material s_dashMaterial;
        private static Material s_sphereFillMaterial;
        private static Vector3[] s_localCircle;
        private static TMP_FontAsset s_font;
        private static Sprite s_panelSprite;
        private static int s_pieceRayMask = -1;
        private static List<(string key, string label, Piece.ComfortGroup group, int comfort)> s_allComfortTypes;
        private static readonly List<Piece> s_scanBuffer = new List<Piece>();
        private static readonly List<Piece> s_activeBuffer = new List<Piece>();
        private static readonly List<Piece> s_visibleBuffer = new List<Piece>();
        private static readonly List<Piece> s_staleBuffer = new List<Piece>();

        private ConfigEntry<KeyboardShortcut> toggleKey;
        private ConfigEntry<KeyboardShortcut> menuKey;
        private ConfigEntry<KeyboardShortcut> soloKey;
        private bool visible;
        private bool menuOpen;
        private DisplayMode mode = DisplayMode.All;
        private MenuTab activeTab = MenuTab.Items;
        private bool sphereMode;
        private float refreshTimer;
        private int menuIndex;

        private const int ListColumnCount = 3;

        private GameObject menuRoot;
        private TMP_Text[] modeTexts;
        private TMP_Text shapeText;
        private TMP_Text tabsText;
        private GameObject itemsSection;
        private GameObject nearbySection;
        private Transform[] listColumns;
        private Transform[] nearbyColumns;
        private readonly List<TMP_Text> listRowTexts = new List<TMP_Text>();
        private readonly List<int> listRowItemIndex = new List<int>();
        private readonly List<int> listNavToDataIndex = new List<int>();
        private int listRowsBuiltFor = -1;

        private readonly List<TMP_Text> nearbyRowTexts = new List<TMP_Text>();
        private readonly List<Piece> nearbyNavPieces = new List<Piece>();
        private readonly HashSet<Piece> lastNearbySet = new HashSet<Piece>();

        private float navRepeatTimer;
        private const float NavInitialDelay = 0.35f;
        private const float NavRepeatInterval = 0.08f;

        private readonly Dictionary<Piece, RingEntry> activeRings = new Dictionary<Piece, RingEntry>();
        private readonly HashSet<Piece> pinned = new HashSet<Piece>();
        private readonly HashSet<string> selectedTypes = new HashSet<string>();
        private Piece hoveredPiece;

        private class RingEntry
        {
            public GameObject Root;
            public LineRenderer Ring;
            public LineRenderer Post;
            public LineRenderer Spoke;
            public GameObject SphereRoot;
            public LineRenderer[] SphereRings;
            public Color Color;
        }

        private void Awake()
        {
            toggleKey = Config.Bind("General", "Toggle Radius", new KeyboardShortcut(KeyCode.F6),
                "Key used to toggle the comfort radius rings on and off.");
            menuKey = Config.Bind("General", "Toggle Menu", new KeyboardShortcut(KeyCode.F7),
                "Key used to open the comfort radius selection menu.");
            soloKey = Config.Bind("General", "Pin Looked-At Item", new KeyboardShortcut(KeyCode.F8),
                "Key used to pin or unpin the exact comfort item you're looking at.");

            s_ringMaterial = new Material(Shader.Find("Sprites/Default"));

            Texture2D dashTexture = new Texture2D(4, 1, TextureFormat.RGBA32, false);
            dashTexture.wrapMode = TextureWrapMode.Repeat;
            dashTexture.filterMode = FilterMode.Point;
            dashTexture.SetPixels(new[] { Color.white, Color.white, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f) });
            dashTexture.Apply();
            s_dashMaterial = new Material(Shader.Find("Sprites/Default"));
            s_dashMaterial.mainTexture = dashTexture;
            s_dashMaterial.mainTextureScale = new Vector2(SpokeDashesPerRadius, 1f);

            s_sphereFillMaterial = new Material(Shader.Find("Sprites/Default"));

            s_localCircle = new Vector3[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                s_localCircle[i] = new Vector3(Mathf.Cos(angle) * ComfortRadius, HeightOffset, Mathf.Sin(angle) * ComfortRadius);
            }
        }

        private void Update()
        {
            if (toggleKey.Value.IsDown())
            {
                visible = !visible;
                if (!visible)
                {
                    ClearRings();
                }
            }

            if (soloKey.Value.IsDown())
            {
                TogglePin(RaycastForComfortPiece());
            }

            if (menuKey.Value.IsDown() || (menuOpen && Input.GetKeyDown(KeyCode.Escape)))
            {
                menuOpen = !menuOpen;
                if (menuOpen)
                {
                    BuildMenu();
                    visible = true;
                    menuIndex = 0;
                }
                if (menuRoot != null)
                {
                    menuRoot.SetActive(menuOpen);
                }
            }

            if (menuOpen)
            {
                UpdateMenuInput();
                UpdateMenuVisuals();
            }

            if (!visible)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                ClearRings();
                return;
            }

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;
                RefreshRings(player);
                if (menuRoot != null && NearbySetChanged())
                {
                    RebuildNearbyRows(player);
                }
            }

            UpdateHover();
        }

        private void TogglePin(Piece target)
        {
            if (target == null)
            {
                return;
            }
            if (!pinned.Add(target))
            {
                pinned.Remove(target);
            }
            mode = DisplayMode.Picker;
            visible = true;
            refreshTimer = 0f;
        }

        private static int PieceRayMask()
        {
            if (s_pieceRayMask == -1)
            {
                s_pieceRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
            }
            return s_pieceRayMask;
        }

        private static Piece RaycastForComfortPiece()
        {
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            Player player = Player.m_localPlayer;
            if (cam == null || player == null)
            {
                return null;
            }

            if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, HoverRayDistance, PieceRayMask()))
            {
                Piece direct = hit.collider.GetComponentInParent<Piece>();
                if (direct != null && direct.m_comfort > 0)
                {
                    return direct;
                }
            }

            s_scanBuffer.Clear();
            Piece.GetAllComfortPiecesInRadius(player.transform.position, ScanRadius, s_scanBuffer);

            Piece closest = null;
            float closestLateral = SoloAimTolerance;
            foreach (Piece piece in s_scanBuffer)
            {
                if (piece == null)
                {
                    continue;
                }
                Vector3 toPiece = piece.transform.position - cam.position;
                float along = Vector3.Dot(toPiece, cam.forward);
                if (along <= 0f || along > HoverRayDistance)
                {
                    continue;
                }
                float lateral = Vector3.Distance(cam.position + cam.forward * along, piece.transform.position);
                if (lateral < closestLateral)
                {
                    closestLateral = lateral;
                    closest = piece;
                }
            }
            return closest;
        }

        private const int ShapeRowIndex = 2;
        private const int TabRowIndex = 3;
        private const int FirstListRowIndex = 4;

        private int MenuRowCount()
        {
            return FirstListRowIndex + (activeTab == MenuTab.Items ? listNavToDataIndex.Count : nearbyNavPieces.Count);
        }

        private void UpdateMenuInput()
        {
            int rowCount = MenuRowCount();
            menuIndex = Mathf.Clamp(menuIndex, 0, rowCount - 1);

            int direction = 0;
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                direction = 1;
                navRepeatTimer = NavInitialDelay;
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                direction = -1;
                navRepeatTimer = NavInitialDelay;
            }
            else if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.UpArrow))
            {
                navRepeatTimer -= Time.unscaledDeltaTime;
                if (navRepeatTimer <= 0f)
                {
                    direction = Input.GetKey(KeyCode.DownArrow) ? 1 : -1;
                    navRepeatTimer = NavRepeatInterval;
                }
            }
            if (direction != 0)
            {
                menuIndex = (menuIndex + direction + rowCount) % rowCount;
            }

            bool pressedRight = Input.GetKeyDown(KeyCode.RightArrow);
            bool pressedLeft = Input.GetKeyDown(KeyCode.LeftArrow);
            if (menuIndex == TabRowIndex)
            {
                if (pressedRight || pressedLeft)
                {
                    activeTab = activeTab == MenuTab.Items ? MenuTab.Nearby : MenuTab.Items;
                }
            }
            else if (pressedRight)
            {
                ActivateMenuRow(menuIndex);
            }
        }

        private void ActivateMenuRow(int index)
        {
            if (index == 0)
            {
                ToggleMode(DisplayMode.All);
                return;
            }
            if (index == 1)
            {
                ToggleMode(DisplayMode.ActiveOnly);
                return;
            }
            if (index == ShapeRowIndex)
            {
                sphereMode = !sphereMode;
                refreshTimer = 0f;
                return;
            }

            int itemIndex = index - FirstListRowIndex;
            if (activeTab == MenuTab.Nearby)
            {
                if (itemIndex >= 0 && itemIndex < nearbyNavPieces.Count)
                {
                    TogglePin(nearbyNavPieces[itemIndex]);
                }
                return;
            }

            if (s_allComfortTypes == null || itemIndex < 0 || itemIndex >= listNavToDataIndex.Count)
            {
                return;
            }
            string key = s_allComfortTypes[listNavToDataIndex[itemIndex]].key;
            if (!selectedTypes.Add(key))
            {
                selectedTypes.Remove(key);
            }
            mode = DisplayMode.Picker;
            visible = true;
            refreshTimer = 0f;
        }

        private void ToggleMode(DisplayMode targetMode)
        {
            if (mode == targetMode && visible)
            {
                visible = false;
                ClearRings();
                return;
            }
            mode = targetMode;
            visible = true;
            refreshTimer = 0f;
        }

        private static readonly Piece.ComfortGroup[] GroupOrder =
        {
            Piece.ComfortGroup.Fire,
            Piece.ComfortGroup.Chair,
            Piece.ComfortGroup.Table,
            Piece.ComfortGroup.Bed,
            Piece.ComfortGroup.Banner,
            Piece.ComfortGroup.Carpet,
            Piece.ComfortGroup.None,
        };

        private static string GroupLabel(Piece.ComfortGroup group)
        {
            return group == Piece.ComfortGroup.None ? "Other" : group.ToString();
        }

        private static readonly HashSet<string> ApprovedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Campfire", "Bonfire", "Hanging brazier", "Standing brazier", "Blue standing brazier", "Hearth",
            "Deer rug", "Wolf rug", "Lox rug", "Hare rug", "Asksvin rug", "Straw", "Bearskin rug", "Red jute carpet", "Blue jute carpet",
            "Table", "Black marble table", "Round table", "Long heavy table",
            "Bench", "Sitting log", "Stool", "Black marble bench", "Ashwood bench", "Chair", "Darkwood chair", "Barber station",
            "Raven throne", "Stone throne", "Black marble throne", "Bone throne",
            "Bed", "Ashwood bed", "Dragon bed",
            "Black banner", "Blue banner", "White and red striped banner", "Red banner", "Green banner",
            "Blue, red and white banner", "White and blue striped banner", "Yellow banner", "Purple banner", "White banner", "Orange banner",
            "Hot tub", "Lava lantern", "Armor stand", "Maypole", "Yule tree",
        };

        private static bool EnsureAllComfortTypes()
        {
            if (s_allComfortTypes != null)
            {
                return true;
            }
            if (ZNetScene.instance == null)
            {
                return false;
            }
            List<(string key, string label, Piece.ComfortGroup group, int comfort)> list = new List<(string, string, Piece.ComfortGroup, int)>();
            HashSet<string> seen = new HashSet<string>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null || piece.m_comfort <= 0 || !seen.Add(piece.m_name))
                {
                    continue;
                }
                string label = Localization.instance.Localize(piece.m_name);
                if (ApprovedLabels.Contains(label))
                {
                    list.Add((piece.m_name, label, piece.m_comfortGroup, piece.m_comfort));
                }
            }
            list.Sort((a, b) =>
            {
                int groupCompare = System.Array.IndexOf(GroupOrder, a.group).CompareTo(System.Array.IndexOf(GroupOrder, b.group));
                return groupCompare != 0 ? groupCompare : string.CompareOrdinal(a.label, b.label);
            });
            s_allComfortTypes = list;
            return true;
        }

        private static bool EnsureFont()
        {
            if (s_font != null)
            {
                return true;
            }
            s_font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "Valheim-AveriaSerifLibre");
            return s_font != null;
        }

        private static bool EnsurePanelSprite()
        {
            if (s_panelSprite != null)
            {
                return true;
            }
            s_panelSprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "woodpanel_crafting");
            return s_panelSprite != null;
        }

        private void BuildMenu()
        {
            if (menuRoot != null || !EnsureFont() || !EnsurePanelSprite())
            {
                return;
            }

            menuRoot = new GameObject("ComfortViewMenu");
            Canvas canvas = menuRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9990;
            menuRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            GameObject panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(menuRoot.transform, false);
            Image panelImage = panelGo.AddComponent<Image>();
            panelImage.sprite = s_panelSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = RenderSettings.ambientLight;

            RectTransform panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f);
            panelRect.sizeDelta = new Vector2(620f, 0f);

            VerticalLayoutGroup layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 3f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            panelGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateText(panelRect, "Comfort View", TextAlignmentOptions.Left, 15f, Color.white);
            CreateText(panelRect, "Up/Down move * Right select/pin/toggle * Esc close", TextAlignmentOptions.Left, 9f, new Color(0.8f, 0.8f, 0.8f));

            modeTexts = new TMP_Text[2];
            for (int i = 0; i < modeTexts.Length; i++)
            {
                modeTexts[i] = CreateText(panelRect, "", TextAlignmentOptions.Left, 11f, Color.white);
            }

            shapeText = CreateText(panelRect, "", TextAlignmentOptions.Left, 11f, Color.white);

            tabsText = CreateText(panelRect, "", TextAlignmentOptions.Left, 11f, Color.white);

            itemsSection = BuildColumnSection(panelRect, out listColumns);
            nearbySection = BuildColumnSection(panelRect, out nearbyColumns);

            EnsureAllComfortTypes();
        }

        private static GameObject BuildColumnSection(Transform parent, out Transform[] columns)
        {
            GameObject sectionGo = new GameObject("Section", typeof(RectTransform));
            sectionGo.transform.SetParent(parent, false);
            HorizontalLayoutGroup rowLayout = sectionGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childAlignment = TextAnchor.UpperLeft;

            columns = new Transform[ListColumnCount];
            for (int c = 0; c < ListColumnCount; c++)
            {
                GameObject colGo = new GameObject("Col" + c, typeof(RectTransform));
                colGo.transform.SetParent(sectionGo.transform, false);
                VerticalLayoutGroup colLayout = colGo.AddComponent<VerticalLayoutGroup>();
                colLayout.childForceExpandWidth = true;
                colLayout.childForceExpandHeight = false;
                colLayout.spacing = 1f;
                columns[c] = colGo.transform;
            }
            return sectionGo;
        }

        private static TMP_Text CreateText(Transform parent, string text, TextAlignmentOptions alignment, float fontSize, Color color)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = s_font;
            tmp.fontSharedMaterial = s_font.material;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.text = text;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }

        private void UpdateMenuVisuals()
        {
            UpdateModeRowText(0, "Show all nearby");
            UpdateModeRowText(1, "Show active only");

            bool shapeHighlighted = menuIndex == ShapeRowIndex;
            shapeText.text = (shapeHighlighted ? "> " : "  ") + "Shape: " + (sphereMode ? "Spheres" : "Circles");
            shapeText.color = shapeHighlighted ? Color.yellow : Color.white;

            bool tabHighlighted = menuIndex == TabRowIndex;
            string tabsLabel = activeTab == MenuTab.Items ? "[ Items ]   Nearby" : "  Items   [ Nearby ]";
            tabsText.text = (tabHighlighted ? "> " : "  ") + tabsLabel;
            tabsText.color = tabHighlighted ? Color.yellow : Color.white;

            itemsSection.SetActive(activeTab == MenuTab.Items);
            nearbySection.SetActive(activeTab == MenuTab.Nearby);

            EnsureAllComfortTypes();
            if (s_allComfortTypes == null)
            {
                return;
            }

            if (listRowsBuiltFor != s_allComfortTypes.Count)
            {
                RebuildListRows();
            }

            for (int row = 0; row < listRowTexts.Count; row++)
            {
                int navPos = listRowItemIndex[row];
                if (navPos < 0)
                {
                    continue;
                }
                (string key, string label, Piece.ComfortGroup group, int comfort) entry = s_allComfortTypes[listNavToDataIndex[navPos]];
                bool selected = selectedTypes.Contains(entry.key);
                bool highlighted = activeTab == MenuTab.Items && menuIndex == FirstListRowIndex + navPos;
                listRowTexts[row].text = (highlighted ? "> " : "  ") + (selected ? "[x] " : "[ ] ") + $"{entry.label} (+{entry.comfort})";
                listRowTexts[row].color = highlighted ? Color.yellow : Color.white;
            }

            for (int i = 0; i < nearbyRowTexts.Count; i++)
            {
                if (i >= nearbyNavPieces.Count)
                {
                    continue;
                }
                Piece piece = nearbyNavPieces[i];
                bool isPinned = pinned.Contains(piece);
                bool highlighted = activeTab == MenuTab.Nearby && menuIndex == FirstListRowIndex + i;
                Player player = Player.m_localPlayer;
                float dist = player != null ? Vector3.Distance(player.transform.position, piece.transform.position) : 0f;
                nearbyRowTexts[i].text = (highlighted ? "> " : "  ") + (isPinned ? "[x] " : "[ ] ") + $"{Localization.instance.Localize(piece.m_name)} ({dist:0}m)";
                nearbyRowTexts[i].color = highlighted ? Color.yellow : (isPinned ? new Color(1f, 0.85f, 0.4f) : Color.white);
            }
        }

        private void UpdateModeRowText(int index, string label)
        {
            bool selected = index == (int)mode && visible;
            bool highlighted = menuIndex == index;
            modeTexts[index].text = (highlighted ? "> " : "  ") + (selected ? "(*) " : "( ) ") + label;
            modeTexts[index].color = highlighted ? Color.yellow : Color.white;
        }

        private static int ShortestColumn(int[] colLoad)
        {
            int best = 0;
            for (int c = 1; c < colLoad.Length; c++)
            {
                if (colLoad[c] < colLoad[best])
                {
                    best = c;
                }
            }
            return best;
        }

        private void RebuildListRows()
        {
            foreach (Transform col in listColumns)
            {
                foreach (Transform child in col)
                {
                    Destroy(child.gameObject);
                }
            }
            listRowTexts.Clear();
            listRowItemIndex.Clear();
            listNavToDataIndex.Clear();

            List<List<(int start, int end, Piece.ComfortGroup group)>> perColumn = new List<List<(int, int, Piece.ComfortGroup)>>();
            for (int c = 0; c < listColumns.Length; c++)
            {
                perColumn.Add(new List<(int, int, Piece.ComfortGroup)>());
            }

            int[] colLoad = new int[listColumns.Length];
            int i = 0;
            while (i < s_allComfortTypes.Count)
            {
                Piece.ComfortGroup group = s_allComfortTypes[i].group;
                int j = i;
                while (j < s_allComfortTypes.Count && s_allComfortTypes[j].group == group)
                {
                    j++;
                }
                int col = ShortestColumn(colLoad);
                colLoad[col] += 1 + (j - i);
                perColumn[col].Add((i, j, group));
                i = j;
            }

            for (int c = 0; c < listColumns.Length; c++)
            {
                foreach ((int start, int end, Piece.ComfortGroup group) run in perColumn[c])
                {
                    TMP_Text header = CreateText(listColumns[c], GroupLabel(run.group), TextAlignmentOptions.Left, 10f, new Color(0.85f, 0.75f, 0.5f));
                    listRowTexts.Add(header);
                    listRowItemIndex.Add(-1);

                    for (int k = run.start; k < run.end; k++)
                    {
                        listRowTexts.Add(CreateText(listColumns[c], "", TextAlignmentOptions.Left, 10f, Color.white));
                        listRowItemIndex.Add(listNavToDataIndex.Count);
                        listNavToDataIndex.Add(k);
                    }
                }
            }
            listRowsBuiltFor = s_allComfortTypes.Count;
        }

        private bool NearbySetChanged()
        {
            if (s_scanBuffer.Count != lastNearbySet.Count)
            {
                return true;
            }
            foreach (Piece piece in s_scanBuffer)
            {
                if (!lastNearbySet.Contains(piece))
                {
                    return true;
                }
            }
            return false;
        }

        private void RebuildNearbyRows(Player player)
        {
            lastNearbySet.Clear();
            lastNearbySet.UnionWith(s_scanBuffer);

            foreach (Transform col in nearbyColumns)
            {
                foreach (Transform child in col)
                {
                    Destroy(child.gameObject);
                }
            }
            nearbyRowTexts.Clear();
            nearbyNavPieces.Clear();

            if (s_scanBuffer.Count == 0)
            {
                CreateText(nearbyColumns[0], "(none in range)", TextAlignmentOptions.Left, 10f, new Color(0.7f, 0.7f, 0.7f));
                return;
            }

            List<Piece> ordered = s_scanBuffer.OrderBy(p => Vector3.Distance(player.transform.position, p.transform.position)).ToList();

            List<List<Piece>> perColumn = new List<List<Piece>>();
            for (int c = 0; c < nearbyColumns.Length; c++)
            {
                perColumn.Add(new List<Piece>());
            }
            int[] colLoad = new int[nearbyColumns.Length];
            foreach (Piece piece in ordered)
            {
                int col = ShortestColumn(colLoad);
                colLoad[col]++;
                perColumn[col].Add(piece);
            }

            for (int c = 0; c < nearbyColumns.Length; c++)
            {
                foreach (Piece piece in perColumn[c])
                {
                    nearbyRowTexts.Add(CreateText(nearbyColumns[c], "", TextAlignmentOptions.Left, 10f, Color.white));
                    nearbyNavPieces.Add(piece);
                }
            }
        }

        private void UpdateHover()
        {
            Piece hit = null;
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam != null && Physics.Raycast(cam.position, cam.forward, out RaycastHit hitInfo, HoverRayDistance, PieceRayMask()))
            {
                Piece piece = hitInfo.collider.GetComponentInParent<Piece>();
                if (piece != null && activeRings.ContainsKey(piece))
                {
                    hit = piece;
                }
            }

            if (hit == hoveredPiece)
            {
                return;
            }

            if (hoveredPiece != null && activeRings.TryGetValue(hoveredPiece, out RingEntry previous))
            {
                SetHover(previous, isHovered: false);
            }
            hoveredPiece = hit;
            if (hoveredPiece != null && activeRings.TryGetValue(hoveredPiece, out RingEntry current))
            {
                SetHover(current, isHovered: true);
            }
        }

        private void RefreshRings(Player player)
        {
            s_scanBuffer.Clear();
            Piece.GetAllComfortPiecesInRadius(player.transform.position, ScanRadius, s_scanBuffer);

            pinned.RemoveWhere(p => p == null);

            s_visibleBuffer.Clear();
            switch (mode)
            {
                case DisplayMode.ActiveOnly:
                    ComputeActiveSet(s_scanBuffer, player.transform.position, s_visibleBuffer);
                    break;
                case DisplayMode.Picker:
                    foreach (Piece piece in s_scanBuffer)
                    {
                        if (pinned.Contains(piece) || selectedTypes.Contains(piece.m_name))
                        {
                            s_visibleBuffer.Add(piece);
                        }
                    }
                    break;
                default:
                    s_visibleBuffer.AddRange(s_scanBuffer);
                    break;
            }

            s_staleBuffer.Clear();
            foreach (KeyValuePair<Piece, RingEntry> kv in activeRings)
            {
                if (kv.Key == null || !s_visibleBuffer.Contains(kv.Key))
                {
                    s_staleBuffer.Add(kv.Key);
                }
            }
            foreach (Piece stale in s_staleBuffer)
            {
                if (activeRings.TryGetValue(stale, out RingEntry entry) && entry.Root != null)
                {
                    Destroy(entry.Root);
                }
                activeRings.Remove(stale);
                if (hoveredPiece == stale)
                {
                    hoveredPiece = null;
                }
            }

            foreach (Piece piece in s_visibleBuffer)
            {
                if (piece == null || activeRings.ContainsKey(piece))
                {
                    continue;
                }

                activeRings[piece] = CreateRing(piece.transform, CategoryColor(piece.m_comfortGroup));
            }

            foreach (KeyValuePair<Piece, RingEntry> kv in activeRings)
            {
                kv.Value.SphereRoot.SetActive(sphereMode || pinned.Contains(kv.Key));
            }
        }

        private static void ComputeActiveSet(List<Piece> nearby, Vector3 playerPos, List<Piece> output)
        {
            s_activeBuffer.Clear();
            foreach (Piece piece in nearby)
            {
                if (piece != null && Vector3.Distance(piece.transform.position, playerPos) < ComfortRadius)
                {
                    s_activeBuffer.Add(piece);
                }
            }
            s_activeBuffer.Sort(CompareComfort);

            for (int i = 0; i < s_activeBuffer.Count; i++)
            {
                Piece piece = s_activeBuffer[i];
                if (i > 0)
                {
                    Piece prev = s_activeBuffer[i - 1];
                    if ((piece.m_comfortGroup != Piece.ComfortGroup.None && piece.m_comfortGroup == prev.m_comfortGroup) || piece.m_name == prev.m_name)
                    {
                        continue;
                    }
                }
                output.Add(piece);
            }
        }

        private static int CompareComfort(Piece x, Piece y)
        {
            if (x.m_comfortGroup != y.m_comfortGroup)
            {
                return x.m_comfortGroup.CompareTo(y.m_comfortGroup);
            }
            float cx = x.GetComfort();
            float cy = y.GetComfort();
            if (cx != cy)
            {
                return cy.CompareTo(cx);
            }
            return string.CompareOrdinal(y.m_name, x.m_name);
        }

        private static RingEntry CreateRing(Transform parent, Color color)
        {
            GameObject root = new GameObject("ComfortRing");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            LineRenderer ring = root.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = s_localCircle.Length;
            ring.SetPositions(s_localCircle);
            ring.material = s_ringMaterial;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;

            GameObject postGo = new GameObject("Post");
            postGo.transform.SetParent(root.transform, false);
            LineRenderer post = postGo.AddComponent<LineRenderer>();
            post.useWorldSpace = false;
            post.loop = false;
            post.positionCount = 2;
            post.SetPositions(new[] { Vector3.zero, Vector3.up * PostHeight });
            post.widthMultiplier = DimWidth;
            post.material = s_ringMaterial;
            post.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            post.receiveShadows = false;
            postGo.SetActive(false);

            GameObject spokeGo = new GameObject("Spoke");
            spokeGo.transform.SetParent(root.transform, false);
            LineRenderer spoke = spokeGo.AddComponent<LineRenderer>();
            spoke.useWorldSpace = false;
            spoke.loop = false;
            spoke.positionCount = 2;
            spoke.SetPositions(new[] { Vector3.zero, new Vector3(ComfortRadius, HeightOffset, 0f) });
            spoke.widthMultiplier = SpokeWidth;
            spoke.material = s_dashMaterial;
            spoke.textureMode = LineTextureMode.Tile;
            spoke.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            spoke.receiveShadows = false;
            Color spokeColor = color;
            spokeColor.a = SpokeAlpha;
            spoke.startColor = spokeColor;
            spoke.endColor = spokeColor;

            GameObject sphereRoot = new GameObject("Sphere");
            sphereRoot.transform.SetParent(root.transform, false);
            LineRenderer[] sphereRings =
            {
                CreateSphereRing(sphereRoot.transform, Quaternion.Euler(90f, 0f, 0f)),
                CreateSphereRing(sphereRoot.transform, Quaternion.Euler(0f, 0f, 90f)),
                CreateSphereRing(sphereRoot.transform, Quaternion.Euler(45f, 0f, 45f)),
                CreateSphereRing(sphereRoot.transform, Quaternion.Euler(-45f, 0f, 45f)),
            };

            GameObject fillGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fillGo.name = "SphereFill";
            fillGo.transform.SetParent(sphereRoot.transform, false);
            fillGo.transform.localScale = Vector3.one * (ComfortRadius * 2f);
            UnityEngine.Object.DestroyImmediate(fillGo.GetComponent<Collider>());
            MeshRenderer fillRenderer = fillGo.GetComponent<MeshRenderer>();
            fillRenderer.material = s_sphereFillMaterial;
            fillRenderer.material.color = new Color(color.r, color.g, color.b, SphereFillAlpha);
            fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fillRenderer.receiveShadows = false;

            sphereRoot.SetActive(false);

            RingEntry entry = new RingEntry { Root = root, Ring = ring, Post = post, Spoke = spoke, SphereRoot = sphereRoot, SphereRings = sphereRings, Color = color };
            SetHover(entry, isHovered: false);
            return entry;
        }

        private static LineRenderer CreateSphereRing(Transform parent, Quaternion localRotation)
        {
            GameObject go = new GameObject("SphereRing");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = localRotation;
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = s_localCircle.Length;
            line.SetPositions(s_localCircle);
            line.material = s_ringMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static void SetHover(RingEntry entry, bool isHovered)
        {
            Color c = entry.Color;
            c.a = isHovered ? 1f : DimAlpha;
            float width = isHovered ? HoverWidth : DimWidth;

            entry.Ring.widthMultiplier = width;
            entry.Ring.startColor = c;
            entry.Ring.endColor = c;

            foreach (LineRenderer sphereRing in entry.SphereRings)
            {
                sphereRing.widthMultiplier = width;
                sphereRing.startColor = c;
                sphereRing.endColor = c;
            }

            Color postColor = entry.Color;
            postColor.a = 1f;
            entry.Post.startColor = postColor;
            entry.Post.endColor = postColor;
            entry.Post.gameObject.SetActive(isHovered);
        }

        private void ClearRings()
        {
            foreach (RingEntry entry in activeRings.Values)
            {
                if (entry.Root != null)
                {
                    Destroy(entry.Root);
                }
            }
            activeRings.Clear();
            hoveredPiece = null;
        }
    }
}
