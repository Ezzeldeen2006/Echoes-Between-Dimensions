// Checks that the generated HUD is actually wired, rather than merely present.
//
// This exists because of how the Phase 4 bugs on the website went: three of them lived in code
// paths with passing tests, and every one was invisible until the data it operated on existed.
// The equivalent here is a HUD whose GameObjects are all in the scene and whose script
// references are null -- the hierarchy looks completely correct, nothing throws, and the game
// ships with a health bar that never changes. An unassigned reference in Unity is not an error,
// it is a null that most code politely skips. GameHUD skips it: every Update method starts with
// a null guard, so a missing PlayerHealth means the health display silently never updates.
//
// Run from Tools > Echoes > 4. Verify the HUD wiring.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class HUDVerify
{
    [MenuItem("Tools/Echoes/4. Verify the HUD wiring", priority = 4)]
    public static void Verify()
    {
        if (!EnsureSceneOpen()) return;

        var problems = new List<string>();

        var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
        if (hud == null)
        {
            problems.Add("no GameHUD in the scene -- run 'Build the HUD and minimap' first");
        }
        else
        {
            Require(problems, hud.playerHealth, "GameHUD.playerHealth");
            Require(problems, hud.gunSystem, "GameHUD.gunSystem");
            Require(problems, hud.itemHolder, "GameHUD.itemHolder");
            Require(problems, hud.healthGroup, "GameHUD.healthGroup");
            Require(problems, hud.heatRoot, "GameHUD.heatRoot");
            Require(problems, hud.heatFill, "GameHUD.heatFill");
            Require(problems, hud.heatGlow, "GameHUD.heatGlow");
            Require(problems, hud.heatLabel, "GameHUD.heatLabel");
            Require(problems, hud.inventoryGroup, "GameHUD.inventoryGroup");
            Require(problems, hud.gunIcon, "GameHUD.gunIcon");
            Require(problems, hud.scannerIcon, "GameHUD.scannerIcon");

            Require(problems, hud.rootGroup, "GameHUD.rootGroup");
            Require(problems, hud.healthReadout, "GameHUD.healthReadout");

            /*
             * The hull readout is three parallel arrays -- cores, sockets and blooms -- and each
             * is checked for LENGTH as well as for null. An Image[0] is not null and would pass
             * a null check while displaying no health at all, and an array that is merely
             * shorter than the others is worse: GameHUD.UpdateHealth guards every index, so a
             * two-long glow array produces a readout where the third plate silently never
             * blooms. That is a bug with no error and no crash, visible only by noticing an
             * absence in one state of one element.
             */
            // One plate per hit point. Checking against PlayerHealth rather than against a
            // literal 3 is the point: the two used to be independent constants, and the whole
            // failure mode was them drifting apart without anything noticing.
            var plates = hud.playerHealth != null ? hud.playerHealth.MaxHealth() : 3;
            RequireLayer(problems, hud.healthSegments, "healthSegments", plates);
            RequireLayer(problems, hud.healthShells, "healthShells", plates);
            RequireLayer(problems, hud.healthGlows, "healthGlows", plates);

            // The menu gate. Null is legal in GameHUD -- it means "always visible" -- but in
            // THIS scene there is a MainMenu, so a null here means the wiring in HUDBuilder
            // failed and the HUD will sit on top of the title screen.
            if (Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include) != null)
            {
                Require(problems, hud.menuPanel, "GameHUD.menuPanel (scene has a MainMenu)");
            }

            // fillAmount does nothing on a Simple image. The gauge would sit permanently full
            // and read as "the heat system is broken" rather than as a UI setting.
            if (hud.heatFill != null && hud.heatFill.type != Image.Type.Filled)
            {
                problems.Add($"GameHUD.heatFill is Image.Type.{hud.heatFill.type}, must be Filled or fillAmount is ignored");
            }

            Debug.Log($"[HUDVerify] GameHUD on '{hud.name}': " +
                      $"{(hud.healthSegments?.Length ?? 0)} health segment(s), " +
                      $"heat root '{(hud.heatRoot != null ? hud.heatRoot.name : "MISSING")}', " +
                      $"inventory alpha {(hud.inventoryGroup != null ? hud.inventoryGroup.alpha.ToString("0.##") : "n/a")}");
        }

        var minimap = Object.FindFirstObjectByType<Minimap>(FindObjectsInactive.Include);
        if (minimap == null)
        {
            problems.Add("no Minimap in the scene");
        }
        else
        {
            Require(problems, minimap.minimapCamera, "Minimap.minimapCamera");
            Require(problems, minimap.mapRoot, "Minimap.mapRoot");
            Require(problems, minimap.dotLayer, "Minimap.dotLayer");
            Require(problems, minimap.playerDot, "Minimap.playerDot");
            Require(problems, minimap.enemyDotPrefab, "Minimap.enemyDotPrefab");
            Require(problems, minimap.container, "Minimap.container");
            Require(problems, minimap.player, "Minimap.player");

            if (minimap.minimapCamera != null)
            {
                var camera = minimap.minimapCamera;

                if (camera.targetTexture == null)
                {
                    // Without a target texture the map camera renders over the game view --
                    // the map would replace the game rather than appear beside it.
                    problems.Add("Minimap.minimapCamera has no targetTexture; it would render to the screen");
                }

                // The brief's actual requirement: the player and enemies must not be directly
                // visible on the map. This is that requirement expressed as an assertion.
                foreach (var layer in new[] { "Playerbody", "Robotlayer" })
                {
                    var index = LayerMask.NameToLayer(layer);
                    if (index >= 0 && (camera.cullingMask & (1 << index)) != 0)
                    {
                        problems.Add($"the minimap camera still renders layer '{layer}' -- the brief requires dots, not the objects themselves");
                    }
                }

                Debug.Log($"[HUDVerify] minimap camera: ortho={camera.orthographic}, size={camera.orthographicSize}, " +
                          $"target={(camera.targetTexture != null ? camera.targetTexture.name : "NONE")}, " +
                          $"culls={DescribeCulledLayers(camera.cullingMask)}");
            }

            // dotLayer.rect.width converts world units into map pixels. At zero every enemy dot
            // lands on the player, which looks like a working map right up until you read it.
            if (minimap.dotLayer != null && minimap.dotLayer.rect.width <= 1f)
            {
                problems.Add($"Minimap.dotLayer is {minimap.dotLayer.rect.width} wide; dot positions would all collapse to the centre");
            }
        }

        // The dots must be UNDER mapRoot, or they will not inherit its rotation and the map will
        // turn while the enemies stay put -- half of requirement (f), silently missing.
        if (minimap != null && minimap.dotLayer != null && minimap.mapRoot != null
            && !minimap.dotLayer.IsChildOf(minimap.mapRoot))
        {
            problems.Add("Minimap.dotLayer is not a child of mapRoot, so the dots will not rotate with the map");
        }

        if (problems.Count == 0)
        {
            Debug.Log("[HUDVerify] PASS -- every HUD and minimap reference is wired.");
            return;
        }

        foreach (var problem in problems)
        {
            Debug.LogError("[HUDVerify] " + problem);
        }
        Debug.LogError($"[HUDVerify] FAIL -- {problems.Count} problem(s).");
    }

    static void Require(List<string> problems, Object value, string name)
    {
        // Unity's == is overloaded so a destroyed object compares equal to null. Using it
        // deliberately here: a reference to a deleted GameObject is exactly as broken as an
        // unassigned one, and this catches both.
        if (value == null) problems.Add(name + " is not assigned");
    }

    /// <summary>One of the per-plate arrays in the hull readout: one entry per hit point, none null.</summary>
    static void RequireLayer(List<string> problems, Image[] layer, string name, int plates)
    {
        if (layer == null || layer.Length != plates)
        {
            problems.Add($"GameHUD.{name} is {(layer == null ? "null" : layer.Length + " long")}, expected {plates}");
            return;
        }

        for (var i = 0; i < layer.Length; i++)
        {
            Require(problems, layer[i], $"GameHUD.{name}[{i}]");
        }
    }

    static string DescribeCulledLayers(int mask)
    {
        var culled = new List<string>();
        for (var i = 0; i < 32; i++)
        {
            var name = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(name)) continue;
            if ((mask & (1 << i)) == 0) culled.Add(name);
        }
        return culled.Count == 0 ? "nothing" : string.Join(", ", culled);
    }

    static bool EnsureSceneOpen()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && active.isLoaded && !string.IsNullOrEmpty(active.path)) return true;

        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;
            EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            return true;
        }

        Debug.LogError("[HUDVerify] no enabled scene in Build Settings to open.");
        return false;
    }
}
