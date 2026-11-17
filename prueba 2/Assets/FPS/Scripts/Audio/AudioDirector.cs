using System.Collections;
using Unity.FPS.AI;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Game
{
    // Runs during gameplay: owns the ambience, the radio voice and the defeat
    // sting, and crossfades between calm and tense ambience as enemies spot you.
    public class AudioDirector : MonoBehaviour
    {
        static AudioDirector s_Instance;

        AudioManager m_AudioManager;
        AudioMixer m_Mixer;

        AudioSource m_CalmSource;
        AudioSource m_TenseSource;
        AudioSource m_BaseSource;
        AudioSource m_RhythmSource;
        AudioSource m_MelodySource;
        AudioSource m_VoiceSource;

        AudioClip m_IntroLine;
        AudioClip m_CompleteLine;
        AudioClip[] m_KillLines;

        float m_CalmLevel = 1f;
        float m_TenseLevel;
        float m_CalmTarget = 1f;
        float m_TenseTarget;
        float m_MusicDuck;
        float m_NextEnemyScan;
        bool m_PlayerDead;
        double m_MusicStartTime = -1.0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            s_Instance = null;

            if (scene.name == "LoseScene")
            {
                PlayDefeatSting();
                return;
            }

            if (FindObjectOfType<AudioManager>() != null && FindObjectOfType<PlayerCharacterController>() != null)
            {
                Create();
            }
        }

        static void Create()
        {
            if (s_Instance != null)
                return;

            GameObject go = new GameObject("Audio Director");
            s_Instance = go.AddComponent<AudioDirector>();
        }

        void Awake()
        {
            s_Instance = this;

            m_AudioManager = FindObjectOfType<AudioManager>();
            if (m_AudioManager == null || m_AudioManager.AudioMixers.Length == 0)
                return;

            m_Mixer = m_AudioManager.AudioMixers[0];

            SilenceStockAmbience();

            m_CalmSource = CreateLoop("Ambience Calm", AudioLibrary.Get("Audio/Ambience/Calm"), FindGroup("Ambiente"), 1f);
            m_TenseSource = CreateLoop("Ambience Tense", AudioLibrary.Get("Audio/Ambience/Tense"), FindGroup("Ambiente"), 0f);
            m_BaseSource = CreateLoop("Music Base", AudioLibrary.Get("Audio/Music/Base"), FindGroup("MusicBase"), 1f);
            m_RhythmSource = CreateLoop("Music Rhythm", AudioLibrary.Get("Audio/Music/Rhythm"), FindGroup("MusicRitmo"), 1f);
            m_MelodySource = CreateLoop("Music Melody", AudioLibrary.Get("Audio/Music/Melody"), FindGroup("MusicMelodia"), 1f);
            m_VoiceSource = CreateOneShot("Radio Voice", FindGroup("Voice"));

            m_IntroLine = AudioLibrary.Get("Audio/Radio/Intro");
            m_CompleteLine = AudioLibrary.Get("Audio/Radio/Complete");
            m_KillLines = new[]
            {
                AudioLibrary.Get("Audio/Radio/Kill1"),
                AudioLibrary.Get("Audio/Radio/Kill2"),
                AudioLibrary.Get("Audio/Radio/Kill3"),
                AudioLibrary.Get("Audio/Radio/Kill4")
            };

            m_CalmSource.Play();
            m_TenseSource.Play();
            StartMusicLayers();

            EventManager.AddListener<EnemyKillEvent>(OnEnemyKilled);
            EventManager.AddListener<AllObjectivesCompletedEvent>(OnObjectivesCompleted);
            EventManager.AddListener<PlayerDeathEvent>(OnPlayerDeath);

            StartCoroutine(PlayIntroLater(2.5f));
        }

        public static AudioDirector Instance => s_Instance;

        // Length of the music loop, exported so other systems can line their
        // changes up with the bar line instead of guessing.
        public double MusicLoopLength =>
            m_RhythmSource != null && m_RhythmSource.clip != null ? m_RhythmSource.clip.length : 0.0;

        public double MusicDspTime => AudioSettings.dspTime;

        // Seconds left until the music loop starts over. 0 when the music is
        // not running yet.
        public double TimeToLoopStart()
        {
            double length = MusicLoopLength;
            if (length <= 0.0 || m_MusicStartTime < 0.0)
                return 0.0;

            double now = AudioSettings.dspTime;
            if (now < m_MusicStartTime)
                return m_MusicStartTime - now;

            return length - (now - m_MusicStartTime) % length;
        }

        // The three stems are scheduled on the same DSP tick, so they keep
        // their relative timing for as long as the level lasts.
        void StartMusicLayers()
        {
            double start = AudioSettings.dspTime + 0.4;
            PlayAt(m_BaseSource, start);
            PlayAt(m_RhythmSource, start);
            PlayAt(m_MelodySource, start);
            m_MusicStartTime = start;
        }

        static void PlayAt(AudioSource source, double dspTime)
        {
            if (source != null && source.clip != null)
                source.PlayScheduled(dspTime);
        }

        void Update()
        {
            UpdateAmbienceTarget();

            m_CalmLevel = Mathf.MoveTowards(m_CalmLevel, m_CalmTarget, Time.deltaTime * 0.8f);
            m_TenseLevel = Mathf.MoveTowards(m_TenseLevel, m_TenseTarget, Time.deltaTime * 0.8f);
            if (m_CalmSource != null)
                m_CalmSource.volume = m_CalmLevel;
            if (m_TenseSource != null)
                m_TenseSource.volume = m_TenseLevel;

            // Duck the music under the radio so the voice stays on top of the mix.
            float duckTarget = (m_VoiceSource != null && m_VoiceSource.isPlaying) ? -7f : 0f;
            m_MusicDuck = Mathf.MoveTowards(m_MusicDuck, duckTarget, Time.deltaTime * 25f);
            if (m_Mixer != null)
                m_Mixer.SetFloat("MusicVolume", m_MusicDuck);
        }

        void UpdateAmbienceTarget()
        {
            if (m_PlayerDead)
            {
                m_CalmTarget = 0f;
                m_TenseTarget = 0f;
                return;
            }

            if (Time.time < m_NextEnemyScan)
                return;
            m_NextEnemyScan = Time.time + 0.4f;

            bool spotted = false;
            EnemyController[] enemies = FindObjectsOfType<EnemyController>();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && enemies[i].KnownDetectedTarget != null)
                {
                    spotted = true;
                    break;
                }
            }

            m_CalmTarget = spotted ? 0f : 1f;
            m_TenseTarget = spotted ? 1f : 0f;
        }

        IEnumerator PlayIntroLater(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!m_PlayerDead)
                PlayLine(m_IntroLine);
        }

        void OnEnemyKilled(EnemyKillEvent evt)
        {
            // The last kill leads into the objective-complete line instead.
            if (evt.RemainingEnemyCount <= 0 || m_KillLines == null || m_KillLines.Length == 0)
                return;

            AudioClip line = m_KillLines[Random.Range(0, m_KillLines.Length)];
            PlayLine(line);
        }

        void OnObjectivesCompleted(AllObjectivesCompletedEvent evt)
        {
            PlayLine(m_CompleteLine);
        }

        void OnPlayerDeath(PlayerDeathEvent evt)
        {
            m_PlayerDead = true;
            m_CalmTarget = 0f;
            m_TenseTarget = 0f;
            if (m_VoiceSource != null)
                m_VoiceSource.Stop();
        }

        void PlayLine(AudioClip clip)
        {
            if (clip == null || m_VoiceSource == null)
                return;

            m_VoiceSource.Stop();
            m_VoiceSource.clip = clip;
            m_VoiceSource.Play();
        }

        void SilenceStockAmbience()
        {
            AudioSource[] sources = FindObjectsOfType<AudioSource>();
            for (int i = 0; i < sources.Length; i++)
            {
                AudioMixerGroup group = sources[i].outputAudioMixerGroup;
                if (group != null && (group.name == "Ambiente" || group.name == "Ambient"))
                {
                    sources[i].Stop();
                    sources[i].mute = true;
                    sources[i].volume = 0f;
                }
            }
        }

        AudioMixerGroup FindGroup(string groupName)
        {
            if (m_AudioManager == null)
                return null;

            AudioMixerGroup[] groups = m_AudioManager.FindMatchingGroups(groupName);
            if (groups != null && groups.Length > 0)
                return groups[0];

            Debug.LogWarning("AudioDirector: mixer group not found: " + groupName);
            return null;
        }

        AudioSource CreateLoop(string sourceName, AudioClip clip, AudioMixerGroup group, float volume)
        {
            AudioSource source = CreateSource(sourceName, group);
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            return source;
        }

        AudioSource CreateOneShot(string sourceName, AudioMixerGroup group)
        {
            AudioSource source = CreateSource(sourceName, group);
            source.loop = false;
            return source;
        }

        AudioSource CreateSource(string sourceName, AudioMixerGroup group)
        {
            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = group;
            return source;
        }

        static void PlayDefeatSting()
        {
            AudioClip clip = AudioLibrary.Get("Audio/SFX/Defeat");
            if (clip == null)
                return;

            AudioManager audioManager = FindObjectOfType<AudioManager>();
            AudioMixerGroup group = null;
            if (audioManager != null)
            {
                // The game flow fades the master out on death; bring it back for the defeat screen.
                audioManager.SetFloat("MasterVolume", 0f);
                AudioMixerGroup[] groups = audioManager.FindMatchingGroups("Voice");
                if (groups != null && groups.Length > 0)
                    group = groups[0];
            }

            GameObject go = new GameObject("Defeat Sting");
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = group;
            source.Play();

            Destroy(go, clip.length + 0.5f);
        }

        void OnDestroy()
        {
            EventManager.RemoveListener<EnemyKillEvent>(OnEnemyKilled);
            EventManager.RemoveListener<AllObjectivesCompletedEvent>(OnObjectivesCompleted);
            EventManager.RemoveListener<PlayerDeathEvent>(OnPlayerDeath);
        }
    }
}
