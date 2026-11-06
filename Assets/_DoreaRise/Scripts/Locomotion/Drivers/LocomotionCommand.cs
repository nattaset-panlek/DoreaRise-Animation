using System;
using UnityEngine;

namespace DoreaRise.Locomotion
{
    /// <summary>
    /// Handle returned by ScriptedLocomotionDriver commands.
    /// Can be yielded from a coroutine: <c>yield return driver.MoveTo(point);</c>
    /// </summary>
    public class LocomotionCommand : CustomYieldInstruction
    {
        #region [Variables/Fields]
        private Action<LocomotionCommand> completed;
        #endregion

        #region [Properties]
        public string Name { get; }
        public bool IsDone { get; private set; }
        public bool WasCancelled { get; private set; }
        public override bool keepWaiting => !IsDone;
        #endregion

        public LocomotionCommand(string name)
        {
            Name = name;
        }

        #region [Public Methods]
        /// <summary>Invoked once when the command completes or is cancelled (immediately if already done).</summary>
        public LocomotionCommand OnCompleted(Action<LocomotionCommand> callback)
        {
            if (callback == null) return this;
            if (IsDone) callback(this);
            else completed += callback;
            return this;
        }

        internal void Finish(bool cancelled)
        {
            if (IsDone) return;
            IsDone = true;
            WasCancelled = cancelled;
            Action<LocomotionCommand> callback = completed;
            completed = null;
            callback?.Invoke(this);
        }
        #endregion
    }
}
