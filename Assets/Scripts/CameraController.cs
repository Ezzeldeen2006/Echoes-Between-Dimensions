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
        /*
         * No aiming during a pickup.
         *
         * Zooming to first person mid-crouch put the camera inside a character who was still
         * reaching for the item, and combined with the gun being usable from the attach event
         * onwards it let the player aim and shoot before the animation had finished. Movement
         * was already locked for the pickup; the camera was not, which meant "locked" only ever
         * meant "cannot walk".
         *
         * The release branch is deliberately NOT gated. If the button is let go during a pickup
         * the camera must still return to third person -- otherwise a player who happens to be
         * holding right-click when they press E is stuck in first person until they press and
         * release it again.
         */
        if (playerActions.Player.SwitchCamera.WasPressedThisFrame()
            && (item == null || !item.IsPickingUp()))
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

    /// <summary>
    /// Leaves first person from outside this script. Used when the player dies.
    ///
    /// <para>The professor recorded this as "Dying while in FPS mode is buggy" and docked a
    /// mark. Death disabled the player controller but left the CAMERA in first person with the
    /// held item still on screen, so dying mid-aim showed a floating gun in front of a
    /// viewpoint that could no longer move -- and releasing aim then revealed the body lying
    /// dead. Two contradictory states at once.</para>
    ///
    /// <para>A separate public method rather than just making ThirdPersonMode public, because
    /// the culling mask matters as much as the camera: first person hides the player's own body
    /// so the camera does not sit inside it, and a corpse you cannot see is not much of a
    /// death.</para>
    /// </summary>
    public void ForceThirdPerson()
    {
        ThirdPersonMode();
    }

    private void OnDestroy()
    {
        playerActions.Disable();
    }
}