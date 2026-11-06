using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// On-screen readout of every locomotion layer (F1 toggles), so it is obvious in Play Mode which systems
    /// are active and what they are doing: speed / gait / cycle, actions, drift, feet, pelvis, lean, events.
    /// </summary>
    public class LocomotionDebugHUD : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private bool visible = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private int fontSize = 14;
        [SerializeField] private int maxEvents = 6;

        private LocomotionMotor motor;
        private FootPlanting feet;
        private LocomotionLean lean;
        private readonly List<string> events = new List<string>();
        private readonly StringBuilder text = new StringBuilder(512);
        private GUIStyle style;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            motor = GetComponentInParent<LocomotionMotor>();
            feet = GetComponentInChildren<FootPlanting>();
            lean = GetComponentInChildren<LocomotionLean>();
        }

        private void OnEnable()
        {
            if (motor == null) return;
            motor.ActionStarted += OnAction;
            motor.DriftStarted += OnDrift;
            motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (motor == null) return;
            motor.ActionStarted -= OnAction;
            motor.DriftStarted -= OnDrift;
            motor.Landed -= OnLanded;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || motor == null) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = fontSize,
                    richText = true,
                    padding = new RectOffset(10, 10, 8, 8)
                };
                style.normal.textColor = Color.white;
            }

            BuildText();
            GUIContent content = new GUIContent(text.ToString());
            Vector2 size = style.CalcSize(content);
            GUI.Box(new Rect(10f, 10f, size.x, size.y), content, style);
        }
        #endregion

        #region [Private Methods]
        private void BuildText()
        {
            text.Clear();
            LocomotionAnimator anim = motor.LocomotionAnimator;

            text.Append("<b>Locomotion</b>  <color=#888>(").Append(toggleKey).Append(" to hide)</color>\n");
            text.AppendFormat("Speed {0:0.0} m/s   target {1:0.0}   gait {2}\n",
                motor.CurrentSpeed, motor.GetGaitSpeed(motor.Gait), motor.Gait);

            if (anim != null)
            {
                text.AppendFormat("Cycle {0}   playback {1:0.00}x   phase {2:0.00}\n",
                    anim.ActiveGaitName, anim.PlaybackRate, anim.Phase);
                string action = anim.IsActionActive
                    ? anim.ActionKind + (anim.IsInFollowUp ? " (follow-up)" : "") + string.Format(" {0:0%}", anim.ActionNormalizedTime)
                    : "-";
                text.Append("Action ").Append(action);
            }

            text.Append(motor.IsDrifting ? "   <color=#ffb040>DRIFT</color>" : "");
            text.Append(motor.IsGrounded ? "   grounded" : string.Format("   <color=#60c0ff>AIR {0:0.00}s</color>", motor.AirTime));
            text.Append('\n');

            if (feet != null)
            {
                AppendFoot("L", feet.LeftLeg);
                text.Append("   ");
                AppendFoot("R", feet.RightLeg);
                text.AppendFormat("\nPelvis {0:+0.00;-0.00} m   step smoothing {1:+0.00;-0.00} m\n",
                    feet.PelvisOffset, motor.VisualStepOffset);
            }

            if (lean != null)
                text.AppendFormat("Lean roll {0:0}°  pitch {1:0}°  twist {2:0}°\n", lean.CurrentRoll, lean.CurrentPitch, lean.CurrentTwist);

            if (events.Count > 0)
            {
                text.Append("<color=#aaa>Events:</color>");
                for (int i = events.Count - 1; i >= 0; i--) text.Append("\n  ").Append(events[i]);
            }
        }

        private void AppendFoot(string side, FootPlanting.Leg leg)
        {
            if (leg == null || !leg.IsValid)
            {
                text.Append(side).Append(" <color=#f55>no bones</color>");
                return;
            }

            string state = leg.locked ? "<color=#5f5>LOCK</color>" : (leg.grounded > 0.5f ? "ground" : "air");
            text.AppendFormat("{0} {1} tilt {2:0}° offset {3:+0.00;-0.00}", side, state, leg.appliedTilt, leg.groundOffset);
        }

        private void Log(string message)
        {
            events.Add(string.Format("{0:0.0}s  {1}", Time.time, message));
            while (events.Count > maxEvents) events.RemoveAt(0);
        }

        private void OnAction(LocomotionActionKind kind)
        {
            Log(string.Format("{0} (entry {1:0.0} m/s)", kind, motor.ActionEntrySpeed));
        }

        private void OnDrift()
        {
            Log(string.Format("Drift ({0:0.0} m/s)", motor.CurrentSpeed));
        }

        private void OnLanded(float impact)
        {
            Log(string.Format("Landed ({0:0.0} m/s)", impact));
        }
        #endregion
    }
}
