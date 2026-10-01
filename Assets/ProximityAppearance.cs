using UnityEngine;

public class ProximityAppearance : MonoBehaviour
{
    // get object upon the plinth of this exhibit
    public GameObject plinthObject;

    // define variables for this exhibit and player locations
    private Transform exhibitLocation;
    private Transform playerCamera;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        exhibitLocation = transform;
        playerCamera = GameObject.FindWithTag("MainCamera").transform;
    }

    // Update is called once per frame
    void Update()
    {
        float distance = Vector3.Distance(exhibitLocation.position, playerCamera.position);

        plinthObject.SetActive(distance < 3f);
    }
}
