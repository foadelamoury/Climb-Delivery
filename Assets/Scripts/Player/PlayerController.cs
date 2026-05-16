using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TarodevController
{
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
        public bool JumpHeld => _frameInput.JumpHeld;

        private Rigidbody2D _rb;
        private CapsuleCollider2D _col;
        private FrameInput _frameInput;
        private Vector2 _frameVelocity;
        private bool _cachedQueryStartInColliders;
        
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

        // Input actions created entirely in code — no files needed
        private InputActionMap _actionMap;
        private InputAction _moveAction;
        private InputAction _jumpAction;
        private InputAction _dashAction;
        private InputAction _climbAction;
        
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
            
            CreateInputActions();
        }

        /// <summary>
        /// Builds all input actions in pure code. No .inputactions file needed.
        /// </summary>
        private void CreateInputActions()
        {
            _actionMap = new InputActionMap("Player");

            // Move — Vector2 composite from WASD, Arrows, and Gamepad stick
            _moveAction = _actionMap.AddAction("Move", InputActionType.Value);
            
            // WASD composite
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            // Arrow keys composite
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            // Gamepad left stick
            _moveAction.AddBinding("<Gamepad>/leftStick");

            // Jump — Space, C, Gamepad South
            _jumpAction = _actionMap.AddAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Keyboard>/c");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");

            // Dash — X, Left Mouse, Gamepad East (SEPARATE from Climb)
            _dashAction = _actionMap.AddAction("Dash", InputActionType.Button);
            _dashAction.AddBinding("<Keyboard>/x");
            _dashAction.AddBinding("<Mouse>/leftButton");
            _dashAction.AddBinding("<Gamepad>/buttonEast");

            // Climb — Left Shift, Z, Gamepad West (SEPARATE from Dash)
            _climbAction = _actionMap.AddAction("Climb", InputActionType.Button);
            _climbAction.AddBinding("<Keyboard>/leftShift");
            _climbAction.AddBinding("<Keyboard>/z");
            _climbAction.AddBinding("<Gamepad>/buttonWest");
        }

        private void OnEnable()
        {
            _actionMap.Enable();
            _jumpAction.performed += OnJumpPerformed;
            
            Debug.Log("[PlayerController] Input actions ENABLED. Press WASD/Arrows to move, Space to jump, X to dash, Shift to climb.");
        }

        private void OnDisable()
        {
            _jumpAction.performed -= OnJumpPerformed;
            _actionMap.Disable();
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
            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            
            // DEBUG: Log every second to check state
            if (Time.frameCount % 60 == 0)
            {
                bool oldInputWorks = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space);
                Debug.Log($"[INPUT DEBUG] Frame={Time.frameCount} " +
                    $"ActionMap.enabled={_actionMap.enabled} " +
                    $"MoveAction.enabled={_moveAction.enabled} " +
                    $"MoveValue={moveInput} " +
                    $"OldInputWorks={oldInputWorks} " +
                    $"Keyboard.current={UnityEngine.InputSystem.Keyboard.current} " +
                    $"AnyKey={UnityEngine.InputSystem.Keyboard.current?.anyKey.isPressed}");
            }
            
            _frameInput = new FrameInput
            {
                JumpDown = _jumpPressedThisFrame,
                JumpHeld = _jumpAction.IsPressed(),
                DashDown = _dashAction.WasPressedThisFrame(),
                ClimbHeld = _climbAction.IsPressed(),
                Move = moveInput
            };
            
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

            bool groundHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.down, _stats.GrounderDistance, ~_stats.PlayerLayer);
            bool ceilingHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.up, _stats.GrounderDistance, ~_stats.PlayerLayer);

            _onLeftWall = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.left, _stats.GrounderDistance, ~_stats.PlayerLayer);
            _onRightWall = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.right, _stats.GrounderDistance, ~_stats.PlayerLayer);
            _onWall = _onLeftWall || _onRightWall;

            if (ceilingHit) _frameVelocity.y = Mathf.Min(0, _frameVelocity.y);

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
                    _frameVelocity = Vector2.zero;
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
                float wallDir = _onRightWall ? 1f : -1f;
                Vector2 topOfPlayer = (Vector2)_col.bounds.center + new Vector2(0, _col.bounds.extents.y);
                
                bool wallAbove = Physics2D.Raycast(topOfPlayer, new Vector2(wallDir, 0), _col.bounds.extents.x + _stats.GrounderDistance + 0.1f, ~_stats.PlayerLayer);
                
                Vector2 aboveWallCheck = topOfPlayer + new Vector2(wallDir * (_col.bounds.extents.x + 0.2f), 0.3f);
                bool openAboveWall = !Physics2D.OverlapPoint(aboveWallCheck, ~_stats.PlayerLayer);
                
                if (!wallAbove && openAboveWall && _frameInput.Move.y > 0)
                {
                    _isClimbing = false;
                    _frameVelocity.y = _stats.ClimbSpeed * 1.5f;
                    _frameVelocity.x = wallDir * _stats.MaxSpeed * 0.5f;
                    return;
                }
                
                float climbInput = _frameInput.Move.y;
                float speedModifier = climbInput > 0 ? 0.7f : 1.2f;
                _frameVelocity.y = climbInput * _stats.ClimbSpeed * speedModifier;
                _frameVelocity.x = 0;

                float consumptionRate = climbInput > 0 ? 5 : 2;
                _currentStamina -= consumptionRate * Time.fixedDeltaTime;

                if (_currentStamina <= 0)
                {
                    _isClimbing = false;
                }

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