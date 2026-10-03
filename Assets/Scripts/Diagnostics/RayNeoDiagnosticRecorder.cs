using System;
using System.Globalization;
using System.IO;
using System.Text;
using RayNeo.API;
using UnityEngine;

namespace ACLRehab.RayNeoDiagnostics
{
    [DefaultExecutionOrder(200)]
    public sealed class RayNeoDiagnosticRecorder : MonoBehaviour
    {
        [SerializeField] private RayNeoSpatialPoseProbe poseProbe;
        [SerializeField] private RayNeoPlaneCoordinateProbe planeProbe;
        [SerializeField, Range(1f, 30f)] private float samplesPerSecond = 10f;
        [SerializeField, Min(0.01f)] private float planeJumpThreshold = 0.15f;
        [SerializeField, Min(0.1f)] private float planeAngleJumpThreshold = 5f;

        private readonly CultureInfo invariant = CultureInfo.InvariantCulture;
        private StreamWriter writer;
        private float nextSampleTime;
        private float nextFlushTime;
        private float nextStatusLogTime;
        private int previousSnapshotSerial;
        private Algorithm.SlamState previousSlamState;
        private bool hasPreviousSlamState;
        private float previousPlanePollRealtime = -1f;

        public string OutputFilePath { get; private set; }
        public int RowsWritten { get; private set; }

        public void Configure(
            RayNeoSpatialPoseProbe pose,
            RayNeoPlaneCoordinateProbe planes)
        {
            poseProbe = pose;
            planeProbe = planes;
        }

        private void Start()
        {
            OpenWriter();
            MarkEvent("APP_START");
        }

        private void Update()
        {
            if (writer == null)
            {
                return;
            }

            DetectStateEvents();

            if (Time.unscaledTime >= nextSampleTime)
            {
                nextSampleTime = Time.unscaledTime + 1f / samplesPerSecond;
                WriteCurrentRows("SAMPLE");
            }

            if (Time.unscaledTime >= nextFlushTime)
            {
                nextFlushTime = Time.unscaledTime + 1f;
                writer.Flush();
            }

            if (Time.unscaledTime >= nextStatusLogTime)
            {
                nextStatusLogTime = Time.unscaledTime + 1f;
                Debug.Log(
                    "RAYNEO_DIAG_STATUS " +
                    $"slam={(planeProbe != null ? planeProbe.SlamState.ToString() : "NONE")}, " +
                    $"planes={(planeProbe != null ? planeProbe.ObservationCount : 0)}, " +
                    $"rawHead={(poseProbe != null && poseProbe.HasRawPose ? poseProbe.RawPose.position.ToString("F4") : "NONE")}, " +
                    $"headWorld={(poseProbe != null ? poseProbe.HeadWorldPose.position.ToString("F4") : "NONE")}, " +
                    $"headStep={(poseProbe != null ? poseProbe.HeadWorldStep : 0f):F4}, " +
                    $"snapshot={(planeProbe != null ? planeProbe.SnapshotSerial : 0)}, " +
                    $"rows={RowsWritten}",
                    this);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                MarkEvent("APPLICATION_PAUSE");
                writer?.Flush();
            }
            else
            {
                MarkEvent("APPLICATION_RESUME");
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            MarkEvent(focused ? "APPLICATION_FOCUS" : "APPLICATION_UNFOCUS");
        }

        private void OnDisable()
        {
            CloseWriter();
        }

        private void OnApplicationQuit()
        {
            CloseWriter();
        }

        public void MarkEvent(string eventName)
        {
            if (writer == null)
            {
                return;
            }

            WriteCurrentRows(Sanitize(eventName));
            writer.Flush();
        }

        private void OpenWriter()
        {
            try
            {
                string fileName =
                    "rayneo_spatial_diagnostic_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", invariant) +
                    ".csv";
                OutputFilePath = Path.Combine(
                    Application.persistentDataPath,
                    fileName);
                writer = new StreamWriter(OutputFilePath, false, new UTF8Encoding(false));
                writer.WriteLine(
                    "utc_iso,realtime_s,frame,event,slam,plane_count,plane_index," +
                    "plane_type,plane_timestamp,plane_polygon_count,plane_area," +
                    "plane_range_x,plane_range_z," +
                    "raw_plane_px,raw_plane_py,raw_plane_pz," +
                    "raw_plane_qx,raw_plane_qy,raw_plane_qz,raw_plane_qw," +
                    "raw_plane_step_m,raw_plane_angle_step_deg," +
                    "official_px,official_py,official_pz,official_qx,official_qy,official_qz,official_qw," +
                    "origin_child_px,origin_child_py,origin_child_pz,origin_child_qx,origin_child_qy,origin_child_qz,origin_child_qw," +
                    "explicit_px,explicit_py,explicit_pz,explicit_qx,explicit_qy,explicit_qz,explicit_qw," +
                    "sdk_head_valid,sdk_head_px,sdk_head_py,sdk_head_pz,sdk_head_qx,sdk_head_qy,sdk_head_qz,sdk_head_qw," +
                    "sdk_head_step,sdk_head_angle_step," +
                    "head_local_px,head_local_py,head_local_pz,head_local_qx,head_local_qy,head_local_qz,head_local_qw," +
                    "head_world_px,head_world_py,head_world_pz,head_world_qx,head_world_qy,head_world_qz,head_world_qw," +
                    "head_world_step,head_world_angle_step," +
                    "xr_device_valid,xr_device_px,xr_device_py,xr_device_pz,xr_device_qx,xr_device_qy,xr_device_qz,xr_device_qw," +
                    "native_head_result,native_head_valid,native_head_px,native_head_py,native_head_pz,native_head_qx,native_head_qy,native_head_qz,native_head_qw," +
                    "native_head_step,native_head_angle_step," +
                    "xr_origin_px,xr_origin_py,xr_origin_pz,xr_origin_qx,xr_origin_qy,xr_origin_qz,xr_origin_qw," +
                    "snapshot_serial");
                writer.Flush();
                Debug.Log("RAYNEO_DIAG_FILE " + OutputFilePath, this);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "RAYNEO_DIAG could not open CSV: " + exception,
                    this);
                writer = null;
            }
        }

        private void DetectStateEvents()
        {
            if (planeProbe == null)
            {
                return;
            }

            if (!hasPreviousSlamState || planeProbe.SlamState != previousSlamState)
            {
                string previous = hasPreviousSlamState
                    ? previousSlamState.ToString()
                    : "NONE";
                MarkEvent($"SLAM_{previous}_TO_{planeProbe.SlamState}");
                previousSlamState = planeProbe.SlamState;
                hasPreviousSlamState = true;
            }

            if (planeProbe.SnapshotSerial != previousSnapshotSerial)
            {
                previousSnapshotSerial = planeProbe.SnapshotSerial;
                MarkEvent("SNAPSHOT_SERIAL_" + previousSnapshotSerial);
            }

            if (Mathf.Approximately(
                    planeProbe.LastPollRealtime,
                    previousPlanePollRealtime))
            {
                return;
            }

            previousPlanePollRealtime = planeProbe.LastPollRealtime;
            for (int i = 0; i < planeProbe.Observations.Count; i++)
            {
                RayNeoPlaneCoordinateProbe.PlaneObservation observation =
                    planeProbe.Observations[i];
                if (observation.RawPositionStep > planeJumpThreshold ||
                    observation.RawAngleStep > planeAngleJumpThreshold)
                {
                    MarkEvent(
                        $"PLANE_{observation.Index}_JUMP_dp=" +
                        $"{observation.RawPositionStep:F4}_dr={observation.RawAngleStep:F2}");
                }
            }
        }

        private void WriteCurrentRows(string eventName)
        {
            if (writer == null)
            {
                return;
            }

            int count = planeProbe != null ? planeProbe.Observations.Count : 0;
            if (count == 0)
            {
                WriteOneRow(eventName, null);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                WriteOneRow(eventName, planeProbe.Observations[i]);
            }
        }

        private void WriteOneRow(
            string eventName,
            RayNeoPlaneCoordinateProbe.PlaneObservation plane)
        {
            Pose emptyPose = default;
            Pose rawPlane = plane != null ? plane.RawConvertedPose : emptyPose;
            Pose official = plane != null ? plane.OfficialDirectWorldPose : emptyPose;
            Pose originChild = plane != null ? plane.OriginChildWorldPose : emptyPose;
            Pose explicitWorld = plane != null ? plane.ExplicitWorldPose : emptyPose;
            Pose sdkHead = poseProbe != null ? poseProbe.RawPose : emptyPose;
            Pose headLocal = poseProbe != null ? poseProbe.HeadLocalPose : emptyPose;
            Pose headWorld = poseProbe != null ? poseProbe.HeadWorldPose : emptyPose;
            Pose xrDevice = poseProbe != null ? poseProbe.XrDevicePose : emptyPose;
            Pose nativeHead = poseProbe != null ? poseProbe.NativeHeadPose : emptyPose;
            Pose origin = poseProbe != null ? poseProbe.XrOriginWorldPose : emptyPose;

            StringBuilder row = new StringBuilder(1024);
            Add(row, DateTime.UtcNow.ToString("O", invariant));
            Add(row, F(Time.realtimeSinceStartup));
            Add(row, Time.frameCount.ToString(invariant));
            Add(row, Sanitize(eventName));
            Add(row, planeProbe != null ? planeProbe.SlamState.ToString() : "NONE");
            Add(row, (planeProbe != null ? planeProbe.ObservationCount : 0).ToString(invariant));
            Add(row, (plane != null ? plane.Index : -1).ToString(invariant));
            Add(row, plane != null ? plane.Property.ToString() : "NONE");
            Add(row, (plane != null ? plane.Timestamp : 0UL).ToString(invariant));
            Add(row, (plane != null ? plane.PolygonCount : 0).ToString(invariant));
            Add(row, F(plane != null ? plane.Area : 0f));
            Add(row, F(plane != null ? plane.LocalRange.x : 0f));
            Add(row, F(plane != null ? plane.LocalRange.y : 0f));
            AddPose(row, rawPlane);
            Add(row, F(plane != null ? plane.RawPositionStep : 0f));
            Add(row, F(plane != null ? plane.RawAngleStep : 0f));
            AddPose(row, official);
            AddPose(row, originChild);
            AddPose(row, explicitWorld);
            Add(row, Bool(poseProbe != null && poseProbe.HasRawPose));
            AddPose(row, sdkHead);
            Add(row, F(poseProbe != null ? poseProbe.RawPoseStep : 0f));
            Add(row, F(poseProbe != null ? poseProbe.RawPoseAngleStep : 0f));
            AddPose(row, headLocal);
            AddPose(row, headWorld);
            Add(row, F(poseProbe != null ? poseProbe.HeadWorldStep : 0f));
            Add(row, F(poseProbe != null ? poseProbe.HeadWorldAngleStep : 0f));
            Add(row, Bool(poseProbe != null && poseProbe.HasXrDevicePose));
            AddPose(row, xrDevice);
            Add(
                row,
                (poseProbe != null ? poseProbe.NativeHeadPoseResult : int.MinValue)
                    .ToString(invariant));
            Add(row, Bool(poseProbe != null && poseProbe.HasNativeHeadPose));
            AddPose(row, nativeHead);
            Add(row, F(poseProbe != null ? poseProbe.NativeHeadPoseStep : 0f));
            Add(row, F(poseProbe != null ? poseProbe.NativeHeadPoseAngleStep : 0f));
            AddPose(row, origin);
            AddLast(
                row,
                (planeProbe != null ? planeProbe.SnapshotSerial : 0).ToString(invariant));
            writer.WriteLine(row.ToString());
            RowsWritten++;
        }

        private void AddPose(StringBuilder row, Pose pose)
        {
            Add(row, F(pose.position.x));
            Add(row, F(pose.position.y));
            Add(row, F(pose.position.z));
            Add(row, F(pose.rotation.x));
            Add(row, F(pose.rotation.y));
            Add(row, F(pose.rotation.z));
            Add(row, F(pose.rotation.w));
        }

        private string F(float value)
        {
            return value.ToString("R", invariant);
        }

        private static string Bool(bool value)
        {
            return value ? "1" : "0";
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace(',', ';').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static void Add(StringBuilder builder, string value)
        {
            builder.Append(value);
            builder.Append(',');
        }

        private static void AddLast(StringBuilder builder, string value)
        {
            builder.Append(value);
        }

        private void CloseWriter()
        {
            if (writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("RAYNEO_DIAG CSV close failed: " + exception, this);
            }
            finally
            {
                writer = null;
            }
        }
    }
}
