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
        if(!item.IsFirstPerson()|| !item.IsHoldingGun())
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
    private void OnDestroy()
    {
        playerAction.Disable();
    }
}
