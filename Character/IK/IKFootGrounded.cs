using APG.Core;
using APG.Core.Character;
using Sirenix.OdinValidator.Editor;
using UnityEngine;

namespace FoxGameLab.Character.IK
{
    public class IKFootGrounded : MonoBehaviour
    {
        ///Base concept
        ///Define a class for storing Foot Transform, raycast hit info,...
        ///Raycast to hit ground, apply foot into hit position and rotation
        ///
        private class LegIK
        {
            public AvatarIKGoal AvatarTarget;

            public Transform Foot;
            public Transform LowerLeg;
            public Transform Toes;

            public float FootOffset;

            public float MaxLegLength;

            public float GroundDistance;
            public Vector3 GroundPoint;
            public Vector3 GroundNormal;

            public float IKWeight;

            public float RaycastDistance;

            public Vector3 DebugPoint;
        }

        [SerializeField] private Animator _animator;
        [Header("Settings")]
        [SerializeField] private float _hipAdjustmentSpeed = 5f;
        [SerializeField] private float _footActiveAdjustmentSpeed = 10f;
        [SerializeField] private float _footInactiveAdjustmentSpeed = 2f;
        [SerializeField] private float _minHipOffset = 0.1f;

        [SerializeField] private LayerMask _groundLayer;

        private LegIK[] _legs; //2 legs for humanoid yet
        private IMovement _movement; //You can replace with your project Grounded check or simply remove it
        private Transform _transform;
        private RaycastHit _raycastHit;

        private Transform _hip;
        private Vector3 _hipPosition;
        private float _hipOffset;

    

        private IMovement Movement { get { if (_movement == null) _movement = GetComponentInParent<ICharacterEntity>()?.Movement; return _movement; } }
        private void Awake()
        {
            if (_animator == null) _animator = GetComponent<Animator>();

            _transform = transform;

            _legs = new LegIK[2]
            {
                new LegIK
                {
                    AvatarTarget = AvatarIKGoal.LeftFoot,
                    Foot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                    LowerLeg = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                    Toes = _animator.GetBoneTransform(HumanBodyBones.LeftToes)
                },
                new LegIK
                {
                    AvatarTarget = AvatarIKGoal.RightFoot,
                    Foot = _animator.GetBoneTransform(HumanBodyBones.RightFoot),
                    LowerLeg = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                    Toes = _animator.GetBoneTransform(HumanBodyBones.RightToes)
                }
            };

            foreach (var leg in _legs)
            {
                leg.MaxLegLength = transform.InverseTransformPoint(leg.LowerLeg.position).y;
                leg.FootOffset = transform.InverseTransformPoint(leg.Foot.position).y;
            }

            _hip = _animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (Movement == null) return;

            if (Movement.IsGrounded) //Can remove
            {
                //Perform Raycast for each foot
                PerformFootRaycast(_legs[0]);
                PerformFootRaycast(_legs[1]);
            }
            else
            {
                //Reset foot IK when not grounded
                foreach (var leg in _legs)
                {
                    leg.GroundDistance = float.MaxValue;
                    leg.IKWeight = 0f;
                }
            }

            ApplyHipPosition();//Apply hip before apply foot IK
            //Now Apply IK
            UpdateFeetIK(_legs[0]);
            UpdateFeetIK(_legs[1]);
        }

        private void LateUpdate()
        {
            if (Movement == null) return;
            _hip.position = transform.TransformPoint(_hipPosition);
        }

        private void ApplyHipPosition()
        {
            float hipOffset = _minHipOffset; 
            foreach (var leg in _legs)
            {
                if (leg.GroundDistance == float.MaxValue) continue; //Skip if no ground hit
                var offset = leg.GroundDistance - leg.RaycastDistance - transform.InverseTransformPoint(leg.Foot.position).y;//LegRaycastDistance
                if (offset > hipOffset)
                {
                    hipOffset = offset;
                }
            }
            _hipOffset = Mathf.MoveTowards(_hipOffset, hipOffset, _hipAdjustmentSpeed * Time.deltaTime);
            _hipPosition = transform.InverseTransformPoint(_hip.position);
            _hipPosition.y -= _hipOffset;
        }

        private void UpdateFeetIK(LegIK leg)
        {
            var position = _animator.GetIKPosition(leg.AvatarTarget);
            var rotation = _animator.GetIKRotation(leg.AvatarTarget);
            float targetWeight = 0f;
            float adjustmentSpeed = _footInactiveAdjustmentSpeed;

            if (_movement.IsGrounded)
            {
                if (leg.GroundDistance != float.MaxValue && leg.GroundDistance > 0)
                {
                    bool isFootBelowGround = transform.InverseTransformDirection(position - leg.GroundPoint).y - leg.FootOffset - _hipOffset < 0; //Check if foot is below ground point

                    if (isFootBelowGround)
                    {
                        var localFootPositon = _transform.InverseTransformPoint(position);
                        localFootPositon.y = transform.InverseTransformPoint(leg.GroundPoint).y; //Set foot y position to ground point y position
                        position = _transform.TransformPoint(localFootPositon) + Vector3.up * (leg.FootOffset + _hipOffset);
                        rotation = Quaternion.LookRotation(Vector3.Cross(leg.GroundNormal, rotation * Vector3.left), Vector3.up);
                        targetWeight = 1f;
                        adjustmentSpeed = _footActiveAdjustmentSpeed;
                    }
                }
            }


            leg.DebugPoint = position;

            leg.IKWeight = Mathf.MoveTowards(leg.IKWeight, targetWeight, adjustmentSpeed * Time.deltaTime);
            //Apply IK position
            _animator.SetIKPosition(leg.AvatarTarget, position);
            _animator.SetIKPositionWeight(leg.AvatarTarget, leg.IKWeight);

            _animator.SetIKRotation(leg.AvatarTarget, rotation);
            _animator.SetIKRotationWeight(leg.AvatarTarget, leg.IKWeight);
        }


        private void PerformFootRaycast(LegIK leg)
        {
            var origin = GetFootRaycastPosition(leg.Foot, leg.LowerLeg, out float distance);

            var maxDistance = distance + leg.MaxLegLength; //LegMaxLength

            if (Physics.Raycast(origin, Vector3.down, out _raycastHit, maxDistance, _groundLayer))
            {
                leg.GroundDistance = _raycastHit.distance;
                leg.GroundPoint = _raycastHit.point;
                leg.GroundNormal = _raycastHit.normal;
                leg.RaycastDistance = distance * transform.localScale.y; //Convert to world scale distance

            }
            else
            {
                leg.GroundDistance = float.MaxValue;
            }

            PerformToesRaycast(leg);
        }

        private void PerformToesRaycast(LegIK leg)
        {
            if (leg.Toes == null) return;

            var origin = GetFootRaycastPosition(leg.Toes, leg.LowerLeg, out float distance);
            var maxDistance = distance + leg.FootOffset + leg.MaxLegLength;

            if (!Physics.Raycast(origin, Vector3.down, out _raycastHit, maxDistance, _groundLayer)) return; //If no hit, skip

            bool isCloserThanFoot = _raycastHit.distance + _minHipOffset < leg.GroundDistance;
            if (!isCloserThanFoot) return; //If hit is farther than foot raycast, skip

            leg.RaycastDistance = distance * _transform.lossyScale.y;
            leg.GroundDistance = _raycastHit.distance;
            leg.GroundPoint = _raycastHit.point;
            leg.GroundNormal = _raycastHit.normal;

        }

        private Vector3 GetFootRaycastPosition(Transform targetTransform, Transform lowerLeg, out float distance)
        {
            var raycastPosition = _transform.InverseTransformPoint(targetTransform.position);
            var localLowerLegPosition = _transform.InverseTransformPoint(lowerLeg.position);
            distance = localLowerLegPosition.y - raycastPosition.y;
            raycastPosition.y = localLowerLegPosition.y;
            return _transform.TransformPoint(raycastPosition);
        }


        private void OnDrawGizmos()
        {

            if (_legs != null && _legs.Length > 1)
            {
                Gizmos.color = Color.red;
                if (_legs[0].GroundDistance != float.MaxValue)
                {
                    Gizmos.DrawSphere(_legs[0].DebugPoint, 0.1f);
                }
                Gizmos.color = Color.blue;
                if (_legs[1].GroundDistance != float.MaxValue)
                {
                    Gizmos.DrawSphere(_legs[1].DebugPoint, 0.1f);
                }
            }
        }
    }
}