using UnityEngine;

public class music : MonoBehaviour
{
    private static music instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        //ai assisted, i wanted to have 1 instance of music
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
