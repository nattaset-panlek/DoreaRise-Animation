using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Play Mode probe for authored transitions: drives a short scripted sequence (walk start, walk reversals to
    /// both sides, stop) and writes one CSV row per frame to Logs/LocomotionClipProbe.csv — action + clip time,
    /// root, feet, hands and the fastest-rotating bone, so pops and foot slides can be found by time.
    /// Added at runtime (Tools > DoreaRise > Locomotion > Run Clip Probe); leaves Play Mode when done.
    /// </summary>
    public class LocomotionClipProbe : MonoBehaviour
    {
        /// <summary>Run only the jump cases (Tools > DoreaRise > Locomotion > Run Jump Probe).</summary>
        public static bool JumpsOnly;
        /// <summary>Walk the scene's Jump Test props (Tools > DoreaRise > Locomotion > Run Jump Test Props Probe).</summary>
        public static bool PropsOnly;
        public static string ShotFolder => Path.Combine(Application.dataPath, "..", "Logs", "JumpProbe");
        public static string ReportPath => Path.Combine(Application.dataPath, "..", "Logs", "LocomotionClipProbe.csv");

        private LocomotionMotor motor;
        private LocomotionAnimator anim;
        private ScriptedLocomotionDriver driver;
        private Transform model;
        private Transform[] bones;
        private Quaternion[] lastRot;
        private Transform footL, footR, handL, handR, pelvis;
        private FootPlanting planting;
        private Transform hipL, hipR, kneeL, kneeR;
        private readonly StringBuilder csv = new StringBuilder();
        private string segment = "idle";
        private bool done;

        private bool wasRunInBackground;

        private void Start()
        {
            // keep ticking while the Editor window is unfocused (runtime value only, not saved to the project)
            wasRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            motor = GetComponent<LocomotionMotor>();
            anim = GetComponentInChildren<LocomotionAnimator>(true);
            driver = GetComponent<ScriptedLocomotionDriver>() ?? gameObject.AddComponent<ScriptedLocomotionDriver>();
            planting = GetComponentInChildren<FootPlanting>(true);
            Animator animator = GetComponentInChildren<Animator>(true);
            model = animator.transform;
            bones = model.GetComponentsInChildren<Transform>(true);
            lastRot = new Quaternion[bones.Length];
            foreach (Transform t in bones)
            {
                if (t.name == "Foot.L") footL = t;
                else if (t.name == "Foot.R") footR = t;
                else if (t.name == "Hand.L") handL = t;
                else if (t.name == "Hand.R") handR = t;
                else if (t.name == "Spine") pelvis = t;
                else if (t.name == "Hip.L") hipL = t;
                else if (t.name == "Hip.R") hipR = t;
                else if (t.name == "Leg.L") kneeL = t;
                else if (t.name == "Leg.R") kneeR = t;
            }
            csv.AppendLine("t,segment,action,clip,ntime,speed,rootX,rootZ,rootY,yaw,footLX,footLY,footLZ,footRX,footRY,footRZ,handLX,handLY,handLZ,handRX,handRY,handRZ,pelvisY,maxBoneDegPerSec,maxBone,lockStretch,kneeAzL,kneeAzR");
            motor.ActionStarted += kind =>
            {
                ActionClip a = anim != null ? anim.CurrentAction : null;
                float signed = Vector3.SignedAngle(Flat(transform.forward), requested, Vector3.up);
                float vel = Vector3.SignedAngle(Flat(transform.forward), Flat(motor.Velocity), Vector3.up);
                Debug.Log($"[ClipProbe] t {Time.time:F3} {kind} {(a != null && a.clip != null ? a.clip.name : "-")} clipAngle {(a is DirectionalAction d ? d.angle : 0f)} " +
                          $"facing {transform.eulerAngles.y:F1} requested {signed:F1} velocityDir {vel:F1} phase {anim.Phase:F3}");
            };
            StartCoroutine(PropsOnly ? PropsSequence() : JumpsOnly ? JumpSequence() : Sequence());
        }

        /// <summary>Every way a jump can start while moving, letting go right after; a Game view shot 1.5 s later.</summary>
        private IEnumerator JumpSequence()
        {
            Directory.CreateDirectory(ShotFolder);
            yield return new WaitForSeconds(1.5f);
            (string name, Gait gait, float run, float release)[] cases =
            {
                ("walk", Gait.Walk, 1.6f, 0.15f), ("run", Gait.Run, 1.6f, 0.15f), ("runLate", Gait.Run, 1.6f, 0.8f),
                ("run2", Gait.Run, 2.3f, 0.3f), ("sprint", Gait.Sprint, 2.2f, 0.15f), ("sprintLate", Gait.Sprint, 2.2f, 1.6f),
                ("sprintFast", Gait.SprintFast, 2.2f, 0.15f), ("runHold", Gait.Run, 1.6f, 99f)
            };
            // turn while in the air, keep holding the new way through the landing, then let go
            float[] turnTo = { 120f, -150f, 90f };
            float[] letGo = { 0.6f, 0.75f, 0.9f };
            for (int i = 0; i < turnTo.Length; i++)
            {
                segment = "airTurn" + i;
                driver.MoveInDirection(Flat(transform.forward), i == 1 ? Gait.Walk : Gait.Run, 30f);
                yield return new WaitForSeconds(1.6f);
                motor.Jump();
                yield return new WaitForSeconds(0.35f);
                driver.MoveInDirection(Quaternion.Euler(0f, turnTo[i], 0f) * Flat(transform.forward), Gait.Walk, 30f);
                yield return new WaitForSeconds(letGo[i]);
                driver.Stop();
                segment = "airTurn" + i + "Stand";
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotFolder, "a" + i + "_airTurn.png"));
                yield return new WaitForSeconds(1.5f);
            }
            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                segment = c.name;
                driver.MoveInDirection(Quaternion.Euler(0f, 45f, 0f) * Flat(transform.forward), c.gait, 30f);
                yield return new WaitForSeconds(c.run);
                motor.Jump();
                if (c.release < 50f)
                {
                    yield return new WaitForSeconds(c.release);
                    driver.Stop();
                }
                else
                {
                    yield return new WaitForSeconds(2.5f);
                    driver.Stop();
                }
                segment = c.name + "Stand";
                yield return new WaitForSeconds(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotFolder, i + "_" + c.name + ".png"));
                yield return new WaitForSeconds(2f);
            }
            Finish();
        }

        /// <summary>Up the ramps, off the 5 m platform, then the hurdle lane at a run.</summary>
        private IEnumerator PropsSequence()
        {
            Directory.CreateDirectory(ShotFolder);
            yield return new WaitForSeconds(1f);
            Vector3 north = Vector3.forward;
            segment = "rampsUp";
            motor.Teleport(new Vector3(20f, 0.05f, -2f), Quaternion.LookRotation(north));
            yield return new WaitForSeconds(0.5f);
            driver.MoveInDirection(north, Gait.Walk, 30f);
            float timeout = Time.time + 30f;
            while (Time.time < timeout && transform.position.z < 25.5f) yield return null;
            driver.Stop();
            yield return new WaitForSeconds(1f);
            Debug.Log($"[ClipProbe] props: reached z {transform.position.z:F1} at height {transform.position.y:F2}");
            segment = "walkOff5m";
            driver.MoveInDirection(Vector3.right, Gait.Walk, 30f);
            yield return new WaitForSeconds(2.5f);
            driver.Stop();
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(ShotFolder, "p0_after5m.png"));
            yield return new WaitForSeconds(2f);

            segment = "laneRun";
            motor.Teleport(new Vector3(28f, 0.05f, 0f), Quaternion.LookRotation(north));
            yield return new WaitForSeconds(0.5f);
            driver.MoveInDirection(north, Gait.Run, 30f);
            float[] hurdles = { 10f, 20f, 30f, 39.4f };
            Transform wristL = null, wristR = null;
            foreach (Transform t in bones)
            {
                if (t.name == "Hand.IK.L") wristL = t;
                else if (t.name == "Hand.IK.R") wristR = t;
            }
            for (int h = 0; h < hurdles.Length; h++)
            {
                timeout = Time.time + 8f;
                while (Time.time < timeout && hurdles[h] - 0.2f - transform.position.z > 3.2f) yield return null;
                segment = "laneVault" + h;
                motor.Jump();
                // wait for the hand plant and measure the wrist against its target on the hurdle
                float worst = 0f, best = 9f;
                bool shot = false;
                float end = Time.time + 1.4f;
                while (Time.time < end)
                {
                    yield return new WaitForEndOfFrame();
                    if (motor.TryGetHandPlant(out Vector3 target, out bool left, out float w) && w > 0.95f)
                    {
                        Transform wrist = left ? wristL : wristR;
                        float err = wrist != null ? Vector3.Distance(wrist.position, target) : -1f;
                        worst = Mathf.Max(worst, err);
                        best = Mathf.Min(best, err);
                        if (!shot)
                        {
                            ScreenCapture.CaptureScreenshot(Path.Combine(ShotFolder, "v" + h + "_plant.png"));
                            shot = true;
                            Debug.Log($"[ClipProbe] props: hurdle {h} plant target y {target.y:F2}");
                        }
                    }
                }
                Debug.Log($"[ClipProbe] props: hurdle {h} at z {hurdles[h]}: now z {transform.position.z:F1} y {transform.position.y:F2}, action {anim.ActionKind}, " +
                          $"wrist-to-target {(shot ? best.ToString("F3") + ".." + worst.ToString("F3") : "no plant")}");
            }
            driver.Stop();
            yield return new WaitForSeconds(1.5f);

            // sprint slide
            segment = "slideSprint";
            motor.Teleport(new Vector3(36f, 0.05f, 0f), Quaternion.LookRotation(north));
            yield return new WaitForSeconds(0.5f);
            driver.MoveInDirection(north, Gait.Sprint, 30f);
            yield return new WaitForSeconds(2.5f);
            segment = "slide";
            float z0 = transform.position.z;
            motor.Slide();
            for (int k = 0; k < 6; k++)
            {
                yield return new WaitForSeconds(0.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotFolder, "s" + k + "_slide.png"));
            }
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[ClipProbe] props: slide from z {z0:F1} to {transform.position.z:F1}, action now {anim.ActionKind}, speed {motor.CurrentSpeed:F1}");
            driver.Stop();
            yield return new WaitForSeconds(1.5f);

            // under the 1 m bar (x 36, z 29) and through the 0.95 m tunnel (x 40, z 28..33)
            CharacterController cc = GetComponent<CharacterController>();
            foreach (var lane in new[] { (x: 36f, name: "bar", past: 30f), (x: 40f, name: "tunnel", past: 34f) })
            {
                segment = "slideUnder_" + lane.name;
                motor.Teleport(new Vector3(lane.x, 0.05f, 4f), Quaternion.LookRotation(north));
                yield return new WaitForSeconds(0.5f);
                driver.MoveInDirection(north, Gait.Sprint, 30f);
                timeout = Time.time + 6f;
                while (Time.time < timeout && transform.position.z < 24f) yield return null;
                motor.Slide();
                float minHeight = cc.height;
                timeout = Time.time + 3f;
                while (Time.time < timeout && transform.position.z < lane.past + 1f)
                {
                    minHeight = Mathf.Min(minHeight, cc.height);
                    yield return null;
                }
                yield return new WaitForSeconds(0.6f);
                Debug.Log($"[ClipProbe] props: slide under {lane.name}: reached z {transform.position.z:F1} (past {lane.past}), " +
                          $"capsule min {minHeight:F2} now {cc.height:F2}, action {anim.ActionKind}, blocker {motor.HeadroomBlocker}, " +
                          $"pos {transform.position}, cc center {cc.center} r {cc.radius}");
                driver.Stop();
                yield return new WaitForSeconds(1.2f);
            }
            yield return new WaitForSeconds(2f);
            Finish();
        }

        private IEnumerator Sequence()
        {
            yield return new WaitForSeconds(1.5f);
            Vector3 fwd = Flat(transform.forward);
            yield return Move("walkStart", fwd, 3f);
            yield return Move("reverseRight", Quaternion.Euler(0f, 175f, 0f) * fwd, 3.5f);
            yield return Move("reverseLeft", Quaternion.Euler(0f, 175f + 185f, 0f) * fwd, 3.5f);
            segment = "stop";
            driver.Stop();
            yield return new WaitForSeconds(2.5f);
            // starts in every direction, each from standing (facing ends where the previous walk went)
            float[] turns = { 90f, -90f, 179f, -179f };
            for (int i = 0; i < turns.Length; i++)
            {
                yield return Move("start" + turns[i].ToString("+0;-0"), Quaternion.Euler(0f, turns[i], 0f) * Flat(transform.forward), 2.5f);
                segment = "stop" + (i + 2);
                driver.Stop();
                yield return new WaitForSeconds(2f);
            }
            segment = "jumpStanding";
            motor.Jump();
            yield return new WaitForSeconds(2.5f);
            segment = "walkJump";
            driver.MoveInDirection(Flat(transform.forward), Gait.Walk, 4f);
            yield return new WaitForSeconds(1.5f);
            segment = "walkJumpAir";
            motor.Jump();
            yield return new WaitForSeconds(2.5f);
            segment = "stopEnd";
            driver.Stop();
            yield return new WaitForSeconds(1.5f);

            // run, jump (no obstacle: the standing jump), let go of the stick right away
            float[] runFor = { 1.5f, 1.2f, 1.83f, 1.37f, 2.1f, 1.61f };
            for (int i = 0; i < runFor.Length; i++)
            {
                segment = "runJumpRelease" + i;
                driver.MoveInDirection(Quaternion.Euler(0f, i * 60f, 0f) * Flat(transform.forward), Gait.Run, runFor[i] + 0.1f);
                yield return new WaitForSeconds(runFor[i]);
                motor.Jump();
                yield return new WaitForSeconds(0.15f);
                driver.Stop();
                segment = "runJumpStand" + i;
                yield return new WaitForSeconds(2.5f);
            }

            // running jump over a test box (runtime only, removed afterwards)
            Vector3 dir = Flat(transform.forward);
            Vector3 boxPos = transform.position + dir * 9f;
            if (Physics.Raycast(boxPos + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 10f)) boxPos.y = ground.point.y;
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "ClipProbe Obstacle";
            box.transform.SetPositionAndRotation(boxPos + Vector3.up * 0.4f, Quaternion.LookRotation(dir));
            box.transform.localScale = new Vector3(2f, 0.8f, 0.4f);
            segment = "vaultRun";
            driver.MoveInDirection(dir, Gait.Run, 5f);
            float timeout = Time.time + 4f;
            while (Time.time < timeout && Vector3.Dot(box.transform.position - transform.position, dir) - 0.2f > 2.3f)
                yield return null;
            segment = "vault";
            motor.Jump();
            yield return new WaitForSeconds(2f);
            Destroy(box);
            segment = "vaultStop";
            driver.Stop();
            yield return new WaitForSeconds(2f);

            // sprint dive: keep running out of it
            segment = "sprint";
            driver.MoveInDirection(Flat(transform.forward), Gait.Sprint, 6f);
            yield return new WaitForSeconds(2f);
            segment = "sprintJump";
            motor.Jump();
            yield return new WaitForSeconds(3f);
            segment = "sprintStop";
            driver.Stop();
            yield return new WaitForSeconds(2.5f);

            // drop from 4 m
            segment = "drop";
            motor.Teleport(transform.position + Vector3.up * 4f, transform.rotation);
            yield return new WaitForSeconds(3f);
            Finish();
        }

        private Vector3 requested;

        private IEnumerator Move(string name, Vector3 dir, float seconds)
        {
            segment = name;
            requested = dir;
            driver.MoveInDirection(dir, Gait.Walk, seconds);
            yield return new WaitForSeconds(seconds);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        private void LateUpdate()
        {
            if (done || bones == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float maxSpeed = 0f;
            string maxBone = "";
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion r = bones[i].localRotation;
                if (Time.frameCount > 2)
                {
                    float deg = Quaternion.Angle(lastRot[i], r) / dt;
                    if (deg > maxSpeed && !bones[i].name.StartsWith("Hair") && !bones[i].name.StartsWith("Tail"))
                    {
                        maxSpeed = deg;
                        maxBone = bones[i].name;
                    }
                }
                lastRot[i] = r;
            }
            string clip = anim != null && anim.CurrentAction != null && anim.CurrentAction.clip != null ? anim.CurrentAction.clip.name : "";
            Vector3 p = transform.position;
            csv.Append(Time.time.ToString("F4")).Append(',').Append(segment).Append(',')
                .Append(anim != null ? anim.ActionKind.ToString() : "").Append(',').Append(clip).Append(',')
                .Append(anim != null ? anim.ActionNormalizedTime.ToString("F3") : "0").Append(',')
                .Append(motor.CurrentSpeed.ToString("F3")).Append(',')
                .Append(p.x.ToString("F4")).Append(',').Append(p.z.ToString("F4")).Append(',').Append(p.y.ToString("F4")).Append(',')
                .Append(transform.eulerAngles.y.ToString("F2"));
            foreach (Transform t in new[] { footL, footR, handL, handR })
            {
                Vector3 w = t != null ? t.position : Vector3.zero;
                csv.Append(',').Append(w.x.ToString("F4")).Append(',').Append(w.y.ToString("F4")).Append(',').Append(w.z.ToString("F4"));
            }
            csv.Append(',').Append(pelvis != null ? (pelvis.position.y - p.y).ToString("F4") : "0")
                .Append(',').Append(maxSpeed.ToString("F0")).Append(',').Append(maxBone)
                .Append(',').Append(planting != null ? planting.LockStretch.ToString("F3") : "0")
                .Append(',').Append(KneeAzimuth(hipL, kneeL, footL).ToString("F0"))
                .Append(',').Append(KneeAzimuth(hipR, kneeR, footR).ToString("F0")).AppendLine();
        }

        /// <summary>Where the knee points, degrees from the character's forward (0 = forward, 180 = backward,
        /// negative = toward its left). -999 when the leg is almost straight (no bend to measure).</summary>
        private float KneeAzimuth(Transform hip, Transform knee, Transform foot)
        {
            if (hip == null || knee == null || foot == null) return -999f;
            Vector3 axis = (foot.position - hip.position).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(knee.position - hip.position, axis);
            if (bend.magnitude < 0.03f) return -999f;
            Vector3 fwd = Flat(transform.forward);
            Vector3 flatBend = Vector3.ProjectOnPlane(bend, Vector3.up);
            if (flatBend.magnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(fwd, flatBend, Vector3.up);
        }

        private void Finish()
        {
            done = true;
            Application.runInBackground = wasRunInBackground;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, csv.ToString());
            Debug.Log("[ClipProbe] wrote " + Path.GetFullPath(ReportPath));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }
    }
}
