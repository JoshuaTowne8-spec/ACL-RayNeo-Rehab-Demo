using System.Collections.Generic;
using UnityEngine;

namespace ACLRehab.RayNeoSpatial
{
    public sealed class EchoPlacementController : MonoBehaviour
    {
        [SerializeField] private TrackOriginCalibrator originCalibrator;
        [SerializeField] private Transform trackOrigin;
        [SerializeField] private GameObject echoPrefab;
        [SerializeField] private Esp32UdpReceiver sensorReceiver;
        [SerializeField] private Transform trackedHead;
        [SerializeField] private Vector3[] localEchoPositions =
        {
            new Vector3(0f, 0.05f, 5f),
            new Vector3(0f, 0.05f, 10f),
            new Vector3(0f, 0.05f, 15f)
        };

        private readonly List<GameObject> spawnedEchoes = new List<GameObject>();

        public void Configure(
            TrackOriginCalibrator calibrator,
            Transform origin,
            GameObject prefab,
            Esp32UdpReceiver receiver,
            Transform head)
        {
            originCalibrator = calibrator;
            trackOrigin = origin;
            echoPrefab = prefab;
            sensorReceiver = receiver;
            trackedHead = head;
        }

        private void OnEnable()
        {
            if (originCalibrator != null)
            {
                originCalibrator.OriginCalibrated += SpawnEchoes;
            }
        }

        private void Start()
        {
            if (originCalibrator != null)
            {
                originCalibrator.OriginCalibrated -= SpawnEchoes;
                originCalibrator.OriginCalibrated += SpawnEchoes;
            }
        }

        private void OnDisable()
        {
            if (originCalibrator != null)
            {
                originCalibrator.OriginCalibrated -= SpawnEchoes;
            }
        }

        public void SpawnEchoes(Transform calibratedOrigin)
        {
            ClearEchoes();
            if (echoPrefab == null || calibratedOrigin == null)
            {
                return;
            }

            trackOrigin = calibratedOrigin;
            for (int i = 0; i < localEchoPositions.Length; i++)
            {
                GameObject echo = Instantiate(echoPrefab, trackOrigin);
                echo.name = $"EchoTarget_{i + 1:00}";
                echo.transform.localPosition = localEchoPositions[i];
                echo.transform.localRotation = Quaternion.identity;

                RehabEchoTest test = echo.GetComponent<RehabEchoTest>();
                if (test != null)
                {
                    test.receiver = sensorReceiver;
                    test.trackedHead = trackedHead;
                }

                spawnedEchoes.Add(echo);
            }
        }

        public void ClearEchoes()
        {
            for (int i = spawnedEchoes.Count - 1; i >= 0; i--)
            {
                if (spawnedEchoes[i] != null)
                {
                    Destroy(spawnedEchoes[i]);
                }
            }

            spawnedEchoes.Clear();
        }
    }
}
