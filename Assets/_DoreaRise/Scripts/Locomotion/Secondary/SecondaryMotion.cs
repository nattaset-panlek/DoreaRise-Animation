using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Dorea.CharacterTailComponent;

namespace DoreaRise.Locomotion
{
    public enum SpringProfile
    {
        Hair,
        Breast,
        Butt,
        Tail
    }

    [Serializable]
    public class SpringSettings
    {
        [Tooltip("Off = bones keep their animation only (e.g. hair that already has baked secondary motion)")]
        public bool enabled = true;
        [Tooltip("Amplitude of the spring offset: 0.6 = 40% less swing than the full simulation")]
        [Range(0f, 1f)] public float weight = 1f;
        [Tooltip("Velocity lost per step (air drag). Higher = calmer, lower = floatier")]
        [Range(0f, 1f)] public float damping = 0.12f;
        [Tooltip("Pull back towards the animated pose per step")]
        [Range(0f, 1f)] public float elasticity = 0.06f;
        [Tooltip("How far a joint may stray from the animated pose (1 = locked to the animation)")]
        [Range(0f, 1f)] public float stiffness = 0.3f;
        [Tooltip("Stiffness multiplier from the first simulated joint (t=0) to the tip (t=1)")]
        public AnimationCurve stiffnessFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.35f);
        [Tooltip("1 = moves rigidly with the character (no lag from walking/running). 0 = full world-space lag")]
        [Range(0f, 1f)] public float inertia = 0.5f;
        [Tooltip("Cap on the travel speed the joints 'feel' (m/s), so a 15 m/s sprint does not blow everything straight back")]
        public float maxLagSpeed = 3f;
        [Tooltip("World acceleration (m/s²)")]
        public Vector3 gravity = new Vector3(0f, -1f, 0f);
        [Tooltip("Extra follow-through from the motor's acceleration (anime exaggeration). 0 = physical only")]
        public float accelerationBoost = 0.6f;
        [Tooltip("Collision radius of each joint (m)")]
        public float radius = 0.02f;
        public bool collide = true;
        [Tooltip("Single-bone chains: lever length (m) along tipAxis")]
        public float tipLength = 0.1f;
        [Tooltip("Single-bone chains: bone-local direction of the lever (Blender bones point along +Y)")]
        public Vector3 tipAxis = Vector3.up;
    }

    [Serializable]
    public class SpringChain
    {
        public string name;
        public SpringProfile profile;
        public bool enabled = true;
        [Tooltip("Root to tip. Bones may be a real hierarchy or siblings (flat Blender export)")]
        public List<Transform> bones = new List<Transform>();

        [NonSerialized] public Vector3[] position;
        [NonSerialized] public Vector3[] previous;
        [NonSerialized] public Vector3[] rest;
        [NonSerialized] public Vector3[] restLocal;
        [NonSerialized] public Vector3 tipLocal;
        [NonSerialized] public bool[] flatChild;
        [NonSerialized] public Vector3[] initialLocalPosition;
        [NonSerialized] public Quaternion[] initialLocalRotation;
        [NonSerialized] public bool poseCaptured;
        [NonSerialized] public bool initialized;

        public bool IsValid
        {
            get
            {
                if (bones == null || bones.Count == 0) return false;
                foreach (Transform bone in bones) if (bone == null) return false;
                return true;
            }
        }
    }

    [Serializable]
    public class SpringCollider
    {
        public Transform bone;
        public Vector3 offset;
        [Tooltip("World radius (m)")]
        public float radius = 0.1f;

        public Vector3 Center => bone.TransformPoint(offset);
    }

    /// <summary>
    /// Spring bones for hair, tail, breasts and butt: joints lag behind and overshoot the animated pose
    /// from character movement and from the motor's acceleration, then settle back.
    /// Verlet integration on a fixed step (deterministic takes), Dynamic-Bone style constraints.
    /// Runs after the animation and all other rig scripts (look-at, lean, foot/hand IK, CharacterTail).
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class SecondaryMotion : MonoBehaviour
    {
        private static readonly Regex SegmentSuffix = new Regex(@"^(.+)\.(\d{3})$");

        #region [Variables/Fields]
        [Header("Global")]
        [Range(0f, 1f)] [SerializeField] private float weight = 1f;
        [Tooltip("Simulation steps per second (fixed, so recorded takes are repeatable)")]
        [SerializeField] private float stepRate = 60f;
        [SerializeField] private int maxStepsPerFrame = 4;
        [Tooltip("Character moves further than this in one frame = teleport, simulation restarts")]
        [SerializeField] private float teleportDistance = 1f;
        [SerializeField] private LocomotionMotor motor;

        [Header("Profiles")]
        [Tooltip("Off by default: the clips already carry baked hair motion")]
        [SerializeField] private SpringSettings hair = new SpringSettings { enabled = false };
        [Tooltip("Off by default (user preference)")]
        [SerializeField] private SpringSettings breast = new SpringSettings
        {
            enabled = false, damping = 0.2f, elasticity = 0.2f, stiffness = 0.4f, inertia = 0.95f, gravity = Vector3.zero,
            accelerationBoost = 0.3f, collide = false, tipLength = 0.08f
        };
        [SerializeField] private SpringSettings butt = new SpringSettings
        {
            damping = 0.25f, elasticity = 0.25f, stiffness = 0.5f, inertia = 0.95f, gravity = Vector3.zero,
            accelerationBoost = 0.2f, collide = false, tipLength = 0.08f
        };
        [SerializeField] private SpringSettings tail = new SpringSettings
        {
            weight = 0.6f, damping = 0.1f, elasticity = 0.05f, stiffness = 0.2f, inertia = 0.4f, maxLagSpeed = 2.5f,
            gravity = Vector3.zero,
            accelerationBoost = 0.8f, collide = false, stiffnessFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.3f)
        };

        [Header("Tail Attachment")]
        [Tooltip("Moves the tail root in character space (x right, y up, z forward). Negative z pulls it out of the body")]
        [SerializeField] private Vector3 tailRootOffset = new Vector3(0f, 0f, -0.04f);

        [Header("Auto Setup (used when Chains is empty)")]
        [SerializeField] private string[] hairPrefixes = { "Hair." };
        [SerializeField] private string[] breastBones = { "Breast.L", "Breast.R" };
        [SerializeField] private string[] buttBones = { "Butt.L", "Butt.R" };
        [Tooltip("Simulate the CharacterTail bones here and switch off its own jiggle")]
        [SerializeField] private bool takeOverTailJiggle = true;
        [SerializeField] private string headBone = "Head";
        [SerializeField] private string neckBone = "Spine.004";
        [SerializeField] private string chestBone = "Spine.003";

        [Header("Chains & Colliders")]
        [SerializeField] private List<SpringChain> chains = new List<SpringChain>();
        [Tooltip("Empty = head / neck / chest spheres are fitted automatically on the first frame")]
        [SerializeField] private List<SpringCollider> colliders = new List<SpringCollider>();

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        private float accumulator;
        private Vector3 lastRootPosition;
        private bool hasRootPosition;
        private bool autoColliders;
        #endregion

        #region [Properties]
        public float Weight
        {
            get => weight;
            set => weight = Mathf.Clamp01(value);
        }

        public IReadOnlyList<SpringChain> Chains => chains;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<LocomotionMotor>();
            if (chains.Count == 0) AutoDetectChains();
            autoColliders = colliders.Count == 0;

            // Two springs in series feel floaty: the tail is simulated here only.
            CharacterTail characterTail = GetComponentInChildren<CharacterTail>(true);
            if (characterTail != null && tail.enabled && chains.Exists(c => c.enabled && c.profile == SpringProfile.Tail))
                characterTail.IsJiggle = false;

            foreach (SpringChain chain in chains)
                CaptureInitialPose(chain);
        }

        private void OnEnable()
        {
            if (motor != null) motor.Teleported += OnTeleported;
        }

        private void OnDisable()
        {
            if (motor != null) motor.Teleported -= OnTeleported;
        }

        private void OnTeleported()
        {
            ResetSimulation();
            hasRootPosition = false;
        }

        private void Update()
        {
            // Bones nobody else resets (e.g. the tail) would accumulate our offsets: start each frame from
            // the initial pose. Animated bones are overwritten by the Animator right after this anyway.
            foreach (SpringChain chain in chains)
            {
                if (IsSimulated(chain) || chain.profile == SpringProfile.Tail)
                    RestoreInitialPose(chain);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 rootPosition = transform.position;
            Vector3 rootMove = hasRootPosition ? rootPosition - lastRootPosition : Vector3.zero;
            lastRootPosition = rootPosition;
            hasRootPosition = true;

            if (rootMove.magnitude > teleportDistance) ResetSimulation();

            bool anyInitialized = false;
            foreach (SpringChain chain in chains)
            {
                if (!chain.enabled || !chain.IsValid) continue;

                // Attachment fix applies even when the tail spring itself is off.
                if (chain.profile == SpringProfile.Tail && tailRootOffset.sqrMagnitude > 0f)
                    chain.bones[0].position += transform.TransformDirection(tailRootOffset);

                if (!IsSimulated(chain)) continue;
                if (!chain.initialized) InitializeChain(chain);
                anyInitialized = true;
            }

            // One attempt only: with no simulated hair there is nothing to fit, and retrying allocates every frame.
            if (anyInitialized && autoColliders)
            {
                autoColliders = false;
                if (colliders.Count == 0) FitAutoColliders();
            }

            accumulator += dt;
            float step = 1f / Mathf.Max(1f, stepRate);
            int steps = Mathf.FloorToInt(accumulator / step);
            if (steps > maxStepsPerFrame)
            {
                steps = maxStepsPerFrame;
                accumulator = 0f;
            }
            else
            {
                accumulator -= steps * step;
            }

            Vector3 acceleration = motor != null ? motor.Acceleration : Vector3.zero;

            foreach (SpringChain chain in chains)
            {
                if (!IsSimulated(chain) || !chain.initialized) continue;

                SpringSettings settings = GetSettings(chain.profile);
                // Carry the joints with the character every frame (also on frames with no simulation step),
                // so only the intended part of the travel is felt as lag.
                CarryWithRoot(chain, settings, rootMove, dt);
                CaptureRest(chain);
                for (int s = 0; s < steps; s++)
                    Step(chain, settings, step, acceleration);
                Apply(chain, settings.weight * weight);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            foreach (SpringChain chain in chains)
            {
                if (!chain.IsValid) continue;
                if (Application.isPlaying && chain.initialized && chain.position != null)
                {
                    for (int i = 1; i < chain.position.Length; i++)
                        Gizmos.DrawLine(chain.position[i - 1], chain.position[i]);
                }
                else
                {
                    for (int i = 1; i < chain.bones.Count; i++)
                        Gizmos.DrawLine(chain.bones[i - 1].position, chain.bones[i].position);
                }
            }

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
            foreach (SpringCollider collider in colliders)
                if (collider != null && collider.bone != null)
                    Gizmos.DrawWireSphere(collider.Center, collider.radius);
        }
        #endregion

        #region [Public Methods]
        /// <summary>Restart from the current animated pose (after a cut or teleport).</summary>
        public void ResetSimulation()
        {
            foreach (SpringChain chain in chains) chain.initialized = false;
            accumulator = 0f;
        }

        [ContextMenu("Auto Detect Chains")]
        public void AutoDetectChains()
        {
            chains.Clear();
            Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);

            foreach (Transform t in byName.Values)
            {
                if (!StartsWithAny(t.name, hairPrefixes) || SegmentSuffix.IsMatch(t.name)) continue;

                SpringChain chain = new SpringChain { name = t.name, profile = SpringProfile.Hair };
                chain.bones.Add(t);
                for (int index = 1; index < 100; index++)
                {
                    if (!byName.TryGetValue($"{t.name}.{index:000}", out Transform next)) break;
                    chain.bones.Add(next);
                }
                chains.Add(chain);
            }

            AddSingleBoneChains(byName, breastBones, SpringProfile.Breast);
            AddSingleBoneChains(byName, buttBones, SpringProfile.Butt);

            CharacterTail characterTail = GetComponentInChildren<CharacterTail>(true);
            if (takeOverTailJiggle && characterTail != null && characterTail.Bones != null && characterTail.Bones.Count > 1)
            {
                SpringChain chain = new SpringChain { name = "Tail", profile = SpringProfile.Tail };
                foreach (TailBone bone in characterTail.Bones)
                    if (bone != null && bone.transform != null) chain.bones.Add(bone.transform);
                chains.Add(chain);
                if (Application.isPlaying) characterTail.IsJiggle = false;
            }

            chains.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }
        #endregion

        #region [Private Methods]
        private bool IsSimulated(SpringChain chain)
        {
            return chain.enabled && chain.IsValid && GetSettings(chain.profile).enabled;
        }

        private SpringSettings GetSettings(SpringProfile profile)
        {
            switch (profile)
            {
                case SpringProfile.Breast: return breast;
                case SpringProfile.Butt: return butt;
                case SpringProfile.Tail: return tail;
                default: return hair;
            }
        }

        private void AddSingleBoneChains(Dictionary<string, Transform> byName, string[] names, SpringProfile profile)
        {
            foreach (string boneName in names)
            {
                if (!byName.TryGetValue(boneName, out Transform bone)) continue;
                SpringChain chain = new SpringChain { name = boneName, profile = profile };
                chain.bones.Add(bone);
                chains.Add(chain);
            }
        }

        private static bool StartsWithAny(string value, string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (!string.IsNullOrEmpty(prefix) && value.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void CaptureInitialPose(SpringChain chain)
        {
            if (!chain.IsValid) return;
            int count = chain.bones.Count;
            chain.initialLocalPosition = new Vector3[count];
            chain.initialLocalRotation = new Quaternion[count];
            chain.flatChild = new bool[count + 1];
            for (int i = 0; i < count; i++)
            {
                chain.initialLocalPosition[i] = chain.bones[i].localPosition;
                chain.initialLocalRotation[i] = chain.bones[i].localRotation;
                chain.flatChild[i] = i > 0 && chain.bones[i].parent != chain.bones[i - 1];
            }
            chain.poseCaptured = true;
        }

        private static void RestoreInitialPose(SpringChain chain)
        {
            if (!chain.poseCaptured || !chain.enabled || !chain.IsValid) return;
            for (int i = 0; i < chain.bones.Count; i++)
            {
                chain.bones[i].localRotation = chain.initialLocalRotation[i];
                // Root: undo the tail attachment offset. Flat children: undo the carried position.
                if (i == 0 || chain.flatChild[i]) chain.bones[i].localPosition = chain.initialLocalPosition[i];
            }
        }

        private void InitializeChain(SpringChain chain)
        {
            if (!chain.poseCaptured) CaptureInitialPose(chain);

            SpringSettings settings = GetSettings(chain.profile);
            int count = chain.bones.Count;
            int points = count + 1;
            chain.position = new Vector3[points];
            chain.previous = new Vector3[points];
            chain.rest = new Vector3[points];
            chain.restLocal = new Vector3[points];

            Transform last = chain.bones[count - 1];
            if (count >= 2)
            {
                Vector3 segment = last.position - chain.bones[count - 2].position;
                chain.tipLocal = last.InverseTransformVector(segment);
            }
            else
            {
                Vector3 axis = last.TransformDirection(settings.tipAxis.sqrMagnitude > 0f ? settings.tipAxis.normalized : Vector3.up);
                chain.tipLocal = last.InverseTransformVector(axis * settings.tipLength);
            }

            CaptureRest(chain);
            for (int i = 0; i < points; i++)
            {
                chain.position[i] = chain.rest[i];
                chain.previous[i] = chain.rest[i];
            }
            chain.initialized = true;
        }

        /// <summary>Animated (target) pose of this frame, before any spring offset is applied.</summary>
        private static void CaptureRest(SpringChain chain)
        {
            int count = chain.bones.Count;
            for (int i = 0; i < count; i++)
                chain.rest[i] = chain.bones[i].position;

            Transform last = chain.bones[count - 1];
            chain.rest[count] = last.position + last.TransformVector(chain.tipLocal);

            for (int i = 1; i <= count; i++)
                chain.restLocal[i] = chain.bones[i - 1].InverseTransformVector(chain.rest[i] - chain.rest[i - 1]);
        }

        /// <summary>
        /// Moves the simulated joints along with the character by the part of its travel that should NOT be felt
        /// (inertia), keeping their velocity. The felt part is capped at maxLagSpeed.
        /// </summary>
        private static void CarryWithRoot(SpringChain chain, SpringSettings settings, Vector3 rootMove, float dt)
        {
            Vector3 felt = rootMove * (1f - settings.inertia);
            float maxFelt = Mathf.Max(0f, settings.maxLagSpeed) * dt;
            if (felt.sqrMagnitude > maxFelt * maxFelt) felt = felt.normalized * maxFelt;
            Vector3 carried = rootMove - felt;
            if (carried.sqrMagnitude < 1e-12f) return;

            for (int i = 1; i < chain.position.Length; i++)
            {
                chain.position[i] += carried;
                chain.previous[i] += carried;
            }
        }

        private void Step(SpringChain chain, SpringSettings settings, float h, Vector3 acceleration)
        {
            Vector3[] position = chain.position;
            Vector3[] previous = chain.previous;
            Vector3[] rest = chain.rest;
            int points = position.Length;

            position[0] = rest[0];
            previous[0] = rest[0];

            Vector3 force = (settings.gravity - acceleration * settings.accelerationBoost) * (h * h);
            float keep = 1f - settings.damping;

            for (int i = 1; i < points; i++)
            {
                Vector3 velocity = position[i] - previous[i];
                previous[i] = position[i];
                position[i] += velocity * keep + force;
            }

            for (int i = 1; i < points; i++)
            {
                Vector3 restOffset = rest[i] - rest[i - 1];
                float restLength = restOffset.magnitude;
                Vector3 target = position[i - 1] + restOffset;

                position[i] += (target - position[i]) * settings.elasticity;

                float t = points > 2 ? (i - 1f) / (points - 2f) : 1f;
                float stiffness = Mathf.Clamp01(settings.stiffness * settings.stiffnessFalloff.Evaluate(t));
                if (stiffness > 0f)
                {
                    float maxDeviation = restLength * (1f - stiffness) * 2f;
                    Vector3 deviation = position[i] - target;
                    float deviationLength = deviation.magnitude;
                    if (deviationLength > maxDeviation && deviationLength > 1e-6f)
                        position[i] = target + deviation * (maxDeviation / deviationLength);
                }

                if (settings.collide)
                {
                    foreach (SpringCollider collider in colliders)
                    {
                        if (collider == null || collider.bone == null) continue;
                        Vector3 center = collider.Center;
                        float minDistance = collider.radius + settings.radius;
                        Vector3 away = position[i] - center;
                        float distanceSq = away.sqrMagnitude;
                        if (distanceSq < minDistance * minDistance && distanceSq > 1e-10f)
                            position[i] = center + away * (minDistance / Mathf.Sqrt(distanceSq));
                    }
                }

                Vector3 segment = position[i] - position[i - 1];
                float length = segment.magnitude;
                if (length > 1e-6f) position[i] = position[i - 1] + segment * (restLength / length);
            }
        }

        private static void Apply(SpringChain chain, float blend)
        {
            if (blend <= 0f) return;

            int count = chain.bones.Count;
            Vector3 previousTarget = chain.rest[0];
            for (int i = 1; i <= count; i++)
            {
                // Blend in position space so weight scales the swing amplitude uniformly along the chain.
                Vector3 target = Vector3.LerpUnclamped(chain.rest[i], chain.position[i], blend);
                Transform parentBone = chain.bones[i - 1];
                Vector3 from = parentBone.TransformVector(chain.restLocal[i]);
                Vector3 to = target - previousTarget;
                previousTarget = target;
                if (from.sqrMagnitude > 1e-10f && to.sqrMagnitude > 1e-10f)
                    parentBone.rotation = Quaternion.FromToRotation(from, to) * parentBone.rotation;

                // Flat chains: carry the next segment to the end of the rotated one.
                if (i < count && chain.flatChild[i])
                    chain.bones[i].position = parentBone.position + parentBone.TransformVector(chain.restLocal[i]);
            }
        }

        private void FitAutoColliders()
        {
            Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);

            List<SpringChain> hairChains = chains.FindAll(c => c.profile == SpringProfile.Hair && c.initialized);
            if (hairChains.Count == 0) return;

            if (byName.TryGetValue(headBone, out Transform head))
            {
                Vector3 center = Vector3.zero;
                foreach (SpringChain chain in hairChains) center += chain.rest[0];
                center /= hairChains.Count;

                float radius = 0f;
                foreach (SpringChain chain in hairChains) radius += Vector3.Distance(chain.rest[0], center);
                radius = radius / hairChains.Count * 0.9f;
                AddFittedCollider(head, center, radius, hairChains);
            }

            if (byName.TryGetValue(neckBone, out Transform neck))
                AddFittedCollider(neck, neck.position, 0.06f, hairChains);

            if (byName.TryGetValue(chestBone, out Transform chest))
                AddFittedCollider(chest, chest.position, 0.13f, hairChains);
        }

        /// <summary>Adds a sphere shrunk so that no joint of the current (animated) pose starts inside it.</summary>
        private void AddFittedCollider(Transform bone, Vector3 center, float radius, List<SpringChain> hairChains)
        {
            foreach (SpringChain chain in hairChains)
            {
                for (int i = 1; i < chain.rest.Length; i++)
                    radius = Mathf.Min(radius, Vector3.Distance(chain.rest[i], center) - hair.radius - 0.005f);
            }

            if (radius < 0.02f) return;
            colliders.Add(new SpringCollider { bone = bone, offset = bone.InverseTransformPoint(center), radius = radius });
        }
        #endregion
    }
}
