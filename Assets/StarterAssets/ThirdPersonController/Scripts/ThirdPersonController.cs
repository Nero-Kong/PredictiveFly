using UnityEngine;
using UnityEngine.InputSystem;

namespace StarterAssets
{
    /// <summary>
    /// Small compatibility controller for assets that depend on Unity's
    /// Starter Assets third-person controller. It is intentionally simple,
    /// but preserves the public surface area used by the imported superhero
    /// add-on so the project can compile and the demo can be wired up later.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Player")]
        public float MoveSpeed = 2f;
        public float SprintSpeed = 5.335f;
        [Range(0.0f, 0.3f)] public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 10f;

        [Header("Player Jumping and Gravity")]
        public float JumpHeight = 1.2f;
        public float Gravity = -15f;
        public float JumpTimeout = 0.5f;
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        public bool Grounded = true;
        public float GroundedOffset = -0.14f;
        public float GroundedRadius = 0.28f;
        public LayerMask GroundLayers = Physics.AllLayers;

        float _speed;
        float _animationBlend;
        float _targetRotation;
        float _rotationVelocity;
        float _verticalVelocity;
        float _terminalVelocity = 53f;

        float _jumpTimeoutDelta;
        float _fallTimeoutDelta;

        int _animIDSpeed;
        int _animIDGrounded;
        int _animIDJump;
        int _animIDFreeFall;
        int _animIDMotionSpeed;

        CharacterController _controller;
        StarterAssetsInputs _input;
        Animator _animator;
        Transform _mainCameraTransform;
        bool _hasAnimator;

        void Start()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
            _hasAnimator = TryGetComponent(out _animator);
            _mainCameraTransform = Camera.main != null ? Camera.main.transform : null;

            AssignAnimationIDs();

            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        void Update()
        {
            if (_controller == null || _input == null)
            {
                return;
            }

            GroundedCheck();
            JumpAndGravity();
            Move();
        }

        void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

        void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(
                transform.position.x,
                transform.position.y + GroundedOffset,
                transform.position.z);

            Grounded = Physics.CheckSphere(
                spherePosition,
                GroundedRadius,
                GroundLayers,
                QueryTriggerInteraction.Ignore);

            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        void Move()
        {
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            if (_input.move == Vector2.zero)
            {
                targetSpeed = 0f;
            }

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z).magnitude;
            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f)
            {
                _animationBlend = 0f;
            }

            Vector3 inputDirection = new Vector3(_input.move.x, 0f, _input.move.y).normalized;
            if (_input.move != Vector2.zero)
            {
                float cameraYaw = _mainCameraTransform != null ? _mainCameraTransform.eulerAngles.y : transform.eulerAngles.y;
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cameraYaw;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            }

            Vector3 targetDirection = Quaternion.Euler(0f, _targetRotation, 0f) * Vector3.forward;
            Vector3 horizontalMotion = targetDirection.normalized * (_speed * Time.deltaTime);
            Vector3 verticalMotion = Vector3.up * (_verticalVelocity * Time.deltaTime);
            _controller.Move(horizontalMotion + verticalMotion);

            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = -2f;
                }

                if (_input.jump && _jumpTimeoutDelta <= 0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                if (_jumpTimeoutDelta >= 0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else if (_hasAnimator)
                {
                    _animator.SetBool(_animIDFreeFall, true);
                }

                _input.jump = false;
            }

            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        void OnDrawGizmosSelected()
        {
            Color groundedColor = Grounded ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0f, 0f, 0.35f);
            Gizmos.color = groundedColor;
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y + GroundedOffset, transform.position.z),
                GroundedRadius);
        }
    }
}
