
using UnityEngine;
using UnityEngine.InputSystem;

public class ChengeScaling : MonoBehaviour
{
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.upArrowKey.wasPressedThisFrame)
        {
            scaleup();
        }
        if (Keyboard.current != null && Keyboard.current.downArrowKey.wasPressedThisFrame)
        {
            scaledown();
        }
    }
    public void scaleup()
    {
        transform.localScale += new Vector3(0.05f, 0.05f, 0.05f);
    }
    public void scaledown()
    {
        transform.localScale -= new Vector3(0.05f, 0.05f, 0.05f);
    }
}
