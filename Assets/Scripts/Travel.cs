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

    [Header("Movement")]
    public float forwardSpeedMultiplier = 18f;
    public float maxForwardSpeed = 25f;
    public float maxBackwardSpeed = 15f;
    public float movementDeadZone = 0.04f;

    public float verticalSpeedMultiplier = 14f;
    public float maxVerticalSpeed = 18f;
    public float verticalDeadZone = 0.04f;

    [Header("Rotation")]
    public float turnSpeed = 140f;
    public float turnDeadZone = 0.12f;

    [Header("Fist Detection")]
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

        if (leftFist && rightFist)
        {
            Vector3 fistCenter = (leftPalmPose.position + rightPalmPose.position) / 2f;

            if (!hasNeutralFistPosition)
            {
                neutralFistCenter = fistCenter;
                hasNeutralFistPosition = true;
            }

            Vector3 worldOffset = fistCenter - neutralFistCenter;

            // Use headset direction for forward/backward movement.
            Vector3 forwardDirection = headsetCamera != null ? headsetCamera.forward : drone.forward;
            forwardDirection.y = 0f;
            forwardDirection.Normalize();

            float forwardAmount = Vector3.Dot(worldOffset, forwardDirection);

            if (Mathf.Abs(forwardAmount) > movementDeadZone)
            {
                float speed = forwardAmount * forwardSpeedMultiplier;
                speed = Mathf.Clamp(speed, -maxBackwardSpeed, maxForwardSpeed);

                drone.position += forwardDirection * speed * Time.deltaTime;
            }

            float verticalAmount = worldOffset.y;

            if (Mathf.Abs(verticalAmount) > verticalDeadZone)
            {
                float verticalSpeed = verticalAmount * verticalSpeedMultiplier;
                verticalSpeed = Mathf.Clamp(verticalSpeed, -maxVerticalSpeed, maxVerticalSpeed);

                drone.position += Vector3.up * verticalSpeed * Time.deltaTime;
            }
        }
        else
        {
            hasNeutralFistPosition = false;
        }

        Vector3 leftPalmUp = leftPalmPose.rotation * Vector3.up;
        Vector3 rightPalmUp = rightPalmPose.rotation * Vector3.up;

        Vector3 localLeftUp = drone.InverseTransformDirection(leftPalmUp);
        Vector3 localRightUp = drone.InverseTransformDirection(rightPalmUp);

        float turnAmount = localRightUp.x - localLeftUp.x;

        if (Mathf.Abs(turnAmount) > turnDeadZone)
        {
            drone.Rotate(Vector3.up, turnAmount * turnSpeed * Time.deltaTime, Space.World);
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
}