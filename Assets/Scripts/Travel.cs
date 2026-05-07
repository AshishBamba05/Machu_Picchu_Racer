using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Movement")]
    public float moveSpeed = 60f;
    public float rotationSpeed = 90f;

    [Header("Vertical Movement")]
    public float verticalSpeed = 35f;
    public float thumbHeightThreshold = 0.08f;

    [Header("Raycast")]
    public float rayDistance = 20f;
    public LayerMask raycastMask = ~0;

    [Header("Gesture Detection")]
    public float fistThreshold = 0.09f;

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

        // Stop everything if hand is not tracked.
        if (!rightHand.isTracked)
            return;

        if (!TryGetPalmPose(rightHand, out Pose rightPalmPose))
            return;

        bool rightFist = IsFist(rightHand, rightPalmPose.position);

        bool rightThumbUp = IsThumbUp(rightHand, rightPalmPose.position);
        bool rightThumbDown = IsThumbDown(rightHand, rightPalmPose.position);

        // Open palm = move and rotate.
        // Fist = stop movement.
        if (!rightFist)
        {
            HandlePalmRayMovement(rightPalmPose);
            HandleVerticalMovement(rightThumbUp, rightThumbDown);
        }
    }

    private void HandlePalmRayMovement(Pose palmPose)
    {
        // Palm forward direction.
        Vector3 rayDirection =
            palmPose.rotation * Vector3.forward;

        // Ignore vertical tilt for horizontal movement.
        rayDirection.y = 0f;

        if (rayDirection.sqrMagnitude < 0.001f)
            return;

        rayDirection.Normalize();

        // Optional debug ray.
        Debug.DrawRay(
            palmPose.position,
            rayDirection * rayDistance,
            Color.green
        );

        // Move drone toward palm direction.
        drone.position +=
            rayDirection *
            moveSpeed *
            Time.deltaTime;

        // Smoothly rotate drone toward movement direction.
        Quaternion targetRotation =
            Quaternion.LookRotation(rayDirection, Vector3.up);

        drone.rotation = Quaternion.RotateTowards(
            drone.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void HandleVerticalMovement(
        bool rightThumbUp,
        bool rightThumbDown)
    {
        // Thumb up = move up.
        if (rightThumbUp)
        {
            drone.position +=
                Vector3.up *
                verticalSpeed *
                Time.deltaTime;
        }
        // Thumb down = move down.
        else if (rightThumbDown)
        {
            drone.position +=
                Vector3.down *
                verticalSpeed *
                Time.deltaTime;
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
            XRHandJoint joint = hand.GetJoint(jointID);

            if (joint.TryGetPose(out Pose tipPose))
            {
                totalDistance +=
                    Vector3.Distance(
                        tipPose.position,
                        palmPosition);

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
        Vector3 palmPosition)
    {
        XRHandJoint thumbTip =
            hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return
            thumbPose.position.y -
            palmPosition.y >
            thumbHeightThreshold;
    }

    private bool IsThumbDown(
        XRHand hand,
        Vector3 palmPosition)
    {
        XRHandJoint thumbTip =
            hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return
            palmPosition.y -
            thumbPose.position.y >
            thumbHeightThreshold;
    }
}