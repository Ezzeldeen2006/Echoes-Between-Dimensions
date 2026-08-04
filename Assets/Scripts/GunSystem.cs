using UnityEngine;

public class GunSystem : MonoBehaviour
{
    public GameObject Laser;
    public Transform gunFirePoint;
    private float currentHeat=0f;
    private float maxHeat=100f;
    private float heatPerShot = 15f;
    private float coolingDown = 20f;
    private bool isOverHeated = false;
    private PlayerInputActions playerAction;
    private ItemHolder item;
    public GameObject gunCrossHair;

    /*
     * Read by the HUD's heat gauge (bonus task "d. Advanced UI").
     *
     * Exposed as accessors rather than by making the fields public: the heat is owned by this
     * script and the UI is a reader. A public field would let any future script quietly cool
     * the gun down, which is the kind of thing that turns an overheat mechanic into an
     * intermittent bug nobody can reproduce.
     */
    public float HeatFraction()
    {
        return maxHeat <= 0f ? 0f : Mathf.Clamp01(currentHeat / maxHeat);
    }

    public bool IsOverHeated()
    {
        return isOverHeated;
    }

    private void Awake()
    {
        playerAction = new PlayerInputActions();
        playerAction.Enable();
        item= GetComponent<ItemHolder>();
    }
    // The empty Start() was removed -- Unity calls every message it finds, so an empty one
    // costs a dispatch for nothing and implies start-up work that does not exist.

    // Update is called once per frame
    void Update()
    {
 
        if(currentHeat>0)
        {
            currentHeat -= coolingDown * Time.deltaTime;

        }
        if(isOverHeated&& currentHeat<=0)
        {
            isOverHeated = false;
        }
        /*
         * `IsPickingUp()` is part of this condition, and it is the fix for being able to shoot
         * during the pickup animation.
         *
         * The item is attached to the hand by an animation event a quarter of a second into a
         * multi-second clip -- that is when it should visually appear in the hand. But
         * IsHoldingGun() flips true at that same instant, so for the whole rest of the animation
         * the player was mid-crouch, still reaching for the weapon, and already able to aim and
         * fire it. The gun existed before the character had finished picking it up.
         *
         * Gating on the pickup rather than on the attach makes "holding it" and "able to use it"
         * two different things, which is what they always were.
         */
        if(!item.IsFirstPerson()|| !item.IsHoldingGun() || item.IsPickingUp())
        {
            gunCrossHair.SetActive(false);
            return;
        }
        else
        {
            gunCrossHair.SetActive(true);
        }
        if (isOverHeated)
            return;
        if (playerAction.Player.ShootGun.WasPressedThisFrame())
        {
            Instantiate(Laser, gunFirePoint.position, gunFirePoint.rotation);
            currentHeat += heatPerShot;
            if (currentHeat >= maxHeat)
            {
                currentHeat= maxHeat;
                isOverHeated = true;
            }
        }
    }
    /// <summary>
    /// Hides the crosshair whenever this script stops running.
    ///
    /// <para><b>This is the fix for the crosshair surviving death in first person.</b> The
    /// crosshair is shown and hidden from Update, and it is the only thing that ever hides it.
    /// PlayerHealth.Die() sets <c>gunSystem.enabled = false</c> -- so on the frame the player
    /// dies while aiming, Update had already turned the crosshair ON, and then never ran again
    /// to turn it off. The reticle stayed floating over the death camera for the rest of the
    /// run.</para>
    ///
    /// <para>Die() already leaves first person and clears the held items before disabling this,
    /// so one more Update would have hidden it correctly -- the bug is purely that it was
    /// switched off a frame too early. Fixing it here rather than by reordering Die() is
    /// deliberate: the invariant is "no gun script running means no gun crosshair", and it
    /// belongs to the component that owns the crosshair. Reordering would fix today's single
    /// caller and leave the next one to rediscover this.</para>
    /// </summary>
    private void OnDisable()
    {
        if (gunCrossHair != null)
        {
            gunCrossHair.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        playerAction.Disable();
    }
}
