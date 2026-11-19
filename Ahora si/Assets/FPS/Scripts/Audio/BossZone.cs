using Unity.FPS.Gameplay;
using UnityEngine;

namespace Unity.FPS.Game
{
    // Trigger sphere placed around the boss. It tells the encounter when the
    // player walks into the fight area so the boss music can start there.
    public class BossZone : MonoBehaviour
    {
        public BossEncounter Encounter;

        void OnTriggerEnter(Collider other)
        {
            if (Encounter == null)
                return;

            if (other.GetComponentInParent<PlayerCharacterController>() != null)
            {
                Encounter.PlayerEnteredZone();
            }
        }
    }
}
