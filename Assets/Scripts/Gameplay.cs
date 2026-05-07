using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Gameplay : MonoBehaviour
{
    private const string VisibleGameplayTextName = "Text (TMP)";
    private const string CountdownOverlayName = "Countdown Overlay Canvas";
    private const float RespawnHeightOffset = 0f;
    private const int ExternalTrackSearchDepth = 5;
    private const float CheckpointSurfaceOffset = 0.15f;
    private const float SurfaceProbePadding = 120f;
    private const float ImportedTrackCoordinateScale = 0.0254f;
    private const string DefaultTrackAnchorName = "machu_picchu_2";

    [Header("References")]
    public Travel travelScript;
    public Transform drone;

    [Header("First Person View")]
    public GameObject droneVisualRoot;
    public bool hideDroneVisual = true;

    [Header("Single UI Text")]
    public TMP_Text gameplayText;

    [Header("Gameplay Settings")]
    public float startCountdownTime = 3f;
    public float crashPenaltyTime = 3f;

    // 30 feet = 9.144 meters
    public float checkpointReachRadius = 9.144f;

    [Header("Track Loading")]
    [SerializeField] private string preferredTrackFileName = "competition.xyz";
    [SerializeField] private string fallbackTrackFileName = "sample_track.xyz";
    [SerializeField] private bool allowProceduralFallback = false;
    [SerializeField] private bool interpretTrackCoordinatesAsModelLocal = true;
    [SerializeField] private bool snapCheckpointHeightToSurface = false;
    [SerializeField] private string trackAnchorObjectName = DefaultTrackAnchorName;

    private readonly List<Transform> checkpoints = new List<Transform>();
    private readonly List<RaceCheckpoint> checkpointComponents = new List<RaceCheckpoint>();
    private readonly List<Vector3> checkpointPositions = new List<Vector3>();

    private int currentCheckpointIndex = 1;
    private int lastClearedCheckpointIndex = 0;

    private float timer = 0f;

    private bool timerRunning = false;
    private bool raceFinished = false;
    private bool isInCrashPenalty = false;
    private bool raceReady = false;

    private string currentMessage = "";
    private TMP_Text countdownText;
    private DroneRaceAudio raceAudio;
    private Bounds courseBounds;
    private bool hasCourseBounds;
    private bool loadedPreferredTrack;

    private void Start()
    {
        if (drone == null)
            drone = transform;

        if (gameplayText == null)
            gameplayText = FindVisibleGameplayText();

        ConfigureGameplayHud();
        EnsureCountdownOverlay();
        PrepareGameplayRuntime();
        EnsureCheckpointsLoaded();

        if (hideDroneVisual && droneVisualRoot != null)
            droneVisualRoot.SetActive(false);

        StartCoroutine(WaitForCheckpointsThenStart());
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && checkpointComponents.Count >= 2)
        {
            StopAllCoroutines();
            SetupDroneAtStart();
            StartCoroutine(RestartRaceAfterReset());
            return;
        }

        if (timerRunning && !raceFinished)
        {
            timer += Time.deltaTime;
        }

        if (raceReady && !raceFinished && !isInCrashPenalty)
        {
            CheckCheckpointProgress();
        }

        UpdateGameplayUI();
    }

    private IEnumerator WaitForCheckpointsThenStart()
    {
        currentMessage = "Loading checkpoints...";

        while (checkpointComponents.Count < 2)
        {
            EnsureCheckpointsLoaded();

            if (checkpointComponents.Count >= 2)
                break;

            yield return null;
        }

        SetupDroneAtStart();
        currentMessage = "";

        yield return StartCoroutine(StartCountdown());

        currentMessage = "";
        raceReady = true;
    }

    private void PrepareGameplayRuntime()
    {
        if (drone == null)
            return;

        DisableConflictingLocomotion();
        EnsureDroneCollisionSetup();
        EnsureEnvironmentCollisionSetup();

        if (travelScript == null)
            travelScript = drone.GetComponent<Travel>();

        if (travelScript == null)
            travelScript = drone.gameObject.AddComponent<Travel>();

        travelScript.drone = drone;
        travelScript.canMove = false;
        travelScript.TriggerEntered -= HandleDroneTriggerEntered;
        travelScript.TriggerEntered += HandleDroneTriggerEntered;

        raceAudio = drone.GetComponent<DroneRaceAudio>();
        if (raceAudio == null)
            raceAudio = drone.gameObject.AddComponent<DroneRaceAudio>();

        raceAudio.Initialize(drone);
        raceAudio.SetEngineActive(false);
    }

    private void DisableConflictingLocomotion()
    {
        var locomotionTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ActionBasedControllerManager",
            "ContinuousMoveProvider",
            "ContinuousMoveProviderBase",
            "ContinuousTurnProvider",
            "ContinuousTurnProviderBase",
            "DynamicMoveProvider",
            "SnapTurnProvider",
            "SnapTurnProviderBase",
            "GrabMoveProvider",
            "TwoHandedGrabMoveProvider",
            "ClimbProvider",
            "TeleportationProvider",
            "CharacterControllerDriver",
            "TunnelingVignetteController",
            "JumpProvider"
        };

        foreach (MonoBehaviour behaviour in drone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && locomotionTypes.Contains(behaviour.GetType().Name))
                behaviour.enabled = false;
        }
    }

    private void EnsureDroneCollisionSetup()
    {
        SphereCollider sphereCollider = drone.GetComponent<SphereCollider>();
        if (sphereCollider == null)
            sphereCollider = drone.gameObject.AddComponent<SphereCollider>();

        sphereCollider.radius = 0.35f;
        sphereCollider.center = Vector3.zero;
        sphereCollider.isTrigger = true;

        Rigidbody rigidbody = drone.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = drone.gameObject.AddComponent<Rigidbody>();

        rigidbody.useGravity = false;
        rigidbody.isKinematic = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void EnsureEnvironmentCollisionSetup()
    {
        foreach (MeshFilter meshFilter in FindObjectsOfType<MeshFilter>(true))
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            GameObject sceneObject = meshFilter.gameObject;
            if (sceneObject == null || sceneObject.transform.IsChildOf(drone) || sceneObject.transform.IsChildOf(transform))
                continue;

            MeshCollider meshCollider = sceneObject.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                if (meshCollider.sharedMesh == meshFilter.sharedMesh)
                    continue;

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                continue;
            }

            if (sceneObject.GetComponent<Collider>() != null)
                continue;

            meshCollider = sceneObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
            meshCollider.isTrigger = false;
        }
    }

    private void EnsureCheckpointsLoaded()
    {
        if (checkpointComponents.Count >= 2)
            return;

        FindGeneratedCheckpoints();
        if (checkpointComponents.Count >= 2)
            return;

        LoadOrBuildTrack();
    }

    private void FindGeneratedCheckpoints()
    {
        checkpoints.Clear();
        checkpointComponents.Clear();
        checkpointPositions.Clear();

        RaceCheckpoint[] foundCheckpoints = FindObjectsOfType<RaceCheckpoint>();

        System.Array.Sort(foundCheckpoints, (a, b) =>
            a.CheckpointIndex.CompareTo(b.CheckpointIndex));

        foreach (RaceCheckpoint checkpoint in foundCheckpoints)
        {
            if (checkpoint != null)
            {
                checkpoints.Add(checkpoint.transform);
                checkpointComponents.Add(checkpoint);
                checkpointPositions.Add(checkpoint.transform.position);
            }
        }
    }

    private void SetupDroneAtStart()
    {
        timer = 0f;
        timerRunning = false;
        raceFinished = false;
        isInCrashPenalty = false;

        lastClearedCheckpointIndex = 0;
        currentCheckpointIndex = 1;
        RefreshCheckpointVisuals();

        MoveDroneToCheckpoint(0);
    }

    private IEnumerator StartCountdown()
    {
        if (travelScript != null)
            travelScript.canMove = false;

        float count = startCountdownTime;

        while (count > 0)
        {
            raceAudio?.PlayCountdownTick(Mathf.CeilToInt(count));
            SetCountdownOverlay(Mathf.CeilToInt(count).ToString(), new Color(1f, 0.95f, 0.7f));

            yield return new WaitForSeconds(1f);
            count--;
        }

        raceAudio?.PlayCountdownGo();
        SetCountdownOverlay("GO!", new Color(0.55f, 1f, 0.65f));

        yield return new WaitForSeconds(1f);

        SetCountdownOverlay(string.Empty, Color.white);

        timerRunning = true;
        raceAudio?.SetEngineActive(true);

        if (travelScript != null)
            travelScript.canMove = true;
    }

    private IEnumerator RestartRaceAfterReset()
    {
        raceReady = false;
        yield return StartCoroutine(StartCountdown());
        raceReady = true;
    }

    private void CheckCheckpointProgress()
    {
        if (currentCheckpointIndex >= checkpoints.Count)
            return;

        Transform targetCheckpoint = checkpoints[currentCheckpointIndex];

        if (targetCheckpoint == null)
            return;

        float distance = Vector3.Distance(drone.position, targetCheckpoint.position);

        if (distance <= checkpointReachRadius)
        {
            lastClearedCheckpointIndex = currentCheckpointIndex;
            currentCheckpointIndex++;
            RefreshCheckpointVisuals();

            if (currentCheckpointIndex >= checkpoints.Count)
            {
                FinishRace();
                return;
            }

            raceAudio?.PlayCheckpoint();
            currentMessage = "Checkpoint reached!";

            StartCoroutine(ClearMessageAfterDelay(1f));
        }
    }

    private void HandleDroneTriggerEntered(Collider other)
    {
        if (other == null || raceFinished || isInCrashPenalty || !raceReady || !timerRunning)
            return;

        // Ignore checkpoints
        if (other.GetComponent<RaceCheckpoint>() != null)
            return;

        // Ignore self collision
        if (other.transform.root == drone.root)
            return;

        OnCrash();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || raceFinished || isInCrashPenalty || !raceReady || !timerRunning)
            return;

        if (collision.transform.root == drone.root)
            return;

        OnCrash();
    }

    public void OnCrash()
    {
        if (raceFinished || isInCrashPenalty || !raceReady || !timerRunning)
            return;

        StartCoroutine(CrashPenalty());
    }

    private IEnumerator CrashPenalty()
    {
        isInCrashPenalty = true;
        raceAudio?.SetEngineActive(false);
        raceAudio?.PlayCrash();

        if (travelScript != null)
            travelScript.canMove = false;

        MoveDroneToCheckpoint(lastClearedCheckpointIndex);

        float count = crashPenaltyTime;

        while (count > 0)
        {
            currentMessage = "Crash! Restarting in " + Mathf.CeilToInt(count);

            yield return new WaitForSeconds(1f);
            count--;
        }

        currentMessage = "";

        if (travelScript != null)
            travelScript.canMove = true;

        isInCrashPenalty = false;
        if (!raceFinished)
            raceAudio?.SetEngineActive(true);
    }

    private void MoveDroneToCheckpoint(int checkpointIndex)
    {
        if (checkpointIndex < 0 || checkpointIndex >= checkpoints.Count)
            return;

        Vector3 checkpointPosition = checkpointPositions[checkpointIndex] + Vector3.up * RespawnHeightOffset;
        drone.position = checkpointPosition;

        int nextIndex = Mathf.Min(checkpointIndex + 1, checkpoints.Count - 1);
        Vector3 flatDirection = checkpointPositions[nextIndex] - checkpointPositions[checkpointIndex];
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude > 0.0001f)
            drone.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
        else
            drone.rotation = Quaternion.identity;

        Rigidbody attachedBody = drone.GetComponent<Rigidbody>();
        if (attachedBody != null)
        {
            attachedBody.linearVelocity = Vector3.zero;
            attachedBody.angularVelocity = Vector3.zero;
        }
    }

    private void FinishRace()
    {
        raceFinished = true;
        timerRunning = false;
        RefreshCheckpointVisuals();
        raceAudio?.SetEngineActive(false);
        raceAudio?.PlayFinish();

        if (travelScript != null)
            travelScript.canMove = false;

        currentMessage = "Finished!";
    }

    private void RefreshCheckpointVisuals()
    {
        for (int index = 0; index < checkpointComponents.Count; index++)
        {
            RaceCheckpoint checkpoint = checkpointComponents[index];
            if (checkpoint == null)
                continue;

            CheckpointVisualState state;
            if (index == 0 || index <= lastClearedCheckpointIndex)
            {
                state = CheckpointVisualState.Completed;
            }
            else if (!raceFinished && index == currentCheckpointIndex)
            {
                state = CheckpointVisualState.Active;
            }
            else
            {
                state = CheckpointVisualState.Pending;
            }

            checkpoint.SetState(state);
        }
    }

    private void LoadOrBuildTrack()
    {
        ClearTrackObjects();
        loadedPreferredTrack = false;

        if (TryLoadTrackFromFile(out List<Vector3> points))
        {
            checkpointPositions.AddRange(points);
            currentMessage = loadedPreferredTrack
                ? "Competition track loaded."
                : "Fallback track loaded.";
        }
        else if (allowProceduralFallback)
        {
            checkpointPositions.AddRange(BuildProceduralTrack());
            currentMessage = "Competition track missing. Using procedural fallback.";
        }
        else
        {
            currentMessage = "Missing required track file.";
            return;
        }

        ConvertTrackCoordinatesToWorldSpace();
        AlignCheckpointHeightsToSurface();
        BuildTrackVisuals();
    }

    private bool TryLoadTrackFromFile(out List<Vector3> points)
    {
        List<string> candidates = new List<string>(EnumerateTrackCandidates());

        foreach (string filePath in candidates)
        {
            if (!Path.GetFileName(filePath).Equals(preferredTrackFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryLoadTrackFile(filePath, out points))
            {
                loadedPreferredTrack = true;
                return true;
            }
        }

        foreach (string filePath in candidates)
        {
            if (!Path.GetFileName(filePath).Equals(fallbackTrackFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryLoadTrackFile(filePath, out points))
                return true;
        }

        foreach (string filePath in candidates)
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.Equals(preferredTrackFileName, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(fallbackTrackFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryLoadTrackFile(filePath, out points))
                return true;
        }

        points = null;
        return false;
    }

    private static bool TryLoadTrackFile(string filePath, out List<Vector3> points)
    {
        points = null;

        if (!File.Exists(filePath))
            return false;

        try
        {
            points = Parse.LoadTrack(filePath, 100);
            return points.Count >= 2;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Unable to load track file '{filePath}': {exception.Message}");
            return false;
        }
    }

    private IEnumerable<string> EnumerateTrackCandidates()
    {
        List<string> candidates = new List<string>();
        HashSet<string> searchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                string normalizedPath = Path.GetFullPath(path);
                if (addedFiles.Add(normalizedPath))
                    candidates.Add(normalizedPath);
            }
            catch (Exception)
            {
            }
        }

        void AddDirectory(string directory, int recursiveDepth = 0)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || !searchedDirectories.Add(directory))
                return;

            AddFile(Path.Combine(directory, preferredTrackFileName));
            AddFile(Path.Combine(directory, fallbackTrackFileName));

            foreach (string filePath in Directory.GetFiles(directory, "*.xyz"))
                AddFile(filePath);

            if (recursiveDepth <= 0)
                return;

            foreach (string subdirectory in Directory.GetDirectories(directory))
                AddDirectory(subdirectory, recursiveDepth - 1);
        }

        void AddDriveRoot(string directory)
        {
            AddDirectory(directory, ExternalTrackSearchDepth);
        }

        void AddExternalMediaCandidates()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Removable)
                        AddDriveRoot(drive.RootDirectory.FullName);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to inspect external drives: {exception.Message}");
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            AddDriveRoot("/Volumes");
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            AddDriveRoot("/media");
            AddDriveRoot("/mnt");
            AddDriveRoot("/run/media");
#endif
        }

        AddDirectory(Application.streamingAssetsPath);
        AddDirectory(Application.persistentDataPath);
        AddDirectory(Path.GetDirectoryName(Application.dataPath));
        AddDirectory(Environment.CurrentDirectory);
        AddExternalMediaCandidates();

        return candidates;
    }

    private List<Vector3> BuildProceduralTrack()
    {
        hasCourseBounds = TryGetSceneBounds(out courseBounds);
        Vector3 center = hasCourseBounds ? courseBounds.center : Vector3.zero;
        float height = hasCourseBounds ? Mathf.Max(courseBounds.extents.y, 8f) : 12f;
        float radiusX = hasCourseBounds ? Mathf.Max(courseBounds.extents.x * 1.6f, 18f) : 22f;
        float radiusZ = hasCourseBounds ? Mathf.Max(courseBounds.extents.z * 1.6f, 18f) : 22f;
        float baseHeight = hasCourseBounds ? Mathf.Max(center.y + courseBounds.extents.y * 0.55f, 6f) : 8f;

        List<Vector3> positions = new List<Vector3>(8);
        for (int index = 0; index < 8; index++)
        {
            float angle = (Mathf.PI * 2f * index / 8f) + (Mathf.PI * 0.2f);
            float x = Mathf.Cos(angle) * radiusX;
            float z = Mathf.Sin(angle) * radiusZ;
            float yOffset = Mathf.Sin(angle * 2f) * (height * 0.25f) + ((index % 2 == 0) ? 2f : -1.5f);
            positions.Add(center + new Vector3(x, baseHeight + yOffset, z));
        }

        return positions;
    }

    private void ConvertTrackCoordinatesToWorldSpace()
    {
        if (checkpointPositions.Count == 0 || !interpretTrackCoordinatesAsModelLocal)
            return;

        Transform trackAnchor = FindTrackAnchor();
        if (trackAnchor == null)
        {
            Debug.LogWarning($"Track anchor '{trackAnchorObjectName}' was not found. Using track coordinates as world positions.");
            return;
        }

        for (int index = 0; index < checkpointPositions.Count; index++)
        {
            Vector3 localPosition = checkpointPositions[index] * ImportedTrackCoordinateScale;
            checkpointPositions[index] = trackAnchor.TransformPoint(localPosition);
        }
    }

    private void AlignCheckpointHeightsToSurface()
    {
        if (!snapCheckpointHeightToSurface || checkpointPositions.Count == 0)
            return;

        hasCourseBounds = TryGetSceneBounds(out courseBounds);
        if (!hasCourseBounds)
            return;

        for (int index = 0; index < checkpointPositions.Count; index++)
        {
            Vector3 checkpointPosition = checkpointPositions[index];
            Vector3 probeOrigin = new Vector3(checkpointPosition.x, courseBounds.max.y + SurfaceProbePadding, checkpointPosition.z);
            if (TryProjectCheckpointHeight(probeOrigin, out Vector3 groundedPosition))
                checkpointPositions[index] = groundedPosition;
        }
    }

    private Transform FindTrackAnchor()
    {
        foreach (Transform sceneTransform in FindObjectsOfType<Transform>(true))
        {
            if (sceneTransform != null &&
                sceneTransform.name.Equals(trackAnchorObjectName, StringComparison.OrdinalIgnoreCase))
                return sceneTransform;
        }

        return null;
    }

    private bool TryProjectCheckpointHeight(Vector3 probeOrigin, out Vector3 groundedPosition)
    {
        float rayLength = (courseBounds.size.y + SurfaceProbePadding * 2f) + 1f;
        RaycastHit[] hits = Physics.RaycastAll(probeOrigin, Vector3.down, rayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        Array.Sort(hits, (left, right) => right.point.y.CompareTo(left.point.y));

        foreach (RaycastHit hit in hits)
        {
            if (!IsValidSurfaceHit(hit))
                continue;

            groundedPosition = hit.point + Vector3.up * CheckpointSurfaceOffset;
            return true;
        }

        groundedPosition = default;
        return false;
    }

    private bool IsValidSurfaceHit(RaycastHit hit)
    {
        if (hit.collider == null || hit.collider.isTrigger)
            return false;

        Transform hitTransform = hit.collider.transform;
        if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(drone))
            return false;

        return true;
    }

    private void BuildTrackVisuals()
    {
        checkpoints.Clear();
        checkpointComponents.Clear();

        hasCourseBounds = TryGetSceneBounds(out courseBounds);

        for (int index = 0; index < checkpointPositions.Count; index++)
        {
            Vector3 previousPosition = index > 0 ? checkpointPositions[index - 1] : checkpointPositions[index];
            Vector3 facingPosition = checkpointPositions[Mathf.Min(index + 1, checkpointPositions.Count - 1)];
            if (index == checkpointPositions.Count - 1)
                facingPosition = checkpointPositions[index];

            RaceCheckpoint checkpoint = CreateCheckpoint(index, checkpointPositions[index], previousPosition, facingPosition);
            checkpoints.Add(checkpoint.transform);
            checkpointComponents.Add(checkpoint);
        }
    }

    private RaceCheckpoint CreateCheckpoint(int index, Vector3 position, Vector3 previousPosition, Vector3 facingPosition)
    {
        GameObject checkpointRoot = new GameObject($"Checkpoint {index + 1}");
        checkpointRoot.transform.SetParent(transform, false);
        checkpointRoot.transform.position = position;
        checkpointRoot.transform.rotation = Quaternion.LookRotation(
            (facingPosition - position).sqrMagnitude > 0.001f ? facingPosition - position : Vector3.forward,
            Vector3.up);

        SphereCollider trigger = checkpointRoot.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = checkpointReachRadius;

        RaceCheckpoint checkpoint = checkpointRoot.AddComponent<RaceCheckpoint>();
        checkpoint.Initialize(index);
        checkpoint.CacheRenderers(previousPosition);
        checkpoint.SetState(CheckpointVisualState.Pending);
        return checkpoint;
    }

    private void ClearTrackObjects()
    {
        foreach (RaceCheckpoint checkpoint in checkpointComponents)
        {
            if (checkpoint != null)
                Destroy(checkpoint.gameObject);
        }

        checkpoints.Clear();
        checkpointComponents.Clear();
        checkpointPositions.Clear();
    }

    private bool TryGetSceneBounds(out Bounds bounds)
    {
        Renderer[] renderers = FindObjectsOfType<Renderer>();
        bool foundBounds = false;
        bounds = default;

        foreach (Renderer sceneRenderer in renderers)
        {
            if (!sceneRenderer.enabled || sceneRenderer.transform.IsChildOf(transform) || sceneRenderer.transform.IsChildOf(drone))
                continue;

            if (sceneRenderer is TrailRenderer)
                continue;

            if (!foundBounds)
            {
                bounds = sceneRenderer.bounds;
                foundBounds = true;
            }
            else
            {
                bounds.Encapsulate(sceneRenderer.bounds);
            }
        }

        return foundBounds;
    }

    private IEnumerator ClearMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (!raceFinished && !isInCrashPenalty)
            currentMessage = "";
    }

    private void UpdateGameplayUI()
    {
        if (gameplayText == null)
            return;

        string timerString = "Time: " + FormatTime(timer);
        int checkpointsPassed = checkpoints.Count == 0
            ? 0
            : Mathf.Clamp(lastClearedCheckpointIndex + 1, 0, checkpoints.Count);

        string checkpointString;
        string distanceString;

        if (raceFinished)
        {
            checkpointString =
                "Checkpoints: " +
                checkpoints.Count + "/" + checkpoints.Count;
            distanceString = "Distance: 0.0m";
        }
        else
        {
            float distanceToNextCheckpoint = GetDistanceToNextCheckpoint();

            checkpointString =
                "Checkpoints: " +
                checkpointsPassed +
                "/" +
                checkpoints.Count +
                "\nNext: " +
                (currentCheckpointIndex + 1);
            distanceString = "Distance: " + distanceToNextCheckpoint.ToString("0.0") + "m";
        }

        gameplayText.text =
            timerString +
            "\n" +
            checkpointString +
            "\n" +
            distanceString +
            "\n" +
            currentMessage;
    }

    private void ConfigureGameplayHud()
    {
        if (gameplayText == null)
            return;

        if (gameplayText is TextMeshProUGUI uiText)
        {
            RectTransform textRect = uiText.rectTransform;
            textRect.anchorMin = new Vector2(0.5f, 1f);
            textRect.anchorMax = new Vector2(0.5f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -12f);
            textRect.sizeDelta = new Vector2(720f, 220f);

            uiText.fontSize = 40f;
            uiText.alignment = TextAlignmentOptions.Top;
            uiText.enableWordWrapping = false;

            Canvas hudCanvas = uiText.GetComponentInParent<Canvas>();
            if (hudCanvas != null)
            {
                RectTransform canvasRect = hudCanvas.GetComponent<RectTransform>();
                canvasRect.localPosition = new Vector3(0f, 0.34f, 1.2f);
                canvasRect.localRotation = Quaternion.identity;
                canvasRect.localScale = new Vector3(0.0015f, 0.0015f, 0.0015f);
                canvasRect.sizeDelta = new Vector2(760f, 240f);
            }
        }
    }

    private void EnsureCountdownOverlay()
    {
        if (countdownText != null)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Transform existing = mainCamera.transform.Find(CountdownOverlayName);
        if (existing != null)
        {
            countdownText = existing.GetComponentInChildren<TMP_Text>(true);
            return;
        }

        GameObject countdownCanvasObject = new GameObject(CountdownOverlayName);
        countdownCanvasObject.transform.SetParent(mainCamera.transform, false);
        countdownCanvasObject.transform.localPosition = new Vector3(0f, 0f, 1.1f);
        countdownCanvasObject.transform.localRotation = Quaternion.identity;
        countdownCanvasObject.transform.localScale = new Vector3(0.0022f, 0.0022f, 0.0022f);

        Canvas countdownCanvas = countdownCanvasObject.AddComponent<Canvas>();
        countdownCanvas.renderMode = RenderMode.WorldSpace;
        countdownCanvas.worldCamera = mainCamera;

        CanvasScaler scaler = countdownCanvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 20f;

        countdownCanvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = countdownCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1200f, 600f);

        GameObject countdownTextObject = new GameObject("Countdown Text");
        countdownTextObject.transform.SetParent(countdownCanvasObject.transform, false);

        TextMeshProUGUI countdownUiText = countdownTextObject.AddComponent<TextMeshProUGUI>();
        countdownUiText.font = TMP_Settings.defaultFontAsset;
        countdownUiText.alignment = TextAlignmentOptions.Center;
        countdownUiText.fontSize = 220f;
        countdownUiText.color = new Color(1f, 0.95f, 0.7f);
        countdownUiText.enableWordWrapping = false;
        countdownUiText.overflowMode = TextOverflowModes.Overflow;
        countdownUiText.text = string.Empty;

        RectTransform textRect = countdownUiText.rectTransform;
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(1200f, 600f);

        countdownText = countdownUiText;
    }

    private void SetCountdownOverlay(string message, Color color)
    {
        EnsureCountdownOverlay();

        if (countdownText == null)
            return;

        countdownText.text = message;
        countdownText.color = color;
        countdownText.fontSize = message == "GO!" ? 260f : 220f;
        countdownText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private float GetDistanceToNextCheckpoint()
    {
        if (drone == null || currentCheckpointIndex < 0 || currentCheckpointIndex >= checkpoints.Count)
            return 0f;

        Transform nextCheckpoint = checkpoints[currentCheckpointIndex];
        if (nextCheckpoint == null)
            return 0f;

        return Vector3.Distance(drone.position, nextCheckpoint.position);
    }

    private TMP_Text FindVisibleGameplayText()
    {
        TMP_Text[] texts = FindObjectsOfType<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            if (text == null)
                continue;

            if (text.name == VisibleGameplayTextName)
                return text;
        }

        return null;
    }

    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        int milliseconds = Mathf.FloorToInt((time * 100f) % 100f);

        return minutes.ToString("00") + ":" +
               seconds.ToString("00") + "." +
               milliseconds.ToString("00");
    }
}
