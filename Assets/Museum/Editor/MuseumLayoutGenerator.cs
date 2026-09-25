using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Builds the museum shell (no textures) from Unity primitives.
///
/// Layout (top-down, north = +Z). The building is a 4x4 grid of 16 m cells:
///
///   [ G4 ][ G5 ][ G6 ][ G7 ]
///   [ G3 ][  courtyard  ][ G8 ]
///   [ G2 ][  (open air) ][ G9 ]
///   [ G1 ][   LOBBY     ][ G10]
///              entrance
///
/// Visitors enter the lobby from the south plaza, walk the galleries in order
/// (G1 -> G10, oldest era to newest) and arrive back in the lobby.
///
/// Re-running the menu item only rebuilds the "Museum_Building" object.
/// The "Exhibits" object and the XR rig are left alone, so exhibit work is never lost.
/// </summary>
public static class MuseumLayoutGenerator
{
    const string ScenePath = "Assets/Scenes/Museum.unity";
    const string MaterialFolder = "Assets/Museum/Materials";
    const string RigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string BuildingRootName = "Museum_Building";
    const string ExhibitsRootName = "Exhibits";

    const float Cell = 16f;          // gallery footprint (m)
    const float WallThickness = 0.3f;
    const float GalleryHeight = 6f;
    const float LobbyHeight = 9f;
    const float GridMinX = -32f;     // building spans x -32..32, z 0..64
    const int TeleportInteractionLayer = 1 << 31; // "Teleport" layer used by the XRI Starter Assets rig

    enum Side { North, South, East, West }

    class Gallery
    {
        public int Number;
        public string Title;
        public string Era;
        public Color Color;
        public Vector2Int GridCell;
        public Side FeatureWall; // solid outer wall that gets the colored title wall

        public Rect Rect => CellRect(GridCell);
        public string Id => Number.ToString("00");
    }

    // Placeholder titles. Rename them here and re-run the menu item.
    static readonly Gallery[] Galleries =
    {
        new Gallery { Number = 1,  Title = "The Dawn of Games",        Era = "1958 - 1972",   Color = new Color(0.36f, 0.62f, 0.88f), GridCell = new Vector2Int(0, 0), FeatureWall = Side.West  },
        new Gallery { Number = 2,  Title = "Arcade Golden Age",        Era = "1972 - 1983",   Color = new Color(0.93f, 0.36f, 0.50f), GridCell = new Vector2Int(0, 1), FeatureWall = Side.West  },
        new Gallery { Number = 3,  Title = "Home Consoles & The Crash", Era = "1977 - 1983",  Color = new Color(0.95f, 0.56f, 0.26f), GridCell = new Vector2Int(0, 2), FeatureWall = Side.West  },
        new Gallery { Number = 4,  Title = "The 8-Bit Revival",        Era = "1983 - 1990",   Color = new Color(0.86f, 0.27f, 0.27f), GridCell = new Vector2Int(0, 3), FeatureWall = Side.North },
        new Gallery { Number = 5,  Title = "16-Bit Console Wars",      Era = "1988 - 1996",   Color = new Color(0.22f, 0.45f, 0.85f), GridCell = new Vector2Int(1, 3), FeatureWall = Side.North },
        new Gallery { Number = 6,  Title = "Handheld Revolution",      Era = "1989 - Today",  Color = new Color(0.55f, 0.76f, 0.31f), GridCell = new Vector2Int(2, 3), FeatureWall = Side.North },
        new Gallery { Number = 7,  Title = "The 3D Revolution",        Era = "1994 - 2001",   Color = new Color(0.56f, 0.42f, 0.86f), GridCell = new Vector2Int(3, 3), FeatureWall = Side.East  },
        new Gallery { Number = 8,  Title = "Online & HD Era",          Era = "2000 - 2012",   Color = new Color(0.16f, 0.64f, 0.64f), GridCell = new Vector2Int(3, 2), FeatureWall = Side.East  },
        new Gallery { Number = 9,  Title = "Motion & Mobile",          Era = "2006 - 2015",   Color = new Color(0.95f, 0.78f, 0.27f), GridCell = new Vector2Int(3, 1), FeatureWall = Side.East  },
        new Gallery { Number = 10, Title = "Modern Era & VR",          Era = "2013 - Today",  Color = new Color(0.31f, 0.84f, 0.80f), GridCell = new Vector2Int(3, 0), FeatureWall = Side.South },
    };

    static readonly Rect LobbyRect = new Rect(-16f, 0f, 32f, 16f);
    static readonly Rect CourtyardRect = new Rect(-16f, 16f, 32f, 32f);

    struct Opening
    {
        public float Center; // world coordinate along the wall
        public float Width;
        public float Height;
        public Opening(float center, float width, float height) { Center = center; Width = width; Height = height; }
    }

    // Materials
    static Material s_Wall, s_Floor, s_LobbyFloor, s_Ceiling, s_Accent, s_Glass, s_Ground, s_Plaza, s_LightPanel, s_Plinth, s_Grass, s_Foliage;

    [MenuItem("Museum/Build Museum Layout", priority = 2)]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateMaterials();

        var old = GameObject.Find(BuildingRootName);
        if (old != null)
            Object.DestroyImmediate(old);

        var building = Group(BuildingRootName, null);
        BuildSite(Group("Site", building));
        BuildLobby(Group("Lobby", building));
        BuildCourtyard(Group("Courtyard", building));
        foreach (var g in Galleries)
            BuildGallery(Group($"Gallery_{g.Id}", building), g);
        BuildWalls(Group("Walls", building));
        BuildLighting(Group("Lighting", building));

        EnsureExhibitAnchors();
        EnsureXRRig();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings();
        Debug.Log("[Museum] Layout built and saved to " + ScenePath);
    }

    // ------------------------------------------------------------------ site

    static void BuildSite(Transform parent)
    {
        Box("Ground", parent, new Vector3(0f, -0.25f, 32f), new Vector3(300f, 0.4f, 300f), s_Ground);

        var plaza = TeleportFloor("Plaza_Floor", parent);
        Box("Slab", plaza, new Vector3(0f, -0.1f, -10f), new Vector3(48f, 0.2f, 20f), s_Plaza);

        // Entrance canopy
        Box("Canopy", parent, new Vector3(0f, 4.35f, -3f), new Vector3(14f, 0.3f, 6f), s_Accent);
        Box("Canopy_Column_L", parent, new Vector3(-6.5f, 2.1f, -5.5f), new Vector3(0.3f, 4.2f, 0.3f), s_Accent);
        Box("Canopy_Column_R", parent, new Vector3(6.5f, 2.1f, -5.5f), new Vector3(0.3f, 4.2f, 0.3f), s_Accent);

        // Freestanding name sign on the plaza
        Box("Name_Monolith", parent, new Vector3(-11f, 1.3f, -7f), new Vector3(5f, 2.6f, 0.5f), s_Accent);
        AddText(parent, "PRESS START\n<size=50%>Museum of Video Game History</size>",
            new Vector3(-11f, 1.4f, -7.27f), Vector3.back, new Vector2(4.6f, 2f), Color.white);
    }

    // ----------------------------------------------------------------- lobby

    static void BuildLobby(Transform parent)
    {
        var floor = TeleportFloor("Lobby_Floor", parent);
        Box("Slab", floor, new Vector3(0f, -0.1f, 8f), new Vector3(32f, 0.2f, 16f), s_LobbyFloor);

        // Roof with a central skylight (x -8..8, z 4..12)
        float y = LobbyHeight + 0.15f;
        var roof = Group("Roof", parent);
        Box("Roof_South", roof, new Vector3(0f, y, 2f), new Vector3(32f, 0.3f, 4f), s_Ceiling);
        Box("Roof_North", roof, new Vector3(0f, y, 14f), new Vector3(32f, 0.3f, 4f), s_Ceiling);
        Box("Roof_West", roof, new Vector3(-12f, y, 8f), new Vector3(8f, 0.3f, 8f), s_Ceiling);
        Box("Roof_East", roof, new Vector3(12f, y, 8f), new Vector3(8f, 0.3f, 8f), s_Ceiling);
        Box("Skylight_Glass", roof, new Vector3(0f, LobbyHeight + 0.25f, 8f), new Vector3(16f, 0.05f, 8f), s_Glass, shadows: false);
        for (int i = 1; i < 4; i++)
            Box($"Skylight_Beam_{i}", roof, new Vector3(-8f + i * 4f, LobbyHeight + 0.1f, 8f), new Vector3(0.15f, 0.2f, 8f), s_Accent);

        // Furniture blockout
        Box("Reception_Desk", parent, new Vector3(8f, 0.55f, 10.5f), new Vector3(6f, 1.1f, 1.2f), s_Accent);
        Box("Bench_West", parent, new Vector3(-7f, 0.23f, 5f), new Vector3(3.5f, 0.45f, 0.9f), s_Accent);
        Box("Bench_East", parent, new Vector3(7f, 0.23f, 5f), new Vector3(3.5f, 0.45f, 0.9f), s_Accent);

        // Museum title hung on the courtyard glass
        Sign(parent, "Title_Sign", "PRESS START\n<size=45%>A History of Video Games</size>",
            new Vector3(0f, 6.3f, 16f - 0.15f), Vector3.back, new Vector2(12f, 2.6f), s_Accent);
        Sign(parent, "Courtyard_Sign", "COURTYARD",
            new Vector3(0f, 4.1f, 16f - 0.15f), Vector3.back, new Vector2(3.5f, 0.6f), s_Accent);

        // Map / directory placeholder beside the reception desk
        Sign(parent, "Directory_Placeholder", "MUSEUM MAP\n<size=50%>(placeholder)</size>",
            new Vector3(15.8f - WallThickness / 2f, 2.2f, 13f), Vector3.left, new Vector2(3f, 2.5f), s_Accent);
    }

    // ------------------------------------------------------------- courtyard

    static void BuildCourtyard(Transform parent)
    {
        var c = CourtyardRect.center;
        var floor = TeleportFloor("Courtyard_Floor", parent);
        Box("Slab", floor, new Vector3(c.x, -0.1f, c.y), new Vector3(CourtyardRect.width, 0.2f, CourtyardRect.height), s_Plaza);

        // Centerpiece platform (a spot for a hero piece, e.g. a giant controller)
        Cylinder("Centerpiece_Platform", parent, new Vector3(c.x, 0.15f, c.y), 7f, 0.3f, s_Plinth);

        // Four planters with simple trees
        var offsets = new[] { new Vector2(-9f, -9f), new Vector2(9f, -9f), new Vector2(-9f, 9f), new Vector2(9f, 9f) };
        for (int i = 0; i < offsets.Length; i++)
        {
            var p = new Vector3(c.x + offsets[i].x, 0f, c.y + offsets[i].y);
            var planter = Group($"Planter_{i + 1}", parent);
            Box("Box", planter, p + Vector3.up * 0.3f, new Vector3(6f, 0.6f, 6f), s_Accent);
            Box("Grass", planter, p + Vector3.up * 0.61f, new Vector3(5.6f, 0.02f, 5.6f), s_Grass, collider: false);
            Cylinder("Trunk", planter, p + Vector3.up * 2.1f, 0.35f, 3f, s_Accent);
            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = "Canopy";
            canopy.transform.SetParent(planter, false);
            canopy.transform.localPosition = p + Vector3.up * 4.3f;
            canopy.transform.localScale = Vector3.one * 3.2f;
            Finish(canopy, s_Foliage, collider: false, shadows: true);
        }

        // Benches around the centerpiece
        for (int i = 0; i < 4; i++)
        {
            var dir = Quaternion.Euler(0f, 45f + 90f * i, 0f) * Vector3.forward;
            var bench = Box($"Bench_{i + 1}", parent, new Vector3(c.x, 0.23f, c.y) + dir * 6f, new Vector3(3f, 0.45f, 0.8f), s_Accent);
            bench.transform.localRotation = Quaternion.LookRotation(dir);
        }
    }

    // --------------------------------------------------------------- gallery

    static void BuildGallery(Transform parent, Gallery g)
    {
        var r = g.Rect;
        var center = new Vector3(r.center.x, 0f, r.center.y);

        var floor = TeleportFloor("Floor", parent);
        Box("Slab", floor, center + Vector3.down * 0.1f, new Vector3(Cell, 0.2f, Cell), s_Floor);

        Box("Ceiling", parent, center + Vector3.up * (GalleryHeight + 0.15f), new Vector3(Cell, 0.3f, Cell), s_Ceiling);
        Box("Light_Panel", parent, center + Vector3.up * (GalleryHeight - 0.02f), new Vector3(9f, 0.04f, 9f), s_LightPanel, collider: false, shadows: false);

        // Colored feature wall with the gallery title
        var accent = GetOrCreateMaterial($"M_Gallery_{g.Id}", Color.Lerp(g.Color, Color.white, 0.15f), 0.1f);
        Vector3 inward = SideNormal(g.FeatureWall) * -1f;
        Vector3 wallPos = center - inward * (Cell / 2f - WallThickness / 2f - 0.03f) + Vector3.up * 2.9f;
        bool facesX = g.FeatureWall == Side.East || g.FeatureWall == Side.West;
        Vector3 panelSize = facesX ? new Vector3(0.05f, 5.2f, Cell - 2f) : new Vector3(Cell - 2f, 5.2f, 0.05f);
        Box("Feature_Wall", parent, wallPos, panelSize, accent, collider: false);
        AddText(parent, $"<size=45%>GALLERY {g.Id}</size>\n{g.Title}\n<size=45%>{g.Era}</size>",
            wallPos + inward * 0.04f + Vector3.up * 1.2f, inward, new Vector2(11f, 2.4f), Color.white);
    }

    // ----------------------------------------------------------------- walls

    static void BuildWalls(Transform parent)
    {
        float h = GalleryHeight, t = WallThickness / 2f;

        // Outer shell (solid)
        var outer = Group("Outer", parent);
        BuildWall(outer, "Outer_West", new Vector3(-32f, 0f, -t), new Vector3(-32f, 0f, 64f + t), h, s_Wall);
        BuildWall(outer, "Outer_East", new Vector3(32f, 0f, -t), new Vector3(32f, 0f, 64f + t), h, s_Wall);
        BuildWall(outer, "Outer_North", new Vector3(-32f - t, 0f, 64f), new Vector3(32f + t, 0f, 64f), h, s_Wall);
        BuildWall(outer, "Outer_South_West", new Vector3(-32f - t, 0f, 0f), new Vector3(-16f, 0f, 0f), h, s_Wall);
        BuildWall(outer, "Outer_South_East", new Vector3(16f, 0f, 0f), new Vector3(32f + t, 0f, 0f), h, s_Wall);

        // Lobby glass facade with the main entrance, and the glass wall onto the courtyard
        BuildGlassWall(outer, "Lobby_Facade", new Vector3(-16f, 0f, 0f), new Vector3(16f, 0f, 0f), LobbyHeight,
            new Opening(0f, 6f, 3.5f));
        BuildGlassWall(parent, "Lobby_Courtyard_Glass", new Vector3(-16f, 0f, 16f), new Vector3(16f, 0f, 16f), LobbyHeight,
            new Opening(0f, 4f, 3.5f));

        // Gallery glass walls looking into the courtyard
        var court = Group("Courtyard_Glass", parent);
        BuildGlassWall(court, "Glass_West", new Vector3(-16f, 0f, 16f), new Vector3(-16f, 0f, 48f), h);
        BuildGlassWall(court, "Glass_East", new Vector3(16f, 0f, 16f), new Vector3(16f, 0f, 48f), h);
        BuildGlassWall(court, "Glass_North", new Vector3(-16f, 0f, 48f), new Vector3(16f, 0f, 48f), h);

        // Partitions along the visitor loop: Lobby -> G1 -> ... -> G10 -> Lobby
        var loop = Group("Loop_Partitions", parent);
        var rooms = new List<(Rect rect, string label)> { (LobbyRect, "LOBBY") };
        rooms.AddRange(Galleries.Select(g => (g.Rect, $"{g.Id}  {g.Title}")));
        rooms.Add((LobbyRect, "LOBBY  /  EXIT"));

        for (int i = 0; i < rooms.Count - 1; i++)
        {
            var from = rooms[i];
            var to = rooms[i + 1];
            if (!SharedEdge(from.rect, to.rect, out var a, out var b, out var towardFrom))
            {
                Debug.LogError($"[Museum] Rooms {i} and {i + 1} do not share a wall.");
                continue;
            }

            bool touchesLobby = from.rect == LobbyRect || to.rect == LobbyRect;
            float wallHeight = touchesLobby ? LobbyHeight : GalleryHeight;
            float doorWidth = touchesLobby ? 5f : 4f;
            float doorHeight = touchesLobby ? 4.5f : 4f;
            bool alongX = Mathf.Abs(b.x - a.x) > 0.01f;
            float mid = alongX ? (a.x + b.x) / 2f : (a.z + b.z) / 2f;

            BuildWall(loop, $"Partition_{i:00}_{i + 1:00}", a, b, wallHeight, s_Wall, new Opening(mid, doorWidth, doorHeight));

            // Sign above the doorway, on the side the visitor approaches from
            var doorCenter = (a + b) / 2f;
            string label = i == 0 ? $"START HERE  >  {to.label}" : to.label;
            Sign(loop, $"Door_Sign_{i + 1:00}", label,
                doorCenter + towardFrom * (WallThickness / 2f + 0.04f) + Vector3.up * (doorHeight + 0.6f),
                towardFrom, new Vector2(5.5f, 0.7f), s_Accent);
        }
    }

    // -------------------------------------------------------------- lighting

    static void BuildLighting(Transform parent)
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.transform.SetParent(parent, false);
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.9f);
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, 20f, 0f);
        RenderSettings.sun = sun;

        // Trilight ambient works on Quest without baking lighting first
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.80f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.62f, 0.64f);
        RenderSettings.ambientGroundColor = new Color(0.40f, 0.40f, 0.40f);
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");

        foreach (var g in Galleries)
            PointLight(parent, $"Light_Gallery_{g.Id}", new Vector3(g.Rect.center.x, GalleryHeight - 1f, g.Rect.center.y), 13f, 25f);
        PointLight(parent, "Light_Lobby_West", new Vector3(-9f, LobbyHeight - 2f, 8f), 15f, 35f);
        PointLight(parent, "Light_Lobby_East", new Vector3(9f, LobbyHeight - 2f, 8f), 15f, 35f);
    }

    static void PointLight(Transform parent, string name, Vector3 pos, float range, float intensity)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localPosition = pos;
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.None;
    }

    // ------------------------------------------------------ exhibits and rig

    static void EnsureExhibitAnchors()
    {
        var root = GameObject.Find(ExhibitsRootName);
        if (root == null)
            root = new GameObject(ExhibitsRootName);

        foreach (var g in Galleries)
        {
            string prefix = $"Exhibit_{g.Id}";
            bool exists = root.transform.Cast<Transform>().Any(child => child.name.StartsWith(prefix));
            if (exists)
                continue;

            var anchor = new GameObject($"{prefix}_{g.Title.Replace(" ", "").Replace("&", "And")}").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = new Vector3(g.Rect.center.x, 0f, g.Rect.center.y);
            Cylinder("Plinth_Placeholder", anchor, new Vector3(0f, 0.25f, 0f), 3f, 0.5f, s_Plinth);
        }
    }

    static void EnsureXRRig()
    {
        if (Object.FindAnyObjectByType<XROrigin>() != null)
            return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[Museum] XR Origin prefab not found at " + RigPrefabPath + ". Import XRI Starter Assets, then run this again.");
            return;
        }
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.transform.SetPositionAndRotation(new Vector3(0f, 0f, 3f), Quaternion.identity); // just inside the entrance, facing north
    }

    static void AddSceneToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.path != ScenePath)
            .Select(s => new EditorBuildSettingsScene(s.path, false))
            .ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // --------------------------------------------------------- wall helpers

    static void BuildWall(Transform parent, string name, Vector3 a, Vector3 b, float height, Material mat, params Opening[] openings)
        => BuildWallInternal(parent, name, a, b, height, WallThickness, mat, false, openings);

    static void BuildGlassWall(Transform parent, string name, Vector3 a, Vector3 b, float height, params Opening[] openings)
        => BuildWallInternal(parent, name, a, b, height, 0.06f, s_Glass, true, openings);

    static void BuildWallInternal(Transform parent, string name, Vector3 a, Vector3 b, float height, float thickness,
        Material mat, bool glass, Opening[] openings)
    {
        var root = Group(name, parent);
        bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
        float start = alongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.z, b.z);
        float end = alongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.z, b.z);
        float fixedCoord = alongX ? a.z : a.x;

        Vector3 Pos(float along, float y) => alongX ? new Vector3(along, y, fixedCoord) : new Vector3(fixedCoord, y, along);
        Vector3 Size(float length, float h, float depth) => alongX ? new Vector3(length, h, depth) : new Vector3(depth, h, length);

        int index = 0;
        void Segment(float s, float e, float y0, float y1)
        {
            if (e - s < 0.01f || y1 - y0 < 0.01f)
                return;
            var seg = Box($"Segment_{index++}", root, Pos((s + e) / 2f, (y0 + y1) / 2f), Size(e - s, y1 - y0, thickness), mat, shadows: !glass);
            if (!glass)
                return;

            // Mullions and rails give the glass a modern curtain-wall look
            Box("Rail_Top", seg.transform.parent, Pos((s + e) / 2f, y1 - 0.05f), Size(e - s, 0.1f, 0.14f), s_Accent, collider: false);
            Box("Rail_Bottom", seg.transform.parent, Pos((s + e) / 2f, y0 + 0.05f), Size(e - s, 0.1f, 0.14f), s_Accent, collider: false);
            int count = Mathf.Max(1, Mathf.CeilToInt((e - s) / 2.7f));
            for (int i = 0; i <= count; i++)
            {
                float along = Mathf.Lerp(s, e, i / (float)count);
                Box("Mullion", seg.transform.parent, Pos(along, (y0 + y1) / 2f), Size(0.08f, y1 - y0, 0.14f), s_Accent, collider: false);
            }
        }

        float cursor = start;
        foreach (var o in openings.OrderBy(o => o.Center))
        {
            float o0 = o.Center - o.Width / 2f, o1 = o.Center + o.Width / 2f;
            Segment(cursor, o0, 0f, height);
            Segment(o0, o1, o.Height, height); // lintel above the doorway
            if (!glass)
            {
                // Dark reveal frame around the doorway
                Box("Door_Jamb", root, Pos(o0 + 0.06f, o.Height / 2f), Size(0.12f, o.Height, thickness + 0.08f), s_Accent, collider: false);
                Box("Door_Jamb", root, Pos(o1 - 0.06f, o.Height / 2f), Size(0.12f, o.Height, thickness + 0.08f), s_Accent, collider: false);
                Box("Door_Head", root, Pos(o.Center, o.Height - 0.06f), Size(o.Width, 0.12f, thickness + 0.08f), s_Accent, collider: false);
            }
            cursor = o1;
        }
        Segment(cursor, end, 0f, height);
    }

    /// <summary>Finds the wall shared by two rooms. towardA points from the wall into room A.</summary>
    static bool SharedEdge(Rect a, Rect b, out Vector3 from, out Vector3 to, out Vector3 towardA)
    {
        from = to = towardA = Vector3.zero;
        if (Near(a.xMax, b.xMin) || Near(a.xMin, b.xMax))
        {
            float x = Near(a.xMax, b.xMin) ? a.xMax : a.xMin;
            float z0 = Mathf.Max(a.yMin, b.yMin), z1 = Mathf.Min(a.yMax, b.yMax);
            if (z1 - z0 > 0.01f)
            {
                from = new Vector3(x, 0f, z0);
                to = new Vector3(x, 0f, z1);
                towardA = Near(a.xMax, b.xMin) ? Vector3.left : Vector3.right;
                return true;
            }
        }
        if (Near(a.yMax, b.yMin) || Near(a.yMin, b.yMax))
        {
            float z = Near(a.yMax, b.yMin) ? a.yMax : a.yMin;
            float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
            if (x1 - x0 > 0.01f)
            {
                from = new Vector3(x0, 0f, z);
                to = new Vector3(x1, 0f, z);
                towardA = Near(a.yMax, b.yMin) ? Vector3.back : Vector3.forward;
                return true;
            }
        }
        return false;
    }

    static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    static Rect CellRect(Vector2Int cell) => new Rect(GridMinX + cell.x * Cell, cell.y * Cell, Cell, Cell);

    static Vector3 SideNormal(Side side) => side switch
    {
        Side.North => Vector3.forward,
        Side.South => Vector3.back,
        Side.East => Vector3.right,
        _ => Vector3.left,
    };

    // ------------------------------------------------------ object helpers

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>Empty parent with a TeleportationArea; every collider under it becomes teleportable.</summary>
    static Transform TeleportFloor(string name, Transform parent)
    {
        var root = Group(name, parent);
        var area = root.gameObject.AddComponent<TeleportationArea>();
        area.interactionLayers = TeleportInteractionLayer;
        return root;
    }

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool collider = true, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        Finish(go, mat, collider, shadows);
        return go;
    }

    static GameObject Cylinder(string name, Transform parent, Vector3 center, float diameter, float height, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = new Vector3(diameter, height / 2f, diameter); // Unity cylinder is 2 m tall
        // The default capsule collider balloons on flat cylinders, so use the real mesh instead
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.AddComponent<MeshCollider>();
        Finish(go, mat, true, true);
        return go;
    }

    static void Finish(GameObject go, Material mat, bool collider, bool shadows)
    {
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (!collider)
            Object.DestroyImmediate(go.GetComponent<Collider>());

        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        if (mat != s_Glass)
            flags |= StaticEditorFlags.OccluderStatic;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    static void Sign(Transform parent, string name, string text, Vector3 center, Vector3 facing, Vector2 size, Material panelMat)
    {
        var root = Group(name, parent);
        bool facesX = Mathf.Abs(facing.x) > 0.5f;
        Box("Panel", root, center, facesX ? new Vector3(0.06f, size.y, size.x) : new Vector3(size.x, size.y, 0.06f), panelMat, collider: false, shadows: false);
        AddText(root, text, center + facing * 0.04f, facing, size * 0.9f, Color.white);
    }

    /// <summary>World-space TextMeshPro text readable by someone standing on the <paramref name="facing"/> side.</summary>
    static TextMeshPro AddText(Transform parent, string text, Vector3 position, Vector3 facing, Vector2 size, Color color)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshPro>();
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.LookRotation(-facing);
        tmp.rectTransform.sizeDelta = size;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.5f;
        tmp.fontSizeMax = 60f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    // ------------------------------------------------------------ materials

    static void CreateMaterials()
    {
        EnsureFolder(MaterialFolder);
        s_Wall = GetOrCreateMaterial("M_Wall", new Color(0.93f, 0.93f, 0.92f), 0.1f);
        s_Floor = GetOrCreateMaterial("M_Floor", new Color(0.72f, 0.71f, 0.69f), 0.35f);
        s_LobbyFloor = GetOrCreateMaterial("M_Floor_Lobby", new Color(0.26f, 0.27f, 0.29f), 0.7f);
        s_Ceiling = GetOrCreateMaterial("M_Ceiling", new Color(0.96f, 0.96f, 0.96f), 0.05f);
        s_Accent = GetOrCreateMaterial("M_Accent_Dark", new Color(0.13f, 0.14f, 0.16f), 0.4f);
        s_Plinth = GetOrCreateMaterial("M_Plinth", new Color(0.97f, 0.97f, 0.97f), 0.3f);
        s_Plaza = GetOrCreateMaterial("M_Plaza", new Color(0.62f, 0.62f, 0.60f), 0.15f);
        s_Ground = GetOrCreateMaterial("M_Ground", new Color(0.36f, 0.46f, 0.32f), 0.05f);
        s_Grass = GetOrCreateMaterial("M_Grass", new Color(0.33f, 0.52f, 0.28f), 0.05f);
        s_Foliage = GetOrCreateMaterial("M_Foliage", new Color(0.28f, 0.48f, 0.30f), 0.1f);
        s_LightPanel = GetOrCreateMaterial("M_Light_Panel", Color.white, 0.5f, emission: new Color(1.4f, 1.4f, 1.35f));
        s_Glass = GetOrCreateMaterial("M_Glass", new Color(0.75f, 0.88f, 0.95f, 0.18f), 0.95f, transparent: true);
    }

    /// <summary>
    /// Creates a flat-color URP Lit material the first time only. Later runs reuse the asset,
    /// so textures or colors changed by hand in the editor are kept.
    /// </summary>
    static Material GetOrCreateMaterial(string name, Color color, float smoothness, bool transparent = false, Color? emission = null)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
            return mat;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);

        if (transparent)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
