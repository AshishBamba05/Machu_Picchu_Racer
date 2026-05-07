using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Speeds")]
    public float moveSpeed = 0.1f;
    public float verticalSpeed = 0.2f;
    public float rotationSpeed = 40f;

    [Header("Right Hand Thresholds")]
    public float fistThreshold = 0.075f;
    public float thumbHeightThreshold = 0.08f;
    public float thumbNeutralThreshold = 0.04f;

    [Header("Left Thumb Rotation")]
    public float leftThumbRotationDeadZone = 0.25f;
    public bool invertLeftThumbRotation = false;

    [Header("Gameplay Lock")]
    public bool canMove = true;

    public event Action<Collider> TriggerEntered;

    private XRHandSubsystem handSubsystem;

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
        XRHand leftHand = handSubsystem.leftHand;

        if (rightHand.isTracked)
            HandleRightHandMovement(rightHand);

        if (leftHand.isTracked)
            HandleLeftThumbRotation(leftHand);
    }

    private void HandleRightHandMovement(XRHand rightHand)
    {
        if (!TryGetPalmPose(rightHand, out Pose palmPose))
            return;

        bool fist = IsFistWithoutThumb(rightHand, palmPose.position);

        if (!fist)
            return;

        float thumbOffset = GetThumbVerticalOffset(
            rightHand,
            palmPose.position
        );

        // Fist + thumb up = ONLY up
        if (thumbOffset > thumbHeightThreshold)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
            return;
        }

        // Fist + thumb down = ONLY down
        if (thumbOffset < -thumbHeightThreshold)
        {
            drone.position += Vector3.down * verticalSpeed * Time.deltaTime;
            return;
        }

        // Plain fist only moves forward when thumb is neutral.
        // This prevents forward movement during thumb up/down.
        if (Mathf.Abs(thumbOffset) < thumbNeutralThreshold)
        {
            drone.position += drone.forward * moveSpeed * Time.deltaTime;
        }
    }

    private float GetThumbVerticalOffset(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return 0f;

        return thumbPose.position.y - palmPosition.y;
    }

    private void HandleLeftThumbRotation(XRHand leftHand)
    {
        XRHandJoint thumbTip = leftHand.GetJoint(XRHandJointID.ThumbTip);
        XRHandJoint thumbBase = leftHand.GetJoint(XRHandJointID.ThumbProximal);

        if (!thumbTip.TryGetPose(out Pose tipPose))
            return;

        if (!thumbBase.TryGetPose(out Pose basePose))
            return;

        Vector3 thumbDirection = tipPose.position - basePose.position;

        if (thumbDirection.sqrMagnitude < 0.0001f)
            return;

        thumbDirection.Normalize();

        float turnAmount = thumbDirection.x;

        if (invertLeftThumbRotation)
            turnAmount *= -1f;

        if (Mathf.Abs(turnAmount) > leftThumbRotationDeadZone)
        {
            float adjustedTurn = turnAmount;

            if (turnAmount > 0f)
                adjustedTurn -= leftThumbRotationDeadZone;
            else
                adjustedTurn += leftThumbRotationDeadZone;

            drone.Rotate(
                Vector3.up,
                adjustedTurn * rotationSpeed * Time.deltaTime,
                Space.World
            );
        }
    }

    private bool IsFistWithoutThumb(XRHand hand, Vector3 palmPosition)
    {
        XRHandJointID[] fingerTips =
        {
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip
        };

        int curledCount = 0;

        foreach (XRHandJointID jointID in fingerTips)
        {
            XRHandJoint joint = hand.GetJoint(jointID);

            if (joint.TryGetPose(out Pose tipPose))
            {
                float distance = Vector3.Distance(
                    tipPose.position,
                    palmPosition
                );

                if (distance < fistThreshold)
                    curledCount++;
            }
        }

        return curledCount >= 4;
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
}