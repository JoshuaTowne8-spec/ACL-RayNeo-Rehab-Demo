#include <Wire.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include "secrets.h"

const uint16_t UNITY_PORT = 4210;
const int PRESSURE_PIN_1 = 34; // Right foot / affected side.
const int PRESSURE_PIN_2 = 35; // Left foot.

WiFiUDP udp;
Adafruit_MPU6050 imu1;
Adafruit_MPU6050 imu2;

struct ImuAngle
{
  float roll;
  float pitch;
  bool initialized;
};

ImuAngle angle1 = {0, 0, false};
ImuAngle angle2 = {0, 0, false};

uint32_t sequenceNumber = 0;
uint32_t lastSendTime = 0;
uint32_t lastMicros = 0;

void printWiFiScan()
{
  Serial.println("Scanning nearby Wi-Fi networks...");
  int networkCount = WiFi.scanNetworks(false, true);

  if (networkCount < 0)
  {
    Serial.print("Wi-Fi scan failed, code: ");
    Serial.println(networkCount);
    return;
  }

  Serial.print("Wi-Fi networks found: ");
  Serial.println(networkCount);

  for (int i = 0; i < networkCount; ++i)
  {
    Serial.print("  [");
    Serial.print(i);
    Serial.print("] SSID=\"");
    Serial.print(WiFi.SSID(i));
    Serial.print("\" RSSI=");
    Serial.print(WiFi.RSSI(i));
    Serial.print(" dBm channel=");
    Serial.println(WiFi.channel(i));
  }

  WiFi.scanDelete();
}

void updateImuAngle(
  sensors_event_t& accel,
  sensors_event_t& gyro,
  ImuAngle& state,
  float dt)
{
  float accelRoll =
    atan2(accel.acceleration.y, accel.acceleration.z) * 180.0f / PI;

  float accelPitch =
    atan2(
      -accel.acceleration.x,
      sqrt(
        accel.acceleration.y * accel.acceleration.y +
        accel.acceleration.z * accel.acceleration.z)) *
    180.0f / PI;

  if (!state.initialized)
  {
    state.roll = accelRoll;
    state.pitch = accelPitch;
    state.initialized = true;
    return;
  }

  float gyroRollRate = gyro.gyro.x * 180.0f / PI;
  float gyroPitchRate = gyro.gyro.y * 180.0f / PI;

  // Complementary filter: fast gyro response with slow accelerometer correction.
  state.roll =
    0.98f * (state.roll + gyroRollRate * dt) +
    0.02f * accelRoll;
  state.pitch =
    0.98f * (state.pitch + gyroPitchRate * dt) +
    0.02f * accelPitch;
}

void setup()
{
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println("ACL RayNeo sensor firmware starting...");

  // ESP32 default I2C pins: SDA GPIO21, SCL GPIO22.
  Wire.begin(21, 22);

  // IMU 1 uses address 0x68; IMU 2 uses address 0x69.
  if (!imu1.begin(0x68, &Wire))
  {
    Serial.println("ERROR: IMU 1 not found at 0x68");
    while (true)
    {
      delay(100);
    }
  }

  if (!imu2.begin(0x69, &Wire))
  {
    Serial.println("ERROR: IMU 2 not found at 0x69");
    while (true)
    {
      delay(100);
    }
  }

  imu1.setAccelerometerRange(MPU6050_RANGE_8_G);
  imu1.setGyroRange(MPU6050_RANGE_500_DEG);
  imu1.setFilterBandwidth(MPU6050_BAND_21_HZ);

  imu2.setAccelerometerRange(MPU6050_RANGE_8_G);
  imu2.setGyroRange(MPU6050_RANGE_500_DEG);
  imu2.setFilterBandwidth(MPU6050_BAND_21_HZ);

  analogReadResolution(12);
  Serial.println("Both IMUs connected.");

  WiFi.mode(WIFI_STA);
  WiFi.disconnect(true, true);
  delay(500);
  WiFi.mode(WIFI_STA);
  printWiFiScan();
  WiFi.begin(WIFI_NAME, WIFI_PASSWORD);

  Serial.print("Connecting to Wi-Fi SSID=\"");
  Serial.print(WIFI_NAME);
  Serial.println("\"");

  uint32_t lastWiFiReport = 0;
  uint32_t wiFiAttemptStarted = millis();

  while (WiFi.status() != WL_CONNECTED)
  {
    delay(500);
    Serial.print(".");

    if (millis() - lastWiFiReport >= 5000)
    {
      lastWiFiReport = millis();
      Serial.print(" status=");
      Serial.println((int)WiFi.status());
    }

    if (millis() - wiFiAttemptStarted >= 20000)
    {
      Serial.println();
      Serial.println("Wi-Fi connection timed out; rescanning and retrying.");
      WiFi.disconnect();
      delay(250);
      printWiFiScan();
      WiFi.begin(WIFI_NAME, WIFI_PASSWORD);
      wiFiAttemptStarted = millis();
    }
  }

  WiFi.setSleep(false);

  Serial.println();
  Serial.println("Wi-Fi connected.");
  Serial.print("ESP32 IP: ");
  Serial.println(WiFi.localIP());
  Serial.print("Unity target: ");
  Serial.print(unityIP);
  Serial.print(":");
  Serial.println(UNITY_PORT);

  lastMicros = micros();
}

void loop()
{
  // Send at 50 Hz (one packet every 20 ms).
  if (millis() - lastSendTime < 20)
  {
    return;
  }

  lastSendTime = millis();
  uint32_t currentMicros = micros();
  float dt = (currentMicros - lastMicros) / 1000000.0f;
  lastMicros = currentMicros;

  if (dt <= 0 || dt > 0.2f)
  {
    dt = 0.02f;
  }

  sensors_event_t accel1;
  sensors_event_t gyro1;
  sensors_event_t temp1;
  sensors_event_t accel2;
  sensors_event_t gyro2;
  sensors_event_t temp2;

  imu1.getEvent(&accel1, &gyro1, &temp1);
  imu2.getEvent(&accel2, &gyro2, &temp2);

  updateImuAngle(accel1, gyro1, angle1, dt);
  updateImuAngle(accel2, gyro2, angle2, dt);

  int pressure1 = analogRead(PRESSURE_PIN_1);
  int pressure2 = analogRead(PRESSURE_PIN_2);

  // Prototype knee angle: absolute pitch difference between the two IMUs.
  float kneeAngle = fabs(angle1.pitch - angle2.pitch);

  sequenceNumber++;
  char packet[700];

  snprintf(
    packet,
    sizeof(packet),
    "{"
    "\"seq\":%lu,"
    "\"timeMs\":%lu,"
    "\"imu1Ax\":%.3f,"
    "\"imu1Ay\":%.3f,"
    "\"imu1Az\":%.3f,"
    "\"imu1Gx\":%.3f,"
    "\"imu1Gy\":%.3f,"
    "\"imu1Gz\":%.3f,"
    "\"imu1Roll\":%.2f,"
    "\"imu1Pitch\":%.2f,"
    "\"imu2Ax\":%.3f,"
    "\"imu2Ay\":%.3f,"
    "\"imu2Az\":%.3f,"
    "\"imu2Gx\":%.3f,"
    "\"imu2Gy\":%.3f,"
    "\"imu2Gz\":%.3f,"
    "\"imu2Roll\":%.2f,"
    "\"imu2Pitch\":%.2f,"
    "\"pressure1\":%d,"
    "\"pressure2\":%d,"
    "\"kneeAngle\":%.2f"
    "}",
    sequenceNumber,
    millis(),
    accel1.acceleration.x,
    accel1.acceleration.y,
    accel1.acceleration.z,
    gyro1.gyro.x,
    gyro1.gyro.y,
    gyro1.gyro.z,
    angle1.roll,
    angle1.pitch,
    accel2.acceleration.x,
    accel2.acceleration.y,
    accel2.acceleration.z,
    gyro2.gyro.x,
    gyro2.gyro.y,
    gyro2.gyro.z,
    angle2.roll,
    angle2.pitch,
    pressure1,
    pressure2,
    kneeAngle);

  udp.beginPacket(unityIP, UNITY_PORT);
  udp.write((const uint8_t*)packet, strlen(packet));
  udp.endPacket();

  // Print every tenth packet to reduce Serial Monitor noise.
  if (sequenceNumber % 10 == 0)
  {
    Serial.println(packet);
  }
}

