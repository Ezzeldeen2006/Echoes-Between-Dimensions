// Renders the HUD to a PNG so it can be looked at without opening the editor.
//
// The point is not convenience. Everything asserted about this UI so far -- HUDVerify's pass,
// the reference counts, the culling mask -- is structural: it proves the objects exist and are
// wired to each other. None of it can see that a panel is off the bottom of the screen, that
// two elements overlap, or that a label is drawn in a colour that vanishes against what is
// behind it. A layout is a visual artefact and the only honest check of one is to look at it.
//
// Run from Tools > Echoes > 5. Render a HUD preview.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class HUDPreview
{
    const string OutputPath = "HUDPreview.png";

    [MenuItem("Tools/Echoes/5. Render a HUD preview", priority = 5)]
    public static void Render()
    {
        if (!EnsureSceneOpen()) return;

        var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
        var minimap = Object.FindFirstObjectByType<Minimap>(FindObjectsInactive.Include);
        if (hud == null)
        {
            Debug.LogError("[HUDPreview] no GameHUD in the scene.");
            return;
        }

        var canvas = hud.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogError("[HUDPreview] the HUD has no Canvas.");
            return;
        }

        /*
         * A Screen Space Overlay canvas is composited by the engine after every camera has
         * finished, and is not visible to any camera -- so it cannot be captured by rendering
         * one. Switching it to Screen Space Camera for the duration is what makes it
         * photographable at all.
         *
         * Everything changed here is restored in the finally block, and the scene is never
         * saved. A preview that leaves the scene modified would be a tool that damages the
         * thing it is inspecting.
         */
        var previousMode = canvas.renderMode;
        var previousCamera = canvas.worldCamera;
        var previousDistance = canvas.planeDistance;

        // The states worth seeing: mid-damage, gun out and warm, inventory open. Left at their
        // real runtime values the preview would show three full blocks, no heat gauge at all
        // (it hides when the gun is not held) and an invisible inventory -- a picture of the
        // HUD doing nothing.
        var previousHeatActive = hud.heatRoot != null && hud.heatRoot.activeSelf;
        var previousFill = hud.heatFill != null ? hud.heatFill.fillAmount : 0f;
        var previousAlpha = hud.inventoryGroup != null ? hud.inventoryGroup.alpha : 0f;
        var previousMinimapActive = minimap != null && minimap.container != null && minimap.container.activeSelf;

        GameObject rig = null;
        RenderTexture target = null;

        try
        {
            if (hud.heatRoot != null) hud.heatRoot.SetActive(true);
            if (hud.heatFill != null)
            {
                hud.heatFill.fillAmount = 0.68f;
                hud.heatFill.color = new Color(0.98f, 0.55f, 0.25f);
            }
            if (hud.heatGlow != null) hud.heatGlow.color = new Color(0.98f, 0.72f, 0.24f, 0.34f);
            if (hud.inventoryGroup != null) hud.inventoryGroup.alpha = 1f;
            if (minimap != null && minimap.container != null) minimap.container.SetActive(true);

            /*
             * Drive the map camera by hand before capturing.
             *
             * Its RenderTexture is only written by the camera itself, and in edit mode nothing
             * makes a camera render -- so the first preview showed the border ring and the
             * player dot floating over an empty circle. That is indistinguishable, in a
             * screenshot, from a minimap that is genuinely broken, which makes the preview
             * useless for the one question it was built to answer.
             *
             * Positioning and rendering it here does the job Minimap.FollowPlayer does at
             * runtime, so the capture shows what the player will actually see -- and if the
             * circle is still empty after this, that is a real fault rather than an artefact.
             */
            if (minimap != null && minimap.minimapCamera != null && minimap.player != null)
            {
                var above = minimap.player.position;
                above.y += minimap.height;
                minimap.minimapCamera.transform.position = above;
                minimap.minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                minimap.minimapCamera.orthographicSize = minimap.worldRadius;
                minimap.minimapCamera.Render();
            }

            // One block spent, and the spent one tinted red -- what the display looks like the
            // moment after taking a hit, which is the state it exists for.
            if (hud.healthSegments != null && hud.healthSegments.Length == 3)
            {
                if (hud.healthSegments[2] != null) hud.healthSegments[2].color = new Color(0.55f, 0.20f, 0.20f);
            }

            // The scanner unowned, so the dimming that tells the player what they are still
            // missing is visible rather than having to be taken on trust.
            if (hud.scannerIcon != null) hud.scannerIcon.color = new Color(1f, 1f, 1f, 0.18f);

            rig = new GameObject("__HUDPreviewCamera");
            var camera = rig.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Mid grey rather than transparent or black. The HUD's own panels are near-black
            // with alpha, so on a black background their edges would be invisible and the
            // preview would flatter the design by hiding exactly what it should show.
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
            camera.orthographic = true;
            camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;

            target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            camera.targetTexture = target;

            // Layout is computed lazily. Without this the canvas still holds the sizes it had
            // under the previous render mode and the capture is one frame stale -- which on a
            // canvas that just changed mode means everything in the wrong place.
            Canvas.ForceUpdateCanvases();
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(OutputPath, image.EncodeToPNG());
            Object.DestroyImmediate(image);

            Debug.Log($"[HUDPreview] wrote {Path.GetFullPath(OutputPath)} at {target.width}x{target.height}");
        }
        finally
        {
            canvas.renderMode = previousMode;
            canvas.worldCamera = previousCamera;
            canvas.planeDistance = previousDistance;

            if (hud.heatRoot != null) hud.heatRoot.SetActive(previousHeatActive);
            if (hud.heatFill != null) hud.heatFill.fillAmount = previousFill;
            if (hud.inventoryGroup != null) hud.inventoryGroup.alpha = previousAlpha;
            if (minimap != null && minimap.container != null) minimap.container.SetActive(previousMinimapActive);

            if (rig != null) Object.DestroyImmediate(rig);
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }

            // Deliberately NOT saved. The colours and alphas above were set for the photograph,
            // and writing them back would make the preview's staging permanent.
            Debug.Log("[HUDPreview] scene restored; not saved.");
        }
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

        Debug.LogError("[HUDPreview] no enabled scene in Build Settings to open.");
        return false;
    }
}
