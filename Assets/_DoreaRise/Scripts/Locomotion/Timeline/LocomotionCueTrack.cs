using UnityEngine.Timeline;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Timeline track that sends locomotion commands (move, stop, face, dash, play clip, look at) to a
    /// ScriptedLocomotionDriver. Each clip fires its command once when the playhead enters it, in Play Mode.
    /// </summary>
    [TrackColor(0.95f, 0.55f, 0.2f)]
    [TrackClipType(typeof(LocomotionCueClip))]
    [TrackBindingType(typeof(ScriptedLocomotionDriver))]
    public class LocomotionCueTrack : TrackAsset
    {
    }
}
