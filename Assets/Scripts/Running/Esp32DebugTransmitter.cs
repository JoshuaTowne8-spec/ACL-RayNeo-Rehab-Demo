using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ACLRehab.Running
{
    /// <summary>
    /// Desktop-only UDP source for exercising the exact ESP32 receive path.
    /// It sends JSON to localhost instead of writing receiver state directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Esp32DebugTransmitter : MonoBehaviour
    {
        [Header("UDP")]
        public bool transmissionEnabled = true;
        public string destinationAddress = "127.0.0.1";
        [Min(1)] public int destinationPort = 4210;
        [Range(1f, 100f)] public float sendRateHz = 20f;

        [Header("Simulated sensor values")]
        public int pressure1;
        public int pressure2;
        public float kneeAngle;

        [Header("Keyboard presets")]
        public int validPressure = 80;
        public float validKneeAngle = 20f;

        private UdpClient client;
        private IPEndPoint endpoint;
        private float nextSendTime;
        private int sequence;

        private void OnEnable()
        {
            if (!IsDesktop())
            {
                enabled = false;
                return;
            }

            RecreateClient();
        }

        private void Update()
        {
            HandleKeyboard();

            if (!transmissionEnabled || client == null || Time.unscaledTime < nextSendTime)
            {
                return;
            }

            nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, sendRateHz);
            SendPacket();
        }

        private void OnDisable()
        {
            client?.Dispose();
            client = null;
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                SetReleased();
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                SetValidRightAction();
            }
            else if (keyboard.digit3Key.wasPressedThisFrame)
            {
                SetValidLeftAction();
            }
            else if (keyboard.digit4Key.wasPressedThisFrame)
            {
                pressure1 = validPressure;
                pressure2 = 0;
                kneeAngle = 0f;
            }
            else if (keyboard.digit5Key.wasPressedThisFrame)
            {
                pressure1 = 0;
                pressure2 = 0;
                kneeAngle = validKneeAngle;
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                transmissionEnabled = !transmissionEnabled;
            }
        }

        private void SendPacket()
        {
            Esp32SensorData packet = new Esp32SensorData
            {
                seq = ++sequence,
                timeMs = (int)(Time.realtimeSinceStartup * 1000f),
                pressure1 = pressure1,
                pressure2 = pressure2,
                kneeAngle = kneeAngle
            };

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
                client.Send(bytes, bytes.Length, endpoint);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Debug ESP32 UDP send failed: " + exception.Message);
                RecreateClient();
            }
        }

        private void RecreateClient()
        {
            client?.Dispose();
            client = null;

            if (!IPAddress.TryParse(destinationAddress, out IPAddress address))
            {
                Debug.LogError("Invalid debug ESP32 destination: " + destinationAddress);
                return;
            }

            endpoint = new IPEndPoint(address, destinationPort);
            client = new UdpClient();
        }

        public void SetValidAction()
        {
            SetValidRightAction();
        }

        public void SetValidRightAction()
        {
            pressure1 = validPressure;
            pressure2 = 0;
            kneeAngle = validKneeAngle;
        }

        public void SetValidLeftAction()
        {
            pressure1 = 0;
            pressure2 = validPressure;
            kneeAngle = 0f;
        }

        public void SetReleased()
        {
            pressure1 = 0;
            pressure2 = 0;
            kneeAngle = 0f;
        }

        private void OnGUI()
        {
            const float width = 600f;
            GUI.Box(new Rect(20f, Screen.height - 185f, width, 165f), string.Empty);
            GUI.Label(new Rect(40f, Screen.height - 170f, width - 30f, 24f),
                "RUNNING PROTOTYPE CONTROLS");
            GUI.Label(new Rect(40f, Screen.height - 142f, width - 30f, 22f),
                "WASD move | Mouse look | Shift run | Esc release cursor");
            GUI.Label(new Rect(40f, Screen.height - 116f, width - 30f, 22f),
                "1 Release | 2 Right: P1 + knee | 3 Left: P2 only");
            GUI.Label(new Rect(40f, Screen.height - 90f, width - 30f, 22f),
                "4 P1 only | 5 Knee only | T UDP on/off");
            GUI.Label(new Rect(40f, Screen.height - 62f, width - 30f, 22f),
                $"UDP: {(transmissionEnabled ? "ON" : "OFF")}  P1: {pressure1}  P2: {pressure2}  Knee: {kneeAngle:F1}");
        }

        private static bool IsDesktop()
        {
            return Application.isEditor ||
                   Application.platform == RuntimePlatform.WindowsPlayer ||
                   Application.platform == RuntimePlatform.OSXPlayer ||
                   Application.platform == RuntimePlatform.LinuxPlayer;
        }
    }
}
