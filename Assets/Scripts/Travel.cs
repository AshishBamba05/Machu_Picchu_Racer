using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Pointing Movement")]
    public float moveSpeed = 60f;
    public float rotationSpeed = 80f;
    public float pointingDeadZone = 0.05f;

    [Header("Up / Down Movement")]
    public float verticalSpeed = 35f;
    public float thumbHeightThreshold = 0.08f;

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

        // If the right hand is released / not tracked, stop everything.
        if (!rightHand.isTracked)
            return;

        if (!TryGetPalmPose(rightHand, out Pose rightPalmPose))
            return;

        bool rightFist = IsFist(rightHand, rightPalmPose.position);

        bool rightThumbUp = IsThumbUp(rightHand, rightPalmPose.position);
        bool rightThumbDown = IsThumbDown(rightHand, rightPalmPose.position);

        // Open / pointing hand = move.
        // Fist = stop horizontal movement.
        if (!rightFist)
        {
            HandlePointingMovement(rightHand);
            HandleVerticalMovement(rightThumbUp, rightThumbDown);
        }
    }

    private void HandlePointingMovement(XRHand rightHand)
    {
        if (!TryGetIndexPointDirection(rightHand, out Vector3 pointDirection))
            return;

        pointDirection.y = 0f;

        if (pointDirection.magnitude < pointingDeadZone)
            return;

        pointDirection.Normalize();

        // Move toward where the right index finger points.
        drone.position += pointDirection * moveSpeed * Time.deltaTime;

        // Smoothly rotate drone to face the moving direction.
        Quaternion targetRotation =
            Quaternion.LookRotation(pointDirection, Vector3.up);

        drone.rotation = Quaternion.RotateTowards(
            drone.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
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
                totalDistance +=
                    Vector3.Distance(tipPose.position, palmPosition);

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

        return
            thumbPose.position.y - palmPosition.y >
            thumbHeightThreshold;
    }

    private bool IsThumbDown(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return
            palmPosition.y - thumbPose.position.y >
            thumbHeightThreshold;
    }
}