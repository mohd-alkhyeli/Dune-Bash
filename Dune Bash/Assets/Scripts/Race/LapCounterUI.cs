using TMPro;
using UnityEngine;

/// <summary>
/// Displays a vehicle's lap progress as plain text (e.g. "Lap 2 / 3") and reveals a win banner once
/// the race is won.
/// </summary>
public class LapCounterUI : MonoBehaviour
{
    [Tooltip("The vehicle whose lap progress this readout displays.")]
    public LapTracker lapTracker;
    [Tooltip("TMP text element showing the current lap, e.g. \"Lap 2 / 3\".")]
    public TMP_Text lapText;
    [Tooltip("TMP text element showing this vehicle's live race position among all active LapTrackers, e.g. \"1st\". Leave unassigned to skip.")]
    public TMP_Text placeText;
    [Tooltip("TMP text element shown only once the race is won. Leave unassigned to skip a win banner.")]
    public TMP_Text winText;

    void Awake()
    {
        if (winText != null) winText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (lapTracker == null) return;

        if (lapText != null)
        {
            int displayLap = Mathf.Min(lapTracker.LapCount + 1, lapTracker.lapsToWin);
            lapText.text = $"Lap {displayLap} / {lapTracker.lapsToWin}";
        }

        if (placeText != null)
        {
            int place = RaceManager.GetStandings().IndexOf(lapTracker) + 1;
            placeText.text = ToOrdinal(place);
        }

        if (winText != null) winText.gameObject.SetActive(lapTracker.HasWon);
    }

    static string ToOrdinal(int place)
    {
        if (place <= 0) return "-";
        int rem100 = place % 100;
        if (rem100 >= 11 && rem100 <= 13) return place + "th";
        switch (place % 10)
        {
            case 1: return place + "st";
            case 2: return place + "nd";
            case 3: return place + "rd";
            default: return place + "th";
        }
    }
}
