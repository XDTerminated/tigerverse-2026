using UnityEngine;

namespace Tigerverse.Combat
{
    [CreateAssetMenu(fileName = "MoveCatalog", menuName = "Tigerverse/Move Catalog")]
    public class MoveCatalog : ScriptableObject
    {
        public MoveSO[] moves;

        public MoveSO Find(string displayName)
        {
            if (string.IsNullOrEmpty(displayName) || moves == null)
            {
                Debug.LogWarning($"[MoveCatalog] Find called with invalid name '{displayName}'.");
                return null;
            }
            for (int i = 0; i < moves.Length; i++)
            {
                var m = moves[i];
                if (m == null) continue;
                if (string.Equals(m.displayName, displayName, System.StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            Debug.LogWarning($"[MoveCatalog] Move '{displayName}' not found.");
            return null;
        }

        public int IndexOf(MoveSO move)
        {
            if (move == null || moves == null) return -1;
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i] == move) return i;
            }
            return -1;
        }

        public MoveSO Dodge { get { return Find("Dodge"); } }

        public static MoveCatalog Load() => Resources.Load<MoveCatalog>("MoveCatalog");

        private static MoveCatalog _instance;
        public static MoveCatalog Instance
        {
            get
            {
                if (_instance == null) _instance = Load();
                if (_instance == null)
                    Debug.LogError("[MoveCatalog] Could not load MoveCatalog from Resources/MoveCatalog.asset");
                return _instance;
            }
        }
    }
}
