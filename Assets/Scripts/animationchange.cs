using UnityEngine;

public class animationchange : MonoBehaviour
{

    [SerializeField] private Animator animator;

    public void playSadAnim()
    {
        animator.Play("Sad");
    }
    public void PlayHappyAnim()
    {
        animator.Play("Happy");
    }

}
