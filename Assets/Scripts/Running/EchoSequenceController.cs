using System.Collections.Generic;
using UnityEngine;

namespace ACLRehab.Running
{
    /// <summary>
    /// Enables one collectible at a time and waits for the sensor condition to
    /// be released before arming the next target.
    /// </summary>
    public sealed class EchoSequenceController : MonoBehaviour
    {
        public EchoStepSpawner spawner;
        public Esp32UdpReceiver receiver;

        [Header("Runtime")]
        public int activeEchoIndex = -1;
        public bool waitingForSensorRelease;

        private IReadOnlyList<RehabEchoTest> echoes;

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.EchoesRebuilt += SetEchoes;
            }
        }

        private void Start()
        {
            if (spawner != null && spawner.Echoes.Count > 0)
            {
                SetEchoes(spawner.Echoes);
            }
        }

        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.EchoesRebuilt -= SetEchoes;
            }
        }

        private void Update()
        {
            if (echoes == null || activeEchoIndex < 0 || activeEchoIndex >= echoes.Count)
            {
                return;
            }

            RehabEchoTest activeEcho = echoes[activeEchoIndex];

            if (!waitingForSensorRelease &&
                (activeEcho == null || activeEcho.testCompleted || !activeEcho.gameObject.activeSelf))
            {
                waitingForSensorRelease = true;
            }

            if (waitingForSensorRelease && IsSensorReleased(activeEcho))
            {
                waitingForSensorRelease = false;
                ArmEcho(activeEchoIndex + 1);
            }
        }

        private void SetEchoes(IReadOnlyList<RehabEchoTest> rebuiltEchoes)
        {
            echoes = rebuiltEchoes;

            for (int index = 0; index < echoes.Count; index++)
            {
                if (echoes[index] != null)
                {
                    echoes[index].enabled = false;
                }
            }

            waitingForSensorRelease = false;
            ArmEcho(0);
        }

        private void ArmEcho(int index)
        {
            activeEchoIndex = index;
            if (echoes == null || index < 0 || index >= echoes.Count)
            {
                activeEchoIndex = -1;
                return;
            }

            RehabEchoTest nextEcho = echoes[index];
            if (nextEcho != null)
            {
                nextEcho.enabled = true;
            }
        }

        private bool IsSensorReleased(RehabEchoTest completedEcho)
        {
            if (receiver == null || !receiver.isConnected || receiver.latestData == null ||
                completedEcho == null)
            {
                return true;
            }

            Esp32SensorData data = receiver.latestData;
            return !completedEcho.IsCollectionConditionSatisfied(data);
        }
    }
}
