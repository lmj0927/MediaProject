// Owned by MinJun Lee
using UnityEngine;

/// <summary>
/// MonoBehaviour singleton base.
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : Component
{
    [SerializeField] private bool isDontDestroyOnLoad = false; // persist across scenes
    private static T _instance; // singleton instance
    private static bool quitting = false; // app quit flag

    public static T Instance
    {
        get
        {
            // avoid creating instance during shutdown
            if (quitting)
            {
                return null;
            }

            if (_instance == null)
            {
                // find existing or create new GameObject
                _instance = FindFirstObjectByType<T>();
                if (_instance == null)
                {
                    GameObject obj = new GameObject();
                    obj.name = typeof(T).Name;
                    _instance = obj.AddComponent<T>();
                }
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            if (isDontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
        else
        {
            // destroy duplicate instance
            if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        Initialize();
    }
    protected virtual void OnApplicationQuit()
    {
        quitting = true;
    }

    protected virtual void Initialize()
    {
    }
}