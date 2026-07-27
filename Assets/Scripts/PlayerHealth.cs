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

    private void Awake()
    {
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
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
    private void Die()
    {
        animator.SetTrigger("Death");
        playerController.enabled = false;
        characterController.enabled = false;
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
