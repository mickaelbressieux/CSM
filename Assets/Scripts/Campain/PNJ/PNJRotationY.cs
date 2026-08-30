using UnityEngine;

/// <summary>
/// Fait tourner continuellement un objet autour de l'axe Y mondial.
/// </summary>
[DisallowMultipleComponent]
public class PNJRotationY : MonoBehaviour
{
    [Tooltip("Vitesse de rotation en degres par seconde. Une valeur negative inverse le sens.")]
    [SerializeField] float rotationSpeed = 45f;

    void Update()
    {
        transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
    }
}
