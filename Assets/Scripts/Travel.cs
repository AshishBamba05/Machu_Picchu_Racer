using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Palm Control")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 30f;
    public float forwardDeadZone = 0.25f;
    public float rotationDeadZone = 0.25f;
    public float smoothingSpeed = 6f;

    [Header("Thumb Up / Down")]
    public float verticalSpeed = 4f;
    public float thumbHeightThreshold = 0.08f;

    [Header("Gesture Detection")]
    public float fistThreshold = 0.09f;

    [Header("Gameplay Lock")]
    public bool canMove = true;

    public event Action<Collider> TriggerEntered;

    private XRHandSubsystem handSubsystem;

    private Vector3 smoothedPalmForward;
    private bool hasSmoothedPalmForward = false;

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

        // If hand is not tracked, stop movement
        if (!rightHand.isTracked)
        {
            hasSmoothedPalmForward = false;
            return;
        }

        if (!TryGetPalmPose(rightHand, out Pose rightPalmPose))
        {
            hasSmoothedPalmForward = false;
            return;
        }

        // Fist = stop movement
        bool rightFist = IsFist(rightHand, rightPalmPose.position);

        if (rightFist)
        {
            hasSmoothedPalmForward = false;
            return;
        }

        bool rightThumbUp = IsThumbUp(rightHand, rightPalmPose.position);
        bool rightThumbDown = IsThumbDown(rightHand, rightPalmPose.position);

        HandlePalmForwardAndRotation(rightPalmPose);
        HandleThumbVerticalMovement(rightThumbUp, rightThumbDown);
    }

    private void HandlePalmForwardAndRotation(Pose palmPose)
    {
        Vector3 rawPalmForward = palmPose.rotation * Vector3.forward;

        // If forward feels reversed, change the line above to:
        // Vector3 rawPalmForward = palmPose.rotation * -Vector3.forward;

        if (!hasSmoothedPalmForward)
        {
            smoothedPalmForward = rawPalmForward;
            hasSmoothedPalmForward = true;
        }
        else
        {
            smoothedPalmForward = Vector3.Slerp(
                smoothedPalmForward,
                rawPalmForward,
                smoothingSpeed * Time.deltaTime
            );
        }

        smoothedPalmForward.Normalize();

        // =========================
        // FORWARD ONLY
        // =========================

        float forwardAmount = Vector3.Dot(
            smoothedPalmForward,
            drone.forward
        );

        // Only move if palm points forward.
        // If forwardAmount is negative, we ignore it.
        if (forwardAmount > forwardDeadZone)
        {
            drone.position +=
                drone.forward *
                forwardAmount *
                moveSpeed *
                Time.deltaTime;
        }

        // =========================
        // LEFT / RIGHT ROTATION
        // =========================

        float turnAmount = Vector3.Dot(
            smoothedPalmForward,
            drone.right
        );

        if (Mathf.Abs(turnAmount) > rotationDeadZone)
        {
            drone.Rotate(
                Vector3.up,
                turnAmount * rotationSpeed * Time.deltaTime,
                Space.World
            );
        }
    }

    private void HandleThumbVerticalMovement(
        bool rightThumbUp,
        bool rightThumbDown
    )
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