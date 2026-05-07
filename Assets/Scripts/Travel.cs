using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Index Finger Movement")]
    public float moveSpeed = 35f;
    public float rotationSpeed = 45f;
    public float rotationAngleDeadZone = 12f;
    public float directionSmoothSpeed = 8f;

    [Header("Up / Down Movement")]
    public float verticalSpeed = 25f;
    public float thumbHeightThreshold = 0.08f;

    [Header("Gesture Detection")]
    public float fistThreshold = 0.09f;

    [Header("Gameplay Lock")]
    public bool canMove = true;

    public event Action<Collider> TriggerEntered;

    private XRHandSubsystem handSubsystem;
    private Vector3 smoothedPointDirection;
    private bool hasSmoothedDirection = false;

    void Start()
    {
        if (drone == null)
            drone = transform;

        TryInitializeHands();
    }

    void Update()
    {
        if (handSubsystem == null)
        {
            TryInitializeHands();
            return;
        }

        if (!canMove)
            return;

        XRHand rightHand = handSubsystem.rightHand;

        if (!rightHand.isTracked)
        {
            hasSmoothedDirection = false;
            return;
        }

        if (!TryGetPalmPose(rightHand, out Pose rightPalmPose))
            return;

        bool rightFist = IsFist(rightHand, rightPalmPose.position);

        if (rightFist)
        {
            hasSmoothedDirection = false;
            return;
        }

        bool rightThumbUp = IsThumbUp(rightHand, rightPalmPose.position);
        bool rightThumbDown = IsThumbDown(rightHand, rightPalmPose.position);

        HandleIndexFingerMovement(rightHand);
        HandleVerticalMovement(rightThumbUp, rightThumbDown);
    }

    private void HandleIndexFingerMovement(XRHand rightHand)
    {
        if (!TryGetIndexPointDirection(rightHand, out Vector3 rawDirection))
            return;

        rawDirection.y = 0f;

        if (rawDirection.sqrMagnitude < 0.001f)
            return;

        rawDirection.Normalize();

        if (!hasSmoothedDirection)
        {
            smoothedPointDirection = rawDirection;
            hasSmoothedDirection = true;
        }
        else
        {
            smoothedPointDirection = Vector3.Slerp(
                smoothedPointDirection,
                rawDirection,
                directionSmoothSpeed * Time.deltaTime
            );
        }

        smoothedPointDirection.y = 0f;
        smoothedPointDirection.Normalize();

        float angleToTarget = Vector3.SignedAngle(
            drone.forward,
            smoothedPointDirection,
            Vector3.up
        );

        if (Mathf.Abs(angleToTarget) > rotationAngleDeadZone)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(smoothedPointDirection, Vector3.up);

            drone.rotation = Quaternion.RotateTowards(
                drone.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        Vector3 moveDirection = drone.forward;
        moveDirection.y = 0f;
        moveDirection.Normalize();

        drone.position += moveDirection * moveSpeed * Time.deltaTime;
    }

    private void HandleVerticalMovement(bool rightThumbUp, bool rightThumbDown)
    {
        if (rightThumbUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
        }
        else if (rightThumbDown)
        {
            drone.position += Vector3.down * verticalSpeed * Time.deltaTime;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerEntered?.Invoke(other);
    }

    private void TryInitializeHands()
    {
        var manager = XRGeneralSettings.Instance?.Manager;

        if (manager?.activeLoader == null)
            return;

        handSubsystem =
            manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
    }

    private bool TryGetPalmPose(XRHand hand, out Pose pose)
    {
        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        return palm.TryGetPose(out pose);
    }

    private bool TryGetIndexPointDirection(XRHand hand, out Vector3 direction)
    {
        direction = Vector3.zero;

        XRHandJoint indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        XRHandJoint indexMiddle = hand.GetJoint(XRHandJointID.IndexIntermediate);
        XRHandJoint indexBase = hand.GetJoint(XRHandJointID.IndexProximal);

        bool hasTip = indexTip.TryGetPose(out Pose tipPose);
        bool hasMiddle = indexMiddle.TryGetPose(out Pose middlePose);
        bool hasBase = indexBase.TryGetPose(out Pose basePose);

        if (hasTip && hasBase)
        {
            direction = tipPose.position - basePose.position;
        }
        else if (hasTip && hasMiddle)
        {
            direction = tipPose.position - middlePose.position;
        }
        else
        {
            return false;
        }

        return direction.sqrMagnitude > 0.0001f;
    }

    private bool IsFist(XRHand hand, Vector3 palmPosition)
    {
        float totalDistance = 0f;
        int count = 0;

        XRHandJointID[] fingerTips =
        {
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip
        };

        foreach (XRHandJointID jointID in fingerTips)
        {
            XRHandJoint joint = hand.GetJoint(jointID);

            if (joint.TryGetPose(out Pose tipPose))
            {
                totalDistance += Vector3.Distance(tipPose.position, palmPosition);
                count++;
            }
        }

        if (count == 0)
            return false;

        float averageDistance = totalDistance / count;
        return averageDistance < fistThreshold;
    }

    private bool IsThumbUp(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return thumbPose.position.y - palmPosition.y > thumbHeightThreshold;
    }

    private bool IsThumbDown(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return palmPosition.y - thumbPose.position.y > thumbHeightThreshold;
    }
}