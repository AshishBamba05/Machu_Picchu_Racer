using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Gameplay : MonoBehaviour
{
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

    private readonly List<Transform> checkpoints = new List<Transform>();

    private int currentCheckpointIndex = 1;
    private int lastClearedCheckpointIndex = 0;

    private float timer = 0f;

    private bool timerRunning = false;
    private bool raceFinished = false;
    private bool isInCrashPenalty = false;
    private bool raceReady = false;

    private string currentMessage = "";

    private void Start()
    {
        if (drone == null)
            drone = transform;

        if (travelScript == null)
            travelScript = drone.GetComponent<Travel>();

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

        yield return StartCoroutine(StartCountdown());

        raceReady = true;
    }

    private void FindGeneratedCheckpoints()
    {
        checkpoints.Clear();

        RaceCheckpoint[] foundCheckpoints = FindObjectsOfType<RaceCheckpoint>();

        System.Array.Sort(foundCheckpoints, (a, b) =>
            a.CheckpointIndex.CompareTo(b.CheckpointIndex));

        foreach (RaceCheckpoint checkpoint in foundCheckpoints)
        {
            if (checkpoint != null)
                checkpoints.Add(checkpoint.transform);
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

        MoveDroneToCheckpoint(0);
    }

    private IEnumerator StartCountdown()
    {
        if (travelScript != null)
            travelScript.canMove = false;

        float count = startCountdownTime;

        while (count > 0)
        {
            currentMessage = "Starting in " + Mathf.CeilToInt(count);

            yield return new WaitForSeconds(1f);
            count--;
        }

        currentMessage = "GO!";

        yield return new WaitForSeconds(1f);

        currentMessage = "";

        timerRunning = true;

        if (travelScript != null)
            travelScript.canMove = true;
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

            if (currentCheckpointIndex >= checkpoints.Count)
            {
                FinishRace();
                return;
            }

            currentMessage = "Checkpoint reached!";

            StartCoroutine(ClearMessageAfterDelay(1f));
        }
    }

    private void HandleDroneTriggerEntered(Collider other)
    {
        if (other == null || raceFinished || isInCrashPenalty)
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
        if (collision == null || raceFinished || isInCrashPenalty)
            return;

        if (collision.transform.root == drone.root)
            return;

        OnCrash();
    }

    public void OnCrash()
    {
        if (raceFinished || isInCrashPenalty)
            return;

        StartCoroutine(CrashPenalty());
    }

    private IEnumerator CrashPenalty()
    {
        isInCrashPenalty = true;

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
        raceFinished = true;
        timerRunning = false;

        if (travelScript != null)
            travelScript.canMove = false;

        currentMessage = "Finished!";
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

        string checkpointString;

        if (raceFinished)
        {
            checkpointString =
                "Checkpoints: " +
                checkpoints.Count + "/" + checkpoints.Count;
        }
        else
        {
            checkpointString =
                "Checkpoints: " +
                (lastClearedCheckpointIndex + 1) +
                "/" +
                checkpoints.Count +
                "\nNext: " +
                (currentCheckpointIndex + 1);
        }

        gameplayText.text =
            timerString +
            "\n" +
            checkpointString +
            "\n" +
            currentMessage;
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