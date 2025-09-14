using System.Collections;
using UnityEngine;
using UnityEngine.UI; // For UI elements
using TMPro;
public class PlaneController : MonoBehaviour
{
    public float throttleIncrement = 0.5f; // Increment for throttle adjustment
    public float maxThrust = 2000f; // Maximum thrust of the plane
    public float responsiveness = 0.5f;
    public float maxGlideSpeed = 100f; // Maximum glide speed of the plane
    public float extraforce = 15f; 

    public float throttle;
    public float yaw;   // Yaw angle of the plane
    public float pitch; // Pitch angle of the plane
    public float roll;  // Roll angle of the plane
    public float lift = 200000f; // Lift force of the plane

    public float maxFuel = 100f; // Maximum fuel level
    private float fuel;          // Current fuel level
    public float fuelConsumptionRate = 0.1f; // Fuel consumption rate per throttle usage

    public float responseModifier
    {
        get
        {
            return (rb.mass / 5000f) * responsiveness; // Calculate response modifier based on mass and responsiveness
        }
    }

    private Rigidbody rb; // Rigidbody component of the plane
    [SerializeField] private TextMeshProUGUI hud; // UI text to display throttle value

    // References to the three sphere colliders
    [SerializeField] private SphereCollider sphereCollider1;
    [SerializeField] private SphereCollider sphereCollider2;
    [SerializeField] private SphereCollider sphereCollider3;

    // UI Elements for fuel
    [SerializeField] private Slider fuelSlider; // Slider to represent fuel level
    [SerializeField] private Image fuelFillImage; // Image to color-code the fuel level
    [SerializeField] private Gradient fuelColorGradient; // Gradient for fuel color

    // Engine status HUD elements
    [Header("Engine Status HUD")]
    [SerializeField] private Image engineDiagramImage; // The engine diagram image
    [SerializeField] private TextMeshProUGUI engineStatusText; // Status text above the engine diagram
    [SerializeField] private Color engineNormalColor = Color.green;
    [SerializeField] private Color engineWarningColor = Color.yellow;
    [SerializeField] private Color engineCriticalColor = Color.orange;
    [SerializeField] private Color engineOffColor = Color.red;

    private bool collidersEnabled = true; // State of the colliders
    private bool altitudeHold = false;    // Toggle for altitude hold
    private bool isLanding = false;       // Toggle for landing logic
    private bool hasFuel = true;       // Whether the plane has fuel

    private float lastAltitude;           // For glide calculation

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        fuel = maxFuel; // Initialize fuel to max
        lastAltitude = transform.position.y;
    }

    public void HandleInputs()
    {
        yaw = Input.GetAxis("Yaw");
        pitch = Input.GetAxis("Pitch");
        roll = Input.GetAxis("Roll");

        if (fuel <= 0f)
        {
            hasFuel = false; // No fuel left
            throttle = 0f; // Reset throttle if no fuel
        }
        else
        {
            hasFuel = true; // Fuel is available
        }

        // Throttle input only works if fuel > 0
        if (hasFuel && fuel > 0f)
        {
            if (Input.GetKey(KeyCode.Space))
                throttle += throttleIncrement;
            else if (Input.GetKey(KeyCode.LeftControl))
                throttle -= throttleIncrement;
        }

        // Gradually reduce throttle if fuel is low
        float fuelPercent = fuel / maxFuel;
        if (fuelPercent <= 0.2f)
        {
            // Increase the rate at which throttle decreases when fuel is low
            float targetThrottle = Mathf.Lerp(0f, maxThrust, fuelPercent / 0.2f);
            float lowFuelThrottleDecrement = throttleIncrement * 10f; // 2x faster reduction
            throttle = Mathf.MoveTowards(throttle, targetThrottle, lowFuelThrottleDecrement);
        }

        // Clamp throttle between 0 and maxThrust
        throttle = Mathf.Clamp(throttle, 0f, maxThrust);

        if (Input.GetKeyDown(KeyCode.L))
        {
            collidersEnabled = !collidersEnabled;
            SetCollidersEnabled(collidersEnabled);
        }

        // Toggle altitude hold with J key
        if (Input.GetKeyDown(KeyCode.J))
        {
            altitudeHold = !altitudeHold;
        }

        // Toggle landing logic with K key
        if (Input.GetKeyDown(KeyCode.K))
        {
            isLanding = !isLanding;
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (sphereCollider1 != null) sphereCollider1.enabled = enabled;
        if (sphereCollider2 != null) sphereCollider2.enabled = enabled;
        if (sphereCollider3 != null) sphereCollider3.enabled = enabled;
    }

    private void Update()
    {
        HandleInputs();
        UpdateFuel(); // Update fuel levels

        // Disable altitude hold if fuel is 0
        if (fuel <= 20f && altitudeHold)
        {
            altitudeHold = false;
        }
        if (fuel == 0)
        {
            rb.AddForce(Vector3.down * rb.mass * Physics.gravity.magnitude * extraforce); // Apply gravity force when out of fuel
        }

        UpdateEngineHUD(); // Update engine status HUD
        UpdateHUD();  // Update the HUD with current values
    }

    private void FixedUpdate()
    {
        // Apply forces based on throttle, yaw, pitch, and roll
        rb.AddForce(transform.forward * throttle * maxThrust);
        rb.AddTorque(transform.up * yaw * responseModifier);      // Yaw torque
        rb.AddTorque(transform.right * pitch * responseModifier); // Pitch torque
        rb.AddTorque(transform.forward * roll * responseModifier * 2); // Roll torque

        bool isFlying = rb.linearVelocity.magnitude * 3.6f > 100f;

        if (isFlying)
        {
            if (altitudeHold)
            {
                // Cancel out gravity exactly
                rb.AddForce(Vector3.up * rb.mass * Physics.gravity.magnitude, ForceMode.Force);
            }
            else if (fuel > 20f)
            {
                rb.AddForce(Vector3.up * rb.linearVelocity.magnitude * lift); // Apply lift force only if fuel > 0
            }
            // No lift force applied if fuel is 0
        }

        if (isLanding)
        {
            // Apply landing logic here, e.g., reduce throttle or apply brakes
            throttle = Mathf.Max(0f, throttle - throttleIncrement); // Gradually reduce throttle
            rb.AddForce(Vector3.down * rb.mass * Physics.gravity.magnitude, ForceMode.Force); // Apply gravity force
            Debug.Log("Landing in progress, throttle reduced to: " + throttle);
        }
    }

    public float GetHorizontalForwardSpeed(Rigidbody rb, Transform transform)
    {
        // Remove the vertical (Y) component from the velocity
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        // Project the horizontal velocity onto the object's forward direction
        return Vector3.Dot(horizontalVelocity, transform.forward);
    }

    private void UpdateFuel()
    {
        // Decrease fuel based on throttle usage
        if (fuel > 0f)
        {
            fuel -= throttle / maxThrust * fuelConsumptionRate * Time.deltaTime;
            fuel = Mathf.Clamp(fuel, 0f, maxFuel); // Ensure fuel doesn't go below 0
        }

        // Update the fuel slider and color
        if (fuelSlider != null)
        {
            fuelSlider.value = fuel / maxFuel; // Update slider value
        }

        if (fuelFillImage != null && fuelColorGradient != null)
        {
            fuelFillImage.color = fuelColorGradient.Evaluate(fuel / maxFuel); // Update color based on fuel level
        }
    }

    private void UpdateEngineHUD()
    {
        if (engineDiagramImage == null || engineStatusText == null)
            return;

        float fuelPercent = fuel / maxFuel;
        string status = "";
        Color statusColor = engineNormalColor;

        if (fuel <= 0f)
        {
            status = "ENGINE OFF";
            statusColor = engineOffColor;
        }
        else if (fuelPercent <= 0.1f)
        {
            status = "ENGINE CRITICAL";
            statusColor = engineCriticalColor;
        }
        else if (fuelPercent <= 0.4f)
        {
            status = "ENGINE WARNING";
            statusColor = engineWarningColor;
        }
        else
        {
            status = "ENGINE NORMAL";
            statusColor = engineNormalColor;
        }

        engineStatusText.text = status;
        engineDiagramImage.color = statusColor;
    }

    private void UpdateHUD()
    {
        // Update the HUD with the current throttle value
        hud.text = "Throttle: " + (throttle / maxThrust * 100).ToString("F0") + "%\n";
        hud.text += "Airspeed: " + (rb.linearVelocity.magnitude * 3.6f).ToString("F0") + " km/h\n"; // Convert m/s to km/h
        hud.text += "Altitude: " + transform.position.y.ToString("F0") + " m\n"; // Display altitude in meters
        hud.text += "Altitude Hold: " + (altitudeHold ? "ON" : "OFF") + "\n";
        hud.text += "Fuel: " + fuel.ToString("F1") + " / " + maxFuel.ToString("F1");
    }
}
