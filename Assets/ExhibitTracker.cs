using System.Text;
using TMPro;
using UnityEngine;

public class ExhibitTracker : MonoBehaviour
{
    [System.Serializable]
    public class Exhibit
    {
        public string label;
        public Transform location;
        [HideInInspector] public bool visited;
    }

    // every exhibit the player needs to visit, in museum order
    public Exhibit[] exhibits;

    // same range ProximityAppearance uses to show the console
    public float visitDistance = 3f;

    // checklist board in the courtyard
    public TextMeshPro checklistText;

    // short message that floats in front of the player after each new visit
    public TextMeshPro popupText;
    public float popupSeconds = 3f;

    // award on the courtyard podium, hidden until every exhibit is visited
    public GameObject award;
    public Transform awardTrophy;
    public Transform awardLabel;
    public float spinSpeed = 45f;

    private Transform playerCamera;
    private int visitedCount;
    private float popupTimer;

    void Start()
    {
        playerCamera = GameObject.FindWithTag("MainCamera").transform;
        award.SetActive(false);
        popupText.gameObject.SetActive(false);
        UpdateChecklist();
    }

    void Update()
    {
        foreach (var exhibit in exhibits)
        {
            if (exhibit.visited || Vector3.Distance(exhibit.location.position, playerCamera.position) >= visitDistance)
                continue;

            exhibit.visited = true;
            visitedCount++;
            UpdateChecklist();

            if (visitedCount == exhibits.Length)
            {
                award.SetActive(true);
                ShowPopup("<b>All exhibits visited!</b>\n<size=70%>Your award is waiting in the courtyard</size>");
            }
            else
            {
                ShowPopup($"<b>Exhibit visited!</b>\n<size=70%>{visitedCount} / {exhibits.Length}</size>");
            }
        }

        if (popupTimer > 0f)
        {
            popupTimer -= Time.deltaTime;
            // keep the message in front of the player's face
            var target = playerCamera.position + playerCamera.forward * 1.5f;
            popupText.transform.position = Vector3.Lerp(popupText.transform.position, target, 8f * Time.deltaTime);
            popupText.transform.rotation = Quaternion.LookRotation(popupText.transform.position - playerCamera.position);
            if (popupTimer <= 0f)
                popupText.gameObject.SetActive(false);
        }

        if (award.activeSelf)
        {
            awardTrophy.Rotate(0f, spinSpeed * Time.deltaTime, 0f);
            // turn the label toward the player so it can be read from anywhere in the courtyard
            var away = awardLabel.position - playerCamera.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f)
                awardLabel.rotation = Quaternion.LookRotation(away);
        }
    }

    void ShowPopup(string message)
    {
        popupText.text = message;
        popupText.transform.position = playerCamera.position + playerCamera.forward * 1.5f;
        popupText.gameObject.SetActive(true);
        popupTimer = popupSeconds;
    }

    void UpdateChecklist()
    {
        var sb = new StringBuilder();
        sb.Append("<align=center><b>MUSEUM PASSPORT</b>\n");
        if (visitedCount == exhibits.Length)
            sb.Append("<color=#FFC433><size=70%>All exhibits visited! Your award is on the podium.</size></color>\n");
        else
            sb.Append("<size=70%>Visit every exhibit to unlock the award</size>\n");
        sb.Append($"<size=80%>{visitedCount} / {exhibits.Length} visited</size></align>\n\n");

        foreach (var exhibit in exhibits)
        {
            if (exhibit.visited)
                sb.Append($"<color=#6EE07A>[X]  {exhibit.label}</color>\n");
            else
                sb.Append($"<color=#9AA0A8>[  ]  {exhibit.label}</color>\n");
        }

        checklistText.text = sb.ToString();
    }
}
