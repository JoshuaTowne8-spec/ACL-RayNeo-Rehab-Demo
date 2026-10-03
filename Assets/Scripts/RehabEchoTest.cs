using UnityEngine;
using UnityEngine.Serialization;

public enum PressureSensorRequirement
{
    Either = 0,
    Pressure1Right = 1,
    Pressure2Left = 2
}

/// <summary>
/// Completes an echo when the simulated head is close enough and the latest
/// connected ESP32 sample satisfies the pressure and knee-angle thresholds.
/// </summary>
public class RehabEchoTest : MonoBehaviour
{
    public Esp32UdpReceiver receiver;
    [FormerlySerializedAs("simulatedHead")]
    public Transform trackedHead;

    [Header("Test thresholds")]
    [Min(0f)] public float activationDistance = 0.3f;
    public float requiredKneeAngle = 10f;
    public int requiredPressure = 60;
    public PressureSensorRequirement pressureSensor = PressureSensorRequirement.Either;
    public bool requiresKneeAngle = true;

    [Header("Runtime state")]
    public bool playerInRange;
    public bool sensorConditionMet;
    public bool testCompleted;

    private void Update()
    {
        if (receiver == null || trackedHead == null || testCompleted)
        {
            return;
        }

        Vector3 headGroundPosition = trackedHead.position;
        headGroundPosition.y = 0f;

        Vector3 targetGroundPosition = transform.position;
        targetGroundPosition.y = 0f;

        playerInRange = Vector3.Distance(headGroundPosition, targetGroundPosition)
                        <= activationDistance;

        Esp32SensorData data = receiver.latestData ?? new Esp32SensorData();
        sensorConditionMet = receiver.isConnected && IsCollectionConditionSatisfied(data);

        if (playerInRange && sensorConditionMet)
        {
            CompleteTest();
        }
    }

    public bool IsPressureSatisfied(Esp32SensorData data)
    {
        if (data == null)
        {
            return false;
        }

        switch (pressureSensor)
        {
            case PressureSensorRequirement.Pressure1Right:
                return data.pressure1 > requiredPressure;
            case PressureSensorRequirement.Pressure2Left:
                return data.pressure2 > requiredPressure;
            default:
                return data.pressure1 > requiredPressure ||
                       data.pressure2 > requiredPressure;
        }
    }

    public bool IsCollectionConditionSatisfied(Esp32SensorData data)
    {
        if (!IsPressureSatisfied(data))
        {
            return false;
        }

        return !requiresKneeAngle || data.kneeAngle > requiredKneeAngle;
    }

    private void CompleteTest()
    {
        testCompleted = true;
        Debug.Log("Echo completed using head position and ESP32 data.");
        gameObject.SetActive(false);
    }
}
