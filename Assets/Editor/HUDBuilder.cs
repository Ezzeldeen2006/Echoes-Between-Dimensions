// Builds the in-scene UI objects for the two unclaimed bonus tasks in Echoes Between Dimensions.
//
//   (d) Advanced UI  -- a gun heat indicator, and an inventory shown while TAB is held
//   (f) Minimap      -- circular, bordered, player and enemies as dots, rotating with the camera
//
// GameHUD.cs and Minimap.cs are the behaviour. This file is the other half: the GameObjects,
// the sprites, the RenderTexture and the wiring. It exists as an editor command rather than as
// hand-placed objects for one reason -- a scene is a 40,000-line YAML file, and a UI built by
// hand in it cannot be reviewed, re-run, or corrected without doing the whole thing again.
// Running this twice produces the same result; it deletes its own previous output first.
//
// Run from Tools > Echoes > 3. Build the HUD and minimap.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class HUDBuilder
{
    // Everything this command creates lives under one root, named here so a re-run can find
    // and remove it. Without that, running it twice would silently stack two HUDs -- both
    // updating, one drawn on top of the other, and the duplicate invisible until something
    // flickered.
    const string RootName = "--- HUD (generated) ---";

    const string SpriteDir = "Assets/GameAssets/HUD";

    // The palette, kept identical to the constants in GameHUD.cs. Cyan is the scanner's own
    // light, which is why it reads as "this is your equipment" without a legend.
    static readonly Color Cyan = new Color(0.29f, 0.85f, 0.95f);
    static readonly Color Steel = new Color(0.66f, 0.72f, 0.78f);
    static readonly Color PanelFill = new Color(0.04f, 0.06f, 0.09f, 0.82f);
    static readonly Color Hostile = new Color(0.96f, 0.28f, 0.27f);

    [MenuItem("Tools/Echoes/3. Build the HUD and minimap", priority = 3)]
    public static void Build()
    {
        if (!EnsureSceneOpen()) return;

        var player = FindPlayer();
        if (player == null)
        {
            Debug.LogError("[HUDBuilder] no PlayerHealth in the scene -- nothing to attach the HUD to.");
            return;
        }

        var sprites = BuildSprites();

        // Remove a previous run before building, so this is idempotent.
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("[HUDBuilder] removed the previous generated HUD.");
        }

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build HUD");

        var canvas = BuildCanvas(root.transform);
        var hud = root.AddComponent<GameHUD>();
        hud.playerHealth = player.GetComponent<PlayerHealth>();
        hud.gunSystem = player.GetComponent<GunSystem>();
        hud.itemHolder = player.GetComponent<ItemHolder>();

        BuildHealth(canvas, sprites, hud);
        BuildHeat(canvas, sprites, hud);
        BuildInventory(canvas, sprites, hud);
        BuildMinimap(root, canvas, sprites, player.transform);

        RetireTheOldHealthBar();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[HUDBuilder] built. Health, heat, inventory (hold TAB) and minimap (toggle M).");
    }

    // ---------------------------------------------------------------------------------
    // Canvas
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A canvas of its own rather than reusing the existing one.
    ///
    /// <para>The scene's Canvas holds the crosshairs, which are enabled and disabled by the
    /// aiming code. Parenting the HUD there would tie the health readout's lifetime to the
    /// crosshair's, so raising the gun would hide the health bar -- the kind of coupling that
    /// is obvious once seen and invisible in a hierarchy.</para>
    ///
    /// <para>Sort order 10 puts it above the crosshair canvases. It is drawn last and so wins
    /// overlaps, which is right: a crosshair over a heat gauge is worse than the reverse.</para>
    /// </summary>
    static RectTransform BuildCanvas(Transform parent)
    {
        var go = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        // The same settings FixCanvasScaling enforces everywhere else -- this canvas is created
        // correct rather than created wrong and corrected by the other command.
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Nothing in this HUD is clickable. Leaving the raycaster on would put an invisible
        // full-screen input target over a game that is played with the mouse.
        go.GetComponent<GraphicRaycaster>().enabled = false;

        return go.GetComponent<RectTransform>();
    }

    // ---------------------------------------------------------------------------------
    // Health -- three blocks, bottom left
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <para>Three hit points drawn as three blocks rather than as a bar. A continuous bar
    /// showing three of anything is the least informative option available: at two thirds full
    /// you have to measure it. Blocks are countable at a glance and in peripheral vision, which
    /// is where a health readout is actually read.</para>
    /// </summary>
    static void BuildHealth(RectTransform canvas, Sprites sprites, GameHUD hud)
    {
        var group = Panel(canvas, "Health", new Vector2(0, 0), new Vector2(40, 40), new Vector2(312, 74));
        hud.healthGroup = group.gameObject.AddComponent<CanvasGroup>();
        hud.healthGroup.blocksRaycasts = false;
        hud.healthGroup.interactable = false;

        Label(group, "HULL", new Vector2(14, -8), new Vector2(120, 18), 14, Steel, sprites.font);

        const int count = 3;
        const float width = 88f;
        const float gap = 8f;
        var segments = new Image[count];

        for (var i = 0; i < count; i++)
        {
            var image = Sprite(group, "Segment" + i, sprites.segment);
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(width, 22);
            rt.anchoredPosition = new Vector2(14 + i * (width + gap), -32);
            image.color = Cyan;
            segments[i] = image;
        }

        hud.healthSegments = segments;
    }

    // ---------------------------------------------------------------------------------
    // Heat -- shown only while the gun is held
    // ---------------------------------------------------------------------------------

    static void BuildHeat(RectTransform canvas, Sprites sprites, GameHUD hud)
    {
        var group = Panel(canvas, "Heat", new Vector2(0, 0), new Vector2(40, 124), new Vector2(312, 60));
        hud.heatRoot = group.gameObject;

        hud.heatLabel = Label(group, "HEAT", new Vector2(14, -8), new Vector2(180, 18), 14, Steel, sprites.font);

        // The track the fill runs along, so an empty gauge still reads as a gauge rather than
        // as nothing at all.
        var track = Sprite(group, "Track", sprites.segment);
        Place(track.rectTransform, new Vector2(14, -32), new Vector2(284, 16));
        track.color = new Color(0.10f, 0.13f, 0.17f, 0.95f);

        // Behind the fill and slightly larger: the bleed of a hot component, not a UI border.
        var glow = Sprite(group, "Glow", sprites.glow);
        Place(glow.rectTransform, new Vector2(6, -24), new Vector2(300, 32));
        glow.color = new Color(1f, 1f, 1f, 0f);
        glow.raycastTarget = false;
        hud.heatGlow = glow;

        var fill = Sprite(group, "Fill", sprites.segment);
        Place(fill.rectTransform, new Vector2(14, -32), new Vector2(284, 16));
        // Filled/Horizontal is what makes fillAmount mean anything. Left as Simple, the image
        // would ignore fillAmount entirely and the gauge would be permanently full -- a bug
        // that looks like "the heat system does not work" rather than like a UI setting.
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;
        fill.color = Cyan;
        hud.heatFill = fill;
    }

    // ---------------------------------------------------------------------------------
    // Inventory -- hold TAB
    // ---------------------------------------------------------------------------------

    static void BuildInventory(RectTransform canvas, Sprites sprites, GameHUD hud)
    {
        var group = Panel(canvas, "Inventory", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340, 200));
        var rt = group;
        rt.anchoredPosition = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        hud.inventoryGroup = group.gameObject.AddComponent<CanvasGroup>();
        // Starts invisible. Built visible, the first frame of every play session would flash
        // the panel before Update ran.
        hud.inventoryGroup.alpha = 0f;
        hud.inventoryGroup.blocksRaycasts = false;
        hud.inventoryGroup.interactable = false;

        Label(group, "EQUIPMENT", new Vector2(16, -10), new Vector2(200, 20), 15, Cyan, sprites.font);

        hud.gunIcon = Slot(group, sprites, "Gun", sprites.gun, new Vector2(24, -44), "LASER");
        hud.scannerIcon = Slot(group, sprites, "Scanner", sprites.scanner, new Vector2(184, -44), "SCANNER");
    }

    /// <summary>One inventory cell: a bordered box, an icon, and a caption under it.</summary>
    static Image Slot(RectTransform parent, Sprites sprites, string name, Sprite icon, Vector2 position, string caption)
    {
        var box = Sprite(parent, name + "Slot", sprites.slot);
        Place(box.rectTransform, position, new Vector2(132, 132));
        box.color = new Color(0.10f, 0.14f, 0.19f, 0.9f);

        var image = Sprite(box.rectTransform, name + "Icon", icon);
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 12);
        rt.sizeDelta = new Vector2(84, 84);
        // Preserves the icon's own proportions if the box is ever resized, rather than
        // stretching a gun into a rectangle.
        image.preserveAspect = true;

        var label = Label(box.rectTransform, caption, new Vector2(0, -46), new Vector2(132, 18), 13, Steel, sprites.font);
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0, -46);
        label.alignment = TextAnchor.MiddleCenter;

        return image;
    }

    // ---------------------------------------------------------------------------------
    // Minimap
    // ---------------------------------------------------------------------------------

    static void BuildMinimap(GameObject root, RectTransform canvas, Sprites sprites, Transform player)
    {
        const float size = 236f;
        const float inner = 208f;

        var minimap = root.AddComponent<Minimap>();
        minimap.player = player;

        // ---- the widget -------------------------------------------------------------
        var container = new GameObject("Minimap", typeof(RectTransform));
        container.transform.SetParent(canvas, false);
        container.layer = LayerMask.NameToLayer("UI");
        var crt = container.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1, 1);
        crt.anchoredPosition = new Vector2(-40, -40);
        crt.sizeDelta = new Vector2(size, size);
        minimap.container = container;

        // The circular mask. A Mask needs a graphic to cut against, so this is a filled circle
        // whose own pixels are never seen -- showMaskGraphic is off. This is what makes the map
        // circular, which the brief asks for by name.
        var maskImage = Sprite(crt, "Mask", sprites.circle);
        var mrt = maskImage.rectTransform;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.anchoredPosition = Vector2.zero;
        mrt.sizeDelta = new Vector2(inner, inner);
        var mask = maskImage.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Rotated by Minimap.AlignWithCamera. The map texture AND the dots are both under here,
        // which is why the dots can be positioned in plain north-up world space -- they inherit
        // the rotation instead of each applying it.
        var mapRoot = new GameObject("MapRoot", typeof(RectTransform));
        mapRoot.transform.SetParent(mrt, false);
        mapRoot.layer = LayerMask.NameToLayer("UI");
        var maprt = mapRoot.GetComponent<RectTransform>();
        maprt.anchorMin = maprt.anchorMax = maprt.pivot = new Vector2(0.5f, 0.5f);
        maprt.sizeDelta = new Vector2(inner, inner);
        minimap.mapRoot = maprt;

        // The camera's view. A RawImage, not an Image: a RenderTexture is not a Sprite, and
        // wrapping one in a Sprite every frame is not a thing you want to be doing.
        var view = new GameObject("View", typeof(RectTransform), typeof(RawImage));
        view.transform.SetParent(maprt, false);
        view.layer = LayerMask.NameToLayer("UI");
        var vrt = view.GetComponent<RectTransform>();
        vrt.anchorMin = vrt.anchorMax = vrt.pivot = new Vector2(0.5f, 0.5f);
        // sqrt(2) larger than the circle, so the square texture still covers the circle at 45
        // degrees of rotation. Without this the corners swing inward as the player turns and
        // the map shows blank wedges -- the exact artefact the camera is kept north-up to avoid,
        // reintroduced at the UI layer.
        vrt.sizeDelta = new Vector2(inner * 1.4143f, inner * 1.4143f);

        var texture = CreateRenderTexture();
        view.GetComponent<RawImage>().texture = texture;

        // Dots sit above the texture and inside the same rotating parent.
        var dotLayer = new GameObject("Dots", typeof(RectTransform));
        dotLayer.transform.SetParent(maprt, false);
        dotLayer.layer = LayerMask.NameToLayer("UI");
        var drt = dotLayer.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        // Minimap.DrawDots reads rect.width to convert world units into pixels, so this size is
        // load-bearing, not decorative: it must match the visible circle or every dot lands in
        // the wrong place by a constant factor.
        drt.sizeDelta = new Vector2(inner, inner);
        minimap.dotLayer = drt;

        var playerDot = Sprite(drt, "PlayerDot", sprites.dot);
        Centre(playerDot.rectTransform, new Vector2(16, 16));
        playerDot.color = Cyan;
        minimap.playerDot = playerDot;

        var enemyDot = Sprite(drt, "EnemyDotTemplate", sprites.dot);
        Centre(enemyDot.rectTransform, new Vector2(14, 14));
        enemyDot.color = Hostile;
        // The template is never shown; Minimap clones it. Inactive so it is not a permanent
        // fourth robot sitting at the centre of the map.
        enemyDot.gameObject.SetActive(false);
        minimap.enemyDotPrefab = enemyDot;

        // The border goes OUTSIDE the mask, as a sibling. Inside it, the mask would clip the
        // ring's own outer edge and leave a border thinner on the outside than the inside.
        var border = Sprite(crt, "Border", sprites.ring);
        Centre(border.rectTransform, new Vector2(size, size));
        border.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f);
        border.raycastTarget = false;

        // ---- the camera --------------------------------------------------------------
        var camGo = new GameObject("MinimapCamera");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = minimap.worldRadius;
        cam.targetTexture = texture;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.07f, 0.10f, 1f);
        // Only what is near the ground plane. A 1000-unit far plane on a top-down orthographic
        // camera renders the skybox volume and every distant object for no visible benefit on a
        // 256-pixel texture.
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 200f;
        cam.cullingMask = MinimapCullingMask();
        // No audio listener is added deliberately: a second one makes Unity warn every frame
        // and silently ignore one of them.

        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderType = CameraRenderType.Base;
        // Everything optional is off. This camera renders a 256x256 square every frame on a
        // machine that also has to run the game; post-processing and shadows on a map that
        // shows coloured blobs would be paying full price for nothing.
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.antialiasing = AntialiasingMode.None;

        minimapCameraDepthBelowMain(cam);
        minimap.minimapCamera = cam;
    }

    /// <summary>
    /// Renders before the main camera. Both draw this frame regardless, but a render target
    /// that is written after it was sampled shows the previous frame -- a one-frame lag that
    /// is invisible standing still and smears while turning.
    /// </summary>
    static void minimapCameraDepthBelowMain(Camera cam)
    {
        cam.depth = Camera.main != null ? Camera.main.depth - 1f : -1f;
    }

    /// <summary>
    /// Everything except the player, the robots and the UI.
    ///
    /// <para><b>This is the brief's requirement, not an aesthetic choice.</b> It asks that the
    /// player and enemies "must not be directly visible" on the map -- they are represented by
    /// dots. Culling the layers is the literal reading: they are not rendered at all, rather
    /// than rendered and covered by a dot that happens to be larger.</para>
    ///
    /// <para>It is also what makes the dots work. A dot drawn at the player's screen position
    /// shows an enemy that is behind a building; a rendered robot would disappear behind the
    /// roof, which is the one thing a minimap exists to prevent.</para>
    /// </summary>
    static int MinimapCullingMask()
    {
        var mask = ~0;
        foreach (var layer in new[] { "Playerbody", "Robotlayer", "UI" })
        {
            var index = LayerMask.NameToLayer(layer);
            if (index < 0)
            {
                Debug.LogWarning($"[HUDBuilder] layer '{layer}' does not exist -- it cannot be culled from the minimap.");
                continue;
            }
            mask &= ~(1 << index);
        }
        return mask;
    }

    static RenderTexture CreateRenderTexture()
    {
        Directory.CreateDirectory(SpriteDir);
        var path = SpriteDir + "/MinimapTexture.renderTexture";

        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (existing != null) return existing;

        // 256 square. The widget is 208 points on a 1080p reference, so this is already above
        // one texel per point; more would cost fill rate on a WebGL build to render detail that
        // is then scaled down.
        var texture = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32)
        {
            name = "MinimapTexture",
            filterMode = FilterMode.Bilinear,
            // Nothing samples outside the edge, and Clamp avoids the far side of the map
            // bleeding in at the rim if anything ever does.
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1
        };
        AssetDatabase.CreateAsset(texture, path);
        AssetDatabase.SaveAssets();
        return texture;
    }

    // ---------------------------------------------------------------------------------
    // The old health bar
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Switches off the original Slider health bar, since the segments replace it.
    ///
    /// <para>Deactivated rather than deleted, and PlayerHealth.healthSlider is left wired. The
    /// old bar is still updated by UpdateHealthUI -- writing to an inactive Slider is harmless
    /// -- so turning the object back on restores the previous UI exactly. Deleting it would
    /// also null a serialized reference in the scene, which is a messier thing to undo than a
    /// checkbox.</para>
    /// </summary>
    static void RetireTheOldHealthBar()
    {
        foreach (var slider in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var name = slider.name.ToLowerInvariant();
            if (!name.Contains("health") && !name.Contains("hp")) continue;
            if (!slider.gameObject.activeSelf) continue;

            Undo.RecordObject(slider.gameObject, "Retire the old health bar");
            slider.gameObject.SetActive(false);
            EditorUtility.SetDirty(slider.gameObject);
            Debug.Log($"[HUDBuilder] deactivated the old '{slider.name}' -- the segmented readout replaces it. " +
                      "Re-enable that object to get the original bar back.");
        }
    }

    // ---------------------------------------------------------------------------------
    // Sprite generation
    // ---------------------------------------------------------------------------------

    struct Sprites
    {
        public Sprite segment, circle, ring, dot, glow, slot, gun, scanner;
        public Font font;
    }

    /// <summary>
    /// Draws the HUD's sprites and writes them into the project as PNG assets.
    ///
    /// <para><b>Generated rather than sourced.</b> This project has no icon set, and the pieces
    /// a HUD like this needs are geometry: a chamfered block, a circle, a ring, a dot. Drawing
    /// them in code makes the shapes exact at any size and keeps the whole HUD reproducible
    /// from this one file. They are written to disk as real assets rather than created in
    /// memory because a texture built at editor time and never serialised is null in a
    /// build -- the UI would look right in the editor and be a set of white boxes in WebGL.</para>
    /// </summary>
    static Sprites BuildSprites()
    {
        Directory.CreateDirectory(SpriteDir);

        var sprites = new Sprites
        {
            segment = Save("segment", 128, 32, DrawSegment),
            circle = Save("circle", 256, 256, DrawCircle),
            ring = Save("ring", 256, 256, DrawRing),
            dot = Save("dot", 64, 64, DrawDot),
            glow = Save("glow", 128, 32, DrawGlow),
            slot = Save("slot", 128, 128, DrawSlot),
            gun = Save("icon_gun", 128, 128, DrawGun),
            scanner = Save("icon_scanner", 128, 128, DrawScanner),
            // The built-in font. Legacy UI Text needs a Font, and this project ships no .ttf
            // that is imported as one -- the TextMesh Pro files are SDF assets, a different
            // type that a Text component cannot use.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
        };

        AssetDatabase.Refresh();
        return sprites;
    }

    static Sprite Save(string name, int width, int height, System.Action<Color[], int, int> draw)
    {
        var path = $"{SpriteDir}/{name}.png";
        var pixels = new Color[width * height];
        draw(pixels, width, height);

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        // The shapes are drawn with transparent backgrounds, and without this Unity premultiplies
        // them and leaves a dark halo around every curved edge.
        importer.alphaIsTransparency = true;
        // No mipmaps: UI sprites are drawn at roughly their authored size, and mipmapping one
        // costs a third more memory to produce a blurrier result.
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>A block with cut corners on the right -- the instrumentation read, not a bar.</summary>
    static void DrawSegment(Color[] p, int w, int h)
    {
        const int chamfer = 10;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var fromRight = w - 1 - x;
                var fromEdge = Mathf.Min(y, h - 1 - y);
                var inside = fromRight >= chamfer - fromEdge;
                p[y * w + x] = inside ? Color.white : Color.clear;
            }
        }
    }

    static void DrawCircle(Color[] p, int w, int h)
    {
        var r = w * 0.5f - 1f;
        var c = new Vector2(w * 0.5f, h * 0.5f);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                // A one-pixel ramp at the edge instead of a hard cut. A circle without it is
                // visibly stepped at 208 points, and this is cheaper than supersampling.
                p[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(r - d));
            }
        }
    }

    /// <summary>The map's border: a bright rim, plus four tick marks at the cardinals.</summary>
    static void DrawRing(Color[] p, int w, int h)
    {
        var outer = w * 0.5f - 1f;
        var inner = outer - 5f;
        var c = new Vector2(w * 0.5f, h * 0.5f);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var v = new Vector2(x + 0.5f, y + 0.5f) - c;
                var d = v.magnitude;
                var a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);

                // Ticks at N/E/S/W, extending inward. They are what makes the rotation legible:
                // a plain ring turning looks identical at every angle, so the map would rotate
                // correctly and appear not to.
                if (a <= 0f && d < inner && d > inner - 10f)
                {
                    var angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                    var offset = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f));
                    if (offset < 1.6f) a = 0.9f;
                }

                p[y * w + x] = new Color(1, 1, 1, a);
            }
        }
    }

    static void DrawDot(Color[] p, int w, int h)
    {
        var c = new Vector2(w * 0.5f, h * 0.5f);
        var r = w * 0.32f;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                var core = Mathf.Clamp01(r - d);
                // A soft halo outside the core so a dot stays findable against a bright patch
                // of map without having to be large enough to hide what is under it.
                var halo = Mathf.Clamp01(1f - (d - r) / (w * 0.18f)) * 0.35f;
                p[y * w + x] = new Color(1, 1, 1, Mathf.Max(core, d > r ? halo : 0f));
            }
        }
    }

    /// <summary>A soft horizontal bloom, drawn behind the heat fill.</summary>
    static void DrawGlow(Color[] p, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            var fy = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
            for (var x = 0; x < w; x++)
            {
                var fx = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                p[y * w + x] = new Color(1, 1, 1, Mathf.Pow(fx * fy, 1.5f));
            }
        }
    }

    /// <summary>An inventory cell: dark fill, bright corner brackets.</summary>
    static void DrawSlot(Color[] p, int w, int h)
    {
        const int bracket = 30;
        const int thickness = 3;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var dx = Mathf.Min(x, w - 1 - x);
                var dy = Mathf.Min(y, h - 1 - y);

                // Brackets rather than a full border: the corners say "slot" while leaving the
                // edges open, so four of them in a row do not read as a table.
                var onEdge = (dx < thickness && dy < bracket) || (dy < thickness && dx < bracket);
                p[y * w + x] = onEdge ? Color.white : new Color(1, 1, 1, 0.16f);
            }
        }
    }

    static void DrawGun(Color[] p, int w, int h)
    {
        Clear(p);
        // Body, barrel, grip, and a muzzle block -- enough silhouette to be read as a weapon at
        // 84 points, which is all an inventory icon has to do.
        Rect(p, w, h, 18, 54, 74, 20);
        Rect(p, w, h, 84, 60, 26, 8);
        Rect(p, w, h, 26, 30, 18, 26);
        Rect(p, w, h, 40, 74, 30, 8);
    }

    static void DrawScanner(Color[] p, int w, int h)
    {
        Clear(p);
        // The remote control: a body, a screen cut out of it, and an antenna.
        Rect(p, w, h, 40, 24, 48, 72);
        Rect(p, w, h, 48, 68, 32, 20, new Color(1, 1, 1, 0.35f));
        Rect(p, w, h, 62, 96, 4, 22);
        Rect(p, w, h, 54, 114, 20, 4);
        // Buttons.
        for (var i = 0; i < 3; i++) Rect(p, w, h, 50 + i * 12, 40, 8, 8, new Color(1, 1, 1, 0.35f));
    }

    static void Clear(Color[] p)
    {
        for (var i = 0; i < p.Length; i++) p[i] = Color.clear;
    }

    static void Rect(Color[] p, int w, int h, int x0, int y0, int rw, int rh) =>
        Rect(p, w, h, x0, y0, rw, rh, Color.white);

    static void Rect(Color[] p, int w, int h, int x0, int y0, int rw, int rh, Color colour)
    {
        for (var y = y0; y < y0 + rh; y++)
        {
            if (y < 0 || y >= h) continue;
            for (var x = x0; x < x0 + rw; x++)
            {
                if (x < 0 || x >= w) continue;
                p[y * w + x] = colour;
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------------------------

    static RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = new Vector2(offset.x, offset.y);
        rt.sizeDelta = size;
        // Anchored to a corner, the pivot has to match or the panel hangs off the screen edge.
        if (anchor == Vector2.zero) rt.pivot = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = PanelFill;
        image.raycastTarget = false;
        return rt;
    }

    static Image Sprite(RectTransform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    static Text Label(RectTransform parent, string content, Vector2 position, Vector2 size, int fontSize, Color colour, Font font)
    {
        var go = new GameObject(content + "Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var text = go.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = colour;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        // The labels are set at a fixed size against a 1920x1080 reference and the canvas
        // scales them. Best Fit would let them resize independently of everything around them.
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Place(text.rectTransform, position, size);
        return text;
    }

    /// <summary>Top-left anchored placement, which is how the panels are laid out.</summary>
    static void Place(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    static void Centre(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    static GameObject FindPlayer()
    {
        var health = Object.FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        return health != null ? health.gameObject : null;
    }

    static bool EnsureSceneOpen()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && active.isLoaded && !string.IsNullOrEmpty(active.path)) return true;

        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;
            EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            Debug.Log($"[HUDBuilder] opened {entry.path}");
            return true;
        }

        Debug.LogError("[HUDBuilder] no enabled scene in Build Settings to open.");
        return false;
    }
}
