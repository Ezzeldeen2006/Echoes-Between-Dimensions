using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    private PlayerInputActions playerActions;
    private CharacterController controller;
    private Animator animator;
    public Camera cam;
    public float moveSpeed = 0f;
    private bool isWalking = false;
    public float rotationSpeed = 5f;
    private float yVelocity = 0f;
    private bool isJumping;
    public float jumpForce = 5f;
    private PlayerHealth health;
    private ItemHolder itemHolder;

    // Cached in Awake. `cam.transform` and `transform` are native property calls, and between
    // HandleMovement and OnAnimatorMove they were read five times every frame.
    private Transform camTransform;
    private Transform tf;

    private void Awake()
    {
        playerActions = new PlayerInputActions();
        playerActions.Enable();
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        itemHolder = GetComponent<ItemHolder>();
        tf = transform;

        if (cam != null)
            camTransform = cam.transform;
    }

    void Update()
    {
        HandleMovement();

    }

    private void HandleMovement()
    {
        if (controller.isGrounded)
        {
            yVelocity = -0.1f;
            animator.SetBool("IsGrounded", true);
            isJumping = false;
            animator.SetBool("IsJumping", false);
            animator.SetBool("IsFalling", false);
        }
        else
        {
            yVelocity += Physics.gravity.y * Time.deltaTime;

            if ((isJumping && yVelocity < 0) || yVelocity < -3)
                animator.SetBool("IsFalling", true);

            animator.SetBool("IsGrounded", false);
        }

        bool pickingUp = itemHolder != null && itemHolder.IsPickingUp();

        if (playerActions.Player.Jump.WasPressedThisFrame() && controller.isGrounded && !pickingUp)
        {
            yVelocity = jumpForce;
            isJumping = true;
            animator.SetBool("IsJumping", true);
            animator.SetBool("IsFalling", false);
        }

        if (isJumping && yVelocity <= 0)
        {
            isJumping = false;
            animator.SetBool("IsJumping", false);
        }

        /*
         * Input is zeroed while a pickup animation plays, rather than the script being
         * disabled.
         *
         * Disabling PlayerController would also stop OnAnimatorMove, and the pickup animations
         * are ROOT MOTION -- the crouch moves the character. Killing that would make the
         * player stand rooted while the animation slides underneath them. Zeroing the input
         * leaves the animation fully in charge of movement, which is what root motion means.
         */
        Vector2 input = (itemHolder != null && itemHolder.IsPickingUp())
            ? Vector2.zero
            : playerActions.Player.Movement.ReadValue<Vector2>();

        Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;
        camForward.y = 0; camForward.Normalize();
        camRight.y = 0; camRight.Normalize();

        Vector3 movement = camForward * input.y + camRight * input.x;
        isWalking = movement.sqrMagnitude > 0.01f;

        if (isWalking)
        {
            Quaternion lookRot = Quaternion.LookRotation(movement);
            tf.rotation = Quaternion.Slerp(tf.rotation, lookRot, rotationSpeed * Time.deltaTime);
        }

        if (isWalking)
            moveSpeed += playerActions.Player.Sprint.IsPressed() ? Time.deltaTime : -Time.deltaTime;
        else
            moveSpeed -= Time.deltaTime;

        moveSpeed = Mathf.Clamp01(moveSpeed);

        animator.SetFloat("moveSpeed", moveSpeed);
        animator.SetBool("IsWalking", isWalking);
    }

    private void OnAnimatorMove()
    {
        if (controller.isGrounded)
        {
            Vector3 deltaMove = animator.deltaPosition;
            deltaMove.y = yVelocity * Time.deltaTime;
            controller.Move(deltaMove);
        }
        else
        {
            Vector2 input = (itemHolder != null && itemHolder.IsPickingUp())
                ? Vector2.zero
                : playerActions.Player.Movement.ReadValue<Vector2>();

            Vector3 camForward = camTransform.forward;
            camForward.y = 0; camForward.Normalize();
            Vector3 camRight = camTransform.right;
            camRight.y = 0; camRight.Normalize();

            Vector3 airMove = camForward * input.y + camRight * input.x;
            airMove *= (0.5f + moveSpeed) * 3.0f;
            airMove.y = yVelocity;
            controller.Move(airMove * Time.deltaTime);
        }
    }

    /*
     * Releases the cursor on focus loss; no longer grabs it on focus gain.
     *
     * The same single line was what made AtomBall's WebGL build render a blank screen. On
     * load Unity raises OnApplicationFocus(true); asking for pointer lock there is a request
     * with no user gesture behind it, which every browser rejects with "NotAllowedError: A
     * user gesture is required to request Pointer Lock." Unity's WebGL runtime treats that
     * rejection as fatal, aborts, and calls loseContext() -- and a lost WebGL context never
     * draws again. The game loads, reports nothing wrong, and shows black.
     *
     * MainMenu.cs locks the cursor from a button handler, which IS a user gesture, so that
     * path was always fine and is left alone. This one fires without any input at all.
     *
     * Fixed here as well as in AtomBall, before this project is built for the web, rather
     * than after rediscovering the same blank screen from the other end.
     */
    private void OnApplicationFocus(bool focus)
    {
        if (!focus)
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if(hit.collider.CompareTag("Water"))
        {
            health.RespawnWhenTouchingWater();
        }
        if(hit.collider.CompareTag("Portal"))
        {
            Win();
        }
    }
    private void Win()
    {
        animator.SetTrigger("Win");
        StartCoroutine(WinSequence());
    }

    private IEnumerator WinSequence()
    {
        yield return new WaitForSeconds(2f);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    private void OnDestroy()
    {
        playerActions.Disable();
    }
}
