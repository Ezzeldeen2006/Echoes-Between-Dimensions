using UnityEngine;
public class RobotLaser : MonoBehaviour
{
    private float velocity = 20f;
    private int dmg = 1;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
   
        rb.linearVelocity = transform.forward * velocity;
        Destroy(gameObject, 4f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Player"))
        {
            PlayerHealth health = collision.collider.GetComponent<PlayerHealth>();
            if (health != null)
                health.TakeDmg(dmg);
        }
        Destroy(gameObject);
    }
}