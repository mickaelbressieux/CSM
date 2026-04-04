using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to any GameObject. Populate the 'cameras' array in the Inspector
/// with all the cameras you want to cycle through. Press Tab (default) to switch.
/// </summary>
public class CameraSwitcher : MonoBehaviour
{
    [Header("Cameras")]
    public Camera[] cameras;

    [Header("Input")]
    public Key switchKey = Key.Tab;

    private int currentIndex = 0;

    private void Start()
    {
        if (cameras == null || cameras.Length == 0) return;

        for (int i = 0; i < cameras.Length; i++)
            cameras[i].gameObject.SetActive(i == 0);
    }

    private void Update()
    {
        if (cameras == null || cameras.Length < 2) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current[switchKey].wasPressedThisFrame)
            SwitchToNext();
    }

    private void SwitchToNext()
    {
        cameras[currentIndex].gameObject.SetActive(false);
        currentIndex = (currentIndex + 1) % cameras.Length;
        cameras[currentIndex].gameObject.SetActive(true);
    }

    /// <summary>Switch to a specific camera index from other scripts.</summary>
    public void SwitchTo(int index)
    {
        if (index < 0 || index >= cameras.Length) return;
        cameras[currentIndex].gameObject.SetActive(false);
        currentIndex = index;
        cameras[currentIndex].gameObject.SetActive(true);
    }
}
