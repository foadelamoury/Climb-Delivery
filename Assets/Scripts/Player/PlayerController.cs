using System;
using UnityEngine;

namespace TarodevController
{
    /// <summary>
    /// Hey!
    /// Tarodev here. I built this controller as there was a severe lack of quality & free 2D controllers out there.
    /// I have a premium version on Patreon, which has every feature you'd expect from a polished controller. Link: https://www.patreon.com/tarodev
    /// You can play and compete for best times here: https://tarodev.itch.io/extended-ultimate-2d-controller
    /// If you hve any questions or would like to brag about your score, come to discord: https://discord.gg/tarodev
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class PlayerController : MonoBehaviour, IPlayerController
    {
        [SerializeField] private ScriptableStats _stats;
        public bool IsDashing => _isDashing;
        public bool IsClimbing => _isClimbing;
        public bool OnWall => _onWall;
        public bool OnRightWall => _onRightWall;
        public bool OnLeftWall => _onLeftWall;
        public bool Grounded => _grounded;
        public Vector2 FrameVelocity => _frameVelocity;

        private Rigidbody2D _rb;
        private CapsuleCollider2D _col;
        private FrameInput _frameInput;
        private Vector2 _frameVelocity;
        private bool _cachedQueryStartInColliders;
        
        // Celeste Variables
        private bool _isDashing;
        private bool _canDash = true;
        private float _dashTimeLeft;
        private Vector2 _dashDir;
        
        private bool _onWall;
        private bool _onLeftWall;
        private bool _onRightWall;
        private bool _isClimbing;
        private float _currentStamina;
        private SpriteRenderer _renderer;

        #region Interface

        public Vector2 FrameInput => _frameInput.Move;
        public event Action<bool, float> GroundedChanged;
        public event Action Jumped;

        #endregion

        private float _time;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<CapsuleCollider2D>();
            _renderer = GetComponentInChildren<SpriteRenderer>();

            _cachedQueryStartInColliders = Physics2D.queriesStartInColliders;
            _currentStamina = _stats.MaxStamina;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            GatherInput();
        }

        private void GatherInput()
        {
            _frameInput = new FrameInput
            {
                JumpDown = Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.C),
                JumpHeld = Input.GetButton("Jump") || Input.GetKey(KeyCode.C),
                DashDown = Input.GetButtonDown("Fire1") || Input.GetKeyDown(KeyCode.X),
                ClimbHeld = Input.GetButton("Fire3") || Input.GetKey(KeyCode.Z),
                Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
            };

            if (_stats.SnapInput)
            {
                _frameInput.Move.x = Mathf.Abs(_frameInput.Move.x) < _stats.HorizontalDeadZoneThreshold ? 0 : Mathf.Sign(_frameInput.Move.x);
                _frameInput.Move.y = Mathf.Abs(_frameInput.Move.y) < _stats.VerticalDeadZoneThreshold ? 0 : Mathf.Sign(_frameInput.Move.y);
            }

            if (_frameInput.JumpDown)
            {
                _jumpToConsume = true;
                _timeJumpWasPressed = _time;
            }
        }

        private void FixedUpdate()
        {
            CheckCollisions();

            HandleDash();
            if (_isDashing)
            {
                ApplyMovement();
                return;
            }

            HandleClimb();
            if (_isClimbing)
            {
                ApplyMovement();
                return;
            }

            HandleJump();
            HandleDirection();
            HandleGravity();
            
            ApplyMovement();
        }

        #region Collisions
        
        private float _frameLeftGrounded = float.MinValue;
        private bool _grounded;

        private void CheckCollisions()
        {
            Physics2D.queriesStartInColliders = false;

            // Ground and Ceiling
            bool groundHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.down, _stats.GrounderDistance, ~_stats.PlayerLayer);
            bool ceilingHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.up, _stats.GrounderDistance, ~_stats.PlayerLayer);

            // Wall Detection
            _onLeftWall = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.left, _stats.GrounderDistance, ~_stats.PlayerLayer);
            _onRightWall = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.right, _stats.GrounderDistance, ~_stats.PlayerLayer);
            _onWall = _onLeftWall || _onRightWall;

            // Hit a Ceiling
            if (ceilingHit) _frameVelocity.y = Mathf.Min(0, _frameVelocity.y);

            // Landed on the Ground
            if (!_grounded && groundHit)
            {
                _grounded = true;
                _coyoteUsable = true;
                _bufferedJumpUsable = true;
                _endedJumpEarly = false;
                _canDash = true;
                _currentStamina = _stats.MaxStamina;
                if (_renderer != null) _renderer.color = Color.white;
                GroundedChanged?.Invoke(true, Mathf.Abs(_frameVelocity.y));
            }
            // Left the Ground
            else if (_grounded && !groundHit)
            {
                _grounded = false;
                _frameLeftGrounded = _time;
                GroundedChanged?.Invoke(false, 0);
            }

            Physics2D.queriesStartInColliders = _cachedQueryStartInColliders;
        }

        #endregion


        #region Jumping

        private bool _jumpToConsume;
        private bool _bufferedJumpUsable;
        private bool _endedJumpEarly;
        private bool _coyoteUsable;
        private float _timeJumpWasPressed;

        private bool HasBufferedJump => _bufferedJumpUsable && _time < _timeJumpWasPressed + _stats.JumpBuffer;
        private bool CanUseCoyote => _coyoteUsable && !_grounded && _time < _frameLeftGrounded + _stats.CoyoteTime;

        private void HandleJump()
        {
            if (!_endedJumpEarly && !_grounded && !_frameInput.JumpHeld && _rb.linearVelocity.y > 0) _endedJumpEarly = true;

            if (!_jumpToConsume && !HasBufferedJump) return;

            if (_grounded || CanUseCoyote) ExecuteJump();
            else if (_onWall && _currentStamina >= _stats.WallJumpStaminaCost)
            {
                _currentStamina -= _stats.WallJumpStaminaCost;
                ExecuteJump();
            }

            _jumpToConsume = false;
        }

        private void ExecuteJump()
        {
            _endedJumpEarly = false;
            _timeJumpWasPressed = 0;
            _bufferedJumpUsable = false;
            _coyoteUsable = false;
            _frameVelocity.y = _stats.JumpPower;

            if (_onWall && !_grounded)
            {
                _frameVelocity.x = _onLeftWall ? _stats.MaxSpeed : -_stats.MaxSpeed;
            }

            Jumped?.Invoke();
        }

        #endregion

        #region Horizontal

        private void HandleDirection()
        {
            if (_frameInput.Move.x == 0)
            {
                var deceleration = _grounded ? _stats.GroundDeceleration : _stats.AirDeceleration;
                _frameVelocity.x = Mathf.MoveTowards(_frameVelocity.x, 0, deceleration * Time.fixedDeltaTime);
            }
            else
            {
                _frameVelocity.x = Mathf.MoveTowards(_frameVelocity.x, _frameInput.Move.x * _stats.MaxSpeed, _stats.Acceleration * Time.fixedDeltaTime);
            }
        }

        #endregion

        #region Dash

        private void HandleDash()
        {
            if (_frameInput.DashDown && _canDash)
            {
                _isDashing = true;
                _canDash = false;
                _dashTimeLeft = _stats.DashDuration;
                
                // Dash in movement direction or forward if no movement
                _dashDir = _frameInput.Move.normalized;
                if (_dashDir == Vector2.zero) _dashDir = new Vector2(transform.localScale.x, 0).normalized;
                
                _frameVelocity = _dashDir * _stats.DashSpeed;
            }

            if (_isDashing)
            {
                _dashTimeLeft -= Time.fixedDeltaTime;
                if (_dashTimeLeft <= 0)
                {
                    _isDashing = false;
                    _frameVelocity = Vector2.zero; // Stop after dash
                }
            }
        }

        #endregion

        #region Climb

        private void HandleClimb()
        {
            _isClimbing = _onWall && _frameInput.ClimbHeld && _currentStamina > 0;

            if (_isClimbing)
            {
                // Vertical climb
                float climbInput = _frameInput.Move.y;
                float speedModifier = climbInput > 0 ? 0.7f : 1.2f; // Slower up, faster down
                _frameVelocity.y = climbInput * _stats.ClimbSpeed * speedModifier;
                _frameVelocity.x = 0;

                // Stamina consumption
                float consumptionRate = climbInput > 0 ? 5 : 2;
                _currentStamina -= consumptionRate * Time.fixedDeltaTime;

                if (_currentStamina <= 0)
                {
                    _isClimbing = false;
                }

                // Visual feedback
                if (_renderer != null)
                {
                    if (_currentStamina < _stats.MaxStamina * 0.25f)
                        _renderer.color = Color.Lerp(Color.white, Color.red, Mathf.PingPong(Time.time * 10, 1));
                    else
                        _renderer.color = Color.white;
                }
            }
            else
            {
                if (_renderer != null && !_grounded) _renderer.color = Color.white;
            }
        }

        #endregion

        #region Gravity

        private void HandleGravity()
        {
            if (_grounded && _frameVelocity.y <= 0f)
            {
                _frameVelocity.y = _stats.GroundingForce;
            }
            else
            {
                var inAirGravity = _stats.FallAcceleration;
                if (_endedJumpEarly && _frameVelocity.y > 0) inAirGravity *= _stats.JumpEndEarlyGravityModifier;
                _frameVelocity.y = Mathf.MoveTowards(_frameVelocity.y, -_stats.MaxFallSpeed, inAirGravity * Time.fixedDeltaTime);
            }
        }

        #endregion

        private void ApplyMovement() => _rb.linearVelocity = _frameVelocity;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_stats == null) Debug.LogWarning("Please assign a ScriptableStats asset to the Player Controller's Stats slot", this);
        }
#endif
    }

    public struct FrameInput
    {
        public bool JumpDown;
        public bool JumpHeld;
        public bool DashDown;
        public bool ClimbHeld;
        public Vector2 Move;
    }

    public interface IPlayerController
    {
        public event Action<bool, float> GroundedChanged;

        public event Action Jumped;
        public Vector2 FrameInput { get; }
    }
}