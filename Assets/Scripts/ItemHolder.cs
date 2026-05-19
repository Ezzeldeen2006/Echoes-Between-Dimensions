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
    public void PickupScanner(Interactable groundScanner)
    {
       pendingScanner= groundScanner;
        animator.SetTrigger("ScannerPickup");
    }
    public void PickUpGun(Interactable groundGun)
    {
        pendingGun = groundGun;
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
