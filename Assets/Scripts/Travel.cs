using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Movement")]
    public float moveSpeed = 1f;
    public float verticalSpeed = 2f;
    public float rotationSpeed = 8f;

    [Header("Gesture Thresholds")]
    public float fistThreshold = 0.09f;
    public float thumbHeightThreshold = 0.06f;
    public float rotationDeadZone = 0.55f;

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
        bool thumbUp = IsThumbUp(rightHand, palmPose.position);
        bool thumbDown = IsThumbDown(rightHand, palmPose.position);

        HandleWristRotation(palmPose);

        // Fist = slower forward movement
        if (fist)
        {
            drone.position += drone.forward * moveSpeed * Time.deltaTime;
        }

        // Thumb up/down = vertical movement
        HandleVerticalMovement(thumbUp, thumbDown);
    }

    private void HandleWristRotation(Pose palmPose)
    {
        Vector3 palmRight = palmPose.rotation * Vector3.right;

        float turnAmount = palmRight.y;

        // Larger dead zone prevents constant rotation
        if (Mathf.Abs(turnAmount) > rotationDeadZone)
        {
            float adjustedTurn = turnAmount;

            if (turnAmount > 0f)
                adjustedTurn -= rotationDeadZone;
            else
                adjustedTurn += rotationDeadZone;

            drone.Rotate(
                Vector3.up,
                adjustedTurn * rotationSpeed * Time.deltaTime,
                Space.World
            );
        }
    }

    private void HandleVerticalMovement(bool thumbUp, bool thumbDown)
    {
        if (thumbUp)
        {
            drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
        }
        else if (thumbDown)
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