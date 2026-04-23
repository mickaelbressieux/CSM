using UnityEngine;

public class AIStoneLaunchOnEnter : MonoBehaviour
{
    private void Awake()
    {
        Debug.LogWarning("AIStoneLaunchOnEnter est fusionne dans AIStoneController. Retire ce composant de l'objet.", this);
        enabled = false;
    }
}
