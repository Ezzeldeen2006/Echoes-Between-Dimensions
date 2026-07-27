using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PlayerHealth : MonoBehaviour
{
    private int maxHp = 3;
    private int currentHp;
    public Slider healthSlider;
    private Animator animator;
    private CharacterController characterController;
    private PlayerController playerController;
    public Transform spawnPosition;

    // Everything that must stop responding when the player dies. Resolved in Awake rather than
    // wired in the inspector so nothing is silently forgotten if a component moves.
    private ItemHolder itemHolder;
    private CameraController cameraController;
    private GunSystem gunSystem;
    private ScanTower scanTower;
    private Interaction interaction;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();

        itemHolder = GetComponent<ItemHolder>();
        gunSystem = GetComponent<GunSystem>();
        scanTower = GetComponent<ScanTower>();
        // These two live on children: the camera rig, and the interaction trigger volume.
        cameraController = GetComponentInChildren<CameraController>(true);
        interaction = GetComponentInChildren<Interaction>(true);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHp = maxHp;
        UpdateHealthUI();
    }
    public void TakeDmg(int dmg)
    {
        currentHp -= dmg;
        UpdateHealthUI();
        if (currentHp <= 0)
            Die();
    }
    private void UpdateHealthUI()
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHp;
            healthSlider.value = currentHp;
        }
    }
    /// <summary>
    /// Puts the player fully into the dead state.
    ///
    /// <para><b>This is the fix for "Dying while in FPS mode is buggy"</b> -- the professor's
    /// one deduction on player animation. The original disabled the controller, played the
    /// death animation, and stopped there. Everything else kept running: the camera stayed in
    /// first person, the held gun or scanner stayed rendered in front of it, and the shooting
    /// and scanning scripts were still listening for input. Dying while aiming left a floating
    /// weapon in a viewpoint that could no longer move, and releasing aim revealed the corpse
    /// -- the player appearing alive or dead depending on whether a button was held.
    /// </para>
    ///
    /// <para>Dying is a state change for the whole player, not just for movement, so every
    /// system that answers to input has to be told. <b>The order matters:</b> leave first
    /// person FIRST, because that is what restores the camera's culling mask and makes the
    /// body visible again; then hide the held items; then stop the scripts that could re-show
    /// them.</para>
    /// </summary>
    private void Die()
    {
        animator.SetTrigger("Death");
        playerController.enabled = false;
        characterController.enabled = false;

        // 1. Back to third person, which also un-hides the player's own body.
        if (cameraController != null)
        {
            cameraController.ForceThirdPerson();
            cameraController.enabled = false;
        }

        // 2. Clear the weapon/scanner from the hand AND the first-person overlay.
        if (itemHolder != null)
        {
            itemHolder.HideHeldItems();
            itemHolder.enabled = false;
        }

        // 3. Silence everything else that reads input. Without this a dead player can still
        //    fire, still scan a tower, and still pick items up off the floor.
        if (gunSystem != null) gunSystem.enabled = false;
        if (scanTower != null) scanTower.enabled = false;
        if (interaction != null) interaction.enabled = false;

        //Ai assisted, how to restart the level
        Invoke(nameof(RestartLevel), 5);

    }
    private void RestartLevel()
    {
        //Ai assisted, how to restart the level
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void RespawnWhenTouchingWater()
    {
        characterController.enabled = false;
        transform.position = spawnPosition.position;
        transform.rotation = spawnPosition.rotation;
        characterController.enabled = true; ;
    }
}
