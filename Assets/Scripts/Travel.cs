using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Movement")]
    public float moveSpeed = 2f;
    public float verticalSpeed = 2f;
    public float rotationSpeed = 12f;

    [Header("Gesture Thresholds")]
    public float fistThreshold = 0.09f;
    public float thumbHeightThreshold = 0.08f;
    public float curledFingerThreshold = 0.11f;
    public float rotationDeadZone = 0.25f;

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

        if (!rightHand.isTracked)
            return;

        if (!TryGetPalmPose(rightHand, out Pose palmPose))
            return;

        bool fist = IsFist(rightHand, palmPose.position);
        bool thumbsUp = IsThumbsUp(rightHand, palmPose.position);
        bool thumbsDown = IsThumbsDown(rightHand, palmPose.position);

        // Wrist left / right controls rotation
        HandleWristRotation(palmPose);

        // Fist moves drone forward
        if (fist)
        {
            drone.position += drone.forward * moveSpeed * Time.deltaTime;
        }

        // Thumbs up / down controls vertical movement
        HandleThumbVerticalMovement(thumbsUp, thumbsDown);
    }

    private void HandleWristRotation(Pose palmPose)
    {
        Vector3 palmRight = palmPose.rotation * Vector3.right;

        float turnAmount = palmRight.y;

        if (Mathf.Abs(turnAmount) > rotationDeadZone)
        {
            drone.Rotate(
                Vector3.up,
                turnAmount * rotationSpeed * Time.deltaTime,
                Space.World
            );
        }
    }

    private void HandleThumbVerticalMovement(bool thumbsUp, bool thumbsDown)
    {
        if (thumbsUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
        }
        else if (thumbsDown)
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

    private bool IsThumbsUp(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        bool thumbIsHigh =
            thumbPose.position.y - palmPosition.y > thumbHeightThreshold;

        return thumbIsHigh && AreOtherFingersCurled(hand, palmPosition);
    }

    private bool IsThumbsDown(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        bool thumbIsLow =
            palmPosition.y - thumbPose.position.y > thumbHeightThreshold;

        return thumbIsLow && AreOtherFingersCurled(hand, palmPosition);
    }

    private bool AreOtherFingersCurled(XRHand hand, Vector3 palmPosition)
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

                if (distance < curledFingerThreshold)
                    curledCount++;
            }
        }

        return curledCount >= 3;
    }
}