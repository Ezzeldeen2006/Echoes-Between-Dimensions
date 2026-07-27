using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The heads-up display: health, gun heat, and the held-item inventory.
///
/// <para>Covers bonus task (d) "Advanced UI", which the brief counts as ONE item and requires
/// both halves: a heat system UI and an inventory shown while TAB is held.</para>
///
/// <para>The visual language follows the game rather than a generic template. Everything the
/// player interacts with in this world is powered hardware -- signal towers, a remote scanner,
/// robots, a portal -- so the HUD reads as instrumentation: hard-edged segments, a cyan
/// running colour that matches the scanner, amber and red for heat, and a shield that breaks
/// into discrete blocks rather than draining smoothly. Three hit points shown as a continuous
/// bar is the least informative way to display three of anything; as three blocks you can read
/// your health without looking at it.</para>
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("Health")]
    public Image[] healthSegments;
    public CanvasGroup healthGroup;

    [Header("Heat")]
    public GameObject heatRoot;
    public Image heatFill;
    public Image heatGlow;
    public Text heatLabel;

    [Header("Inventory (hold TAB)")]
    public CanvasGroup inventoryGroup;
    public Image gunIcon;
    public Image scannerIcon;

    [Header("Sources")]
    public PlayerHealth playerHealth;
    public GunSystem gunSystem;
    public ItemHolder itemHolder;

    // Palette. Cyan reads as "systems nominal" and matches the scanner's own light; amber and
    // red are the standard escalation and need no explaining to anyone who has played a game.
    private static readonly Color Cool = new Color(0.29f, 0.85f, 0.95f);
    private static readonly Color Warm = new Color(0.98f, 0.72f, 0.24f);
    private static readonly Color Hot = new Color(0.96f, 0.28f, 0.27f);
    private static readonly Color SegmentFull = new Color(0.29f, 0.85f, 0.95f);
    private static readonly Color SegmentEmpty = new Color(0.12f, 0.15f, 0.20f);

    private int lastHealth = -1;
    private float damageFlash;

    private void Update()
    {
        UpdateHealth();
        UpdateHeat();
        UpdateInventory();
    }

    private void UpdateHealth()
    {
        if (playerHealth == null || healthSegments == null)
        {
            return;
        }

        var current = playerHealth.CurrentHealth();

        // A flash on the frame health actually drops, rather than a permanent pulse. Constant
        // motion in the corner of the screen is noise; motion that only happens when something
        // changed is information.
        if (lastHealth >= 0 && current < lastHealth)
        {
            damageFlash = 1f;
        }
        lastHealth = current;

        damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.deltaTime * 2.5f);

        for (var i = 0; i < healthSegments.Length; i++)
        {
            if (healthSegments[i] == null) continue;

            var filled = i < current;
            var colour = filled ? SegmentFull : SegmentEmpty;

            // The segment you just lost flashes red as it empties, so the eye is drawn to the
            // one that changed rather than to the bar as a whole.
            if (!filled && i == current && damageFlash > 0f)
            {
                colour = Color.Lerp(SegmentEmpty, Hot, damageFlash);
            }

            healthSegments[i].color = colour;
        }

        // On the last block the whole group breathes. This is the one place a permanent
        // animation earns itself: at one hit from death, the player should feel hunted.
        if (healthGroup != null)
        {
            healthGroup.alpha = current <= 1
                ? 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 4f))
                : 1f;
        }
    }

    private void UpdateHeat()
    {
        if (gunSystem == null || heatRoot == null)
        {
            return;
        }

        // The gauge only exists while the gun is out. A heat readout for a weapon you are not
        // holding is clutter, and the brief asks for a gun heat indicator, not a permanent one.
        var holdingGun = itemHolder != null && itemHolder.IsHoldingGun();
        if (heatRoot.activeSelf != holdingGun)
        {
            heatRoot.SetActive(holdingGun);
        }
        if (!holdingGun)
        {
            return;
        }

        var heat = gunSystem.HeatFraction();
        var overheated = gunSystem.IsOverHeated();

        if (heatFill != null)
        {
            heatFill.fillAmount = heat;

            // Cool -> warm across the first half, warm -> hot across the second, so the colour
            // is already changing well before the weapon locks out. A bar that stays green
            // until it turns red gives no warning.
            heatFill.color = overheated
                ? Hot
                : heat < 0.5f
                    ? Color.Lerp(Cool, Warm, heat * 2f)
                    : Color.Lerp(Warm, Hot, (heat - 0.5f) * 2f);
        }

        if (heatGlow != null)
        {
            // Overheated pulses; otherwise the glow simply tracks how hot the gun is.
            var intensity = overheated
                ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 6f))
                : heat * 0.5f;
            var c = overheated ? Hot : Warm;
            c.a = intensity;
            heatGlow.color = c;
        }

        if (heatLabel != null)
        {
            heatLabel.text = overheated ? "OVERHEATED" : "HEAT";
            heatLabel.color = overheated ? Hot : new Color(0.66f, 0.72f, 0.78f);
        }
    }

    private void UpdateInventory()
    {
        if (inventoryGroup == null)
        {
            return;
        }

        var keyboard = Keyboard.current;
        var held = keyboard != null && keyboard.tabKey.isPressed;

        // Faded in and out rather than switched, so holding TAB feels like a panel opening
        // instead of a frame of popcorn.
        inventoryGroup.alpha = Mathf.MoveTowards(inventoryGroup.alpha, held ? 1f : 0f, Time.deltaTime * 8f);
        inventoryGroup.blocksRaycasts = false;

        if (itemHolder == null)
        {
            return;
        }

        // The brief: show the gun icon if the player HAS it, the scanner icon if they have it.
        // Dimmed rather than hidden when not owned, so the panel also tells you what you are
        // still missing -- which is the more useful thing for a player working through a level.
        if (gunIcon != null)
        {
            gunIcon.color = itemHolder.HasGun() ? Color.white : new Color(1f, 1f, 1f, 0.18f);
        }
        if (scannerIcon != null)
        {
            scannerIcon.color = itemHolder.HasScanner() ? Color.white : new Color(1f, 1f, 1f, 0.18f);
        }
    }
}
