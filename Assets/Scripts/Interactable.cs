using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public bool isPickedup = false;
    public GameObject ParticleEffect;
    public GameObject PickUpButton;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ParticleEffect.SetActive(true);
        PickUpButton.SetActive(false);


    }
    public void Interact(GameObject player)
    {
        if (isPickedup)
        {
            return;
        }
        isPickedup = true;
        HideButton();
        OnInteract(player);
    }
    public void ShowButton()
    {
        PickUpButton.SetActive (true);
    }
    public void HideButton()
    {
        PickUpButton.SetActive(false);
    }

    protected abstract void OnInteract(GameObject player);

    // Update is called once per frame
    void Update()
    {

    }
}
