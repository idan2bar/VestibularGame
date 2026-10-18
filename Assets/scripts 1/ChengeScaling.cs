
using UnityEngine;

public class ChengeScaling : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            scaleup();
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
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
