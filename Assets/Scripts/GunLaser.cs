using UnityEngine;

public class GunLaser : MonoBehaviour
{
    private float velocity = 120f;
    private int dmg = 1;
    private Rigidbody rb;
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb.linearVelocity = transform.forward * velocity;
        Destroy(gameObject, 3f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void OnCollisionEnter(Collision collision)
    {
        if(collision.collider.CompareTag("Robot"))
        {
            var robot = collision.collider.GetComponent<RobotHp>();
            robot.RobotDmg(dmg);
        }
        Destroy(gameObject);
    }
}
