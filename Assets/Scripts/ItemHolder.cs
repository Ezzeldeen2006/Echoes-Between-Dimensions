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

        // No swapping items mid-pickup. The animation is of this character reaching for one
        // specific thing; letting 1 and 2 change what ends up in the hand halfway through means
        // the animation and the inventory are describing different events.
        if (isPickingUp)
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
    /*
     * Called by an animation event a quarter of a second into the pickup clip -- the moment the
     * hand reaches the item.
     *
     * These deliberately do NOT end the movement lock any more. They used to, which is what
     * made the lock useless: the event fires near the START of a multi-second animation, so the
     * player got control back almost immediately and could run off mid-crouch. Attaching the
     * item and regaining control are two different moments and are now timed separately.
     */
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
        RefreshInventory();

    }
    /*
     * Movement is locked for the duration of a pickup.
     *
     * Without it you can walk, run and jump straight through the pickup animation: the
     * character slides across the floor mid-crouch, and you can leave the item behind while
     * the animation still resolves and hands it to you from across the room.
     *
     * ---- Why this was still broken after being "fixed" ----
     *
     * The first version released the lock from AttachGunToHand / AttachScannerToHand, on the
     * stated reasoning that those run "at the end of the clip", so the lock would last exactly
     * as long as the animation. They do not. Reading the import settings on the clips:
     *
     *     HumanoidPickup.fbx        GunPickup      frames 0..287, event at t = 0.266s
     *     HumanoidPickupMiddle.fbx  ScannerPickup  frames 0..141, event at t = 0.299s
     *
     * The event is where the item is meant to appear IN THE HAND -- a quarter of a second in,
     * near the start of the reach. So the lock was being released almost immediately and the
     * player was free to run for the remaining several seconds of the animation, which is
     * exactly the bug it was written to prevent. It looked fixed in code and was not fixed in
     * the game, because the comment asserted a timing nobody had checked against the asset.
     *
     * The two concerns are now separate: the animation event attaches the item, and the lock
     * runs on the clip's own measured length. Nothing infers one from the other.
     */
    private bool isPickingUp;

    /// <summary>Fallback lock duration if a clip cannot be measured. Longer than either clip.</summary>
    private const float PickupLockFallbackSeconds = 3f;

    /// <summary>Whether a pickup animation is playing. Read by PlayerController.</summary>
    public bool IsPickingUp()
    {
        return isPickingUp;
    }

    /// <summary>
    /// Locks movement for as long as <paramref name="clipName"/> actually runs.
    ///
    /// <para>The duration is measured from the clip on the animator rather than written down
    /// here. A hardcoded number would be a second copy of a fact that already exists in the
    /// asset, and it would be wrong the first time either animation is retimed -- silently,
    /// and in whichever direction is worse: too short and the bug is back, too long and the
    /// player stands frozen after the animation has visibly finished.</para>
    /// </summary>
    private void BeginPickup(string clipName)
    {
        isPickingUp = true;

        var seconds = ClipLength(clipName);

        // Always an upper bound as well as a duration. If the clip is interrupted by a
        // transition, or the animator is retargeted, or the name stops matching, the player
        // must still get control back -- being frozen for the rest of the session is a far
        // worse bug than the one this exists to fix.
        CancelInvoke(nameof(EndPickup));
        Invoke(nameof(EndPickup), seconds);
    }

    private float ClipLength(string clipName)
    {
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null)
        {
            return PickupLockFallbackSeconds;
        }

        foreach (var clip in controller.animationClips)
        {
            if (clip != null && clip.name == clipName)
            {
                // Divided by the animator's speed so the lock still matches the animation if
                // playback is ever slowed down or sped up.
                var speed = animator.speed > 0.01f ? animator.speed : 1f;
                return clip.length / speed;
            }
        }

        return PickupLockFallbackSeconds;
    }

    private void EndPickup()
    {
        isPickingUp = false;
        CancelInvoke(nameof(EndPickup));
    }

    // One pickup at a time. Two items close together let the player press E on the second one
    // mid-animation, which restarts the clip, re-arms the lock timer, and leaves the first
    // item's pending reference dangling -- so it never disappears from the ground even though
    // the player is holding it.
    public void PickupScanner(Interactable groundScanner)
    {
        if (isPickingUp) return;

        pendingScanner= groundScanner;
        BeginPickup("ScannerPickup");
        animator.SetTrigger("ScannerPickup");
    }
    public void PickUpGun(Interactable groundGun)
    {
        if (isPickingUp) return;

        pendingGun = groundGun;
        BeginPickup("GunPickup");
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
