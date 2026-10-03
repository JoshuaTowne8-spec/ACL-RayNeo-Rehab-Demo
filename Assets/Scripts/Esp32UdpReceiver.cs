using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

[Serializable]
public class Esp32SensorData
{
    public int seq;
    public int timeMs;

    public float imu1Ax;
    public float imu1Ay;
    public float imu1Az;
    public float imu1Gx;
    public float imu1Gy;
    public float imu1Gz;
    public float imu1Roll;
    public float imu1Pitch;

    public float imu2Ax;
    public float imu2Ay;
    public float imu2Az;
    public float imu2Gx;
    public float imu2Gy;
    public float imu2Gz;
    public float imu2Roll;
    public float imu2Pitch;

    public int pressure1;
    public int pressure2;
    public float kneeAngle;
}

/// <summary>
/// Receives the most recent ESP32 JSON packet over UDP. Network work happens on
/// a background thread and Unity objects are touched only from Update.
/// </summary>
public class Esp32UdpReceiver : MonoBehaviour
{
    [Min(1)] public int listenPort = 4210;
    [Min(0.05f)] public float connectionTimeout = 0.3f;

    public Esp32SensorData latestData = new Esp32SensorData();
    public bool isConnected;
    public float timeSinceLastPacket = float.PositiveInfinity;
    public string lastSender = "";

    private readonly ConcurrentQueue<ReceivedPacket> messageQueue =
        new ConcurrentQueue<ReceivedPacket>();

    private UdpClient udpClient;
    private Thread receiverThread;
    private volatile bool running;
    private float lastPacketTime = -1f;

    private struct ReceivedPacket
    {
        public string Json;
        public string Sender;
    }

    private void OnEnable()
    {
        StartReceiver();
    }

    private void Update()
    {
        ReceivedPacket newestPacket = default;
        bool hasPacket = false;

        while (messageQueue.TryDequeue(out ReceivedPacket packet))
        {
            newestPacket = packet;
            hasPacket = true;
        }

        if (hasPacket)
        {
            try
            {
                Esp32SensorData parsed =
                    JsonUtility.FromJson<Esp32SensorData>(newestPacket.Json);

                if (parsed != null)
                {
                    latestData = parsed;
                    lastSender = newestPacket.Sender;
                    lastPacketTime = Time.unscaledTime;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ESP32 JSON error: " + exception.Message);
            }
        }

        if (lastPacketTime < 0f)
        {
            timeSinceLastPacket = float.PositiveInfinity;
            isConnected = false;
            return;
        }

        timeSinceLastPacket = Time.unscaledTime - lastPacketTime;
        isConnected = timeSinceLastPacket < connectionTimeout;
    }

    private void OnDisable()
    {
        StopReceiver();
    }

    private void OnApplicationQuit()
    {
        StopReceiver();
    }

    private void StartReceiver()
    {
        if (running)
        {
            return;
        }

        try
        {
            udpClient = new UdpClient(listenPort);
            udpClient.Client.ReceiveTimeout = 500;
            running = true;

            receiverThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "ESP32 UDP Receiver"
            };
            receiverThread.Start();

            Debug.Log("Listening for ESP32 UDP on port " + listenPort);
        }
        catch (Exception exception)
        {
            running = false;
            isConnected = false;
            Debug.LogError("UDP start failed: " + exception.Message);
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (running)
        {
            try
            {
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);
                messageQueue.Enqueue(new ReceivedPacket
                {
                    Json = Encoding.UTF8.GetString(bytes),
                    Sender = remoteEndPoint.ToString()
                });
            }
            catch (SocketException exception)
            {
                // ReceiveTimeout is expected and lets the thread observe running=false.
                // Other socket failures are ignored here because Unity logging is not
                // thread-safe; closing/re-enabling the component restarts the socket.
                if (exception.SocketErrorCode != SocketError.TimedOut && !running)
                {
                    break;
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                if (!running)
                {
                    break;
                }
            }
        }
    }

    private void StopReceiver()
    {
        running = false;

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        if (receiverThread != null && receiverThread.IsAlive)
        {
            receiverThread.Join(300);
        }

        receiverThread = null;
        isConnected = false;
    }
}
