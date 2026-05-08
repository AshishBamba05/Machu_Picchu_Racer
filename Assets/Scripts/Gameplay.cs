using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Gameplay : MonoBehaviour
{
    private const float MetersToFeet = 3.28084f;

    private const string VisibleGameplayTextName = "Text (TMP)";
    private const string CountdownOverlayName = "Countdown Overlay Canvas";

    [Header("References")]
    public Travel travelScript;
    public Transform drone;

    [Header("First Person View")]
    public GameObject droneVisualRoot;
    public bool hideDroneVisual = true;

    [Header("Single UI Text")]
    public TMP_Text gameplayText;

    [Header("Ghost Champion")]
    [SerializeField] private bool enableGhostChampionMode = true;
    [SerializeField] private bool enableGhostDebugLogs = true;

    [Header("Gameplay Settings")]
    public float startCountdownTime = 3f;
    public float crashPenaltyTime = 3f;

    // 30 feet = 9.144 meters
    public float checkpointReachRadius = 9.144f;

    private readonly List<Transform> checkpoints = new List<Transform>();
    private readonly List<RaceCheckpoint> checkpointComponents = new List<RaceCheckpoint>();

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
    private GhostChampion ghostChampion;

    private void Start()
    {
        if (drone == null)
            drone = transform;

        if (gameplayText == null)
            gameplayText = FindVisibleGameplayText();

        ghostChampion = GetComponent<GhostChampion>();
        if (ghostChampion == null && enableGhostChampionMode)
            ghostChampion = gameObject.AddComponent<GhostChampion>();

        if (ghostChampion != null)
            ghostChampion.playerDrone = drone;

        LogGhostDebug($"Ghost champion present={ghostChampion != null}, enabled={enableGhostChampionMode}.");

        ConfigureGameplayHud();
        EnsureCountdownOverlay();

        if (travelScript == null)
            travelScript = drone.GetComponent<Travel>();

        raceAudio = drone.GetComponent<DroneRaceAudio>();
        if (raceAudio == null)
            raceAudio = drone.gameObject.AddComponent<DroneRaceAudio>();

        raceAudio.Initialize(drone);
        raceAudio.SetEngineActive(false);

        if (travelScript != null)
        {
            travelScript.canMove = false;
            travelScript.TriggerEntered += HandleDroneTriggerEntered;
        }

        if (hideDroneVisual && droneVisualRoot != null)
            droneVisualRoot.SetActive(false);

        StartCoroutine(WaitForCheckpointsThenStart());
    }

    private void Update()
    {
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

        while (checkpoints.Count < 2)
        {
            FindGeneratedCheckpoints();

            if (checkpoints.Count >= 2)
                break;

            yield return null;
        }

        SetupDroneAtStart();
        currentMessage = "";

        yield return StartCoroutine(StartCountdown());

        currentMessage = "";
        raceReady = true;
    }

    private void FindGeneratedCheckpoints()
    {
        checkpoints.Clear();
        checkpointComponents.Clear();

        RaceCheckpoint[] foundCheckpoints = FindObjectsOfType<RaceCheckpoint>();

        System.Array.Sort(foundCheckpoints, (a, b) =>
            a.CheckpointIndex.CompareTo(b.CheckpointIndex));

        foreach (RaceCheckpoint checkpoint in foundCheckpoints)
        {
            if (checkpoint != null)
            {
                checkpoints.Add(checkpoint.transform);
                checkpointComponents.Add(checkpoint);
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
        ghostChampion?.ResetRunState();

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

        if (enableGhostChampionMode)
        {
            LogGhostDebug("Countdown finished. Starting ghost replay and recording.");
            ghostChampion?.StartGhostReplay();
            ghostChampion?.StartRecording();
        }
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

        Transform checkpoint = checkpoints[checkpointIndex];

        if (checkpoint == null)
            return;

        drone.position = checkpoint.position;

        int nextIndex = Mathf.Min(checkpointIndex + 1, checkpoints.Count - 1);
        Transform nextCheckpoint = checkpoints[nextIndex];

        if (nextCheckpoint == null)
            return;

        Vector3 flatDirection = nextCheckpoint.position - checkpoint.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude > 0.0001f)
            drone.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
        else
            drone.rotation = Quaternion.identity;
    }

    private void FinishRace()
    {
        float finalRaceTime = timer;
        LogGhostDebug($"FinishRace reached with time={finalRaceTime:0.000}s.");
        raceFinished = true;
        timerRunning = false;
        RefreshCheckpointVisuals();
        raceAudio?.SetEngineActive(false);
        raceAudio?.PlayFinish();
        ghostChampion?.StopRecordingAndSaveIfBest(finalRaceTime);
        ghostChampion?.StopGhostReplay();

        if (travelScript != null)
            travelScript.canMove = false;

        currentMessage = GetGhostFinishMessage();
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
            distanceString = "Distance: 0.0ft";
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
            distanceString = "Distance: " + (distanceToNextCheckpoint * MetersToFeet).ToString("0.0") + "ft";
        }

        gameplayText.text =
            timerString +
            "\n" +
            checkpointString +
            "\n" +
            distanceString +
            "\n" +
            GetGhostDisplayLine() +
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

    private string GetGhostDisplayLine()
    {
        if (!enableGhostChampionMode || ghostChampion == null)
            return "Recording first best run";

        return ghostChampion.TryGetBestRunTime(out float bestTime)
            ? "Best ghost: " + FormatTime(bestTime)
            : "Recording first best run";
    }

    private string GetGhostFinishMessage()
    {
        if (!enableGhostChampionMode || ghostChampion == null)
            return "Finished!";

        return ghostChampion.WasLastRunNewBest()
            ? "Congrats new record"
            : "Fail to beat best record";
    }

    private void LogGhostDebug(string message)
    {
        if (!enableGhostDebugLogs)
            return;

        Debug.Log($"[Gameplay Ghost] {message}", this);
    }
}
