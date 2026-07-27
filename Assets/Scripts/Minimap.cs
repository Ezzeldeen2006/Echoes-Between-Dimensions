using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Circular minimap. Bonus task (f) from the brief, implemented to its stated requirements:
///
/// <list type="bullet">
/// <item>circular, with a border</item>
/// <item>the player and enemies are NOT directly visible -- they are a blue dot and red dots</item>
/// <item>rotates in sync with the main camera's Y-axis rotation</item>
/// <item>toggled with the M key</item>
/// </list>
///
/// <para>The map itself is a second camera rendering straight down into a RenderTexture. The
/// player and robot layers are culled OUT of it, which is what "must not be directly visible"
/// means -- not "hidden behind a dot", genuinely not rendered. The dots are UI images
/// positioned from world coordinates each frame, so an enemy behind a building still shows,
/// which is the point of a minimap.</para>
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("Wiring (set by the HUD builder)")]
    public Camera minimapCamera;
    public RectTransform mapRoot;      // rotates with the camera
    public RectTransform dotLayer;     // holds the player and enemy dots
    public Image playerDot;
    public Image enemyDotPrefab;
    public GameObject container;       // the whole widget, toggled by M
    public Transform player;

    [Header("Tuning")]
    [Tooltip("World units from the centre of the map to its edge.")]
    public float worldRadius = 45f;

    public float height = 60f;

    private Camera mainCamera;
    private readonly List<Image> enemyDots = new List<Image>();
    private bool visible = true;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        /*
         * M is read from the keyboard directly rather than through PlayerInputActions.
         *
         * The generated input asset has no Minimap action, and adding one means regenerating
         * PlayerInputActions.cs -- a 38 KB generated file that everything else depends on.
         * Reading one key straight from the device is a smaller and more honest dependency
         * than regenerating a file to add a single binding.
         */
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
        {
            visible = !visible;
            if (container != null)
            {
                container.SetActive(visible);
            }
        }

        if (!visible || player == null)
        {
            return;
        }

        FollowPlayer();
        AlignWithCamera();
        DrawDots();
    }

    /// <summary>Keeps the map camera directly above the player, looking down.</summary>
    private void FollowPlayer()
    {
        if (minimapCamera == null)
        {
            return;
        }

        var position = player.position;
        position.y += height;
        minimapCamera.transform.position = position;

        /*
         * The CAMERA stays north-up and the IMAGE is rotated instead (see AlignWithCamera).
         *
         * Rotating the camera would work too, but a rotating orthographic camera has to render
         * a larger area to keep the circle filled at 45 degrees -- otherwise the corners of the
         * captured square swing into view and the map shows blank wedges as you turn. Rotating
         * a square texture inside a circular mask has no such problem: the circle is always
         * inside the square.
         */
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        minimapCamera.orthographicSize = worldRadius;
    }

    /// <summary>
    /// Rotates the map so it turns with the camera, as the brief requires.
    /// </summary>
    private void AlignWithCamera()
    {
        if (mapRoot == null)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        /*
         * POSITIVE, and it is worth being explicit because the sign is easy to get backwards.
         *
         * The map camera is locked north-up, so the texture always has world north at the top
         * and east on the right. Turn to face east (yaw 90) and we want east at the top --
         * which means rotating the image counter-clockwise by 90. Unity's UI treats positive Z
         * as counter-clockwise, so the rotation is +yaw, not -yaw.
         *
         * The dots are children of this transform, so they inherit the rotation and are
         * positioned in plain north-up space. An earlier version rotated them by hand as well,
         * which applied the rotation twice and made enemies orbit the player as you turned.
         */
        mapRoot.localRotation = Quaternion.Euler(0f, 0f, mainCamera.transform.eulerAngles.y);
    }

    private void DrawDots()
    {
        if (dotLayer == null)
        {
            return;
        }

        var radiusPixels = dotLayer.rect.width * 0.5f;

        if (playerDot != null)
        {
            // The player is always dead centre: the map follows them.
            playerDot.rectTransform.anchoredPosition = Vector2.zero;
        }

        var robots = GameObject.FindGameObjectsWithTag("Robot");

        // Grow the pool as needed. Robots are destroyed when killed, so the count only ever
        // falls during a run -- the surplus dots are hidden rather than destroyed so a scene
        // restart does not have to rebuild them.
        while (enemyDots.Count < robots.Length && enemyDotPrefab != null)
        {
            var dot = Instantiate(enemyDotPrefab, dotLayer);
            dot.gameObject.SetActive(false);
            enemyDots.Add(dot);
        }

        for (var i = 0; i < enemyDots.Count; i++)
        {
            if (i >= robots.Length || robots[i] == null)
            {
                enemyDots[i].gameObject.SetActive(false);
                continue;
            }

            var offset = robots[i].transform.position - player.position;
            var flat = new Vector2(offset.x, offset.z);

            // Anything past the edge of the map is simply not shown, rather than being pinned
            // to the rim. A dot clamped to the edge says "an enemy is somewhere that way",
            // which on a map this size is noise -- and it would also sit outside the circular
            // mask and clip against the border.
            if (flat.magnitude > worldRadius)
            {
                enemyDots[i].gameObject.SetActive(false);
                continue;
            }

            enemyDots[i].gameObject.SetActive(true);

            // World XZ straight to map pixels, north-up. The rotation comes from the parent
            // (see AlignWithCamera) -- applying it here too would double-count it.
            var normalised = flat / worldRadius * radiusPixels;
            enemyDots[i].rectTransform.anchoredPosition = normalised;
        }
    }

}
