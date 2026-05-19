using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public GameObject mainMenu;
    private bool isPaused = false;
    private PlayerInputActions playerActions;
    private void Awake()
    {
        playerActions = new PlayerInputActions();
        playerActions.Enable();
    }
    private void Start()
    {
        mainMenu.SetActive(true);

        Time.timeScale = 0f;

        isPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    private void Update()
    {
        if(playerActions.Player.MainMenu.WasPressedThisFrame())
        {
            if (isPaused)
                StartGame();
            else
            {
                PauseGame();
            }
        }
    }
    public void StartGame()
    {
        mainMenu.SetActive(false);
        //ai assited, how to pause/continue the game
        Time.timeScale = 1f;

        isPaused = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    public void PauseGame()
    {
        mainMenu.SetActive(true);
        
        Time.timeScale = 0f;

        isPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    //ai assisted how to quit the game
    public void QuitGame()
    {
        Application.Quit();
    }
    private void OnDestroy()
    {
        playerActions.Disable();
    }
}