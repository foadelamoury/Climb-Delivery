using UnityEngine;
using DG.Tweening;

public class GhostTrail : MonoBehaviour
{
    private TarodevController.PlayerController move;
    private AnimationScript anim;
    private SpriteRenderer sr;
    public Transform ghostsParent;
    public Color trailColor;
    public Color fadeColor;
    public float ghostInterval;
    public float fadeTime;

    private void Start()
    {
        anim = FindAnyObjectByType<AnimationScript>();
        move = FindAnyObjectByType<TarodevController.PlayerController>();
        sr = GetComponent<SpriteRenderer>();
    }

    public void ShowGhost()
    {
        Sequence s = DOTween.Sequence();

        for (int i = 0; i < ghostsParent.childCount; i++)
        {
            Transform currentGhost = ghostsParent.GetChild(i);
            SpriteRenderer ghostSR = currentGhost.GetComponent<SpriteRenderer>();

            s.AppendCallback(() => currentGhost.position = move.transform.position);
            s.AppendCallback(() => ghostSR.flipX = anim.sr.flipX);
            s.AppendCallback(() => ghostSR.sprite = anim.sr.sprite);
            s.Append(ghostSR.material.DOColor(trailColor, 0));
            s.AppendCallback(() => FadeSprite(currentGhost, ghostSR));
            s.AppendInterval(ghostInterval);
        }
    }

    public void FadeSprite(Transform current, SpriteRenderer sr)
    {
        sr.material.DOKill();
        sr.material.DOColor(fadeColor, fadeTime);
    }

}
