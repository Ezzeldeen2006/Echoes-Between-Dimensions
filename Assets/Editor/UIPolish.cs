// UI fixes and polish for Echoes Between Dimensions.
//
// Two of these address marks the professor actually deducted:
//
//   "The crosshair canvas does not scale properly with different screen resolutions."  (-1)
//   "Canvas scaling issues are present in the UI."                                     (-0.5)
//
// Run from Tools > Echoes. Each command is separate so you can apply one and look at it.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class UIPolish
{
    /// <summary>
    /// Puts every CanvasScaler in the open scene onto resolution-relative scaling.
    ///
    /// <para><b>The bug, precisely.</b> Two of this scene's four canvases were set to
    /// <c>Constant Pixel Size</c>. That mode means exactly what it says: a 64-pixel crosshair
    /// stays 64 pixels whether the screen is 1280x720 or 2560x1440. On a larger display every
    /// UI element shrinks relative to the view, and anything anchored with pixel offsets drifts
    /// away from where it was placed. That is why the crosshair "does not scale properly" --
    /// it was never scaling at all.</para>
    ///
    /// <para>The other two were already <c>Scale With Screen Size</c> but matched
    /// <b>width only</b> (<c>m_MatchWidthOrHeight: 0</c>). That is fine until the aspect ratio
    /// changes: on a taller or shorter window the UI is scaled purely by width, so it overflows
    /// or floats. 0.5 splits the difference and is the usual choice for a game that does not
    /// know its display.</para>
    ///
    /// <para>1920x1080 as the reference because that is what the UI was laid out against, and
    /// changing the reference would move every element that was positioned by eye.</para>
    /// </summary>
    [MenuItem("Tools/Echoes/1. Fix canvas scaling", priority = 1)]
    public static void FixCanvasScaling()
    {
        if (!EnsureSceneOpen()) return;

        var scalers = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var changed = 0;

        foreach (var scaler in scalers)
        {
            /*
             * World Space canvases are left alone, and that is not caution -- it is correct.
             *
             * CanvasScaler's scale mode only means something for Screen Space canvases. A World
             * Space canvas is a quad in the level: its size comes from its RectTransform and its
             * apparent size from how far away the camera is. Unity ignores the mode entirely,
             * so "fixing" the two prompt canvases floating over the gun and the scanner would
             * change nothing while making the diff look like it did.
             */
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                Debug.Log($"[UIPolish] skipping {Path(scaler.transform)} -- World Space, scale mode does not apply");
                continue;
            }

            var before = $"{scaler.uiScaleMode}, ref {scaler.referenceResolution}, match {scaler.matchWidthOrHeight}";

            var needs = scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
                        || scaler.referenceResolution != new Vector2(1920, 1080)
                        || scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight
                        || !Mathf.Approximately(scaler.matchWidthOrHeight, 0.5f);
            if (!needs) continue;

            Undo.RecordObject(scaler, "Fix canvas scaling");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            EditorUtility.SetDirty(scaler);
            changed++;

            Debug.Log($"[UIPolish] {Path(scaler.transform)}\n    was: {before}\n    now: ScaleWithScreenSize, ref (1920, 1080), match 0.5");
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log($"[UIPolish] canvas scaling: {changed} of {scalers.Length} scaler(s) corrected.");
    }

    /// <summary>
    /// Reports what the UI currently looks like, so a change can be checked rather than assumed.
    /// </summary>
    [MenuItem("Tools/Echoes/0. Report UI state", priority = 0)]
    public static void ReportUIState()
    {
        if (!EnsureSceneOpen()) return;

        foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var canvas = scaler.GetComponent<Canvas>();
            Debug.Log($"[UIPolish] {Path(scaler.transform)}: render={canvas?.renderMode}, " +
                      $"mode={scaler.uiScaleMode}, ref={scaler.referenceResolution}, " +
                      $"match={scaler.matchWidthOrHeight}, active={scaler.gameObject.activeInHierarchy}");
        }

        foreach (var image in Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var n = image.name.ToLowerInvariant();
            if (!n.Contains("cross") && !n.Contains("hair") && !n.Contains("aim") && !n.Contains("reticle")) continue;
            var rt = image.rectTransform;
            Debug.Log($"[UIPolish] CROSSHAIR {Path(image.transform)}: size={rt.sizeDelta}, " +
                      $"anchorMin={rt.anchorMin}, anchorMax={rt.anchorMax}, pos={rt.anchoredPosition}");
        }

        foreach (var slider in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Debug.Log($"[UIPolish] Slider {Path(slider.transform)}: min={slider.minValue}, max={slider.maxValue}, value={slider.value}");
        }
    }

    /// <summary>
    /// Opens the first enabled build scene when none is loaded.
    ///
    /// <para>Necessary because these commands are run headless as well as from the menu, and
    /// batchmode starts with NO scene open -- so FindObjectsByType returns nothing and the
    /// command silently reports "0 of 0 corrected", which looks like success. Opening the
    /// scene explicitly is the difference between a fix and a no-op that claims to be one.
    /// </para>
    /// </summary>
    static bool EnsureSceneOpen()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && active.isLoaded && !string.IsNullOrEmpty(active.path))
        {
            return true;
        }

        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;
            EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            Debug.Log($"[UIPolish] opened {entry.path}");
            return true;
        }

        Debug.LogError("[UIPolish] no enabled scene in Build Settings to open.");
        return false;
    }

    static string Path(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
