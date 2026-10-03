using UnityEngine;

/// <summary>Simple runtime overlay for validating ESP32 packets without a UI prefab.</summary>
public class SensorDebugOverlay : MonoBehaviour
{
    public Esp32UdpReceiver receiver;

    private GUIStyle titleStyle;
    private GUIStyle textStyle;
    private GUIStyle statusStyle;

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(1f, 0.86f, 0.12f);

        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        textStyle.normal.textColor = Color.white;

        statusStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold };
    }

    private void OnGUI()
    {
        if (receiver == null)
        {
            return;
        }

        EnsureStyles();

        float scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.4f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        Esp32SensorData data = receiver.latestData ?? new Esp32SensorData();
        GUI.Box(new Rect(20, 20, 430, 370), "");
        GUI.Label(new Rect(40, 35, 380, 30), "ACL SENSOR TEST", titleStyle);

        statusStyle.normal.textColor = receiver.isConnected
            ? new Color(0.25f, 1f, 0.4f)
            : new Color(1f, 0.3f, 0.3f);

        GUI.Label(
            new Rect(40, 75, 380, 25),
            "ESP32: " + (receiver.isConnected ? "CONNECTED" : "DISCONNECTED"),
            statusStyle);

        GUI.Label(new Rect(40, 110, 380, 25), "IMU 1 Roll: " + data.imu1Roll.ToString("F1"), textStyle);
        GUI.Label(new Rect(40, 140, 380, 25), "IMU 1 Pitch: " + data.imu1Pitch.ToString("F1"), textStyle);
        GUI.Label(new Rect(40, 175, 380, 25), "IMU 2 Roll: " + data.imu2Roll.ToString("F1"), textStyle);
        GUI.Label(new Rect(40, 205, 380, 25), "IMU 2 Pitch: " + data.imu2Pitch.ToString("F1"), textStyle);
        GUI.Label(new Rect(40, 240, 380, 25), "Pressure 1: " + data.pressure1, textStyle);
        GUI.Label(new Rect(40, 270, 380, 25), "Pressure 2: " + data.pressure2, textStyle);
        GUI.Label(new Rect(40, 305, 380, 25), "Test Knee Angle: " + data.kneeAngle.ToString("F1"), textStyle);

        string sender = string.IsNullOrEmpty(receiver.lastSender) ? "-" : receiver.lastSender;
        GUI.Label(new Rect(40, 335, 380, 25), "Last Sender: " + sender, textStyle);

        GUI.matrix = previousMatrix;
    }
}
