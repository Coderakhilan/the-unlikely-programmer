using System;
using System.Collections;
using System.Collections.Generic;
using Unlikely.Interaction;
using Unlikely.Player;
using Unlikely.UI;
using UnityEngine;

namespace Unlikely.NPC
{
    /// <summary>
    /// A person in the building. Talking to them pops a line over their head (no dialogue box); repeated talks
    /// cycle through their lines. The story can make them walk a route, change their lines or remove them.
    /// </summary>
    public class Npc : Interactable, IWalker
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string[] barks = { "..." };
        [SerializeField] private Vector2 idleFacing = Vector2.down;
        [SerializeField] private float speed = 1.6f;   // metres per second, scaled by the NPC's size

        public string Id => id;
        public string DisplayName => displayName;
        public Vector2 Facing { get; private set; } = Vector2.down;
        public bool IsMoving { get; private set; }
        public bool IsWalking => walk != null;

        /// <summary>Set by the story to replace the default "say a line" behaviour.</summary>
        public Action OnTalk;
        public bool Talkable = true;

        private int barkIndex;
        private Coroutine walk;

        public override string Prompt => Talkable ? "Talk to " + displayName : null;

        public void Configure(string npcId, string name, Vector2 facing, params string[] lines)
        {
            id = npcId;
            displayName = name;
            idleFacing = facing;
            barks = lines;
        }

        private void OnEnable()
        {
            if (!IsWalking) Facing = idleFacing;
        }

        public void SetBarks(params string[] lines)
        {
            barks = lines;
            barkIndex = 0;
        }

        public void Bark(string line, float seconds = 3.2f) => BarkUI.Instance?.Show(transform, line, seconds);

        public override void Interact(PlayerController player)
        {
            if (!Talkable) return;
            // turn to face Donnie
            Vector2 d = player.transform.position - transform.position;
            if (!IsWalking) Facing = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y));
            if (OnTalk != null) { OnTalk(); return; }
            if (barks == null || barks.Length == 0) return;
            Bark(barks[barkIndex % barks.Length]);
            barkIndex++;
        }

        public void FaceIdle() { if (!IsWalking) Facing = idleFacing; }

        /// <summary>For components that move the NPC themselves (patrols): set the facing and walk animation.</summary>
        public void Drive(Vector2 facing, bool moving)
        {
            if (facing.sqrMagnitude > 0.001f) Facing = facing;
            IsMoving = moving;
        }
        public void Face(Vector2 dir) => Facing = dir;

        /// <summary>Walk through world positions in order, then call onDone.</summary>
        public void WalkPath(IList<Vector3> points, Action onDone = null)
        {
            if (walk != null) Host.StopCoroutine(walk);
            walk = Host.StartCoroutine(Walk(points, onDone));
        }

        public void StopWalking()
        {
            if (walk != null && host != null) host.StopCoroutine(walk);
            walk = null;
            IsMoving = false;
        }

        private IEnumerator Walk(IList<Vector3> points, Action onDone)
        {
            foreach (var p in points)
            {
                while (true)
                {
                    if (this == null) yield break;
                    Vector3 to = p - transform.position;
                    to.z = 0;
                    float step = speed * transform.localScale.x * Time.deltaTime;
                    if (to.magnitude <= step) { transform.position = new Vector3(p.x, p.y, transform.position.z); break; }
                    IsMoving = true;
                    Facing = Mathf.Abs(to.x) > Mathf.Abs(to.y) ? new Vector2(Mathf.Sign(to.x), 0) : new Vector2(0, Mathf.Sign(to.y));
                    transform.position += to.normalized * step;
                    yield return null;
                }
            }
            IsMoving = false;
            walk = null;
            onDone?.Invoke();
        }

        // Walks run on an always-active host so people keep moving while their room is hidden.
        private static MonoBehaviour host;
        private static MonoBehaviour Host
        {
            get
            {
                if (host == null) host = new GameObject("NpcWalkHost").AddComponent<WalkHost>();
                return host;
            }
        }
        private sealed class WalkHost : MonoBehaviour { }

        private static List<Npc> all;

        /// <summary>Find a person by id, including ones in hidden rooms.</summary>
        /// <summary>Every character in the scene, hidden ones too (saving and loading where everyone stands).</summary>
        public static IReadOnlyList<Npc> All
        {
            get
            {
                if (all == null) all = new List<Npc>(FindObjectsByType<Npc>(FindObjectsInactive.Include));
                return all;
            }
        }

        public static Npc Find(string npcId)
        {
            if (all == null) all = new List<Npc>(FindObjectsByType<Npc>(FindObjectsInactive.Include));
            return all.Find(n => n != null && n.id == npcId);
        }

        private void OnDestroy() => all = null;
    }
}
