using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DoreaRise.Locomotion
{
    public enum LocomotionCueType
    {
        MoveTo,
        FollowPath,
        MoveInDirection,
        Stop,
        FaceTowards,
        Dash,
        PlayAction,
        LookAt,
        ClearLookAt,
        Teleport
    }

    [Serializable]
    public class LocomotionCueClip : PlayableAsset, ITimelineClipAsset
    {
        #region [Variables/Fields]
        public LocomotionCueType cue = LocomotionCueType.MoveTo;
        [Tooltip("MoveTo / FaceTowards / Dash / LookAt / Teleport: the target. FollowPath: parent whose children are the waypoints. MoveInDirection: its forward")]
        public ExposedReference<Transform> target;
        public Gait gait = Gait.Sprint;
        [Tooltip("MoveTo / FollowPath: end facing the target's (last waypoint's) forward")]
        public bool matchTargetRotation;
        [Tooltip("MoveInDirection: seconds. <= 0 uses the clip length")]
        public float moveDuration = -1f;
        [Tooltip("PlayAction: the clip to play")]
        public AnimationClip actionClip;
        [Tooltip("PlayAction: inertialization half-life. <= 0 = clip set default")]
        public float blendHalfLife = -1f;
        #endregion

        #region [Properties]
        public ClipCaps clipCaps => ClipCaps.None;
        #endregion

        #region [Public Methods]
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<LocomotionCueBehaviour> playable = ScriptPlayable<LocomotionCueBehaviour>.Create(graph);
            LocomotionCueBehaviour behaviour = playable.GetBehaviour();
            behaviour.cue = cue;
            behaviour.target = target.Resolve(graph.GetResolver());
            behaviour.gait = gait;
            behaviour.matchTargetRotation = matchTargetRotation;
            behaviour.moveDuration = moveDuration;
            behaviour.actionClip = actionClip;
            behaviour.blendHalfLife = blendHalfLife;
            return playable;
        }
        #endregion
    }
}
