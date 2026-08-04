using UnityEngine;
using TMPro;

public class ScanTower : MonoBehaviour
{
    private float range= 10f;
    private float duration = 3f;
    public TextMeshProUGUI statusMsg;
    public TextMeshProUGUI ETA;
    public GameObject scannerContainer;
    private PlayerInputActions playerActions;
    private ItemHolder itemHolder;
    private float scanProgress = 0f;
    public Camera cam;

    // cam.transform is a native property call and was read twice every single frame.
    private Transform camTransform;

    private void Awake()
    {
        playerActions= new PlayerInputActions();
        playerActions.Enable();
        itemHolder = GetComponent<ItemHolder>();

        if (cam != null)
            camTransform = cam.transform;
    }

    /*
     * Assigning TextMeshPro's .text is not free: it marks the text dirty and rebuilds the
     * character geometry. This ran every frame with values that are usually identical --
     * "Tower in Range" for as long as you stand there -- so the mesh was regenerated sixty
     * times a second to produce the same pixels. The ETA readout is worse: it also builds a
     * fresh string via ToString("F1") each frame, which allocates and feeds the GC.
     *
     * Guarding on "did it actually change" makes the common case free and changes nothing
     * visible, because a value that has not changed cannot look different.
     */
    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null && label.text != value)
            label.text = value;
    }
    // Update is called once per frame
    void Update()
    {
        // IsPickingUp() for the same reason GunSystem checks it: the scanner is attached to the
        // hand a quarter of a second into its animation, so without this the player can start
        // scanning a tower while still kneeling to pick the scanner up.
        if(!itemHolder.IsFirstPerson()|| !itemHolder.IsHoldingScanner() || itemHolder.IsPickingUp())
        {
            scannerContainer.SetActive(false);
            SetText(ETA, "");
            scanProgress = 0f;
            return;

        }
        else
        {
            scannerContainer.SetActive(true);
        }

        RaycastHit hit;
        if(Physics.Raycast(camTransform.position,camTransform.forward,out hit, range))
        {
            if(hit.collider.CompareTag("Tower"))
            {
                TowerActivation tower = hit.collider.GetComponent<TowerActivation>();

                /*
                 * Null-checked, which the original was not.
                 *
                 * This dereferenced the result of GetComponent immediately. Anything wearing
                 * the "Tower" tag without a TowerActivation component -- a child collider on a
                 * tower whose script sits on the parent is the ordinary way this happens --
                 * throws a NullReferenceException from Update, once per frame, for as long as
                 * the player keeps looking at it. In a WebGL build that is a console full of
                 * exceptions and a visible frame-rate collapse, from simply aiming at the
                 * wrong part of a tower.
                 */
                if(tower == null)
                {
                    scanProgress = 0f;
                    SetText(statusMsg, "Scanning...");
                    SetText(ETA, "");
                }
                else if(tower.isActivated)
                {
                    SetText(statusMsg, "Tower is Activated");
                    SetText(ETA, "");
                    scanProgress = 0f;
                }
                else if(playerActions.Player.Scan.IsPressed())
                {
                    scanProgress += Time.deltaTime;
                    SetText(statusMsg, "Activating...");
                    SetText(ETA, (duration - scanProgress).ToString("F1")+"s");
                    if(scanProgress>=duration)
                    {
                        tower.Activate();
                    }
                }
                else
                {
                    scanProgress = 0f;
                    SetText(statusMsg, "Tower in Range");
                    SetText(ETA, "");
                }
            }
        }
        else
        {
            scanProgress = 0f;
            SetText(statusMsg, "Scanning...");
            SetText(ETA, "");
        }


    }
    private void OnDestroy()
    {
        playerActions.Disable();
    }

}
