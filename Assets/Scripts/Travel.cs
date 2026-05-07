using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Speeds")]
    public float moveSpeed = 0.4f;
    public float verticalSpeed = 2f;
    public float rotationSpeed = 14f;

    [Header("Right Hand Gesture Thresholds")]
    public float fistThreshold = 0.075f;
    public float thumbHeightThreshold = 0.08f;

    [Header("Left Thumb Rotation")]
    public float leftThumbRotationDeadZone = 0.25f;

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

        // Right hand controls forward/up/down
        if (rightHand.isTracked)
        {
            HandleRightHandMovement(rightHand);
        }

        // Left hand controls rotation
        if (leftHand.isTracked)
        {
            HandleLeftThumbRotation(leftHand);
        }
    }

    private void HandleRightHandMovement(XRHand rightHand)
    {
        if (!TryGetPalmPose(rightHand, out Pose palmPose))
            return;

        bool thumbUp = IsThumbUp(rightHand, palmPose.position);
        bool thumbDown = IsThumbDown(rightHand, palmPose.position);
        bool fist = IsFist(rightHand, palmPose.position);

        // Fist = STOP
        if (fist)
            return;

        // Thumb up/down has priority over forward movement
        if (thumbUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
            return;
        }

        if (thumbDown)
        {
            drone.position += Vector3.down * verticalSpeed * Time.deltaTime;
            return;
        }

        // Open hand / palm visible = move forward
        drone.position += drone.forward * moveSpeed * Time.deltaTime;
    }

    private void HandleLeftThumbRotation(XRHand leftHand)
    {
        if (!TryGetPalmPose(leftHand, out Pose palmPose))
            return;

        XRHandJoint thumbTip =
            leftHand.GetJoint(XRHandJointID.ThumbTip);

        XRHandJoint thumbBase =
            leftHand.GetJoint(XRHandJointID.ThumbProximal);

        if (!thumbTip.TryGetPose(out Pose tipPose))
            return;

        if (!thumbBase.TryGetPose(out Pose basePose))
            return;

        Vector3 thumbDirection =
            tipPose.position - basePose.position;

        if (thumbDirection.sqrMagnitude < 0.0001f)
            return;

        thumbDirection.Normalize();

        // Palm local right direction
        Vector3 palmRight =
            palmPose.rotation * Vector3.right;

        float turnAmount =
            Vector3.Dot(thumbDirection, palmRight);

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
        XRHandJoint palm =
            hand.GetJoint(XRHandJointID.Palm);

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
            XRHandJoint joint =
                hand.GetJoint(jointID);

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

        float averageDistance =
            totalDistance / count;

        return averageDistance < fistThreshold;
    }

    private bool IsThumbUp(
        XRHand hand,
        Vector3 palmPosition
    )
    {
        XRHandJoint thumbTip =
            hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return
            thumbPose.position.y - palmPosition.y >
            thumbHeightThreshold;
    }

    private bool IsThumbDown(
        XRHand hand,
        Vector3 palmPosition
    )
    {
        XRHandJoint thumbTip =
            hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return
            palmPosition.y - thumbPose.position.y >
            thumbHeightThreshold;
    }
}