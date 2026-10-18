using UnityEngine;
using UnityEngine.UI;

public class CoinAnimationToUiPos : MonoBehaviour
{
    void Update()
    {
        transform.position = Vector3.Lerp(transform.position, transform.parent.position, Time.deltaTime * 5f);
        if (Vector3.Distance(transform.position, transform.parent.position) < 0.1f)
        {
            gameObject.SetActive(false);
        }
        

    }
}
