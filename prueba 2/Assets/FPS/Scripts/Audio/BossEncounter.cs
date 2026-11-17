using System.Collections;
using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Game
{
    // Turns the toughest enemy of the level into a boss and drives the boss
    // music. The rhythm starts when the player walks into the fight area, the
    // bass joins on the next loop if the boss still sees the player, and the
    // melody waits for a loop as well so every layer enters on the bar line.
    public class BossEncounter : MonoBehaviour
    {
        public float HealthMultiplier = 4f;

        [Tooltip("Radius of the trigger sphere placed around the boss")]
        public float RoomRadius = 16f;

        [Tooltip("Distance at which the player is considered out of the fight")]
        public float ExitRadius = 24f;

        public float RoomFade = 0.8f;
        public float LayerFade = 0.25f;
        public float DeathFade = 2.5f;

        EnemyController m_Boss;
        Health m_BossHealth;
        AudioMixer m_Mixer;
        Transform m_Player;
        GameObject m_Zone;

        bool m_InRoom;
        bool m_Phase2;
        bool m_Dead;

        bool m_WaitingForBass;
        bool m_WaitingForMelody;
        double m_NextLoopTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "LoseScene" || scene.name == "WinScene" || scene.name == "IntroMenu")
                return;

            if (FindObjectOfType<PlayerCharacterController>() != null)
            {
                GameObject go = new GameObject("Boss Encounter");
                go.AddComponent<BossEncounter>();
            }
        }

        void Start()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager != null && audioManager.AudioMixers.Length > 0)
                m_Mixer = audioManager.AudioMixers[0];

            StartCoroutine(SetupWhenReady());
        }

        IEnumerator SetupWhenReady()
        {
            float timeout = 5f;
            while (timeout > 0f)
            {
                m_Player = FindObjectOfType<PlayerCharacterController>()?.transform;
                EnemyManager enemyManager = FindObjectOfType<EnemyManager>();

                if (m_Player != null && enemyManager != null && enemyManager.Enemies.Count > 0)
                {
                    m_Boss = PickBoss(enemyManager);
                    break;
                }

                timeout -= Time.deltaTime;
                yield return null;
            }

            if (m_Boss == null)
                yield break;

            m_BossHealth = m_Boss.GetComponent<Health>();
            if (m_BossHealth != null)
            {
                m_BossHealth.MaxHealth *= HealthMultiplier;
                m_BossHealth.CurrentHealth = m_BossHealth.MaxHealth;
                m_BossHealth.OnDamaged += OnBossDamaged;
                m_BossHealth.OnDie += OnBossDied;
            }

            if (m_Boss.DetectionModule != null)
                m_Boss.DetectionModule.onDetectedTarget += OnBossDetected;

            CreateZone();
        }

        EnemyController PickBoss(EnemyManager enemyManager)
        {
            EnemyController boss = null;
            float farthest = float.NegativeInfinity;
            List<EnemyController> enemies = enemyManager.Enemies;

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyController enemy = enemies[i];
                if (enemy == null)
                    continue;

                float distance = Vector3.Distance(Flat(m_Player.position), Flat(enemy.transform.position));
                if (distance > farthest)
                {
                    farthest = distance;
                    boss = enemy;
                }
            }

            return boss;
        }

        void CreateZone()
        {
            m_Zone = new GameObject("Boss Music Zone");
            m_Zone.transform.SetParent(m_Boss.transform, false);

            SphereCollider sphere = m_Zone.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.center = Vector3.zero;
            sphere.radius = RoomRadius;

            BossZone zone = m_Zone.AddComponent<BossZone>();
            zone.Encounter = this;
        }

        void Update()
        {
            if (m_Boss == null || m_Player == null || m_Dead)
                return;

            float distance = Vector3.Distance(Flat(m_Player.position), Flat(m_Boss.transform.position));

            // the trigger is the main way in, the distance check only covers
            // the case where the player slips inside without touching it
            if (!m_InRoom && distance <= RoomRadius)
            {
                EnterRoom();
            }
            else if (m_InRoom && distance > ExitRadius)
            {
                LeaveRoom();
            }

            if ((m_WaitingForBass || m_WaitingForMelody) && AudioDirector.Instance != null
                && AudioDirector.Instance.MusicDspTime >= m_NextLoopTime)
            {
                ApplyPendingLayers();
            }
        }

        public void PlayerEnteredZone()
        {
            EnterRoom();
        }

        void EnterRoom()
        {
            if (m_InRoom || m_Dead)
                return;

            m_InRoom = true;
            TransitionTo("Boss_Base", RoomFade);

            if (m_BossHealth != null && m_BossHealth.GetRatio() <= 0.5f)
            {
                RequestMelody();
            }
            else if (m_Boss != null && m_Boss.KnownDetectedTarget != null)
            {
                RequestBass();
            }
        }

        void LeaveRoom()
        {
            m_InRoom = false;
            m_Phase2 = false;
            m_WaitingForBass = false;
            m_WaitingForMelody = false;
            TransitionTo("Snapshot", RoomFade * 1.5f);
        }

        void OnBossDetected()
        {
            if (!m_InRoom || m_Phase2 || m_Dead || m_WaitingForBass)
                return;

            if (m_Boss != null && m_Boss.KnownDetectedTarget == null)
                return;

            RequestBass();
        }

        void OnBossDamaged(float damage, GameObject source)
        {
            if (!m_InRoom || m_Phase2 || m_Dead || m_BossHealth == null)
                return;

            if (m_BossHealth.GetRatio() <= 0.5f)
            {
                RequestMelody();
            }
        }

        void OnBossDied()
        {
            m_Dead = true;
            m_WaitingForBass = false;
            m_WaitingForMelody = false;
            TransitionTo("Snapshot", DeathFade);

            if (m_Zone != null)
                m_Zone.SetActive(false);
        }

        // Both the bass and the melody only come in at the start of a loop, so
        // every layer is heard from its first beat.
        void RequestBass()
        {
            m_WaitingForBass = true;
            ScheduleNextLoopCheck();
        }

        void RequestMelody()
        {
            m_WaitingForMelody = true;
            ScheduleNextLoopCheck();
        }

        void ScheduleNextLoopCheck()
        {
            AudioDirector director = AudioDirector.Instance;
            if (director == null)
            {
                m_NextLoopTime = 0.0;
                return;
            }

            double remaining = director.TimeToLoopStart();
            if (remaining <= 0.05)
                remaining = 0.0;

            m_NextLoopTime = director.MusicDspTime + remaining;
        }

        void ApplyPendingLayers()
        {
            float ratio = m_BossHealth != null ? m_BossHealth.GetRatio() : 1f;
            bool detected = m_Boss != null && m_Boss.KnownDetectedTarget != null;

            if (m_WaitingForMelody && ratio <= 0.5f)
            {
                m_WaitingForBass = false;
                m_WaitingForMelody = false;
                m_Phase2 = true;
                TransitionTo("Boss_Phase2", LayerFade);
                return;
            }

            if (m_WaitingForBass && detected)
            {
                m_WaitingForBass = false;
                TransitionTo("Boss_Alert", LayerFade);
                return;
            }

            // the condition did not hold on this bar line, wait for the next one
            AudioDirector director = AudioDirector.Instance;
            if (director != null)
                m_NextLoopTime += director.MusicLoopLength;
            else
                m_NextLoopTime += 8.0;
        }

        void TransitionTo(string snapshotName, float seconds)
        {
            if (m_Mixer == null)
                return;

            AudioMixerSnapshot snapshot = m_Mixer.FindSnapshot(snapshotName);
            if (snapshot != null)
            {
                snapshot.TransitionTo(seconds);
            }
            else
            {
                Debug.LogWarning("BossEncounter: snapshot not found: " + snapshotName);
            }
        }

        static Vector3 Flat(Vector3 position)
        {
            return new Vector3(position.x, 0f, position.z);
        }

        void OnDestroy()
        {
            if (m_BossHealth != null)
            {
                m_BossHealth.OnDamaged -= OnBossDamaged;
                m_BossHealth.OnDie -= OnBossDied;
            }

            if (m_Boss != null && m_Boss.DetectionModule != null)
                m_Boss.DetectionModule.onDetectedTarget -= OnBossDetected;
        }
    }
}
