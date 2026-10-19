using UnityEngine;

public class playmusic : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
  public void Playsound()
    {
        if (audioSource != null)
        {
            audioSource.Play();
        }
    }
    public void Stopsound()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }
}
