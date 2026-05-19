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
    private void Awake()
    {
        playerActions = new PlayerInputActions();
        playerActions.Enable();
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
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

        if (playerActions.Player.Jump.WasPressedThisFrame() && controller.isGrounded)
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

        Vector2 input = playerActions.Player.Movement.ReadValue<Vector2>();

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;
        camForward.y = 0; camForward.Normalize();
        camRight.y = 0; camRight.Normalize();

        Vector3 movement = camForward * input.y + camRight * input.x;
        isWalking = movement.sqrMagnitude > 0.01f;

        if (isWalking)
        {
            Quaternion lookRot = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotationSpeed * Time.deltaTime);
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
            Vector2 input = playerActions.Player.Movement.ReadValue<Vector2>();

            Vector3 camForward = cam.transform.forward;
            camForward.y = 0; camForward.Normalize();
            Vector3 camRight = cam.transform.right;
            camRight.y = 0; camRight.Normalize();

            Vector3 airMove = camForward * input.y + camRight * input.x;
            airMove *= (0.5f + moveSpeed) * 3.0f;
            airMove.y = yVelocity;
            controller.Move(airMove * Time.deltaTime);
        }
    }

    private void OnApplicationFocus(bool focus)
    {
        Cursor.lockState = focus ? CursorLockMode.Locked : CursorLockMode.None;
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
