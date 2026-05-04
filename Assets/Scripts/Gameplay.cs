using System.Collections;
using UnityEngine;
using TMPro;

public class Gameplay : MonoBehaviour
{
    [Header("References")]
    public Travel travelScript;
    public Transform drone;

    [Header("UI")]
    public TMP_Text timerText;
    public TMP_Text messageText;

    [Header("Gameplay Settings")]
    public float startCountdownTime = 3f;
    public float crashPenaltyTime = 3f;

    [Header("Checkpoint (from partner)")]
    public Transform firstCheckpoint;
    public Transform secondCheckpoint;
    public Vector3 lastClearedCheckpointPosition;

    private float timer = 0f;
    private bool timerRunning = false;
    private bool raceFinished = false;
    private bool isInPenalty = false;

    void Start()
    {
        // Disable movement at start
        if (travelScript != null)
            travelScript.canMove = false;

        // Set starting position
        if (firstCheckpoint != null)
        {
            drone.position = firstCheckpoint.position;
            lastClearedCheckpointPosition = firstCheckpoint.position;
        }

        // Face second checkpoint
        if (secondCheckpoint != null)
        {
            Vector3 dir = (secondCheckpoint.position - drone.position).normalized;
            dir.y = 0f;
            if (dir != Vector3.zero)
                drone.rotation = Quaternion.LookRotation(dir);
        }

        StartCoroutine(StartCountdown());
    }

    void Update()
    {
        // Timer runs continuously after start
        if (timerRunning && !raceFinished)
        {
            timer += Time.deltaTime;
            UpdateTimerUI();
        }
    }

    // =========================
    // START COUNTDOWN
    // =========================
    IEnumerator StartCountdown()
    {
        float count = startCountdownTime;

        while (count > 0)
        {
            messageText.text = Mathf.Ceil(count).ToString();
            yield return new WaitForSeconds(1f);
            count--;
        }

        messageText.text = "GO!";
        yield return new WaitForSeconds(1f);

        messageText.text = "";

        if (travelScript != null)
            travelScript.canMove = true;

        timerRunning = true;
    }

    // =========================
    // CRASH HANDLING
    // =========================
    public void OnCrash()
    {
        if (isInPenalty || raceFinished)
            return;

        StartCoroutine(CrashPenalty());
    }

    IEnumerator CrashPenalty()
    {
        isInPenalty = true;

        // Disable movement
        if (travelScript != null)
            travelScript.canMove = false;

        // Reset position
        drone.position = lastClearedCheckpointPosition;

        // Show penalty countdown
        float count = crashPenaltyTime;

        while (count > 0)
        {
            messageText.text = "Crash! " + Mathf.Ceil(count);
            yield return new WaitForSeconds(1f);
            count--;
        }

        messageText.text = "";

        // Re-enable movement
        if (travelScript != null)
            travelScript.canMove = true;

        isInPenalty = false;
    }

    // =========================
    // CALLED BY PARTNER SCRIPT
    // =========================
    public void UpdateCheckpoint(Vector3 newCheckpointPosition)
    {
        lastClearedCheckpointPosition = newCheckpointPosition;
    }

    public void FinishRace()
    {
        raceFinished = true;
        timerRunning = false;

        messageText.text = "Finished!";
    }

    // =========================
    // TIMER UI
    // =========================
    void UpdateTimerUI()
    {
        int minutes = Mathf.FloorToInt(timer / 60);
        int seconds = Mathf.FloorToInt(timer % 60);

        timerText.text = minutes.ToString("00") + ":" + seconds.ToString("00");
    }
}