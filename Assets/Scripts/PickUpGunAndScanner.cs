using UnityEngine;

public class PickUpGunAndScanner : Interactable
{
    protected override void OnInteract(GameObject player)
    {
        ItemHolder holder = player.GetComponent<ItemHolder>();
        if (holder == null)
            return;
        if(CompareTag("Gun"))
        {
            holder.PickUpGun(this);
        }
        if(CompareTag("Scanner"))
        {
            holder.PickupScanner(this);
        }

    }
}
