using UnityEngine;

public class BillboardUI : MonoBehaviour
{
    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;
    }

    void LateUpdate()
    {
        transform.forward = mainCam.transform.forward;
    }
}