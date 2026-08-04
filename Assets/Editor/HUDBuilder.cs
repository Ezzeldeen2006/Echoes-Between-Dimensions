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
    // The unlit socket behind a hull plate. GameHUD recolours these at runtime; this is only
    // what they look like in the editor and in the preview render.
    static readonly Color SteelDim = new Color(0.28f, 0.34f, 0.42f);
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

        /*
         * Hide the whole HUD while the main menu is up.
         *
         * MainMenu shows its panel in Start() with Time.timeScale = 0, so without this the
         * minimap, the hull plates and the heat gauge are all drawn over the title screen from
         * the first frame -- instrumentation for a game that has not started. The same panel is
         * the pause menu, so this covers pausing too, which is the same picture for the same
         * reason.
         *
         * Wired here rather than by hand: this builder is re-run whenever the HUD changes, and
         * a reference that has to be dragged back into the inspector after every rebuild is one
         * that will eventually be forgotten.
         *
         * Deliberately silent when there is no MainMenu -- GameHUD treats a null panel as
         * "always visible", and a scene without a menu is a scene where the HUD should simply
         * always be on, not one where the build should complain.
         */
        hud.rootGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        hud.rootGroup.blocksRaycasts = false;
        hud.rootGroup.interactable = false;

        var menu = Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include);
        if (menu != null && menu.mainMenu != null)
        {
            hud.menuPanel = menu.mainMenu;
            // Start hidden, so the first frame is correct rather than one frame of full HUD
            // followed by a fade. MainMenu.Start() shows the panel, but ordering between the
            // two is not guaranteed and this does not depend on it.
            hud.rootGroup.alpha = 0f;
        }

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
        const float panelW = 348f;
        const float panelH = 92f;

        var group = Panel(canvas, "Health", new Vector2(0, 0), new Vector2(40, 40), new Vector2(panelW, panelH));

        // Lighter than the default panel fill. The corner brackets do the framing now, so the
        // slab behind only has to keep the text legible over a bright part of the scene -- and
        // a HUD that hides less of the game is a better HUD.
        var backing = group.GetComponent<Image>();
        backing.color = new Color(0.03f, 0.05f, 0.08f, 0.55f);

        hud.healthGroup = group.gameObject.AddComponent<CanvasGroup>();
        hud.healthGroup.blocksRaycasts = false;
        hud.healthGroup.interactable = false;

        Corner(group, sprites, "BracketTL", new Vector2(0f, 1f), false, false);
        Corner(group, sprites, "BracketTR", new Vector2(1f, 1f), true, false);
        Corner(group, sprites, "BracketBL", new Vector2(0f, 0f), false, true);
        Corner(group, sprites, "BracketBR", new Vector2(1f, 0f), true, true);

        // "HULL" alone was ambiguous -- it could as easily have labelled the heat gauge above
        // it. The readout names the quantity.
        Label(group, "HULL INTEGRITY", new Vector2(18f, -10f), new Vector2(220f, 18f), 14, Steel, sprites.font);

        /*
         * The count, in large type, right-aligned.
         *
         * The plates alone required the player to count three small shapes in peripheral vision
         * while being shot at. A digit is read without counting, and the two together mean the
         * display works whether it is glanced at or actually looked at. Right-aligned so the
         * number sits at a fixed edge rather than drifting with its own width.
         */
        var readout = Label(group, "03", new Vector2(panelW - 94f, -6f), new Vector2(76f, 34f), 30, Cyan, sprites.font);
        readout.alignment = TextAnchor.MiddleRight;
        hud.healthReadout = readout;

        /*
         * One plate per hit point, read from PlayerHealth rather than hardcoded.
         *
         * It was `const int count = 3`, matching a maxHp that was also hardcoded to 3 in a
         * private field. Two copies of the same number in two files with no link between them:
         * raising the player's health left the HUD still drawing three plates, so the readout
         * quietly stopped describing the thing it was measuring. Deriving it means the two
         * cannot disagree.
         *
         * The plates share a fixed row width, so more hit points means narrower plates rather
         * than a panel that grows off the side of the screen.
         */
        var count = Mathf.Clamp(hud.playerHealth != null ? hud.playerHealth.MaxHealth() : 3, 1, 8);

        const float gap = 10f;
        const float left = 18f;
        const float top = -44f;
        const float rowW = 314f;

        var plateW = (rowW - gap * (count - 1)) / count;
        const float plateH = 30f;

        var cores = new Image[count];
        var shells = new Image[count];
        var glows = new Image[count];

        for (var i = 0; i < count; i++)
        {
            var x = left + i * (plateW + gap);

            /*
             * Glow, then socket, then core -- in that order, because uGUI draws siblings in
             * hierarchy order and there is no z-index to fall back on. Built the other way
             * round the bloom would paint over the plate it is supposed to sit behind, which
             * looks like a wash of colour rather than a lit object.
             */
            var glow = Sprite(group, "PlateGlow" + i, sprites.plateGlow);
            // Offset so the bloom is CENTRED on its plate: the rect is 36 wider and 32 taller,
            // so half of each goes above and to the left. Kept smaller than the 10px gap
            // between plates so two neighbouring glows cannot meet in the middle.
            Place(glow.rectTransform, new Vector2(x - 18f, top + 16f), new Vector2(plateW + 36f, plateH + 32f));
            glow.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f);
            glows[i] = glow;

            var shell = Sprite(group, "PlateSocket" + i, sprites.plateHatch);
            Place(shell.rectTransform, new Vector2(x, top), new Vector2(plateW, plateH));
            shell.color = new Color(SteelDim.r, SteelDim.g, SteelDim.b, 0.55f);
            shells[i] = shell;

            var core = Sprite(group, "Plate" + i, sprites.plate);
            Place(core.rectTransform, new Vector2(x, top), new Vector2(plateW, plateH));
            core.color = Cyan;
            cores[i] = core;
        }

        hud.healthSegments = cores;
        hud.healthShells = shells;
        hud.healthGlows = glows;
    }

    /// <summary>
    /// One corner bracket, mirrored into place.
    ///
    /// <para>Mirroring with a negative localScale rather than rotating: the pivot is already in
    /// the corner the art belongs to, so a flip about it lands correctly, whereas a 90-degree
    /// rotation would also need the pivot moved for each corner and is three more chances to
    /// get one of them subtly wrong.</para>
    /// </summary>
    static void Corner(RectTransform parent, Sprites sprites, string name, Vector2 anchor, bool flipX, bool flipY)
    {
        var image = Sprite(parent, name, sprites.bracket);
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(26f, 26f);
        rt.localScale = new Vector3(flipX ? -1f : 1f, flipY ? -1f : 1f, 1f);
        image.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f);
    }

    // ---------------------------------------------------------------------------------
    // Heat -- shown only while the gun is held
    // ---------------------------------------------------------------------------------

    static void BuildHeat(RectTransform canvas, Sprites sprites, GameHUD hud)
    {
        /*
         * Sits directly above the hull readout, and the two must not touch.
         *
         * The hull panel is 92 tall anchored 40 from the bottom, so it occupies 40..132. This
         * one was anchored at 124, which put its bottom edge eight pixels INSIDE the hull panel
         * -- the corner bracket drew over the heat gauge's border and the two instruments read
         * as one damaged box. Anchoring at 148 leaves a 16px gutter. Worth stating in numbers
         * rather than nudging until it looks right, because the next change to either panel's
         * height has to redo this sum.
         */
        var group = Panel(canvas, "Heat", new Vector2(0, 0), new Vector2(40, 156), new Vector2(348, 64));
        hud.heatRoot = group.gameObject;

        // Matched to the hull panel so the pair reads as one instrument cluster rather than as
        // two widgets that happen to be near each other.
        group.GetComponent<Image>().color = new Color(0.03f, 0.05f, 0.08f, 0.55f);

        /*
         * No corner brackets here, deliberately, and this is a hierarchy decision rather than a
         * saving.
         *
         * Bracketing both panels put two sets of corner marks within a few pixels of each other
         * across the gutter, and the four of them read as one broken glyph instead of as two
         * frames -- more ink in the busiest part of the cluster, saying nothing. Framing only
         * the hull readout makes it the primary instrument and leaves heat as the secondary one
         * attached above it, which is also the true relative importance: running hot costs you
         * a few seconds, running out of hull ends the run.
         */

        hud.heatLabel = Label(group, "HEAT", new Vector2(18, -10), new Vector2(200, 18), 14, Steel, sprites.font);

        // The track the fill runs along, so an empty gauge still reads as a gauge rather than
        // as nothing at all.
        //
        // Sheared, like the hull plates. It was the old chamfered `segment` sprite, which left
        // the two halves of the same cluster drawn in two different shape languages -- rounded
        // above, slanted below -- and made the redesign look half-finished rather than
        // deliberate. The gauge empties horizontally against slanted ends, which is the shape
        // this genre uses for exactly this readout.
        var track = Sprite(group, "Track", sprites.plate);
        Place(track.rectTransform, new Vector2(18, -36), new Vector2(312, 18));
        track.color = new Color(0.10f, 0.13f, 0.17f, 0.95f);

        // Behind the fill and slightly larger: the bleed of a hot component, not a UI border.
        var glow = Sprite(group, "Glow", sprites.glow);
        Place(glow.rectTransform, new Vector2(10, -28), new Vector2(328, 34));
        glow.color = new Color(1f, 1f, 1f, 0f);
        glow.raycastTarget = false;
        hud.heatGlow = glow;

        var fill = Sprite(group, "Fill", sprites.plate);
        Place(fill.rectTransform, new Vector2(18, -36), new Vector2(312, 18));
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
        public Sprite plate, plateHatch, plateGlow, bracket;
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
            // The hull readout. Drawn at roughly 1.3x the size they are shown at, so the
            // sheared edges land on a downscale rather than being stretched up into stair-steps.
            plate = Save("plate", 132, 40, DrawPlate),
            plateHatch = Save("plate_hatch", 132, 40, DrawPlateHatch),
            // Deliberately larger than the plate: the bloom has to have somewhere to go, and a
            // glow the same size as the thing it surrounds is just a second copy of it.
            plateGlow = Save("plate_glow", 196, 104, DrawPlateGlow),
            bracket = Save("bracket", 64, 64, DrawBracket),
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

        /*
         * Cap at the size actually drawn, rather than leaving Unity's 2048 default.
         *
         * Unity never upscales, so a 128px file imported with maxTextureSize 2048 still ships
         * 128px -- the default is harmless to the build. It is not harmless to the texture
         * budget report, which estimates payload from maxTextureSize and so listed all eight
         * of these as 4 MB each: 32 MB of imaginary textures sitting at the top of the "20
         * largest" list, above every real one. A report whose worst offenders are fictional is
         * a report nobody will read twice.
         */
        importer.maxTextureSize = Mathf.Max(width, height);
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ---------------------------------------------------------------------------------
    // Hull plates
    //
    // The shape is a SHEARED PARALLELOGRAM, and that is the whole idea. The readout this
    // replaced was three rounded-end pills in flat cyan on a rounded dark panel, which is the
    // house style of a mobile game -- soft, symmetrical, no direction. The shape vocabulary of
    // this genre is the opposite of that: everything is cut on a slant, corners are notched
    // rather than rounded, and lit elements bleed rather than sitting flat. A slanted plate
    // reads as machined metal at a glance, and a rounded pill does not, and the difference is
    // in the silhouette rather than in the colour -- which is why recolouring the old pills
    // would not have fixed the complaint.
    //
    // The shear is a constant across all three sprites so the core, its socket and its bloom
    // are the same shape and stay registered when they are drawn on top of each other.
    // ---------------------------------------------------------------------------------

    const float PlateShear = 15f;

    /// <summary>
    /// How far inside the plate a point is, in pixels. Positive inside, negative outside.
    ///
    /// <para>One function for the fill, the socket and the bloom, so the three cannot drift
    /// apart -- a bloom that does not line up with its plate is the sort of thing that only
    /// shows once it is on screen and then looks like a rendering fault.</para>
    /// </summary>
    static float PlateDepth(float cx, float cy, int w, int h, float pad)
    {
        var bottom = pad;
        var top = h - 1 - pad;
        var t = top - bottom < 1f ? 0f : (cy - bottom) / (top - bottom);

        // Left and right edges both slide right as they rise, which keeps the width constant.
        var x0 = pad + PlateShear * t;
        var x1 = w - pad - PlateShear + PlateShear * t;

        return Mathf.Min(Mathf.Min(cx - x0, x1 - cx), Mathf.Min(cy - bottom, top - cy));
    }

    /// <summary>The lit plate: one hit point the player still has.</summary>
    static void DrawPlate(Color[] p, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                // +0.5 puts the edge on the pixel boundary rather than its centre, giving one
                // pixel of coverage-based antialiasing. Without it the slanted edges alias into
                // visible stair-steps, which is exactly what makes a shape look cheap.
                var a = Mathf.Clamp01(PlateDepth(x + 0.5f, y + 0.5f, w, h, 0f) + 0.5f);

                // A brighter band along the bottom third: a plate lit from below reads as having
                // thickness, where a flat fill reads as a coloured rectangle.
                var lift = 1f - Mathf.Clamp01((float)y / (h * 0.55f));
                var v = 0.82f + 0.18f * lift;

                p[y * w + x] = new Color(v, v, v, a);
            }
        }
    }

    /// <summary>
    /// The empty socket, hazard-striped.
    ///
    /// <para>A spent hit point is drawn as a DIFFERENT SHAPE rather than as the same shape in a
    /// darker colour. Health in the corner of the screen is read peripherally, and peripheral
    /// vision resolves shape and motion long before it resolves hue -- three cyan blocks and
    /// two cyan blocks plus one grey one are nearly the same image, whereas a lit plate and a
    /// striped hole are not.</para>
    /// </summary>
    static void DrawPlateHatch(Color[] p, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var outer = Mathf.Clamp01(PlateDepth(x + 0.5f, y + 0.5f, w, h, 0f) + 0.5f);
                var inner = Mathf.Clamp01(PlateDepth(x + 0.5f, y + 0.5f, w, h, 3f) + 0.5f);

                // The frame is the difference between the shape and the shape inset by 3px,
                // which gives a constant-width outline that follows the slant automatically.
                var border = Mathf.Clamp01(outer - inner);

                // x + y is a 45-degree stripe. It runs the opposite way to the plate's own
                // shear on purpose -- parallel stripes would read as part of the edge.
                var stripe = (x + y) % 14 < 4 ? 1f : 0f;

                p[y * w + x] = new Color(1f, 1f, 1f, Mathf.Max(border, inner * (0.14f + 0.34f * stripe)));
            }
        }
    }

    /// <summary>The bloom behind a lit plate.</summary>
    static void DrawPlateGlow(Color[] p, int w, int h)
    {
        // The plate sits inset inside this larger sprite; the padding is the room the falloff
        // has to fade out in.
        const float pad = 38f;
        // Tightened from 30. At 30 the bloom reached far enough past its plate that adjacent
        // plates' glows met in the gap between them and summed into a bright vertical smear --
        // three lit plates read as one blurry amber bar, which is the opposite of the countable
        // readout the whole design is for. A glow should say "this is lit", not join things up.
        const float spread = 20f;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = PlateDepth(x + 0.5f, y + 0.5f, w, h, pad);
                var a = Mathf.Clamp01((d + spread) / spread);
                // Squared, twice: a linear falloff looks like a grey box with soft edges, and
                // what is wanted is something tight around the plate that dies off fast.
                a *= a;
                a *= a;
                p[y * w + x] = new Color(1f, 1f, 1f, a * 0.9f);
            }
        }
    }

    /// <summary>
    /// One corner bracket, drawn in the top-left orientation.
    ///
    /// <para>Four of these replace the filled panel the readout used to sit on. A solid slab
    /// behind a HUD element is a background; brackets are a frame, and a frame says "this is an
    /// instrument" while taking almost no pixels and hiding none of the game. The other three
    /// corners are this same sprite mirrored on one or both axes, so the geometry is authored
    /// once and cannot end up subtly different in one corner.</para>
    /// </summary>
    static void DrawBracket(Color[] p, int w, int h)
    {
        Clear(p);

        const int thick = 5;
        const int arm = 34;

        // y is measured from the bottom in a Color[] passed to SetPixels, so "top" is h - thick.
        Rect(p, w, h, 0, h - thick, arm, thick);
        Rect(p, w, h, 0, h - arm, thick, arm);
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
