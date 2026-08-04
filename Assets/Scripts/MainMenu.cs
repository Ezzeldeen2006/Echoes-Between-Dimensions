using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public GameObject mainMenu;
    private bool isPaused = false;
    private PlayerInputActions playerActions;

    /*
     * Survives the scene reload, which is the whole point.
     *
     * Statics are normally the wrong tool, but LoadScene destroys every object in the scene --
     * including this one -- so an instance field cannot carry a message from before the reload
     * to after it. This is exactly the case a static is for. It is cleared the moment it is
     * read, so it can never leak into a later load.
     */
    private static bool restartRequested;

    private void Awake()
    {
        playerActions = new PlayerInputActions();
        playerActions.Enable();

        HideExitButtonOnWeb();
    }
    private void Start()
    {
        /*
         * Restart drops you back into the GAME, not onto the menu.
         *
         * This is the fix for "the restart button does nothing". It was doing something --
         * SceneManager.LoadScene really did reload the level -- but Start() then unconditionally
         * showed the menu and set timeScale to 0, so the reload landed on the same screen the
         * button was clicked from. A correct reload and no visible change is indistinguishable
         * from a dead button, and it is the button's own screen that hid the evidence.
         */
        if (restartRequested)
        {
            restartRequested = false;
            StartGame();
            return;
        }

        mainMenu.SetActive(true);

        Time.timeScale = 0f;

        isPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Removes the Exit button in a browser build.
    ///
    /// <para><b>This is the fix for "after pressing exit I cannot play again until I refresh".</b>
    /// <c>Application.Quit()</c> on WebGL does not close anything -- there is no application to
    /// close -- it shuts the Unity runtime down inside the page. The canvas stops rendering and
    /// stops accepting input, and the only way back is a reload. So the button did precisely
    /// what it was told, and what it was told is meaningless on the web.</para>
    ///
    /// <para>Hidden rather than rewired. "Exit" on a page embedded in a portfolio has nowhere to
    /// go: the menu it would return to is the screen the button is already on. A control with no
    /// sensible destination should not be offered.</para>
    ///
    /// <para>Found by asking each button what it calls, rather than by name or by a serialized
    /// reference. A name would break the day the button is renamed, and a reference would have to
    /// be dragged into the inspector every time the menu is rebuilt -- and the thing that
    /// actually makes this button the wrong one on the web is the method it invokes.</para>
    /// </summary>
    private void HideExitButtonOnWeb()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (mainMenu == null) return;

        foreach (var button in mainMenu.GetComponentsInChildren<Button>(true))
        {
            for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == nameof(QuitGame))
                {
                    button.gameObject.SetActive(false);
                    break;
                }
            }
        }
#endif
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
        // Set BEFORE the load, read by Start() on the other side. Without it the reload lands
        // on the main menu -- see the note in Start().
        restartRequested = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    //ai assisted how to quit the game
    public void QuitGame()
    {
        // Unreachable on the web -- HideExitButtonOnWeb removes the only button that calls this,
        // because Application.Quit() there kills the Unity runtime inside the page and the game
        // cannot be played again without a browser reload. Kept for desktop builds, where it is
        // the correct thing to do.
        Application.Quit();
    }
    private void OnDestroy()
    {
        playerActions.Disable();
    }
}