using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Game
{
    // Pulls optional clips out of the Resources folder and hands out
    // variations, making sure the same clip is not played twice in a row.
    public static class AudioLibrary
    {
        static readonly Dictionary<string, AudioClip[]> s_Sets = new Dictionary<string, AudioClip[]>();
        static readonly Dictionary<string, int> s_LastPlayed = new Dictionary<string, int>();

        public static AudioClip Get(string path)
        {
            return Resources.Load<AudioClip>(path);
        }

        public static AudioClip[] GetSet(string folderPath)
        {
            if (s_Sets.TryGetValue(folderPath, out AudioClip[] cached) && cached != null && cached.Length > 0)
                return cached;

            AudioClip[] set = Resources.LoadAll<AudioClip>(folderPath);

            // a single clip can also be looked up by its own path, so a set of
            // one does not need its own folder
            if (set == null || set.Length == 0)
            {
                AudioClip single = Resources.Load<AudioClip>(folderPath);
                if (single != null)
                    set = new[] {single};
            }

            if (set != null && set.Length > 0)
                s_Sets[folderPath] = set;

            return set;
        }

        public static AudioClip RandomFromSet(string folderPath)
        {
            AudioClip[] set = GetSet(folderPath);
            if (set == null || set.Length == 0)
                return null;

            if (set.Length == 1)
                return set[0];

            int last;
            bool hasLast = s_LastPlayed.TryGetValue(folderPath, out last);

            int index;
            do
            {
                index = Random.Range(0, set.Length);
            } while (hasLast && index == last);

            s_LastPlayed[folderPath] = index;
            return set[index];
        }
    }
}
