using UnityEngine;
using UnityEngine.InputSystem;

public class ItemHolder : MonoBehaviour
{
    public GameObject thirdPersonGun;
    public GameObject thirdPersonScaner;
    public GameObject firstPersonGun;
    public GameObject firstPersonScanner;
    private bool hasGun = false;
    private bool hasScanner = false;
    private bool isFirstPerson = false;
    private PlayerInputActions playerInput;
    private Animator animator;
    private Interactable pendingGun;
    private Interactable pendingScanner;
    private bool cutScene = false;
    public enum  Item { Empty,Gun,Scanner }
    private Item currentItem = Item.Empty;

    public bool HasScanner()
    {
        return hasScanner;

    }
    public bool HasGun()
    {
        return hasGun;
    }
    public bool IsFirstPerson()
    {
        return isFirstPerson;

    }
    public bool IsHoldingScanner()
    {
        return currentItem == Item.Scanner;
    }
    public bool IsHoldingGun()
    {
        return currentItem == Item.Gun;
    }
    private void Awake()
    {
        playerInput= new PlayerInputActions();
        animator=GetComponent<Animator>();
       playerInput.Enable();
    }
    // Update is called once per frame
    void Update()
    {
        if (cutScene)
            return;
        if (playerInput.Player.EquipGun.WasPressedThisFrame() && hasGun)
        {
            currentItem = Item.Gun;
            RefreshInventory();
        }
        if (playerInput.Player.EquipScanner.WasPressedThisFrame() && hasScanner)
        {
            currentItem = Item.Scanner;
            RefreshInventory();
        }


    }
    public void AttachGunToHand()
    {
        hasGun = true;
        currentItem = Item.Gun;
        if (pendingGun != null)
        {
            pendingGun.ParticleEffect.SetActive(false);
            pendingGun.gameObject.SetActive(false);
        }
         pendingGun = null;
        EndPickup();
        RefreshInventory();
    }
    public void AttachScannerToHand()
    {
        hasScanner = true;
        currentItem = Item.Scanner;
        if (pendingScanner != null)
        {
            pendingScanner.ParticleEffect.SetActive(false);
            pendingScanner.gameObject.SetActive(false);
        }
         pendingScanner = null;
        EndPickup();
        RefreshInventory();

    }
    /*
     * Movement is locked for the duration of a pickup.
     *
     * Without it you can walk, run and jump straight through the pickup animation: the
     * character slides across the floor mid-crouch, and you can leave the item behind while
     * the animation still resolves and hands it to you from across the room. The professor did
     * not catch this one; it shows the moment you try it.
     *
     * Released by the animation event that already calls AttachGunToHand /
     * AttachScannerToHand at the end of the clip, so the lock lasts exactly as long as the
     * animation rather than a guessed duration.
     */
    private bool isPickingUp;

    /// <summary>Whether a pickup animation is playing. Read by PlayerController.</summary>
    public bool IsPickingUp()
    {
        return isPickingUp;
    }

    private void BeginPickup()
    {
        isPickingUp = true;

        /*
         * A safety net, not a timer.
         *
         * The lock is meant to be released by an animation event. If that event is ever
         * missing, renamed, or the clip is interrupted by a transition, the player would be
         * frozen for the rest of the session with no way out -- a far worse bug than the one
         * being fixed. Three seconds is comfortably longer than either pickup clip.
         */
        CancelInvoke(nameof(EndPickup));
        Invoke(nameof(EndPickup), 3f);
    }

    private void EndPickup()
    {
        isPickingUp = false;
        CancelInvoke(nameof(EndPickup));
    }

    public void PickupScanner(Interactable groundScanner)
    {
       pendingScanner= groundScanner;
        BeginPickup();
        animator.SetTrigger("ScannerPickup");
    }
    public void PickUpGun(Interactable groundGun)
    {
        pendingGun = groundGun;
        BeginPickup();
        animator.SetTrigger("GunPickup");

    }
    public void SetFirstPerson(bool fp)
    {
        isFirstPerson = fp;
        RefreshInventory();
    }
    public void HideHeldItems()
    {
        currentItem = Item.Empty;
        if (thirdPersonGun)
            thirdPersonGun.SetActive(false);
        if (thirdPersonScaner)
            thirdPersonScaner.SetActive(false);
        if (firstPersonGun)
            firstPersonGun.SetActive(false);
        if (firstPersonScanner)
            firstPersonScanner.SetActive(false);
    }
    public void RefreshInventory()
    {
        if(thirdPersonGun)
            thirdPersonGun.SetActive(false);
        if(thirdPersonScaner)
            thirdPersonScaner.SetActive(false);
        if(firstPersonGun)
            firstPersonGun.SetActive(false);
        if(firstPersonScanner)
            firstPersonScanner.SetActive(false);

        switch (currentItem)
        {
            case Item.Gun:
                if (isFirstPerson)
                    firstPersonGun.SetActive(true);
                else 
                    thirdPersonGun.SetActive(true);
                break;

            case Item.Scanner:
                if (isFirstPerson) 
                    firstPersonScanner.SetActive(true);
                else 
                    thirdPersonScaner.SetActive(true);
                break;

            case Item.Empty:
                break;
        }

    }
    public void SetCutScene(bool scene)
    {
        cutScene = scene;
    }
    private void OnDestroy()
    {
        playerInput.Disable();
    }
}
