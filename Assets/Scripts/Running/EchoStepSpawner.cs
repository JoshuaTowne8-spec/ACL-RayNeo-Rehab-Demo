using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ACLRehab.Running
{
    /// <summary>
    /// Places alternating left/right echo markers along the two visible route
    /// segments. A custom sticker prefab can replace the built-in disc.
    /// </summary>
    public sealed class EchoStepSpawner : MonoBehaviour
    {
        [Header("References")]
        public GameObject echoPrefab;
        public Material placeholderMaterial;
        public Transform trackedHead;
        public Esp32UdpReceiver receiver;

        [Header("Spacing")]
        [Min(0.2f)] public float firstStepOffset = 0.8f;
        [Min(0.2f)] public float stepSpacing = 1.1f;
        [Min(0f)] public float lateralOffset = 0.2f;
        [Min(0.05f)] public float echoSize = 0.7f;
        [Min(0f)] public float groundOffset = 0.025f;

        [Header("Collection Thresholds")]
        [Min(0f)] public float activationDistance = 0.3f;
        public float requiredKneeAngle = 10f;
        public int requiredPressure = 60;

        private readonly List<RehabEchoTest> echoes = new List<RehabEchoTest>();

        public IReadOnlyList<RehabEchoTest> Echoes => echoes;
        public event Action<IReadOnlyList<RehabEchoTest>> EchoesRebuilt;

        public void BuildEchoes(RunningRouteSegment current, RunningRouteSegment next)
        {
            ClearEchoes();

            int stepIndex = 0;
            stepIndex = BuildSegmentEchoes(current, stepIndex);
            BuildSegmentEchoes(next, stepIndex);

            EchoesRebuilt?.Invoke(echoes);
        }

        private int BuildSegmentEchoes(RunningRouteSegment segment, int stepIndex)
        {
            for (float distance = firstStepOffset;
                 distance < segment.length;
                 distance += stepSpacing)
            {
                bool placeOnLeft = stepIndex % 2 == 0;
                float side = placeOnLeft ? -lateralOffset : lateralOffset;
                Vector3 localPosition = segment.start +
                                        segment.forward * distance +
                                        segment.Right * side +
                                        Vector3.up * groundOffset;

                GameObject echo = CreateEchoObject(stepIndex + 1);
                echo.name = $"Echo Step {stepIndex + 1:00} " +
                            (placeOnLeft ? "Left (Pressure 2)" : "Right (Pressure 1)");
                echo.transform.SetParent(transform, false);
                echo.transform.localPosition = localPosition;
                echo.transform.localRotation = Quaternion.LookRotation(
                    segment.forward,
                    Vector3.up);

                RehabEchoTest test = echo.GetComponent<RehabEchoTest>();
                if (test == null)
                {
                    test = echo.AddComponent<RehabEchoTest>();
                }

                test.receiver = receiver;
                test.trackedHead = trackedHead;
                test.activationDistance = activationDistance;
                test.requiredKneeAngle = requiredKneeAngle;
                test.requiredPressure = requiredPressure;
                test.pressureSensor = placeOnLeft
                    ? PressureSensorRequirement.Pressure2Left
                    : PressureSensorRequirement.Pressure1Right;
                test.requiresKneeAngle = !placeOnLeft;
                test.enabled = false;

                echoes.Add(test);
                stepIndex++;
            }

            return stepIndex;
        }

        private GameObject CreateEchoObject(int index)
        {
            GameObject echo;
            if (echoPrefab != null)
            {
                echo = Instantiate(echoPrefab);
                echo.transform.localScale = Vector3.one * echoSize;
            }
            else
            {
                echo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                echo.transform.localScale = new Vector3(echoSize, 0.01f, echoSize);

                Collider echoCollider = echo.GetComponent<Collider>();
                if (echoCollider != null)
                {
                    Destroy(echoCollider);
                }

                Renderer echoRenderer = echo.GetComponent<Renderer>();
                if (echoRenderer != null)
                {
                    echoRenderer.sharedMaterial = placeholderMaterial;
                    echoRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    echoRenderer.receiveShadows = false;
                }
            }

            echo.name = $"Echo Step {index:00}";
            return echo;
        }

        private void ClearEchoes()
        {
            foreach (RehabEchoTest echo in echoes)
            {
                if (echo != null)
                {
                    Destroy(echo.gameObject);
                }
            }

            echoes.Clear();
        }
    }
}
