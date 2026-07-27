// WebGL build-size optimiser.
//
// WHY THIS EXISTS
//
// Unity's own build report for these projects says textures are ~87% of the payload:
//
//     Textures            162.2 mb   87.0%
//     Shaders               9.9 mb    5.3%
//     Meshes                7.5 mb    4.0%
//     Sounds                3.6 mb    1.9%
//     Complete build size 102.4 mb
//
// A 102 MB web build is unshippable twice over: GitHub refuses any single file over 100 MB,
// and a visitor on a phone abandons long before it finishes. Everything else -- code
// stripping, mesh compression, shader variants -- is rearranging the remaining 13%.
//
// The cause is that these textures have no WebGL platform override, so they fall back to the
// default platform's 2048 cap with plain (non-crunched) compression. At 2048 with mipmaps a
// DXT-compressed texture is ~5.3 MB, which is exactly what the build report shows, thirty
// times over.
//
// WHAT THIS CHANGES, AND WHY IT DOES NOT BREAK ANYTHING
//
// 1. maxTextureSize per texture, for the WebGL platform only. Desktop builds are untouched.
//    Resolution is a quality setting, not a behaviour: nothing reads a texture's dimensions
//    to decide what to do. The size is chosen per texture from its role -- a UI crosshair
//    stays sharp, a rock normal map does not need to be.
//
// 2. Crunch compression. This is the one that does the heavy lifting for a *download*.
//    Crunch is a second compression layer on top of DXT that is decoded back to DXT on load,
//    so GPU memory and rendering are completely unchanged; only the bytes on the wire shrink,
//    typically by 4-6x. For a web build that is the entire metric that matters.
//
// 3. Audio to Vorbis, streamed. Music does not need to sit in memory decompressed on a
//    machine that also has to run the game in a browser tab.
//
// 4. WebGL player settings: Brotli compression with the decompression fallback ON. The
//    fallback is not optional here -- GitHub Pages cannot send `Content-Encoding: br`, so
//    without it the browser receives compressed bytes it will not decompress and the loader
//    fails outright. This is the single setting most likely to be got wrong.
//
// Deliberately NOT changed, because each can alter behaviour rather than fidelity:
//   - Read/Write Enabled. Turning it off breaks any script calling GetPixels().
//   - Managed stripping level. High stripping removes code only reached by reflection, which
//     is precisely how serialization and Behaviour Graph node lookup work. The win would be
//     in the 6.7 MB wasm, not the 162 MB of textures -- a bad trade for the risk.
//   - Exception support. Turning it off is a real size win and can silently change control
//     flow in code that relies on catching.
//
// USAGE
//     Tools > WebGL > 1. Report texture budget      (measure first, change nothing)
//     Tools > WebGL > 2. Optimise textures for web
//     Tools > WebGL > 3. Optimise audio for web
//     Tools > WebGL > 4. Apply WebGL player settings
//     Tools > WebGL > 5. Make URP safe for WebGL
//     Tools > WebGL > Optimise everything
//
// Or headless:
//     Unity -batchmode -quit -projectPath <path> -executeMethod WebGLOptimizer.OptimiseAll
//
// KNOWN SIDE EFFECT of any batchmode run, and it looks alarming: batchmode opens no scene, and
// on exit Unity records that -- Library/LastSceneManagerSetup.txt becomes "sceneSetups: []".
// The next time you open the project in the editor it faithfully restores "no scene", so the
// Hierarchy contains only a Main Camera and a Directional Light and it looks as though the
// scene was wiped. Nothing was: the .unity file is untouched. Reopen
// Assets/Scenes/SampleScene.unity and it comes straight back.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class WebGLOptimizer
{
    const string Platform = "WebGL";

    /*
     * Crunch quality: a size/fidelity dial, not a correctness one.
     *
     * Raised from 50 to 80. The first pass was aiming at a hard problem -- a 100.7 MB asset
     * file that GitHub refuses outright -- so it took every saving available. Having landed at
     * 16.9 MB there is room to buy fidelity back, and crunch quality is the cheapest place to
     * spend it: it reduces the block artefacts DXT leaves on smooth gradients (sky, fog, dirt
     * paths) without changing resolution or GPU memory at all.
     *
     * Not 100. Above roughly 80 the returns fall off sharply while the file keeps growing --
     * crunch is entropy-coding a format that is already lossy, so the last 20 points mostly
     * preserve DXT's own artefacts more faithfully.
     */
    const int CrunchQuality = 80;

    /// <summary>
    /// Size caps by role. These are the numbers that decide the build size, so they are
    /// stated in one place rather than scattered through the code.
    /// </summary>
    /*
     * Doubled across the board, now that there is budget for it.
     *
     * The first pass had to get a 100.7 MB asset file under GitHub's 100 MB hard limit, so it
     * was aggressive: 1024 albedo, 512 for everything carrying surface detail. That landed at
     * 16.9 MB -- far more headroom than the problem needed.
     *
     * Doubling a dimension quadruples the cost, so this is roughly 4x the texture data. The
     * ceiling is deliberately 2048 rather than 4096: a browser tab is not a desktop game, and
     * 4096 maps mean both a much larger download and real GPU memory pressure inside a tab
     * that also has to hold the page. 2048 is the point where the difference stops being
     * visible at the size this actually renders.
     *
     * Normal maps are now at the SAME cap as colour, having been one step below.
     *
     * That step existed to save bytes when bytes were the whole problem, and it is no longer
     * buying anything worth having. Measuring the source art settles it: of 115 textures, 54 are
     * authored at 1024 or below and 56 at 2048 -- so a 2048 ceiling puts almost every texture at
     * its FULL authored resolution, and holding normal maps at 1024 was the only thing still
     * throwing detail away.
     *
     * The same measurement is why the ceiling stays 2048 rather than 4096: exactly FIVE textures
     * in this project are authored above 2048. A 4096 cap would re-import five files and change
     * nothing anyone could see, while adding real GPU memory pressure inside a browser tab --
     * and a lost WebGL context is the failure this build already had once.
     *
     * So this is the end of the useful range. Beyond here the source art is the limit, not the
     * settings, and the only dial left is crunch quality.
     */
    const int CapDefault = 2048;   // albedo / colour maps seen up close
    const int CapNormal = 2048;    // normal, mask, AO, metallic, roughness, height
    const int CapTerrain = 2048;   // fills the screen, so it is the most visible of the lot
    const int CapUI = 2048;        // crosshairs and HUD are read directly by the player
    const int CapSkybox = 2048;    // large on screen but never inspected closely

    [MenuItem("Tools/WebGL/1. Report texture budget", priority = 1)]
    public static void ReportTextureBudget()
    {
        var rows = new List<(string path, int current, int target, long estBefore, long estAfter)>();

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

            var settings = importer.GetPlatformTextureSettings(Platform);
            var current = settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
            var target = TargetSizeFor(path, importer);

            // Rough DXT estimate: 1 byte per pixel, x1.33 for the mip chain. Good enough to
            // rank textures and to sanity-check the total against Unity's own build report.
            long Estimate(int size) => (long)(size * (long)size * (importer.mipmapEnabled ? 1.33f : 1f));

            rows.Add((path, current, target, Estimate(current), Estimate(Math.Min(current, target))));
        }

        var before = rows.Sum(r => r.estBefore);
        var after = rows.Sum(r => r.estAfter);

        var report = new StringBuilder();
        report.AppendLine($"[WebGLOptimizer] {rows.Count} textures");
        report.AppendLine($"  estimated texture payload now:   {Mb(before)}");
        report.AppendLine($"  estimated after resizing:        {Mb(after)}   ({(before == 0 ? 0 : 100 - after * 100 / before)}% smaller)");
        report.AppendLine($"  crunch compression then applies a further ~4-6x to what ships.");
        report.AppendLine("  20 largest:");

        foreach (var row in rows.OrderByDescending(r => r.estBefore).Take(20))
            report.AppendLine($"    {Mb(row.estBefore),10} -> {Mb(row.estAfter),-10} {row.current}px -> {row.target}px  {row.path}");

        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/WebGL/2. Optimise textures for web", priority = 2)]
    public static void OptimiseTextures()
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D");
        var changed = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.StartsWith("Assets/")) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                if (EditorUtility.DisplayCancelableProgressBar("Optimising textures for WebGL", path,
                        (float)i / guids.Length))
                    break;

                var target = TargetSizeFor(path, importer);
                var settings = importer.GetPlatformTextureSettings(Platform);

                /*
                 * Never UPSCALE -- a texture authored at 256 must stay 256, since raising it to
                 * the cap inflates the build without adding detail that exists.
                 *
                 * The ceiling is the SOURCE image's own dimensions, not the existing WebGL
                 * override. Reading the override was a bug that only showed up the first time
                 * the caps were RAISED: with an override already sitting at 1024,
                 * Math.Min(1024, 2048) is 1024, so every texture kept its old size and a
                 * deliberate quality increase silently did nothing at all. The rule is about
                 * the authored resolution, so it has to be measured against the authored
                 * resolution.
                 *
                 * Also bounded by the project's own default-platform cap, so the web build never
                 * ends up at a higher resolution than the desktop build it came from.
                 */
                importer.GetSourceTextureWidthAndHeight(out var sourceWidth, out var sourceHeight);
                var authored = Math.Max(sourceWidth, sourceHeight);
                var ceiling = target;
                if (authored > 0) ceiling = Math.Min(ceiling, authored);
                if (importer.maxTextureSize > 0) ceiling = Math.Min(ceiling, importer.maxTextureSize);
                var size = ceiling;

                var wanted = new TextureImporterPlatformSettings
                {
                    name = Platform,
                    overridden = true,
                    maxTextureSize = size,
                    // Automatic lets Unity pick the right family per texture type -- DXT5nm for
                    // normal maps, DXT1 for opaque, DXT5 for alpha. Naming a format by hand is
                    // how normal maps end up decoded wrong.
                    format = TextureImporterFormat.Automatic,
                    textureCompression = TextureImporterCompression.Compressed,
                    crunchedCompression = true,
                    compressionQuality = CrunchQuality,
                    resizeAlgorithm = TextureResizeAlgorithm.Mitchell,
                };

                if (Matches(settings, wanted)) continue;

                importer.SetPlatformTextureSettings(wanted);
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[WebGLOptimizer] textures: {changed} of {guids.Length} re-imported with a WebGL override.");
    }

    [MenuItem("Tools/WebGL/3. Optimise audio for web", priority = 3)]
    public static void OptimiseAudio()
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip");
        var changed = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var isLong = clip != null && clip.length > 8f;

            var settings = importer.GetOverrideSampleSettings(Platform);
            settings.loadType = isLong
                // Music: decoded a chunk at a time instead of held in memory. On a browser tab
                // sharing a machine with the game, a several-MB decompressed buffer is real.
                ? AudioClipLoadType.Streaming
                // Short effects must be resident, or the first play stutters while it decodes.
                : AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = isLong ? 0.4f : 0.6f;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;

            importer.SetOverrideSampleSettings(Platform, settings);
            // Not forced to mono: a stereo music track collapsed to mono is audibly worse, and
            // audio is under 2% of this build. Size is not the constraint here.
            importer.SaveAndReimport();
            changed++;
        }

        Debug.Log($"[WebGLOptimizer] audio: {changed} clip(s) set to Vorbis for WebGL.");
    }

    [MenuItem("Tools/WebGL/4. Apply WebGL player settings", priority = 4)]
    public static void ApplyPlayerSettings()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;

        // NOT optional on GitHub Pages, and the easiest thing here to get wrong.
        //
        // Brotli-compressed Unity builds normally rely on the web server sending
        // `Content-Encoding: br` so the browser transparently decompresses. GitHub Pages
        // serves static files and cannot be told to add that header. Without the fallback the
        // browser hands the loader compressed bytes, and the build fails to start with an
        // error that says nothing about compression.
        //
        // The fallback ships a JavaScript decompressor instead. It costs a little startup
        // time; it is the difference between a build that loads and one that does not.
        PlayerSettings.WebGL.decompressionFallback = true;

        // Cache the data file in IndexedDB so a second visit does not re-download ~20 MB.
        PlayerSettings.WebGL.dataCaching = true;

        // Optimise for size rather than speed. Gameplay here is not CPU-bound -- it is a
        // student physics/AI project, not a simulation -- so trading a little runtime for a
        // smaller wasm is the right way round for something delivered over the network.
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);

        // Drop engine subsystems no scene references.
        PlayerSettings.stripEngineCode = true;

        // Managed stripping is deliberately left at whatever the project already uses. High
        // stripping removes code reachable only by reflection, which is exactly how Unity
        // serialization and Behaviour Graph node resolution work -- and the payoff would be a
        // slice of the 6.7 MB wasm against 162 MB of textures. Wrong risk for the reward.

        /*
         * Use the site's own WebGL template rather than Unity's default.
         *
         * This is a correctness fix, not cosmetics. The default template hardcodes the canvas
         * to 960x600 and centres it on a WHITE page, which is written for a build that owns a
         * browser tab. Inside the site's 835x470 iframe the canvas overflowed both axes: the
         * embed grew scrollbars, showed the middle of the Unity splash, and surrounded it with
         * white -- which is exactly what "the game is a white screen" turned out to be.
         *
         * The default also reports failures through alert(), and the embed is a sandboxed
         * iframe without allow-modals, so the browser blocks that silently. A build that failed
         * to start had no way to say so.
         *
         * Assets/WebGLTemplates/SiteEmbed fixes all of it: a canvas sized in CSS to fill its
         * container, the site's near-black background, and errors rendered into the page.
         * Setting it here means a rebuild cannot quietly regress to the broken default.
         */
        PlayerSettings.WebGL.template = "PROJECT:SiteEmbed";

        // The splash screen is 2.7 MB of the build and cannot be disabled on a Personal
        // licence, so it is left alone rather than pretended about.

        Debug.Log("[WebGLOptimizer] player settings: Brotli + decompression fallback + data caching + size-optimised IL2CPP.");
    }

    /// <summary>
    /// Makes the URP configuration survive a real browser's GLSL compiler.
    ///
    /// <para><b>This fixes a blank game, not a slow one.</b> The first build loaded, reported
    /// no errors, and rendered nothing. The browser console had the reason:</para>
    /// <pre>
    ///   Shader Hidden/Universal Render Pipeline/UberPost: GLSL compilation failed, no infolog provided
    ///   Creation of internal variant of shader 'UberPost' failed.
    ///   WebGL: CONTEXT_LOST_WEBGL: loseContext: context lost
    /// </pre>
    /// <para>UberPost is URP's post-processing uber-shader. When it fails to compile the
    /// context is lost, and a lost context renders nothing at all -- so the symptom is a blank
    /// frame with no error visible on the page.</para>
    ///
    /// <para><b>Why it was not caught earlier, which is the part worth remembering.</b> The
    /// first verification ran headless Chrome with <c>--use-gl=swiftshader</c>, a SOFTWARE
    /// renderer. Swiftshader compiles this shader happily. A real GPU going through ANGLE does
    /// not. Testing WebGL on a software rasteriser proves the build downloads and the wasm
    /// runs; it proves nothing about whether anything appears on screen.</para>
    ///
    /// <para>The fix removes post-processing from the WebGL render path, so UberPost is never
    /// compiled. That is a real visual cost -- bloom, tonemapping and colour grading go -- and
    /// it is the right trade twice over: the alternative is a game that does not render, and
    /// full-screen post-processing is the most expensive thing you can ask of a WebGL context
    /// in a browser tab. HDR goes with it for the same reason.</para>
    ///
    /// <para>Written against the serialized properties rather than URP's C# types, so this
    /// compiles whether or not the URP editor assembly is referenced.</para>
    /// </summary>
    [MenuItem("Tools/WebGL/5. Make URP safe for WebGL", priority = 5)]
    public static void MakeRenderPipelineWebGLSafe()
    {
        var changed = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null) continue;

            var typeName = asset.GetType().Name;
            if (typeName != "UniversalRenderPipelineAsset" && typeName != "UniversalRendererData")
                continue;

            var serialized = new SerializedObject(asset);
            var touched = false;

            // The renderer's post-process data. Null means URP skips the post-processing pass
            // entirely, so UberPost is never requested and never compiled.
            var postProcess = serialized.FindProperty("postProcessData");
            if (postProcess != null && postProcess.objectReferenceValue != null)
            {
                postProcess.objectReferenceValue = null;
                touched = true;
            }

            // HDR forces an intermediate float render target that WebGL handles poorly and
            // that only post-processing was consuming anyway.
            var hdr = serialized.FindProperty("m_SupportsHDR");
            if (hdr != null && hdr.boolValue)
            {
                hdr.boolValue = false;
                touched = true;
            }

            /*
             * Shadow complexity, which is what actually killed the terrain.
             *
             * After post-processing was disabled the build still rendered nothing, and the
             * console named a different shader:
             *
             *   Shader Universal Render Pipeline/Terrain/Lit: GLSL compilation failed, no infolog provided
             *
             * "No infolog" means the driver refused it without saying why, which is what
             * happens when a shader exceeds a hard limit rather than containing a mistake.
             * Terrain/Lit is already one of URP's largest fragment shaders, and this project
             * was multiplying it by four shadow cascades, soft-shadow filtering, and shadows
             * from up to four additional lights per object. Compiled together through ANGLE
             * to D3D11, that is past what the compiler will accept.
             *
             * One cascade over a 50-unit shadow distance is barely distinguishable here, hard
             * shadows on a stylised forest are fine, and the player's point light still lights
             * the scene -- it just stops casting its own shadows. Set only on assets that
             * declare these fields, so the renderer data (which does not) is left alone.
             */
            foreach (var (name, value) in new[] { ("m_ShadowCascadeCount", 1) })
            {
                var property = serialized.FindProperty(name);
                if (property != null && property.intValue != value)
                {
                    property.intValue = value;
                    touched = true;
                }
            }

            /*
             * SRP Batcher off, and this is the one that decides whether anything renders at all
             * in Chrome on Windows.
             *
             * Measured: the same build, three graphics backends, one URL.
             *
             *   ANGLE d3d11  ->  467 shader compilation failures, WebGL context lost, blank
             *   ANGLE gl     ->    3 (benign editor-only shaders), renders correctly
             *   swiftshader  ->    3 (benign), renders correctly
             *
             * Chrome and Brave on Windows default to the D3D11 backend, so the game was blank
             * for essentially every visitor while rendering perfectly under any other backend --
             * which is also why an early check using a software renderer passed.
             *
             * The SRP Batcher is the plausible mechanism: it packs per-material properties into
             * large uniform blocks, and ANGLE maps uniform blocks onto D3D11 constant buffers,
             * which have hard limits on count and size. Exceeding them fails compilation with an
             * EMPTY infolog -- exactly the "GLSL compilation failed, no infolog provided" that
             * appeared for every shader including Hidden/Universal/CoreBlit, which is far too
             * simple to fail on its own merits.
             *
             * The cost is draw-call batching, i.e. some CPU time per frame. For a single-scene
             * student game that is nothing next to not rendering.
             */
            foreach (var name in new[] { "m_UseSRPBatcher", "m_SoftShadowsSupported", "m_AdditionalLightShadowsSupported" })
            {
                var property = serialized.FindProperty(name);
                if (property != null && property.boolValue)
                {
                    property.boolValue = false;
                    touched = true;
                }
            }

            if (touched)
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                changed++;
                Debug.Log($"[WebGLOptimizer] made WebGL-safe: {path} ({typeName})");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[WebGLOptimizer] render pipeline: {changed} asset(s) adjusted for WebGL.");
    }

    /// <summary>
    /// Temporarily removes normal maps from every TerrainLayer, for the duration of a build.
    ///
    /// <para><b>Why.</b> With instanced terrain the shader failures dropped from 465 to 6, the
    /// WebGL context survived, and the game rendered -- trees, grass, player. But the GROUND was
    /// missing, because the six remaining failures are all the same shader:</para>
    /// <pre>Universal Render Pipeline/Terrain/Lit</pre>
    /// <para>A terrain whose shader will not compile simply is not drawn. Collision still works,
    /// which is why it feels present while you can see the fog through it.</para>
    ///
    /// <para>Both layers here set a normal map, which selects the {@code _NORMALMAP} variant of
    /// URP's largest fragment shader -- roughly doubling its texture reads and arithmetic. On top
    /// of the instanced per-pixel-normal path, that is what tips it past what ANGLE's D3D11
    /// compiler will accept. Dropping the normal maps costs surface relief on the ground; not
    /// dropping them costs the ground.</para>
    ///
    /// <para><b>Restored afterwards, always.</b> This is a build-time transformation, not a
    /// project edit: the desktop game keeps its normal-mapped terrain. The restore runs in a
    /// finally block so a failed or cancelled build cannot leave the project stripped.</para>
    /// </summary>
    static Dictionary<string, Texture2D> StripTerrainNormalMaps()
    {
        var stripped = new Dictionary<string, Texture2D>();

        foreach (var guid in AssetDatabase.FindAssets("t:TerrainLayer", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null || layer.normalMapTexture == null) continue;

            stripped[path] = layer.normalMapTexture;
            layer.normalMapTexture = null;
            EditorUtility.SetDirty(layer);
            Debug.Log($"[WebGLOptimizer] temporarily removed the normal map from {path}");
        }

        if (stripped.Count > 0)
        {
            AssetDatabase.SaveAssets();
        }
        return stripped;
    }

    static void RestoreTerrainNormalMaps(Dictionary<string, Texture2D> stripped)
    {
        foreach (var (path, texture) in stripped)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) continue;
            layer.normalMapTexture = texture;
            EditorUtility.SetDirty(layer);
        }

        if (stripped.Count > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[WebGLOptimizer] restored {stripped.Count} terrain normal map(s)");
        }
    }

    /// <summary>
    /// Builds a WebGL player containing one camera, one light and one cube.
    ///
    /// <para><b>A diagnostic, not a feature.</b> AtomBall's build renders nothing in Chrome on
    /// Windows: 467 shader compilation failures on ANGLE's D3D11 backend, while the identical
    /// build renders correctly on the GL backend and on swiftshader. Among the failures is
    /// <c>Hidden/Universal/CoreBlit</c>, which is a full-screen texture copy -- far too simple
    /// to fail on its own merits.</para>
    ///
    /// <para>That points away from AtomBall's content and towards the toolchain, but pointing
    /// is not proving. This settles it: same project, same URP assets, same player settings,
    /// same Unity version, but a scene with nothing in it. If a cube is also blank on D3D11,
    /// the build's contents are irrelevant and the problem is Unity 6000.3.11f1's WebGL shaders
    /// against this ANGLE/driver combination -- which is a version decision, not something that
    /// can be tuned away in project settings.</para>
    ///
    /// <para>Deletes the probe scene afterwards so it does not linger in the project.</para>
    /// </summary>
    [MenuItem("Tools/WebGL/Diagnose: build a minimal probe", priority = 41)]
    public static void BuildMinimalProbe()
    {
        const string probeScene = "Assets/Scenes/_WebGLProbe.unity";

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                UnityEditor.SceneManagement.NewSceneMode.Single);

        // A lit cube in front of the default camera: enough to require URP's core shaders and
        // one material, and nothing else.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(0f, 0f, 4f);

        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, probeScene);

        var output = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "Builds", "Probe");

        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { probeScene },
            locationPathName = output,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        });

        Debug.Log($"[WebGLOptimizer] PROBE {result.summary.result} -- {result.summary.totalSize / 1048576f:0.0} MB, " +
                  $"{result.summary.totalErrors} error(s) -> {output}");

        AssetDatabase.DeleteAsset(probeScene);
        AssetDatabase.SaveAssets();

        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    /// <summary>
    /// Switches every Terrain in the build scenes to instanced drawing.
    ///
    /// <para><b>Why this is the next thing to try, narrowed by measurement.</b> A minimal build
    /// from this same project -- same Unity version, same URP assets, same player settings, one
    /// camera and one cube -- renders correctly on ANGLE D3D11. AtomBall does not. So the
    /// toolchain is fine and the cause is in the content, and the dominant failure by a wide
    /// margin is:</para>
    /// <pre>Shader Universal Render Pipeline/Terrain/Lit: GLSL compilation failed, no infolog provided</pre>
    ///
    /// <para>Terrain/Lit is one of URP's largest fragment shaders, and both terrain layers here
    /// carry normal maps, which selects its heaviest variant. Instanced terrain rendering takes
    /// a different code path through that shader, so it is worth one attempt -- and unlike
    /// dropping the normal maps or cutting shadow quality, it costs <b>nothing visually</b>.</para>
    ///
    /// <para>This modifies the scene, which is tracked in git and unmodified, so
    /// <c>git checkout -- Assets/Scenes/</c> undoes it exactly.</para>
    /// </summary>
    [MenuItem("Tools/WebGL/6. Terrain: draw instanced", priority = 6)]
    public static void MakeTerrainInstanced()
    {
        var changed = 0;

        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(entry.path);
            var touched = false;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (terrain.drawInstanced) continue;
                terrain.drawInstanced = true;
                EditorUtility.SetDirty(terrain);
                touched = true;
                changed++;
                Debug.Log($"[WebGLOptimizer] terrain '{terrain.name}' -> drawInstanced");
            }

            if (touched)
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[WebGLOptimizer] terrain: {changed} terrain(s) switched to instanced drawing.");
    }

    [MenuItem("Tools/WebGL/Optimise everything", priority = 20)]
    public static void OptimiseAll()
    {
        ReportTextureBudget();
        OptimiseTextures();
        OptimiseAudio();
        ApplyPlayerSettings();

        /*
         * MakeRenderPipelineWebGLSafe() is deliberately NOT called here.
         *
         * It was written to fix a blank WebGL build and it did not: see that method for the
         * measurements. Worse, it edits the shared UniversalRenderPipelineAsset and renderer
         * data, which are project-wide -- so it silently removes post-processing, HDR and soft
         * shadows from the DESKTOP game as well. Leaving an unrequested global quality
         * regression behind for a fix that does not work is the wrong trade, so it is now a
         * menu item you invoke on purpose rather than part of the default pass.
         */
        AssetDatabase.SaveAssets();
        ReportTextureBudget();
    }

    /// <summary>
    /// Builds the WebGL player, so the size claim can be checked rather than asserted.
    /// <para>
    /// Everything above changes import settings, and import settings are a prediction about
    /// build size, not a measurement of it. Unity's own build report is the only thing that
    /// actually knows, and it is printed at the end of this.
    /// </para>
    /// <para>
    /// Output goes to &lt;project&gt;/Builds/WebGL. It is deliberately NOT written straight into
    /// the website repository: a build is a generated artefact, and pointing a build at a
    /// source tree is how half-finished output ends up committed.
    /// </para>
    /// </summary>
    [MenuItem("Tools/WebGL/Build WebGL player", priority = 40)]
    public static void BuildWebGL()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[WebGLOptimizer] No enabled scenes in Build Settings -- nothing to build.");
            EditorApplication.Exit(2);
            return;
        }

        var output = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "Builds", "WebGL");

        Debug.Log($"[WebGLOptimizer] building {scenes.Length} scene(s) to {output}");

        /*
         * Force a release build, explicitly, every time.
         *
         * The first attempt at this project died in the WASM linker with "Allocation failed" --
         * wasm-ld running out of memory. Two things fed that. One was other processes holding
         * RAM; the other is that these settings are EDITOR state, not project state. They live
         * in Library/, they are whatever the last person to touch the Build Settings window
         * left them as, and they are not in version control -- so a build can fail on one
         * machine and succeed on another for a reason that appears in no diff.
         *
         * A development build keeps every symbol the linker must resolve and hold in memory at
         * once. On a project this size -- 219 MB of source textures plus the whole URP shader
         * set -- that is the difference between linking and not.
         *
         * Set here rather than in ApplyPlayerSettings because it is a property of THIS build
         * rather than of the project, and a shipping web build is never a debug build.
         */
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;

        /*
         * Assert the embed template, rather than trusting that step 4 was run.
         *
         * This build came out with Unity's DEFAULT template, because BuildWebGL was run on its
         * own and ApplyPlayerSettings sets the template. That is the precise cause of the
         * white screen this project already spent a day on: the default hardcodes the canvas
         * to 960x600 and centres it on a white page, so inside the site's 835x470 iframe the
         * canvas overflows both axes and a visitor sees white with scrollbars. It also reports
         * failures through alert(), which a sandboxed iframe blocks silently.
         *
         * The failure mode is what makes this worth enforcing here: the build SUCCEEDS. Nothing
         * warns, the folder looks right, the size is right, and it is broken in a way only
         * visible by loading it in a browser. A build step whose omission produces a
         * successful-looking broken artefact should not be a separate menu item you remember.
         */
        const string embedTemplate = "PROJECT:SiteEmbed";
        if (PlayerSettings.WebGL.template != embedTemplate)
        {
            Debug.Log($"[WebGLOptimizer] template was '{PlayerSettings.WebGL.template}' -- setting {embedTemplate}");
            PlayerSettings.WebGL.template = embedTemplate;
        }

        if (!System.IO.Directory.Exists("Assets/WebGLTemplates/SiteEmbed"))
        {
            // Setting a template that does not exist does not fail -- Unity falls back to the
            // default and carries on, which is how this would silently regress if the folder
            // were ever renamed or lost in a merge.
            Debug.LogError("[WebGLOptimizer] Assets/WebGLTemplates/SiteEmbed is missing. The build " +
                           "would fall back to Unity's default template and render as a white screen " +
                           "inside the site's iframe.");
            EditorApplication.Exit(3);
            return;
        }

        // See StripTerrainNormalMaps: without this the terrain shader does not compile on
        // ANGLE D3D11 and the ground is simply absent. Restored in the finally below whatever
        // happens, so the project is never left modified.
        var strippedNormalMaps = StripTerrainNormalMaps();

        UnityEditor.Build.Reporting.BuildReport.GetLatestReport();
        UnityEditor.Build.Reporting.BuildSummary summary;
        try
        {
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });
            summary = result.summary;
        }
        finally
        {
            RestoreTerrainNormalMaps(strippedNormalMaps);
        }
        Debug.Log($"[WebGLOptimizer] BUILD {summary.result} -- {summary.totalSize / 1048576f:0.0} MB total, " +
                  $"{summary.totalErrors} error(s), took {summary.totalTime}");

        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    /// <summary>
    /// Picks a cap from what the texture is FOR, inferred from its path and importer type.
    /// <para>
    /// Role beats uniformity here. A flat 1024 across the board would leave normal maps -- of
    /// which these projects have dozens, each as expensive as a colour map -- four times
    /// larger than they need to be, while a flat 512 would visibly soften the crosshair the
    /// player stares at all game.
    /// </para>
    /// </summary>
    static int TargetSizeFor(string path, TextureImporter importer)
    {
        var lower = path.ToLowerInvariant();

        if (importer.textureType == TextureImporterType.NormalMap) return CapNormal;
        if (importer.textureType == TextureImporterType.Sprite || lower.Contains("/ui/")) return CapUI;
        if (lower.Contains("crosshair") || lower.Contains("hud") || lower.Contains("icon")) return CapUI;
        if (lower.Contains("skybox") || lower.Contains("cubemap")) return CapSkybox;
        if (lower.Contains("terrain")) return CapTerrain;

        // Suffix conventions used by every asset pack in these projects. These maps carry
        // surface detail rather than anything the eye reads directly, so they tolerate half
        // the resolution of an albedo without a visible difference.
        foreach (var hint in new[]
                 {
                     "_n.", "_normal", "normals", "_mask", "_ao", "_s.", "_spec", "_metal",
                     "_rough", "_gloss", "_orm", "_ord", "_height", "_disp", "_emissive", "_opacity",
                 })
            if (lower.Contains(hint))
                return CapNormal;

        return CapDefault;
    }

    static bool Matches(TextureImporterPlatformSettings a, TextureImporterPlatformSettings b) =>
        a.overridden == b.overridden
        && a.maxTextureSize == b.maxTextureSize
        && a.textureCompression == b.textureCompression
        && a.crunchedCompression == b.crunchedCompression
        && a.compressionQuality == b.compressionQuality;

    static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
}
