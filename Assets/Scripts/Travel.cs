using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Headset")]
    public Transform headsetCamera;

    [Header("Forward / Backward Movement")]
    public float forwardSpeed = 60f;
    public float backwardSpeed = 50f;
    public float movementDeadZone = 0.012f;

    [Header("Up / Down Movement")]
    public float verticalSpeed = 35f;
    public float thumbHeightThreshold = 0.08f;

    [Header("Rotation")]
    public float turnSpeed = 45f;
    public float turnDeadZone = 0.03f;

    [Header("Gesture Detection")]
    public float fistThreshold = 0.09f;

    [Header("Gameplay Lock")]
    public bool canMove = true;

    public event Action<Collider> TriggerEntered;

    private XRHandSubsystem handSubsystem;

    private bool hasNeutralFistPosition = false;
    private Vector3 neutralFistPosition;

    private bool hasNeutralWristRotation = false;
    private Quaternion neutralWristRotation;

    void Start()
    {
        if (drone == null)
            drone = transform;

        if (headsetCamera == null && Camera.main != null)
            headsetCamera = Camera.main.transform;

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
            ResetGestureState();
            return;
        }

        if (!TryGetPalmPose(rightHand, out Pose rightPalmPose))
            return;

        bool rightFist = IsFist(rightHand, rightPalmPose.position);
        bool rightThumbUp = IsThumbUp(rightHand, rightPalmPose.position);
        bool rightThumbDown = IsThumbDown(rightHand, rightPalmPose.position);

        HandleForwardBackwardMovement(rightPalmPose, rightFist);
        HandleVerticalMovement(rightThumbUp, rightThumbDown);
        HandleWristRotation(rightPalmPose, rightFist);
    }

    private void HandleForwardBackwardMovement(Pose rightPalmPose, bool rightFist)
    {
        // Right fist push forward/backward = move forward/backward.
        if (rightFist)
        {
            if (!hasNeutralFistPosition)
            {
                neutralFistPosition = rightPalmPose.position;
                hasNeutralFistPosition = true;
            }

            Vector3 worldOffset = rightPalmPose.position - neutralFistPosition;

            Vector3 forwardDirection = headsetCamera != null ? headsetCamera.forward : drone.forward;
            forwardDirection.y = 0f;
            forwardDirection.Normalize();

            // Push fist forward = move forward.
            float forwardAmount = -Vector3.Dot(worldOffset, forwardDirection);

            if (Mathf.Abs(forwardAmount) > movementDeadZone)
            {
                float speed = forwardAmount > 0f ? forwardSpeed : backwardSpeed;
                drone.position += forwardDirection * Mathf.Sign(forwardAmount) * speed * Time.deltaTime;
            }
        }
        else
        {
            hasNeutralFistPosition = false;
        }
    }

    private void HandleVerticalMovement(bool rightThumbUp, bool rightThumbDown)
    {
        // Right thumb up = move up.
        if (rightThumbUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
        }
        // Right thumb down = move down.
        else if (rightThumbDown)
        {
            drone.position += Vector3.down * verticalSpeed * Time.deltaTime;
        }
    }

    private void HandleWristRotation(Pose rightPalmPose, bool rightFist)
    {
        // Right fist wrist rotation = rotate drone.
        if (rightFist)
        {
            if (!hasNeutralWristRotation)
            {
                neutralWristRotation = rightPalmPose.rotation;
                hasNeutralWristRotation = true;
            }

            Quaternion deltaRotation = rightPalmPose.rotation * Quaternion.Inverse(neutralWristRotation);

            float yawDelta = deltaRotation.eulerAngles.y;

            if (yawDelta > 180f)
                yawDelta -= 360f;

            if (Mathf.Abs(yawDelta) > turnDeadZone)
            {
                drone.Rotate(Vector3.up, yawDelta * turnSpeed * Time.deltaTime, Space.World);
            }
        }
        else
        {
            hasNeutralWristRotation = false;
        }
    }

    private void ResetGestureState()
    {
        hasNeutralFistPosition = false;
        hasNeutralWristRotation = false;
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

        handSubsystem = manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
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