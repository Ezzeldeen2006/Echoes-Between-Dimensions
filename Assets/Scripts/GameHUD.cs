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
    [Header("Root visibility")]
    /// <summary>
    /// The whole HUD, faded out while the main menu is up.
    ///
    /// <para>The menu is shown at <c>Start</c> with <c>Time.timeScale = 0</c> and is the pause
    /// menu as well, so without this the minimap, the hull readout and the heat gauge all sit
    /// behind it from the moment the game loads -- instrumentation for a game that has not
    /// begun, on top of the title screen.</para>
    /// </summary>
    public CanvasGroup rootGroup;

    /// <summary>
    /// The menu panel itself, not the <c>MainMenu</c> component.
    ///
    /// <para>Its <c>activeSelf</c> IS the game's paused state -- <c>MainMenu</c> drives both
    /// from the same place -- so reading it needs no new flag and cannot fall out of step with
    /// one. Watching a bool on <c>MainMenu</c> instead would mean two things to keep in sync.</para>
    ///
    /// <para>Null means "always show". That direction of failure is deliberate: a HUD that
    /// appears a few seconds early is a blemish, a HUD that never appears is the game losing
    /// its health readout, and an unwired reference should not be able to cause the second one.</para>
    /// </summary>
    public GameObject menuPanel;

    [Header("Health")]
    /// <summary>The bright cores. One per hit point; hidden when that point is spent.</summary>
    public Image[] healthSegments;
    /// <summary>The hatched sockets behind them, always visible, so an empty slot still reads.</summary>
    public Image[] healthShells;
    /// <summary>Bloom behind each core. Tinted with the core and faded out with it.</summary>
    public Image[] healthGlows;
    /// <summary>The "03" count, which is the part read at a glance from the corner of the eye.</summary>
    public Text healthReadout;
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
        // The menu is modal, so nothing behind it should be drawn. Returning early also stops
        // the damage flash and the critical pulse from advancing while paused, which would
        // otherwise burn through their animations behind the menu and be over by the time the
        // player is looking at the game again.
        if (!UpdateRootVisibility())
        {
            return;
        }

        UpdateHealth();
        UpdateHeat();
        UpdateInventory();
    }

    /// <summary>Fades the HUD with the menu. Returns whether the HUD is currently shown.</summary>
    private bool UpdateRootVisibility()
    {
        var menuUp = menuPanel != null && menuPanel.activeInHierarchy;

        if (rootGroup != null)
        {
            // unscaledDeltaTime, because the menu sets Time.timeScale to 0 and the fade has to
            // run while it is up -- with scaled time the HUD would still be at full opacity for
            // the entire main menu and only snap away on the frame the game unpauses.
            rootGroup.alpha = Mathf.MoveTowards(rootGroup.alpha, menuUp ? 0f : 1f, Time.unscaledDeltaTime * 6f);
        }

        return !menuUp;
    }

    /// <summary>
    /// The hull readout: three sheared plates, a bloom behind each, and a count.
    ///
    /// <para><b>The colour escalates with the remaining plates rather than staying cyan.</b> The
    /// previous version drew every surviving block in the same colour and let the count carry
    /// the whole message, which means at two hit points the display looks exactly as calm as it
    /// does at three -- the player has to notice an absence to learn they are in trouble. A
    /// plate going amber and then red is a change you catch without looking at it, which is the
    /// only way a health bar in the corner of the screen is ever actually read.</para>
    /// </summary>
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

        damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.deltaTime * 2.2f);

        // Escalation by what is LEFT, not by what the maximum is, so it reads the same whether
        // the player is at 2 of 3 having taken a hit or at 2 of 2 in some later tuning pass.
        var tint = current >= 3 ? Cool : current == 2 ? Warm : Hot;

        for (var i = 0; i < healthSegments.Length; i++)
        {
            var filled = i < current;
            // The plate that was just spent, which is the one the eye should go to.
            var justLost = !filled && i == current && damageFlash > 0f;

            if (healthSegments[i] != null)
            {
                // The core does not change colour when it empties -- it goes away. An empty
                // slot is the hatched socket underneath, which is a different shape rather than
                // the same shape in a darker colour, and shape survives peripheral vision.
                var core = filled ? tint : Hot;
                core.a = filled ? 1f : damageFlash;
                healthSegments[i].color = core;
            }

            if (healthGlows != null && i < healthGlows.Length && healthGlows[i] != null)
            {
                var glow = filled ? tint : Hot;
                // Brighter as the situation worsens: at one plate the last core is haloed hard.
                var strength = filled ? (current <= 1 ? 0.42f : current == 2 ? 0.30f : 0.22f) : damageFlash * 0.55f;
                glow.a = strength;
                healthGlows[i].color = glow;
            }

            if (healthShells != null && i < healthShells.Length && healthShells[i] != null)
            {
                // A spent socket is hazard-striped in red at low alpha; an occupied one is a
                // near-invisible frame, because behind a lit plate it is structure, not signal.
                healthShells[i].color = filled
                    ? new Color(SegmentEmpty.r, SegmentEmpty.g, SegmentEmpty.b, 0.55f)
                    : Color.Lerp(new Color(Hot.r, Hot.g, Hot.b, 0.42f), new Color(Hot.r, Hot.g, Hot.b, 0.85f), justLost ? damageFlash : 0f);
            }
        }

        if (healthReadout != null)
        {
            // Zero-padded so the glyph count never changes. "3" becoming "10" would shift the
            // whole readout sideways, and a number that moves is a number you re-read.
            healthReadout.text = Mathf.Max(0, current).ToString("00");
            healthReadout.color = tint;
        }

        // On the last plate the whole group breathes. This is the one place a permanent
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
