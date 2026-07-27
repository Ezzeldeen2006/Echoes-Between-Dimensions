using UnityEngine;

public class RobotHp : MonoBehaviour
{
    private int maxHp = 2;
    private int currentHp;
    public ParticleSystem spark;
    public ParticleSystem explosion;

    /*
     * Guards against a robot dying twice.
     *
     * Two lasers can land on the same robot in the same frame -- easy to do by holding the
     * trigger at close range, which is exactly how the professor described playing it ("the
     * enemies are so aggressive"). Both calls saw currentHp drop to zero or below, so both
     * spawned an explosion and both called Destroy. The double Destroy is harmless, but two
     * explosions firing on the same frame at the same point look like a graphical glitch, and
     * the second call also skipped the spark branch for a robot that was already gone.
     */
    private bool isDead;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHp = maxHp;

    }

    // The empty Update() was removed. Unity dispatches the message to every robot every frame,
    // across an interop boundary, to run nothing.

    public void RobotDmg(int dmg)
    {
        if (isDead)
        {
            return;
        }

        currentHp -= dmg;

        if (currentHp>0)
        {
           GameObject sparkeffect= Instantiate(spark, transform.position, Quaternion.identity).gameObject;
            Destroy(sparkeffect, 2f);
        }
        if(currentHp<=0)
        {
            isDead = true;
           GameObject explosioneffect= Instantiate(explosion, transform.position, Quaternion.identity).gameObject;
            Destroy(explosioneffect, 2f);
            Destroy(gameObject);
        }
    }
}
