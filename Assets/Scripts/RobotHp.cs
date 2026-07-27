using UnityEngine;

public class RobotHp : MonoBehaviour
{
    private int maxHp = 2;
    private int currentHp;
    public ParticleSystem spark;
    public ParticleSystem explosion;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHp = maxHp;

    }

    // Update is called once per frame
    void Update()
    {
      
    }
    public void RobotDmg(int dmg)
    {
        currentHp -= dmg;
      
        if (currentHp>0)
        {
           GameObject sparkeffect= Instantiate(spark, transform.position, Quaternion.identity).gameObject;
            Destroy(sparkeffect, 2f);
        }
        if(currentHp<=0)
        {
           GameObject explosioneffect= Instantiate(explosion, transform.position, Quaternion.identity).gameObject;
            Destroy(explosioneffect, 2f);
            Destroy(gameObject);
        }
    }
}
