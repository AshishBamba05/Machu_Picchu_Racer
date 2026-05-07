using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class Travel : MonoBehaviour
{
    [Header("Drone")]
    public Transform drone;

    [Header("Palm Forward + Rotation")]
    public float moveSpeed = 2f;
    public float rotationSpeed = 12f;
    public float forwardDeadZone = 0.45f;
    public float rotationDeadZone = 0.4f;
    public float smoothingSpeed = 10f;

    [Header("Thumbs Up / Down Vertical Movement")]
    public float verticalSpeed = 2f;
    public float thumbHeightThreshold = 0.08f;
    public float curledFingerThreshold = 0.11f;

    [Header("Fist Stop")]
    public float fistThreshold = 0.09f;

    [Header("Palm Direction Visual")]
    public LineRenderer palmDirectionLine;
    public float palmRayLength = 3f;

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

        if (!rightHand.isTracked)
        {
            StopPalmVisual();
            hasSmoothedPalmForward = false;
            return;
        }

        if (!TryGetPalmPose(rightHand, out Pose rightPalmPose))
        {
            StopPalmVisual();
            hasSmoothedPalmForward = false;
            return;
        }

        ShowPalmDirection(rightPalmPose);

        // Fist = stop everything
        if (IsFist(rightHand, rightPalmPose.position))
        {
            hasSmoothedPalmForward = false;
            return;
        }

        bool thumbsUp = IsThumbsUp(rightHand, rightPalmPose.position);
        bool thumbsDown = IsThumbsDown(rightHand, rightPalmPose.position);

        HandlePalmForwardAndRotation(rightPalmPose);
        HandleThumbVerticalMovement(thumbsUp, thumbsDown);
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

        // Forward only
        float forwardAmount = Vector3.Dot(smoothedPalmForward, drone.forward);

        if (forwardAmount > forwardDeadZone)
        {
            drone.position +=
                drone.forward *
                forwardAmount *
                moveSpeed *
                Time.deltaTime;
        }

        // Left / right rotation
        float turnAmount = Vector3.Dot(smoothedPalmForward, drone.right);

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

        // At least 3 fingers curled = clear thumbs up/down gesture
        return curledCount >= 3;
    }

    private void ShowPalmDirection(Pose palmPose)
    {
        if (palmDirectionLine == null)
            return;

        Vector3 start = palmPose.position;
        Vector3 direction = palmPose.rotation * Vector3.forward;

        // If your palm visual points backward, change the line above to:
        // Vector3 direction = palmPose.rotation * -Vector3.forward;

        Vector3 end = start + direction.normalized * palmRayLength;

        palmDirectionLine.enabled = true;
        palmDirectionLine.positionCount = 2;
        palmDirectionLine.SetPosition(0, start);
        palmDirectionLine.SetPosition(1, end);
    }

    private void StopPalmVisual()
    {
        if (palmDirectionLine != null)
            palmDirectionLine.enabled = false;
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
}