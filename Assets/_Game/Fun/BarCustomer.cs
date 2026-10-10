using System.Collections;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// A guest sitting at a bar on a tall stool, facing the counter: one of the tourist women, chatting now and then.
    /// The host seats her where the builder put her (exactly: the stool is higher than the floor under her).
    /// </summary>
    [RequireComponent(typeof(StoryNpc))]
    public class BarCustomer : MonoBehaviour
    {
        [SerializeField] private string _name = "Guest";
        [Tooltip("AvatarLook.Bodies id of her model.")]
        [SerializeField] private byte _body;

        private static readonly string[] Chat =
        {
            "Another round!", "This cocktail is amazing.", "Did you see that lifeguard?", "Best fries on the island!",
            "I'm never going in the water again.", "Cheers, girls!"
        };

        private Vector3 _at;
        private float _yaw;

        // Where she was put, before the NPC snaps itself down onto the floor under the stool.
        private void Awake()
        {
            _at = transform.position;
            _yaw = transform.eulerAngles.y;
        }

        private IEnumerator Start()
        {
            var npc = GetComponent<StoryNpc>();
            while (!npc.IsSpawned) yield return null;
            if (!npc.IsServerInitialized) yield break; // the host seats her; everyone else sees it
            yield return null;
            Vector3 at = _at;
            float yaw = _yaw;
            var look = new AvatarLook { Body = _body, Figure = (byte)(AvatarLook.Bodies.IsFeminine(_body) ? 1 : 0) };
            npc.ServerSetup(_name, NpcRole.Bystander, look);
            npc.ServerSetPose(AvatarPose.SitChair);
            npc.ServerTeleport(at, yaw, keepExact: true);
            npc.ServerFace(at + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 4f);
            var wait = new WaitForSeconds(Random.Range(18f, 30f));
            while (isActiveAndEnabled)
            {
                yield return wait;
                if (npc.IsSpawned) npc.ServerSay(Chat[Random.Range(0, Chat.Length)], 3f);
            }
        }
    }
}
