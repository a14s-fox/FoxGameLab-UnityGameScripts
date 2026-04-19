using System;
using UnityEngine;

namespace FoxGameLab.Character.IK
{
    public class IKLookAt : MonoBehaviour
    {
        [Serializable]
        private class LookState
        {
            [SerializeField] private HumanBodyBones _bone;

            [Tooltip("The rotational offset applied to the bone.")]
            [SerializeField] private Vector3 _euler;
            [SerializeField] private float _weight;
            public HumanBodyBones Bone => _bone;

            /// <summary>
            /// The rotational offset.
            /// </summary>
            public Quaternion Rotation => Quaternion.Euler(_euler);

            /// <summary>
            /// The weight contribution of this section.
            /// </summary>
            public float Weight => _weight;

            /// <summary>
            /// Indicates whether the transform for this bone is valid.
            /// </summary>
            public bool IsValid => Transform != null;

            /// <summary>
            /// The runtime transform representing the bone.
            /// </summary>
            public Transform Transform { get; set; }

            /// <summary>
            /// Initializes a new instance of the LookSection class.
            /// </summary>
            /// <param name="bone">The human body bone.</param>
            /// <param name="weight">The look weight.</param>
            public LookState(HumanBodyBones bone, float weight)
            {
                _bone = bone;
                _euler = Vector3.zero;
                _weight = weight;
            }
        }

        private const float SMOOTH_TIME = 0.15f;

        public enum LookMode
        {
            None = 0,
            Transform = 1,
            Direction = 2,
        }

        [SerializeField] private Animator _animator;
        [SerializeField] private float _trackSpeed = 270f;
        [SerializeField] private float _maxAngle = 45f;
        [SerializeField] private float _minDistance = 0.3f; //Prevent looking at targets that are too close, which can cause unnatural head rotations. Can replace by Character Radius

        /// <summary>
        /// Dead zone below _maxAngle required to re-engage tracking after it has been lost.
        /// Prevents flickering when the target hovers near the angle boundary.
        /// </summary>
        [SerializeField] private float _hysteresis = 10f;

        [SerializeField]
        private LookState[] _sections =
        {
            new LookState(HumanBodyBones.Chest, 1f),
            new LookState(HumanBodyBones.Neck, 2f),
            new LookState(HumanBodyBones.Head, 3f),
        };

        [NonSerialized] private float _weightTarget;
        [NonSerialized] private float _currentWeight;
        [NonSerialized] private float _weightVelocity;

        [NonSerialized] private LookMode _currentMode = LookMode.None;
        [NonSerialized] private Quaternion _lookRotation;
        [NonSerialized] private Transform _lookTransform;

        [NonSerialized] private bool _isLookActive;
        [NonSerialized] private Vector3 _lastValidDirection;
        [NonSerialized] private Transform _headBone;


        /// <summary>
        /// Sets the look target using a specific transform's position.
        /// </summary>
        /// <param name="target">The target transform to look at.</param>
        public void SetLookTransform(Transform target)
        {
            _lookTransform = target;
            _currentMode = target != null ? LookMode.Transform : LookMode.None;
            _isLookActive = target != null;
        }

        /// <summary>
        /// Sets the look target using a specific transform's forward direction.
        /// </summary>
        /// <param name="target">The target transform whose forward direction is used.</param>
        public void SetLookDirection(Transform target)
        {
            _lookTransform = target;
            _currentMode = target != null ? LookMode.Direction : LookMode.None;
            _isLookActive = target != null;
        }

        /// <summary>
        /// Clears the current look target.
        /// </summary>
        public void ClearLookTarget()
        {
            _lookTransform = null;
            _currentMode = LookMode.None;
            _isLookActive = false;
        }
        private void Awake()
        {
            if(_animator == null) _animator = GetComponent<Animator>();

        }

        private void OnEnable()
        {
            InitBones();
            _lookRotation = transform.rotation;
            _lastValidDirection = transform.forward;

        }

        private void InitBones()
        {
            foreach (LookState section in _sections)
            {
                Transform bone = _animator.GetBoneTransform(section.Bone);
                section.Transform = bone;
            }

            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
        }

        private Vector3 EyesPosition => _headBone != null ? _headBone.position : transform.position;

        private void Update()
        {
            Vector3 targetDirection = GetTargetDirection();

            _currentWeight = Mathf.SmoothDamp(
                _currentWeight,
                _weightTarget,
                ref _weightVelocity,
                SMOOTH_TIME
            );

            if (targetDirection == Vector3.zero) return;

            Vector3 upVector = Mathf.Abs(Vector3.Dot(targetDirection.normalized, Vector3.up)) > 0.999f
                ? transform.forward
                : Vector3.up;

            _lookRotation = Quaternion.RotateTowards(
                _lookRotation,
                Quaternion.LookRotation(targetDirection, upVector),
                Time.deltaTime * _trackSpeed
            );
        }

        private Vector3 GetTargetDirection()
        {
            switch (_currentMode)
            {
                case LookMode.Transform:
                    return GetTransformDirection();

                case LookMode.Direction:
                    return GetForwardDirection();

                case LookMode.None:
                default:
                    _weightTarget = 0f;
                    return _lastValidDirection;
            }
        }

        private Vector3 GetTransformDirection()
        {
            if (_lookTransform == null)
            {
                _isLookActive = false;
                _weightTarget = 0f;
                return _lastValidDirection;
            }

            Vector3 targetPosition = _lookTransform.position;
            Vector3 toTarget = targetPosition - EyesPosition;

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 flatToTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            float angle = flatToTarget.sqrMagnitude > 0.001f
                ? Vector3.Angle(flatForward, flatToTarget.normalized)
                : 0f;

            float distance = Vector3.Distance(EyesPosition, targetPosition);

            if (_isLookActive && angle > _maxAngle)
                _isLookActive = false;
            else if (!_isLookActive && angle < _maxAngle - _hysteresis)
                _isLookActive = true;

            if (!_isLookActive || distance < _minDistance)
            {
                _weightTarget = 0f;
                return _lastValidDirection;
            }

            _weightTarget = 1f;
            _lastValidDirection = toTarget;
            return toTarget;
        }

        private Vector3 GetForwardDirection()
        {
            if (_lookTransform == null)
            {
                _isLookActive = false;
                _weightTarget = 0f;
                return _lastValidDirection;
            }

            Vector3 targetDirection = _lookTransform.forward;

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 flatTarget = Vector3.ProjectOnPlane(targetDirection, Vector3.up);
            float angle = flatTarget.sqrMagnitude > 0.001f
                ? Vector3.Angle(flatForward, flatTarget.normalized)
                : 0f;

            if (_isLookActive && angle > _maxAngle)
                _isLookActive = false;
            else if (!_isLookActive && angle < _maxAngle - _hysteresis)
                _isLookActive = true;

            if (!_isLookActive)
            {
                _weightTarget = 0f;
                return _lastValidDirection;
            }

            _weightTarget = 1f;
            _lastValidDirection = targetDirection;
            return targetDirection;
        }

        private void LateUpdate()
        {
            Vector3 targetDirection = _lookRotation * Vector3.forward;
            Vector3 targetLocalDirection = transform.InverseTransformDirection(targetDirection).normalized;

            float yaw = Mathf.Atan2(targetLocalDirection.x, targetLocalDirection.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(-targetLocalDirection.y) * Mathf.Rad2Deg;

            float totalWeight = 0f;

            foreach (LookState section in _sections)
            {
                if (!section.IsValid) continue;

                section.Transform.localRotation *= section.Rotation;
                totalWeight += section.Weight;
            }

            if (totalWeight <= float.Epsilon) return;

            foreach (LookState section in _sections)
            {
                if (!section.IsValid) continue;

                float weightRatio = section.Weight / totalWeight;
                float w = _currentWeight * weightRatio;

                section.Transform.Rotate(Vector3.up, yaw * w, Space.World);
                section.Transform.Rotate(transform.right, pitch * w, Space.World);
            }
        }



        private void OnDrawGizmosSelected()
        {


            if (!Application.isPlaying)
            {
                return;
            }
            Vector3 eyes = EyesPosition;
            // Current direction (red) — where _lookRotation is pointing
            Gizmos.color = Color.red;
            Gizmos.DrawLine(eyes, eyes + (_lookRotation * Vector3.forward * 2f));

            // Last valid target direction (blue) — read-only, no state mutation
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(eyes, eyes + (_lastValidDirection.normalized * 2f));

            // Per-section bone forward directions (green) — actual bone orientations after rotation
            foreach (LookState section in _sections)
            {
                if (!section.IsValid) continue;

                Vector3 bonePos = section.Transform.position;
                Gizmos.color = Color.green;
                Gizmos.DrawLine(bonePos, bonePos + section.Transform.forward * 2f);
            }
        }
    }
}