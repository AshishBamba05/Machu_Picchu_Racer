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
    public float rotationSpeed = 65f;
    public float pointingDeadZone = 0.08f;

    [Header("Gesture Detection")]
    public float fistThreshold = 0.09f;

    [Header("Gameplay Lock")]
    public bool canMove = true;

    public event Action<Collider> TriggerEntered;

    private XRHandSubsystem handSubsystem;

    private bool hasNeutralFistPosition = false;
    private Vector3 neutralFistCenter;

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

        XRHand leftHand = handSubsystem.leftHand;
        XRHand rightHand = handSubsystem.rightHand;

        if (!leftHand.isTracked || !rightHand.isTracked)
        {
            hasNeutralFistPosition = false;
            return;
        }

        if (!TryGetPalmPose(leftHand, out Pose leftPalmPose) ||
            !TryGetPalmPose(rightHand, out Pose rightPalmPose))
            return;

        bool leftFist = IsFist(leftHand, leftPalmPose.position);
        bool rightFist = IsFist(rightHand, rightPalmPose.position);

        bool leftThumbUp = IsThumbUp(leftHand, leftPalmPose.position);
        bool rightThumbUp = IsThumbUp(rightHand, rightPalmPose.position);

        bool leftThumbDown = IsThumbDown(leftHand, leftPalmPose.position);
        bool rightThumbDown = IsThumbDown(rightHand, rightPalmPose.position);

        HandleForwardBackwardMovement(leftPalmPose, rightPalmPose, leftFist, rightFist);
        HandleVerticalMovement(leftThumbUp, rightThumbUp, leftThumbDown, rightThumbDown);
        HandlePointingRotation(rightHand, rightFist);
    }

    private void HandleForwardBackwardMovement(Pose leftPalmPose, Pose rightPalmPose, bool leftFist, bool rightFist)
    {
        // Both fists = forward/backward movement.
        if (leftFist && rightFist)
        {
            Vector3 fistCenter = (leftPalmPose.position + rightPalmPose.position) / 2f;

            if (!hasNeutralFistPosition)
            {
                neutralFistCenter = fistCenter;
                hasNeutralFistPosition = true;
            }

            Vector3 worldOffset = fistCenter - neutralFistCenter;

            Vector3 forwardDirection = headsetCamera != null ? headsetCamera.forward : drone.forward;
            forwardDirection.y = 0f;
            forwardDirection.Normalize();

            // Push fists forward = move forward.
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

    private void HandleVerticalMovement(bool leftThumbUp, bool rightThumbUp, bool leftThumbDown, bool rightThumbDown)
    {
        // Both thumbs up = move up.
        if (leftThumbUp && rightThumbUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
        }
        // Both thumbs down = move down.
        else if (leftThumbDown && rightThumbDown)
        {
            drone.position += Vector3.down * verticalSpeed * Time.deltaTime;
        }
    }

    private void HandlePointingRotation(XRHand rightHand, bool rightFist)
    {
        // Right index finger pointing = drone rotates toward that pointing direction.
        // If right hand is a fist, do not rotate, because fist is used for movement.
        if (rightFist)
            return;

        if (!TryGetIndexPointDirection(rightHand, out Vector3 pointDirection))
            return;

        pointDirection.y = 0f;

        if (pointDirection.magnitude < pointingDeadZone)
            return;

        pointDirection.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(pointDirection, Vector3.up);

        // Smooth rotation so it is not too sensitive or snappy.
        drone.rotation = Quaternion.RotateTowards(
            drone.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
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

    private bool TryGetIndexPointDirection(XRHand hand, out Vector3 direction)
    {
        direction = Vector3.zero;

        XRHandJoint indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        XRHandJoint indexBase = hand.GetJoint(XRHandJointID.IndexProximal);

        if (!indexTip.TryGetPose(out Pose tipPose) ||
            !indexBase.TryGetPose(out Pose basePose))
            return false;

        direction = tipPose.position - basePose.position;
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