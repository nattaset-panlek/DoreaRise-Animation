using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Automated Play Mode check of the whole locomotion stack on the "Terrain Test" course and flat ground.
    /// Drives the character with ScriptedLocomotionDriver, measures what each layer actually did (foot tilt,
    /// ground error, foot slide, pelvis, step smoothing, speeds, stop distance, pivot / drift / dash /
    /// landing events, exceptions) and writes Logs/LocomotionSelfTest.md. Started from
    /// Tools > DoreaRise > Locomotion > Run Self Test.
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public class LocomotionSelfTest : MonoBehaviour
    {
        private class Segment
        {
            public string name;
            public float targetSpeed;
            public int frames;
            public float maxSpeed;
            public float maxTilt;
            public float minPelvis;
            public float maxStepSmoothing;
            public float maxBodyJump;
            public float lastBodyY;
            public bool hasBodyY;
            public float jitterFinalSum;
            public float jitterAnimatedSum;
            public int jitterSamples;
            public int locksStart;
            public int locks;

            public float JitterFinal => jitterSamples > 0 ? jitterFinalSum / jitterSamples : 0f;
            public float JitterAnimated => jitterSamples > 0 ? jitterAnimatedSum / jitterSamples : 0f;
            public float LocksPerSecond => time > 0.01f ? locks / time : 0f;
            public float slideSum;
            public int slideSamples;
            public float slideMax;
            public float groundErrorSum;
            public int groundErrorSamples;
            public float groundErrorMax;
            public int airFrames;
            public int landings;
            public float maxLanding;
            public int drifts;
            public float stopDistance = -1f;
            public bool hasStopStart;
            public Vector3 stopStart;
            public float arrivalError = -1f;
            public float time;
            public float timeTo80 = -1f;
            public float facingError = -1f;
            public bool timedOut;
            public readonly Dictionary<LocomotionActionKind, int> actions = new Dictionary<LocomotionActionKind, int>();

            public int Count(LocomotionActionKind kind) => actions.TryGetValue(kind, out int n) ? n : 0;
        }

        #region [Variables/Fields]
        /// <summary>Raised with (report path, one-line summary) when the run is finished.</summary>
        public static event Action<string, string> Completed;

        private const float SegmentTimeout = 35f;

        private LocomotionMotor motor;
        private ScriptedLocomotionDriver driver;
        private FootPlanting feet;
        private readonly List<Segment> segments = new List<Segment>();
        private readonly List<string> errors = new List<string>();
        private Segment current;
        private Vector3 previousLeft, previousRight;
        private bool previousLeftLocked, previousRightLocked;

        /// <summary>Per-leg history for the shin jitter measure (second difference of its direction).</summary>
        private class ShinTrack
        {
            public Vector3 lastFinal, lastAnimated;
            public float lastFinalDelta, lastAnimatedDelta;
            public int samples;
        }

        private readonly ShinTrack leftShin = new ShinTrack();
        private readonly ShinTrack rightShin = new ShinTrack();
        #endregion

        #region [Properties]
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LocomotionSelfTest.md"));
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            if (motor == null) return;
            motor.ActionStarted -= OnAction;
            motor.DriftStarted -= OnDrift;
            motor.Landed -= OnLanded;
        }

        private IEnumerator Start()
        {
            motor = GetComponent<LocomotionMotor>();
            driver = GetComponent<ScriptedLocomotionDriver>();
            feet = GetComponentInChildren<FootPlanting>();

            if (motor == null || driver == null)
            {
                Finish("ABORTED: LocomotionMotor / ScriptedLocomotionDriver not found on " + name);
                yield break;
            }

            motor.ActionStarted += OnAction;
            motor.DriftStarted += OnDrift;
            motor.Landed += OnLanded;

            bool hold = driver.HoldControlWhenIdle;
            bool idleBreaks = motor.PlayIdleBreaks;
            driver.HoldControlWhenIdle = true;
            motor.PlayIdleBreaks = false;

            // Flat ground first: plain walking with turns is where leg flicker is most visible.
            yield return FlatWalk();

            // Terrain course (see Terrain Test in the scene).
            yield return Walk("Hill 15° up / plateau / down", new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 23f));
            yield return Walk("Stairs 6 up / landing / 6 down", new Vector3(5f, 0f, 2f), new Vector3(5f, 0f, 14f));
            yield return Walk("Side slope 12° (low side)", new Vector3(-11.2f, 0f, 3f), new Vector3(-11.2f, 0f, 18f));
            yield return Walk("Bumps 6-20 cm", new Vector3(10f, 0f, 3f), new Vector3(10f, 0f, 15f));
            yield return Move("Steep 25° ramp + 2 m ledge drop (sprint)", new Vector3(-5f, 0f, 2f), new Vector3(-5f, 0f, 18f), Gait.Sprint);

            // Flat ground behind the spawn.
            yield return SprintStop();
            yield return Pivot();
            yield return Drift();
            yield return Dash();
            yield return TurnStart();

            // Authored run clips (Blender generator, Batch 1). Skipped when the clip set has no Run loop.
            if (motor.GetGaitSpeed(Gait.Run) > 0f && motor.LocomotionAnimator != null
                && motor.LocomotionAnimator.GetLoopSpeed(Gait.Run) > 0f)
            {
                yield return RunLoop();
                yield return RunStartStop();
                yield return RunStartBack();
                yield return WalkStop();
            }

            driver.HoldControlWhenIdle = hold;
            motor.PlayIdleBreaks = idleBreaks;
            Finish(null);
        }

        private void LateUpdate()
        {
            if (current == null || motor == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            current.frames++;
            current.time += dt;
            current.maxSpeed = Mathf.Max(current.maxSpeed, motor.CurrentSpeed);
            if (current.timeTo80 < 0f && motor.CurrentSpeed >= current.targetSpeed * 0.8f) current.timeTo80 = current.time;
            current.maxStepSmoothing = Mathf.Max(current.maxStepSmoothing, Mathf.Abs(motor.VisualStepOffset));
            if (!motor.IsGrounded) current.airFrames++;

            // How far the visible body jumps vertically in one grounded frame (stairs should be smooth).
            Transform body = motor.VisualRoot != null ? motor.VisualRoot : motor.transform;
            float bodyY = body.position.y;
            if (current.hasBodyY && motor.IsGrounded)
                current.maxBodyJump = Mathf.Max(current.maxBodyJump, Mathf.Abs(bodyY - current.lastBodyY));
            current.lastBodyY = bodyY;
            current.hasBodyY = true;

            if (feet == null) return;
            current.minPelvis = Mathf.Min(current.minPelvis, feet.PelvisOffset);
            SampleFoot(feet.LeftLeg, ref previousLeft, ref previousLeftLocked, dt);
            SampleFoot(feet.RightLeg, ref previousRight, ref previousRightLocked, dt);
            SampleShin(feet.LeftLeg, leftShin);
            SampleShin(feet.RightLeg, rightShin);
        }

        /// <summary>
        /// Flicker shows up as the shin direction reversing its frame-to-frame change: compare that "jerk"
        /// after the IK with the same measure on the untouched animation.
        /// </summary>
        private void SampleShin(FootPlanting.Leg leg, ShinTrack track)
        {
            if (leg == null || !leg.IsValid || !motor.IsGrounded) return;

            if (track.samples > 0)
            {
                float finalDelta = Vector3.Angle(track.lastFinal, leg.finalShin);
                float animatedDelta = Vector3.Angle(track.lastAnimated, leg.animatedShin);
                if (track.samples > 1)
                {
                    current.jitterFinalSum += Mathf.Abs(finalDelta - track.lastFinalDelta);
                    current.jitterAnimatedSum += Mathf.Abs(animatedDelta - track.lastAnimatedDelta);
                    current.jitterSamples++;
                }
                track.lastFinalDelta = finalDelta;
                track.lastAnimatedDelta = animatedDelta;
            }

            track.lastFinal = leg.finalShin;
            track.lastAnimated = leg.animatedShin;
            track.samples++;
        }
        #endregion

        #region [Private Methods]
        private void SampleFoot(FootPlanting.Leg leg, ref Vector3 previous, ref bool wasLocked, float dt)
        {
            if (leg == null || !leg.IsValid) return;
            Vector3 ankle = leg.end.position;

            current.maxTilt = Mathf.Max(current.maxTilt, leg.appliedTilt);

            bool solidLock = leg.locked && leg.lockWeight > 0.95f;
            if (solidLock && wasLocked)
            {
                Vector3 step = ankle - previous;
                step.y = 0f;
                float slide = step.magnitude / dt;
                current.slideSum += slide;
                current.slideSamples++;
                current.slideMax = Mathf.Max(current.slideMax, slide);
            }

            // In stance the ankle should sit its rest height above whatever ground is under it.
            if (solidLock && leg.hasGround && leg.grounded > 0.95f && motor.IsGrounded)
            {
                float error = Mathf.Abs(ankle.y - (leg.groundY + leg.restHeight));
                current.groundErrorSum += error;
                current.groundErrorSamples++;
                current.groundErrorMax = Mathf.Max(current.groundErrorMax, error);
            }

            previous = ankle;
            wasLocked = solidLock;
        }

        private IEnumerator Walk(string label, Vector3 from, Vector3 to)
        {
            yield return Move(label, from, to, Gait.Walk);
        }

        private IEnumerator Move(string label, Vector3 from, Vector3 to, Gait gait)
        {
            yield return Place(from, to - from);
            Begin(label, gait);
            LocomotionCommand command = driver.MoveTo(to, gait);
            yield return WaitFor(command);
            Vector3 offset = motor.transform.position - to;
            offset.y = 0f;
            current.arrivalError = offset.magnitude;
            End();
        }

        private IEnumerator FlatWalk()
        {
            Vector3[] path =
            {
                new Vector3(10f, 0f, -15f), new Vector3(15f, 0f, -10f), new Vector3(20f, 0f, -15f), new Vector3(20f, 0f, -25f)
            };
            yield return Place(new Vector3(10f, 0f, -25f), Vector3.forward);
            Begin("Flat walk with turns", Gait.Walk);
            LocomotionCommand command = driver.FollowPath(path, Gait.Walk);
            yield return WaitFor(command);
            Vector3 offset = motor.transform.position - path[path.Length - 1];
            offset.y = 0f;
            current.arrivalError = offset.magnitude;
            End();
        }

        private IEnumerator SprintStop()
        {
            yield return Place(new Vector3(20f, 0f, -32f), Vector3.forward);
            Begin("Sprint 2.5 s then stop", Gait.Sprint);
            yield return WaitFor(driver.MoveInDirection(Vector3.forward, Gait.Sprint, 2.5f));
            Vector3 stopFrom = motor.transform.position;
            yield return WaitFor(driver.Stop());
            // Measure from the frame the brake clip actually started (recorded by OnAction).
            if (current.hasStopStart) stopFrom = current.stopStart;
            Vector3 travelled = motor.transform.position - stopFrom;
            travelled.y = 0f;
            current.stopDistance = travelled.magnitude;
            End();
        }

        private IEnumerator Pivot()
        {
            yield return Place(new Vector3(25f, 0f, -32f), Vector3.forward);
            Begin("Sprint then reverse (pivot)", Gait.Sprint);
            yield return WaitFor(driver.MoveInDirection(Vector3.forward, Gait.Sprint, 2f));
            yield return WaitFor(driver.MoveInDirection(Vector3.back, Gait.Sprint, 1.5f));
            yield return WaitFor(driver.Stop());
            End();
        }

        private IEnumerator Drift()
        {
            yield return Place(new Vector3(-25f, 0f, -32f), Vector3.forward);
            Begin("SprintFast then 90° turn (drift)", Gait.SprintFast);
            yield return WaitFor(driver.MoveInDirection(Vector3.forward, Gait.SprintFast, 1.8f));
            yield return WaitFor(driver.MoveInDirection(Vector3.right, Gait.SprintFast, 1.2f));
            yield return WaitFor(driver.Stop());
            End();
        }

        private IEnumerator Dash()
        {
            yield return Place(new Vector3(0f, 0f, -32f), Vector3.forward);
            Begin("Dash from standing", Gait.Sprint);
            yield return WaitFor(driver.Dash(Vector3.forward));
            yield return WaitFor(driver.Stop());
            End();
        }

        private IEnumerator TurnStart()
        {
            yield return Place(new Vector3(-10f, 0f, -20f), Vector3.forward);
            Begin("Start running backwards (turn-first start)", Gait.Sprint);
            yield return WaitFor(driver.MoveInDirection(Vector3.back, Gait.Sprint, 1.2f));
            current.facingError = motor.FacingAngleTo(Vector3.back);
            yield return WaitFor(driver.Stop());
            End();
        }

        private IEnumerator RunLoop()
        {
            Vector3[] path =
            {
                new Vector3(-20f, 0f, -18f), new Vector3(-12f, 0f, -12f), new Vector3(-6f, 0f, -18f), new Vector3(-6f, 0f, -28f)
            };
            yield return Place(new Vector3(-20f, 0f, -30f), Vector3.forward);
            Begin("Run-loop with turns (authored)", Gait.Run);
            LocomotionCommand command = driver.FollowPath(path, Gait.Run);
            yield return WaitFor(command);
            Vector3 offset = motor.transform.position - path[path.Length - 1];
            offset.y = 0f;
            current.arrivalError = offset.magnitude;
            End();
        }

        private IEnumerator RunStartStop()
        {
            yield return Place(new Vector3(15f, 0f, -32f), Vector3.forward);
            Begin("Run-start + stop (authored clips)", Gait.Run);
            yield return WaitFor(driver.MoveInDirection(Vector3.forward, Gait.Run, 2f));
            Vector3 stopFrom = motor.transform.position;
            yield return WaitFor(driver.Stop());
            if (current.hasStopStart) stopFrom = current.stopStart;
            Vector3 travelled = motor.transform.position - stopFrom;
            travelled.y = 0f;
            current.stopDistance = travelled.magnitude;
            End();
        }

        private IEnumerator RunStartBack()
        {
            yield return Place(new Vector3(5f, 0f, -14f), Vector3.forward);
            Begin("Run-start 180 (authored clip)", Gait.Run);
            yield return WaitFor(driver.MoveInDirection(Vector3.back, Gait.Run, 1.6f));
            current.facingError = motor.FacingAngleTo(Vector3.back);
            yield return WaitFor(driver.Stop());
            End();
        }

        private IEnumerator WalkStop()
        {
            yield return Place(new Vector3(0f, 0f, -28f), Vector3.forward);
            Begin("Walk-stop (authored clip)", Gait.Walk);
            yield return WaitFor(driver.MoveInDirection(Vector3.forward, Gait.Walk, 2f));
            yield return WaitFor(driver.Stop());
            End();
        }

        private IEnumerator Place(Vector3 position, Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            driver.Cancel();
            motor.Teleport(position + Vector3.up * 0.05f, Quaternion.LookRotation(facing.normalized));
            if (feet != null) feet.ReleaseAll();
            float settle = 0f;
            while (settle < 0.6f)
            {
                settle += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator WaitFor(LocomotionCommand command)
        {
            float time = 0f;
            while (!command.IsDone && time < SegmentTimeout)
            {
                time += Time.deltaTime;
                yield return null;
            }
            if (!command.IsDone)
            {
                if (current != null) current.timedOut = true;
                driver.Cancel();
            }
        }

        private void Begin(string label, Gait gait)
        {
            current = new Segment { name = label, targetSpeed = motor.GetGaitSpeed(gait) };
            if (feet != null) current.locksStart = feet.LeftLeg.lockCount + feet.RightLeg.lockCount;
            segments.Add(current);
            previousLeftLocked = previousRightLocked = false;
            leftShin.samples = rightShin.samples = 0;
        }

        private void End()
        {
            if (current != null && feet != null)
                current.locks = feet.LeftLeg.lockCount + feet.RightLeg.lockCount - current.locksStart;
            current = null;
        }

        private void OnAction(LocomotionActionKind kind)
        {
            if (current == null) return;
            current.actions.TryGetValue(kind, out int count);
            current.actions[kind] = count + 1;

            if (kind == LocomotionActionKind.Stop && !current.hasStopStart)
            {
                current.hasStopStart = true;
                current.stopStart = motor.transform.position;
            }
        }

        private void OnDrift()
        {
            if (current != null) current.drifts++;
        }

        private void OnLanded(float impact)
        {
            if (current == null) return;
            current.landings++;
            current.maxLanding = Mathf.Max(current.maxLanding, impact);
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (errors.Count < 20) errors.Add(type + ": " + message);
            else if (errors.Count == 20) errors.Add("... more errors truncated");
        }

        private void Finish(string abortReason)
        {
            string report = abortReason ?? BuildReport(out _);
            string summary = abortReason ?? SummaryLine();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, report, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LocomotionSelfTest] Could not write report: " + exception.Message);
            }

            Debug.Log("[LocomotionSelfTest] " + summary + "\n" + report);
            Completed?.Invoke(ReportPath, summary);
            Destroy(this);
        }

        private int passCount, warnCount, failCount;

        private string SummaryLine()
        {
            return string.Format("{0} PASS / {1} WARN / {2} FAIL — {3}", passCount, warnCount, failCount, ReportPath);
        }

        private string BuildReport(out string summary)
        {
            passCount = warnCount = failCount = 0;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Locomotion Self Test");
            sb.AppendLine();
            sb.AppendLine($"Run: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ·  Unity {Application.unityVersion}");
            sb.AppendLine();
            sb.AppendLine("## Checks");
            sb.AppendLine();
            sb.AppendLine("| Result | Check | Measured | Expected |");
            sb.AppendLine("|---|---|---|---|");

            Check(sb, "No errors / exceptions during the run", errors.Count == 0, false, errors.Count.ToString(), "0");

            Segment flat = Find("Flat walk");
            Segment hill = Find("Hill");
            Segment stairs = Find("Stairs");
            Segment side = Find("Side slope");
            Segment bumps = Find("Bumps");
            Segment ledge = Find("Steep");
            Segment stop = Find("Sprint 2.5");
            Segment pivot = Find("pivot");
            Segment drift = Find("drift");
            Segment dash = Find("Dash");
            Segment turn = Find("turn-first");
            Segment runLoop = Find("Run-loop");
            Segment runStop = Find("Run-start +");
            Segment runBack = Find("Run-start 180");
            Segment walkStop = Find("Walk-stop");

            if (hill != null)
            {
                Check(sb, "Hill: feet tilt with the slope", hill.maxTilt >= 10f, hill.maxTilt >= 5f, $"{hill.maxTilt:0.0}°", "≥ 10° (slope 15°)");
                Check(sb, "Hill: hips drop for the lower foot", hill.minPelvis <= -0.02f, hill.minPelvis < -0.005f, $"{hill.minPelvis:0.000} m", "≤ -0.02 m");
                CheckGround(sb, "Hill", hill);
            }
            if (stairs != null)
            {
                Check(sb, "Stairs: hips drop between steps", stairs.minPelvis <= -0.05f, stairs.minPelvis <= -0.02f, $"{stairs.minPelvis:0.000} m", "≤ -0.05 m");
                // The 0.5 m capsule radius already rides up 15 cm step edges like a ramp; step smoothing only kicks
                // in for real pops. What matters is that the visible body never jumps.
                Check(sb, "Stairs: body moves up/down smoothly (no step pops)", stairs.maxBodyJump < 0.05f, stairs.maxBodyJump < 0.1f,
                    $"max {stairs.maxBodyJump:0.000} m/frame (smoothing used {stairs.maxStepSmoothing:0.000} m)", "< 0.05 m per frame");
                CheckGround(sb, "Stairs", stairs);
            }
            if (side != null)
                Check(sb, "Side slope: feet roll with the slope", side.maxTilt >= 8f, side.maxTilt >= 4f, $"{side.maxTilt:0.0}°", "≥ 8° (slope 12°)");
            if (bumps != null) CheckGround(sb, "Bumps", bumps);
            if (ledge != null)
                Check(sb, "Ledge drop: landing detected", ledge.landings >= 1 && ledge.maxLanding >= 5f, ledge.landings >= 1,
                    $"{ledge.landings} landing(s), {ledge.maxLanding:0.0} m/s", "≥ 1, impact ≥ 5 m/s");

            foreach (Segment walk in new[] { flat, hill, stairs, side, bumps, runLoop, runStop, walkStop })
            {
                if (walk == null || walk.slideSamples == 0) continue;
                float avg = walk.slideSum / walk.slideSamples;
                Check(sb, $"{Short(walk)}: locked feet do not slide", avg < 0.05f, avg < 0.15f, $"avg {avg:0.000} m/s, max {walk.slideMax:0.00}", "avg < 0.05 m/s");
            }

            foreach (Segment walk in new[] { flat, hill, stairs, side, bumps, runLoop })
            {
                if (walk == null || walk.jitterSamples == 0) continue;
                float jf = walk.JitterFinal, ja = walk.JitterAnimated;
                Check(sb, $"{Short(walk)}: legs do not flicker (shin jerk after IK vs animation)", jf <= ja * 1.5f + 0.3f, jf <= ja * 3f + 1f,
                    $"{jf:0.00}° vs animation {ja:0.00}°", "≤ animation × 1.5 + 0.3°");
                Check(sb, $"{Short(walk)}: feet lock once per step (no lock flicker)", walk.LocksPerSecond <= 4f, walk.LocksPerSecond <= 8f,
                    $"{walk.LocksPerSecond:0.0} locks/s", "≤ 4 per second (both feet)");
            }

            foreach (Segment walk in new[] { flat, hill, stairs, side, bumps, ledge, runLoop })
            {
                if (walk == null) continue;
                bool arrived = !walk.timedOut && walk.arrivalError >= 0f && walk.arrivalError < 0.3f;
                Check(sb, $"{Short(walk)}: reached the destination", arrived, !walk.timedOut,
                    walk.timedOut ? "timed out" : $"{walk.arrivalError:0.00} m off", "< 0.3 m, no timeout");
            }

            if (stop != null)
            {
                Check(sb, "Sprint reaches its target speed", stop.maxSpeed >= stop.targetSpeed * 0.9f, stop.maxSpeed >= stop.targetSpeed * 0.7f,
                    $"{stop.maxSpeed:0.0} m/s", $"≥ {stop.targetSpeed * 0.9f:0.0} m/s");
                Check(sb, "Sprint stop plays the brake clip", stop.Count(LocomotionActionKind.Stop) >= 1, false,
                    stop.Count(LocomotionActionKind.Stop).ToString(), "≥ 1");
                Check(sb, "Brake skid length", stop.stopDistance >= 0.3f && stop.stopDistance <= 3f, stop.stopDistance >= 0f,
                    $"{stop.stopDistance:0.00} m", "0.3 – 3 m");
            }
            if (drift != null)
            {
                Check(sb, "SprintFast reaches its target speed", drift.maxSpeed >= drift.targetSpeed * 0.9f, drift.maxSpeed >= drift.targetSpeed * 0.7f,
                    $"{drift.maxSpeed:0.0} m/s", $"≥ {drift.targetSpeed * 0.9f:0.0} m/s");
                Check(sb, "90° turn at speed drifts", drift.drifts >= 1, false, drift.drifts.ToString(), "≥ 1");
            }
            if (pivot != null)
                Check(sb, "Reversing at sprint pivots (brake + turn)", pivot.Count(LocomotionActionKind.Pivot) >= 1, pivot.Count(LocomotionActionKind.Stop) >= 1,
                    $"pivot {pivot.Count(LocomotionActionKind.Pivot)}, stop {pivot.Count(LocomotionActionKind.Stop)}", "pivot ≥ 1");
            if (dash != null)
                Check(sb, "Dash plays", dash.Count(LocomotionActionKind.Dash) >= 1, false, dash.Count(LocomotionActionKind.Dash).ToString(), "≥ 1");
            if (turn != null)
            {
                bool faced = turn.facingError >= 0f && turn.facingError < 20f;
                bool quick = turn.timeTo80 > 0f && turn.timeTo80 < 1f;
                Check(sb, "Backwards start: turns round, then reaches 80% speed within 1 s", faced && quick && !turn.timedOut, faced,
                    $"facing error {turn.facingError:0}°, 80% speed after {(turn.timeTo80 > 0f ? turn.timeTo80.ToString("0.00") + " s" : "never")}",
                    "< 20°, < 1 s");
            }
            if (runStop != null)
            {
                Check(sb, "Run start plays the authored start clip", runStop.Count(LocomotionActionKind.Start) >= 1, false,
                    runStop.Count(LocomotionActionKind.Start).ToString(), "≥ 1");
                Check(sb, "Run reaches its target speed", runStop.maxSpeed >= runStop.targetSpeed * 0.9f, runStop.maxSpeed >= runStop.targetSpeed * 0.7f,
                    $"{runStop.maxSpeed:0.0} m/s", $"≥ {runStop.targetSpeed * 0.9f:0.0} m/s");
                Check(sb, "Run stop plays an authored stop clip", runStop.Count(LocomotionActionKind.Stop) >= 1, false,
                    runStop.Count(LocomotionActionKind.Stop).ToString(), "≥ 1");
                Check(sb, "Run stop distance", runStop.stopDistance >= 0.3f && runStop.stopDistance <= 1.6f, runStop.stopDistance >= 0f,
                    $"{runStop.stopDistance:0.00} m", "0.3 – 1.6 m");
            }
            if (runBack != null)
            {
                bool faced = runBack.facingError >= 0f && runBack.facingError < 20f;
                Check(sb, "Run start backwards uses the 180 clip and ends facing the new way",
                    faced && runBack.Count(LocomotionActionKind.Start) >= 1, faced,
                    $"start clips {runBack.Count(LocomotionActionKind.Start)}, facing error {runBack.facingError:0}°", "≥ 1, < 20°");
            }
            if (walkStop != null)
                Check(sb, "Walk stop plays an authored stop clip", walkStop.Count(LocomotionActionKind.Stop) >= 1, false,
                    walkStop.Count(LocomotionActionKind.Stop).ToString(), "≥ 1");
            if (stop != null)
                Check(sb, "Sprint start: 80% speed within 0.6 s", stop.timeTo80 > 0f && stop.timeTo80 < 0.6f, stop.timeTo80 > 0f && stop.timeTo80 < 1.5f,
                    stop.timeTo80 > 0f ? $"{stop.timeTo80:0.00} s" : "never", "< 0.6 s");

            sb.AppendLine();
            sb.AppendLine("## Segments (raw)");
            sb.AppendLine();
            sb.AppendLine("| Segment | Max speed | Max tilt | Min pelvis | Step smooth | Body jump/frame | Ground err avg/max | Slide avg/max | Shin jerk IK/anim | Locks/s | Air frames | Actions | Drifts | Landings |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (Segment s in segments)
            {
                string actions = s.actions.Count == 0 ? "-" : string.Join(", ", ActionList(s));
                string ground = s.groundErrorSamples > 0 ? $"{s.groundErrorSum / s.groundErrorSamples:0.000} / {s.groundErrorMax:0.000}" : "-";
                string slide = s.slideSamples > 0 ? $"{s.slideSum / s.slideSamples:0.000} / {s.slideMax:0.00}" : "-";
                sb.AppendLine($"| {s.name}{(s.timedOut ? " ⚠ timeout" : "")} | {s.maxSpeed:0.0} | {s.maxTilt:0.0}° | {s.minPelvis:0.000} | {s.maxStepSmoothing:0.000} | {s.maxBodyJump:0.000} | {ground} | {slide} | {s.JitterFinal:0.00}° / {s.JitterAnimated:0.00}° | {s.LocksPerSecond:0.0} | {s.airFrames} | {actions} | {s.drifts} | {s.landings} |");
            }

            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Errors");
                sb.AppendLine();
                foreach (string error in errors) sb.AppendLine("- " + error.Replace("\n", " "));
            }

            summary = SummaryLine();
            sb.Insert(0, $"<!-- {summary} -->\n");
            return sb.ToString();
        }

        private void CheckGround(StringBuilder sb, string label, Segment s)
        {
            if (s.groundErrorSamples == 0)
            {
                Check(sb, $"{label}: planted feet sit on the ground", false, false, "no planted samples", "feet lock on contact");
                return;
            }
            float avg = s.groundErrorSum / s.groundErrorSamples;
            Check(sb, $"{label}: planted feet sit on the ground", avg < 0.04f, avg < 0.08f, $"avg {avg:0.000} m, max {s.groundErrorMax:0.000}", "avg < 0.04 m");
        }

        private void Check(StringBuilder sb, string check, bool pass, bool warnInstead, string measured, string expected)
        {
            string result;
            if (pass) { result = "✅ PASS"; passCount++; }
            else if (warnInstead) { result = "⚠️ WARN"; warnCount++; }
            else { result = "❌ FAIL"; failCount++; }
            sb.AppendLine($"| {result} | {check} | {measured} | {expected} |");
        }

        private Segment Find(string key)
        {
            foreach (Segment s in segments)
                if (s.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            return null;
        }

        private static string Short(Segment s)
        {
            int cut = s.name.IndexOf(' ');
            return cut > 0 ? s.name.Substring(0, cut) : s.name;
        }

        private static IEnumerable<string> ActionList(Segment s)
        {
            foreach (KeyValuePair<LocomotionActionKind, int> pair in s.actions) yield return $"{pair.Key}×{pair.Value}";
        }
        #endregion
    }
}
