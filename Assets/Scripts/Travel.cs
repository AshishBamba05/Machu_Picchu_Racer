using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Speeds")]
    public float moveSpeed = 1f;
    public float verticalSpeed = 2f;
    public float rotationSpeed = 10f;

    [Header("Right Hand Gesture Thresholds")]
    public float fistThreshold = 0.09f;
    public float thumbHeightThreshold = 0.06f;

    [Header("Left Thumb Rotation")]
    public float leftThumbRotationDeadZone = 0.35f;

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

        // Right hand controls forward/up/down.
        // If right hand is gone, movement stops automatically.
        if (rightHand.isTracked)
        {
            HandleRightHandMovement(rightHand);
        }

        // Left hand controls rotation.
        // If left hand is gone, rotation stops automatically.
        if (leftHand.isTracked)
        {
            HandleLeftThumbRotation(leftHand);
        }
    }

    private void HandleRightHandMovement(XRHand rightHand)
    {
        if (!TryGetPalmPose(rightHand, out Pose palmPose))
            return;

        bool fist = IsFist(rightHand, palmPose.position);
        bool thumbUp = IsThumbUp(rightHand, palmPose.position);
        bool thumbDown = IsThumbDown(rightHand, palmPose.position);

        // Priority:
        // thumb up/down first, then fist forward.
        // This prevents up/down from also moving forward.
        if (thumbUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
        }
        else if (thumbDown)
        {
            drone.position += Vector3.down * verticalSpeed * Time.deltaTime;
        }
        else if (fist)
        {
            drone.position += drone.forward * moveSpeed * Time.deltaTime;
        }
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

        // x direction controls left/right turning.
        float turnAmount = thumbDirection.x;

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
                totalDistance += Vector3.Distance(
                    tipPose.position,
                    palmPosition
                );

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