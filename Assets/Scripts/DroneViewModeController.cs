using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class DroneViewModeController : MonoBehaviour
{
    private enum ViewMode
    {
        Pilot,
        Cockpit,
        Chase
    }

    private const float GestureHoldDuration = 1f;
    private const float GestureCooldownDuration = 1f;
    private const float OpenHandThreshold = 0.12f;
    private static readonly Vector3 ChaseOffset = new Vector3(0f, 2.2f, -5.5f);

    private readonly XRHandJointID[] openHandJoints =
    {
        XRHandJointID.ThumbTip,
        XRHandJointID.IndexTip,
        XRHandJointID.MiddleTip,
        XRHandJointID.RingTip,
        XRHandJointID.LittleTip
    };

    private Transform droneRoot;
    private Camera viewCamera;
    private Transform cameraOffset;
    private XRHandSubsystem handSubsystem;
    private GameObject cockpitVisual;
    private GameObject droneVisual;
    private Vector3 defaultCameraOffsetLocalPosition;
    private ViewMode currentMode;
    private float gestureHoldTime;
    private float gestureCooldownUntil;
    private bool initialized;

    public void Initialize(Transform root, Camera cameraToUse)
    {
        if (initialized)
        {
            return;
        }

        droneRoot = root;
        viewCamera = cameraToUse;
        cameraOffset = viewCamera.transform.parent;
        defaultCameraOffsetLocalPosition = cameraOffset != null ? cameraOffset.localPosition : Vector3.zero;

        EnsureVisuals();
        ApplyViewMode(ViewMode.Pilot);
        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        if (handSubsystem == null)
        {
            var manager = XRGeneralSettings.Instance?.Manager;
            if (manager?.activeLoader != null)
            {
                handSubsystem = manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
            }
        }

        if (handSubsystem == null || Time.time < gestureCooldownUntil)
        {
            return;
        }

        if (!IsCycleGestureActive())
        {
            gestureHoldTime = 0f;
            return;
        }

        gestureHoldTime += Time.deltaTime;
        if (gestureHoldTime < GestureHoldDuration)
        {
            return;
        }

        gestureHoldTime = 0f;
        gestureCooldownUntil = Time.time + GestureCooldownDuration;
        ApplyViewMode((ViewMode)(((int)currentMode + 1) % 3));
    }

    private void LateUpdate()
    {
        if (!initialized || cameraOffset == null)
        {
            return;
        }

        if (currentMode == ViewMode.Chase)
        {
            UpdateChaseCameraPose();
        }
    }

    private bool IsCycleGestureActive()
    {
        var leftHand = handSubsystem.leftHand;
        var rightHand = handSubsystem.rightHand;
        if (!leftHand.isTracked || !rightHand.isTracked)
        {
            return false;
        }

        if (!TryGetPalmPose(leftHand, out var leftPalmPose) || !TryGetPalmPose(rightHand, out var rightPalmPose))
        {
            return false;
        }

        var leftPalmUp = Vector3.Dot(leftPalmPose.rotation * Vector3.up, Vector3.up);
        var rightPalmUp = Vector3.Dot(rightPalmPose.rotation * Vector3.up, Vector3.up);
        var handsSeparated = Vector3.Distance(leftPalmPose.position, rightPalmPose.position) > 0.18f;

        return leftPalmUp > 0.75f &&
               rightPalmUp > 0.75f &&
               handsSeparated &&
               IsOpenHand(leftHand, leftPalmPose.position) &&
               IsOpenHand(rightHand, rightPalmPose.position);
    }

    private static bool TryGetPalmPose(XRHand hand, out Pose pose)
    {
        return hand.GetJoint(XRHandJointID.Palm).TryGetPose(out pose);
    }

    private bool IsOpenHand(XRHand hand, Vector3 palmPosition)
    {
        var totalDistance = 0f;
        var count = 0;

        foreach (var jointId in openHandJoints)
        {
            if (hand.GetJoint(jointId).TryGetPose(out var jointPose))
            {
                totalDistance += Vector3.Distance(jointPose.position, palmPosition);
                count++;
            }
        }

        if (count == 0)
        {
            return false;
        }

        return (totalDistance / count) > OpenHandThreshold;
    }

    private void EnsureVisuals()
    {
        cockpitVisual = CreateCockpitVisual();
        droneVisual = CreateDroneVisual();
    }

    private GameObject CreateCockpitVisual()
    {
        var cockpitRoot = new GameObject("Virtual Cockpit");
        cockpitRoot.transform.SetParent(viewCamera.transform, false);
        cockpitRoot.transform.localPosition = new Vector3(0f, -0.4f, 0.55f);

        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Dash", new Vector3(0f, -0.1f, 0f), new Vector3(0.9f, 0.08f, 0.25f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cylinder, "Left Rail", new Vector3(-0.35f, 0.05f, 0.05f), new Vector3(0.03f, 0.25f, 0.03f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cylinder, "Right Rail", new Vector3(0.35f, 0.05f, 0.05f), new Vector3(0.03f, 0.25f, 0.03f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Canopy", new Vector3(0f, 0.25f, 0.25f), new Vector3(0.75f, 0.02f, 0.45f));

        cockpitRoot.SetActive(false);
        return cockpitRoot;
    }

    private GameObject CreateDroneVisual()
    {
        var droneRootVisual = new GameObject("Drone Body Visual");
        droneRootVisual.transform.SetParent(droneRoot, false);
        droneRootVisual.transform.localPosition = Vector3.zero;

        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cube, "Core", new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.08f, 0.3f));
        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cylinder, "Arm A", new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.35f, 0.03f), new Vector3(0f, 0f, 45f));
        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cylinder, "Arm B", new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.35f, 0.03f), new Vector3(0f, 0f, -45f));

        var propOffsets = new[]
        {
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, -0.5f)
        };

        for (var index = 0; index < propOffsets.Length; index++)
        {
            CreatePrimitive(
                droneRootVisual.transform,
                PrimitiveType.Sphere,
                $"Prop {index + 1}",
                propOffsets[index],
                Vector3.one * 0.12f);
        }

        droneRootVisual.SetActive(false);
        return droneRootVisual;
    }

    private void ApplyViewMode(ViewMode nextMode)
    {
        currentMode = nextMode;

        if (cameraOffset != null)
        {
            cameraOffset.localPosition = defaultCameraOffsetLocalPosition;
            cameraOffset.localRotation = Quaternion.identity;
        }

        cockpitVisual.SetActive(nextMode == ViewMode.Cockpit);
        droneVisual.SetActive(nextMode == ViewMode.Chase);

        if (nextMode == ViewMode.Chase && cameraOffset != null)
        {
            UpdateChaseCameraPose();
        }
    }

    private void UpdateChaseCameraPose()
    {
        cameraOffset.localPosition = defaultCameraOffsetLocalPosition + ChaseOffset;

        var lookDirection = droneRoot.position - cameraOffset.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        cameraOffset.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    private static void CreatePrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale)
    {
        CreatePrimitive(parent, primitiveType, objectName, localPosition, localScale, Vector3.zero);
    }

    private static void CreatePrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEulerAngles)
    {
        var child = GameObject.CreatePrimitive(primitiveType);
        child.name = objectName;
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = localScale;
        child.transform.localEulerAngles = localEulerAngles;

        var collider = child.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }
    }
}
