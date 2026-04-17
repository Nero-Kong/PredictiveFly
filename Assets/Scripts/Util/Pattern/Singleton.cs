using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{

    private static bool _ShuttingDown;
    private static object _Lock = new object();
    private static T _Instance;


    public static T Instance
    {
        get
        {

            lock (_Lock)
            {
                if (!Application.isEditor)
                {
                    if (_ShuttingDown)
                        throw new System.Exception("Attempting to access Singleton instance while the application is shutting down !");
                }
                    
                    
                if (_Instance == null)
                {
                    _Instance = FindFirstObjectByType<T>();
                     if (_Instance == null)
                    {
                        GameObject container = new GameObject();
                        _Instance = container.AddComponent<T>();
                        container.name = typeof(T).ToString() + " (Singleton)";
                    }
                }

                return _Instance;
            }
        }
    }

    private void Awake()
    {
        if (_Instance != null)
            DontDestroyOnLoad(_Instance);
    }   




    private void OnApplicationQuit()
    {
        _ShuttingDown = true;
    }

    private void OnDestroy()
    {
        _ShuttingDown = true;
    }

}
