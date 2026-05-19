using UnityEngine;
using UnityEngine.Animations;

public class TowerActivation : MonoBehaviour
{
    private Light light;
    private Animator animator;
    public bool isActivated = false;
    public portal Portal;
   
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        light = GetComponentInChildren<Light>();
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void Activate()
    {
        if (isActivated)
        {
            return;
        }
        isActivated = true;
        animator.SetBool("Activated", true);
        light.color = Color.green;
        Portal.TowerWasActivated();
    }
}
