using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

public class portal : MonoBehaviour
{
    private ParticleSystem portalParticle;
    private SphereCollider portalCollider;
    private int totalTower = 3;
    private int activatedTowers = 0;
    public ItemHolder itemHolder;
    public CinemachineCamera portalCutscene;
    public CinemachineCamera thirdPersonCamera;
    public Animator animator;
    private void Awake()
    {
        portalParticle = GetComponentInChildren<ParticleSystem>();
        portalCollider = GetComponent<SphereCollider>();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        portalParticle.Stop();
        portalCollider.enabled = false;
        portalCutscene.Priority = 0;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void TowerWasActivated()
    {
        activatedTowers++;
        if (activatedTowers == totalTower)
        {
            //ai assisted
            StartCoroutine(PortalSequence());
          
        }
    }
    //ai assisted
    private IEnumerator PortalSequence()
    {
        itemHolder.SetCutScene(true);
        itemHolder.HideHeldItems();
        portalCutscene.Priority = 3;
        animator.SetTrigger("PortalCutScene");
        yield return new WaitForSeconds(2f);
        UnlockPortal();
        yield return new WaitForSeconds(2f);
        portalCutscene.Priority = 0;
        itemHolder.SetCutScene(false);

    }
    public void UnlockPortal()
    {
        portalParticle.Play();
        portalCollider.enabled = true;
    }
  
}
