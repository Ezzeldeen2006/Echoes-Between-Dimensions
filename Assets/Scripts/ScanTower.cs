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

    private void Awake()
    {
        playerActions= new PlayerInputActions();
        playerActions.Enable();
        itemHolder = GetComponent<ItemHolder>();
    }
    // Update is called once per frame
    void Update()
    {
        if(!itemHolder.IsFirstPerson()|| !itemHolder.IsHoldingScanner())
        {
            scannerContainer.SetActive(false);
            ETA.text = "";
            scanProgress = 0f;
            return;

        }
        else
        {
            scannerContainer.SetActive(true);
        }

        RaycastHit hit;
        if(Physics.Raycast(cam.transform.position,cam.transform.forward,out hit, range))
        {
            if(hit.collider.CompareTag("Tower"))
            {
                TowerActivation tower = hit.collider.GetComponent<TowerActivation>();
                if(tower.isActivated)
                {
                    statusMsg.text = "Tower is Activated";
                    ETA.text = "";
                    scanProgress = 0f;
                }
                else if(playerActions.Player.Scan.IsPressed())
                {
                    scanProgress += Time.deltaTime;
                    statusMsg.text = "Activating...";
                    ETA.text = (duration - scanProgress).ToString("F1")+"s";
                    if(scanProgress>=duration)
                    {
                        tower.Activate();
                    }
                }
                else
                {
                    scanProgress = 0f;
                    statusMsg.text = "Tower in Range";
                    ETA.text = "";
                }
            }
        }
        else
        {
            scanProgress = 0f;
            statusMsg.text = "Scanning...";
            ETA.text = "";
        }


    }
    private void OnDestroy()
    {
        playerActions.Disable();
    }

}
