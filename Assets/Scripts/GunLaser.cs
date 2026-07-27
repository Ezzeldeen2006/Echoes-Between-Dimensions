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

    // The empty Update() was removed.
    //
    // Unity calls Update on every MonoBehaviour that declares one, across an interop boundary,
    // every frame. An empty one is not free -- it is the full dispatch cost for a method that
    // returns immediately. That matters more here than in most places because a laser is
    // spawned on every shot, so holding the trigger accumulates objects each paying for it.

    private void OnCollisionEnter(Collision collision)
    {
        if(collision.collider.CompareTag("Robot"))
        {
            var robot = collision.collider.GetComponent<RobotHp>();

            // Null-checked, which it was not.
            //
            // RobotLaser -- the mirror-image script for the enemy's shots -- already does this,
            // so the two disagreed about whether the check was needed. Anything tagged "Robot"
            // without a RobotHp component throws a NullReferenceException here; a child collider
            // on a robot whose health script sits on the parent is the ordinary way that
            // happens. The shot then dies to an exception instead of dealing damage.
            if (robot != null)
                robot.RobotDmg(dmg);
        }
        Destroy(gameObject);
    }
}
