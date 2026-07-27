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
