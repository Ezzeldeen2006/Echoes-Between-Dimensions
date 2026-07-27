using UnityEngine;

public class BillboardUI : MonoBehaviour
{
    private Transform camTransform;
    private Transform tf;

    private void Start()
    {
        tf = transform;

        // Camera.main is a tagged search, so it is resolved once here rather than per frame.
        Camera mainCam = Camera.main;
        if (mainCam != null)
            camTransform = mainCam.transform;
    }

    void LateUpdate()
    {
        // Guarded, and this one is not theoretical: Camera.main returns null whenever no
        // enabled camera carries the MainCamera tag. This script previously dereferenced it
        // unconditionally in LateUpdate, so a scene with an untagged camera -- or a camera
        // disabled at runtime, which is exactly what a first/third-person swap does -- threw
        // a NullReferenceException EVERY FRAME, per billboard. That is a console flooding at
        // 60 Hz and a visible frame-rate collapse.
        if (camTransform == null)
            return;

        tf.forward = camTransform.forward;
    }
}
