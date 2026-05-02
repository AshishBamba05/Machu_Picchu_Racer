using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Movement")]
    public float forwardSpeedMultiplier = 4f;
    public float maxForwardSpeed = 8f;
    public float maxBackwardSpeed = 4f;
    public float movementDeadZone = 0.08f;

    [Header("Rotation")]
    public float turnSpeed = 80f;
    public float turnDeadZone = 0.15f;

    [Header("Fist Detection")]
    public float fistThreshold = 0.09f;

    [Header("Gameplay Lock")]
    public bool canMove = true;

    private XRHandSubsystem handSubsystem;

    private bool hasNeutralFistPosition = false;
    private Vector3 neutralFistCenter;

    void Start()
    {
        if (drone == null)
            drone = transform;

        handSubsystem = XRGeneralSettings.Instance.Manager.activeLoader
            .GetLoadedSubsystem<XRHandSubsystem>();
    }

    void Update()
    {
        if (!canMove || handSubsystem == null)
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

        // =========================
        // FIST FORWARD / BACKWARD MOVEMENT
        // =========================
        if (leftFist && rightFist)
        {
            Vector3 fistCenter = (leftPalmPose.position + rightPalmPose.position) / 2f;

            // First frame with both fists becomes the neutral hand position
            if (!hasNeutralFistPosition)
            {
                neutralFistCenter = fistCenter;
                hasNeutralFistPosition = true;
            }

            Vector3 worldOffset = fistCenter - neutralFistCenter;
            Vector3 localOffset = drone.InverseTransformDirection(worldOffset);

            float forwardAmount = localOffset.z;

            if (Mathf.Abs(forwardAmount) > movementDeadZone)
            {
                float speed = forwardAmount * forwardSpeedMultiplier;
                speed = Mathf.Clamp(speed, -maxBackwardSpeed, maxForwardSpeed);

                drone.position += drone.forward * speed * Time.deltaTime;
            }
        }
        else
        {
            // Reset neutral when fists are released
            hasNeutralFistPosition = false;
        }

        // =========================
        // PALM WRIST ROTATION / STEERING
        // =========================
        if (!leftFist && !rightFist)
        {
            Vector3 leftPalmUp = leftPalmPose.rotation * Vector3.up;
            Vector3 rightPalmUp = rightPalmPose.rotation * Vector3.up;

            Vector3 localLeftUp = drone.InverseTransformDirection(leftPalmUp);
            Vector3 localRightUp = drone.InverseTransformDirection(rightPalmUp);

            // Difference between wrist rotations acts like steering wheel turning
            float turnAmount = localRightUp.x - localLeftUp.x;

            if (Mathf.Abs(turnAmount) > turnDeadZone)
            {
                drone.Rotate(Vector3.up, turnAmount * turnSpeed * Time.deltaTime, Space.World);
            }
        }
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