using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationScript : MonoBehaviour
{

    private Animator anim;
    private TarodevController.PlayerController move;
    [HideInInspector]
    public SpriteRenderer sr;

    void Start()
    {
        anim = GetComponent<Animator>();
        move = GetComponentInParent<TarodevController.PlayerController>();
        sr = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        anim.SetBool("onGround", move.Grounded);
        anim.SetBool("onWall", move.OnWall);
        anim.SetBool("onRightWall", move.OnRightWall);
        anim.SetBool("wallGrab", move.IsClimbing);
        anim.SetBool("wallSlide", move.OnWall && !move.Grounded && move.FrameVelocity.y < 0);
        anim.SetBool("canMove", true);
        anim.SetBool("isDashing", move.IsDashing);
    }

    public void SetHorizontalMovement(float x,float y, float yVel)
    {
        anim.SetFloat("HorizontalAxis", x);
        anim.SetFloat("VerticalAxis", y);
        anim.SetFloat("VerticalVelocity", yVel);
    }

    public void SetTrigger(string trigger)
    {
        anim.SetTrigger(trigger);
    }

    public void Flip(int side)
    {

        if (move.IsClimbing || move.OnWall)
        {
            if (side == -1 && sr.flipX)
                return;

            if (side == 1 && !sr.flipX)
            {
                return;
            }
        }

        bool state = (side == 1) ? false : true;
        sr.flipX = state;
    }
}
