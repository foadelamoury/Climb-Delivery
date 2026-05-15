using System;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] private InputActionAsset _inputActions;
        
        public bool IsDashing => _isDashing;
        public bool IsClimbing => _isClimbing;
        public bool OnWall => _onWall;
        public bool OnRightWall => _onRightWall;
        public bool OnLeftWall => _onLeftWall;
        public bool Grounded => _grounded;
        public Vector2 FrameVelocity => _frameVelocity;
        
        // Expose JumpHeld for BetterJumping
        public bool JumpHeld => _frameInput.JumpHeld;

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

        // New Input System actions
        private InputAction _moveAction;
        private InputAction _jumpAction;
        private InputAction _dashAction;
        private InputAction _climbAction;
        
        // Track jump pressed this frame
        private bool _jumpPressedThisFrame;

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
            
            // Setup Input Actions
            SetupInputActions();
        }

        private void SetupInputActions()
        {
            if (_inputActions == null)
            {
                Debug.LogError("PlayerController: InputActionAsset is not assigned! Please assign the PlayerInputActions asset in the Inspector.", this);
                return;
            }
            
            var playerMap = _inputActions.FindActionMap("Player", true);
            _moveAction = playerMap.FindAction("Move", true);
            _jumpAction = playerMap.FindAction("Jump", true);
            _dashAction = playerMap.FindAction("Dash", true);
            _climbAction = playerMap.FindAction("Climb", true);
        }

        private void OnEnable()
        {
            if (_inputActions != null)
            {
                _inputActions.FindActionMap("Player")?.Enable();
            }
            
            // Subscribe to jump performed event for reliable press detection
            if (_jumpAction != null)
            {
                _jumpAction.performed += OnJumpPerformed;
            }
        }

        private void OnDisable()
        {
            if (_jumpAction != null)
            {
                _jumpAction.performed -= OnJumpPerformed;
            }
            
            if (_inputActions != null)
            {
                _inputActions.FindActionMap("Player")?.Disable();
            }
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            _jumpPressedThisFrame = true;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            GatherInput();
        }

        private void GatherInput()
        {
            if (_moveAction == null) return;
            
            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            
            _frameInput = new FrameInput
            {
                JumpDown = _jumpPressedThisFrame,
                JumpHeld = _jumpAction.IsPressed(),
                DashDown = _dashAction.WasPressedThisFrame(),
                ClimbHeld = _climbAction.IsPressed(),
                Move = moveInput
            };
            
            // Consume the jump press flag
            _jumpPressedThisFrame = false;

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
                // --- Wall-Top Transition ---
                // Check if the player has reached the top of the wall
                // Cast from the top of the collider in the wall direction to see if the wall continues above
                float wallDir = _onRightWall ? 1f : -1f;
                Vector2 topOfPlayer = (Vector2)_col.bounds.center + new Vector2(0, _col.bounds.extents.y);
                
                // Check if there's still wall above the player's top
                bool wallAbove = Physics2D.Raycast(topOfPlayer, new Vector2(wallDir, 0), _col.bounds.extents.x + _stats.GrounderDistance + 0.1f, ~_stats.PlayerLayer);
                
                // Check if there's open space at the top of the wall (diagonal upward toward the wall)
                Vector2 aboveWallCheck = topOfPlayer + new Vector2(wallDir * (_col.bounds.extents.x + 0.2f), 0.3f);
                bool openAboveWall = !Physics2D.OverlapPoint(aboveWallCheck, ~_stats.PlayerLayer);
                
                if (!wallAbove && openAboveWall && _frameInput.Move.y > 0)
                {
                    // Player has climbed past the top of the wall — vault onto it
                    _isClimbing = false;
                    
                    // Give upward + horizontal boost to pop the player onto the wall top
                    _frameVelocity.y = _stats.ClimbSpeed * 1.5f;
                    _frameVelocity.x = wallDir * _stats.MaxSpeed * 0.5f;
                    return;
                }
                
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
            if (_inputActions == null) Debug.LogWarning("Please assign a PlayerInputActions asset to the Player Controller's Input Actions slot", this);
        }
        
        private void OnDrawGizmosSelected()
        {
            // Visualize wall-top detection in editor
            if (_col == null) return;
            
            Gizmos.color = Color.cyan;
            Vector2 topOfPlayer = (Vector2)_col.bounds.center + new Vector2(0, _col.bounds.extents.y);
            
            // Right wall check
            Gizmos.DrawLine(topOfPlayer, topOfPlayer + new Vector2(_col.bounds.extents.x + 0.3f, 0));
            Gizmos.DrawWireSphere(topOfPlayer + new Vector2(_col.bounds.extents.x + 0.2f, 0.3f), 0.05f);
            
            // Left wall check
            Gizmos.DrawLine(topOfPlayer, topOfPlayer + new Vector2(-_col.bounds.extents.x - 0.3f, 0));
            Gizmos.DrawWireSphere(topOfPlayer + new Vector2(-_col.bounds.extents.x - 0.2f, 0.3f), 0.05f);
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