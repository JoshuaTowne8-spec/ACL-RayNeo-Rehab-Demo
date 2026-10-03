# ESP32 sensor firmware

This Arduino sketch reads two MPU6050 IMUs and two pressure sensors, calculates
a prototype right-knee angle, and sends the latest sample to the RayNeo Unity
application as UTF-8 JSON over UDP at 50 Hz.

## Hardware mapping

| Signal | ESP32 connection | Meaning |
| --- | --- | --- |
| I2C SDA | GPIO 21 | Shared MPU6050 data line |
| I2C SCL | GPIO 22 | Shared MPU6050 clock line |
| IMU 1 address | `0x68` | One segment of the right leg; AD0 low |
| IMU 2 address | `0x69` | Other segment of the right leg; AD0 high |
| Pressure 1 | GPIO 34 ADC | Right foot / affected side |
| Pressure 2 | GPIO 35 ADC | Left foot |
| UDP destination | Port 4210 | RayNeo headset IPv4 address |

Both MPU6050 modules share 3.3 V, ground, SDA, and SCL. Their AD0 pins must be
different so that one module responds at `0x68` and the other at `0x69`.

Each resistive pressure sensor needs a voltage divider. A typical arrangement
is `3.3 V -> pressure sensor -> ADC node -> 10 kOhm resistor -> GND`, with the
ADC node connected to GPIO 34 or GPIO 35. Confirm the safe voltage range for the
specific ESP32 board and sensor before powering the circuit.

## Required Arduino libraries

Install these libraries through the Arduino Library Manager:

- Adafruit MPU6050
- Adafruit Unified Sensor

`Wire`, `WiFi`, and `WiFiUDP` are provided by the ESP32 Arduino core.

## Configure network settings

1. Copy `secrets.example.h` to `secrets.h` in this sketch directory.
2. Set `WIFI_NAME` and `WIFI_PASSWORD` to the Wi-Fi network shared by the ESP32
   and RayNeo headset.
3. Set `unityIP` to the headset's current Wi-Fi IPv4 address.
4. Do not commit `secrets.h`; it is ignored by the repository.

## Upload

1. Install the ESP32 board package in Arduino IDE.
2. Open `ACL_RayNeo_Sensor.ino`.
3. Select the exact ESP32 board and serial port.
4. Upload the sketch.
5. Open Serial Monitor at 115200 baud.
6. Confirm that both IMUs are detected, Wi-Fi connects, and JSON packets appear.

## Data interpretation

- `pressure1`: right-foot pressure value.
- `pressure2`: left-foot pressure value.
- `kneeAngle`: absolute pitch difference between the two right-leg IMUs.

The current Unity collection rules are:

- Right echo: distance no more than 0.3 m, `pressure1 > 60`, and
  `kneeAngle > 10 degrees`.
- Left echo: distance no more than 0.3 m and `pressure2 > 60`.

The knee-angle calculation is a prototype measurement. Sensor mounting,
orientation, zeroing, and clinical validation should be completed before using
the value for formal rehabilitation assessment.

