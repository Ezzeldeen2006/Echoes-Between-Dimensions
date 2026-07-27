using UnityEngine;

public class Interaction : MonoBehaviour
{
    private PlayerInputActions playerActions;
    private Interactable currentTarget;
    private void Awake()
    {
        playerActions = new PlayerInputActions();
        playerActions.Enable();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (playerActions.Player.Interact.WasPressedThisFrame() && currentTarget != null)
        {
            FaceTarget();
            currentTarget.Interact(gameObject);
            currentTarget.HideButton();
            currentTarget = null;

        }
       
       
    }
    private void OnTriggerEnter(Collider other)
    {
        Interactable target = other.GetComponent<Interactable>();
        if (target != null && !target.isPickedup)
        {
            currentTarget= target;
            currentTarget.ShowButton();
        }
    }
    private void OnTriggerExit(Collider other)
    {
        Interactable target = other.GetComponent<Interactable>();
        if (target != null && target == currentTarget)
        {
            currentTarget.HideButton();
            currentTarget = null;
           
        }
    }
    private void FaceTarget()
    {
        Vector3 direction = currentTarget.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) 
            return;
        transform.rotation = Quaternion.LookRotation(direction);
    }
    private void OnDisable()
    {
        playerActions.Disable();
    }
}
