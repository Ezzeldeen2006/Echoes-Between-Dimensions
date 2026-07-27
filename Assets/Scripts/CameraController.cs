using UnityEngine;
using Unity.Cinemachine;


public class CameraController : MonoBehaviour
{
    public CinemachineCamera firstPerson;
    public CinemachineCamera thirdPerson;
    public Transform cameraRig;
    public Camera mainCamera;
    private PlayerInputActions playerActions;
    private int playerBodyLayer;
    private int defaultCullingMask;
    private CinemachinePanTilt panTilt;
    private ItemHolder item;
    private void Awake()
    {
        playerActions = new PlayerInputActions();
        playerActions.Enable();
        panTilt = firstPerson.GetComponent<CinemachinePanTilt>();
        item = GetComponentInParent<ItemHolder>();
    }

    private void Start()
    {
        playerBodyLayer = LayerMask.NameToLayer("Playerbody");
        defaultCullingMask = mainCamera.cullingMask;
    }
    void Update()
    {
        if (playerActions.Player.SwitchCamera.WasPressedThisFrame())
        {
            FirstPersonMode();
        }
        if (playerActions.Player.SwitchCamera.WasReleasedThisFrame())
        {
            ThirdPersonMode();
        }

    }
    private void FirstPersonMode()
    {
        Vector3 currentRotation = mainCamera.transform.eulerAngles;
        //Ai assisted, when looking up in 3rd person mode and then switch to 1st person the camera look at the ground automatically
        float pitch = currentRotation.x;
        if (pitch > 180) pitch -= 360;
        if (panTilt != null)
        {
            panTilt.PanAxis.Value = currentRotation.y;
            panTilt.TiltAxis.Value = pitch;
        }
        transform.rotation = Quaternion.Euler(0f, currentRotation.y, 0f);
        firstPerson.Priority = 2;
        thirdPerson.Priority = 1;
        //ai Assited, i didnt want the camera to clip through the player
        mainCamera.cullingMask &= ~(1 << playerBodyLayer);
        if (item != null)
        {
            item.SetFirstPerson(true);
        }
    }
    private void ThirdPersonMode()
    {
      
        firstPerson.Priority = 1;
        thirdPerson.Priority = 2;
        mainCamera.cullingMask = defaultCullingMask;
        if (item != null)
        {
            item.SetFirstPerson(false);
        }

    }
    private void OnDestroy()
    {
        playerActions.Disable();
    }
}