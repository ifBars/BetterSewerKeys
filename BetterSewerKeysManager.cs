using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Linq;
#if MONO
using ScheduleOne.Doors;
using ScheduleOne.Map;
#else
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Map;
#endif

namespace BetterSewerKeys
{
    /// <summary>
    /// Manager for tracking discovered sewer entrances and their unlock states
    /// </summary>
    public class BetterSewerKeysManager
    {
        private static BetterSewerKeysManager? _instance;
        public static BetterSewerKeysManager Instance => _instance ??= new BetterSewerKeysManager();

        private Dictionary<int, SewerDoorController> _entranceMap = new Dictionary<int, SewerDoorController>();
        private Dictionary<SewerDoorController, int> _doorToEntranceMap = new Dictionary<SewerDoorController, int>();
        private BetterSewerKeysSave? _saveData;
        private bool _isInitialized = false;
        private int _nextEntranceID = 0;

        private BetterSewerKeysManager()
        {
        }

        /// <summary>
        /// Initialize the manager with save data reference
        /// </summary>
        public void Initialize(BetterSewerKeysSave saveData)
        {
            if (_isInitialized)
                return;

            _saveData = saveData;
            Utils.ModLogger.Info("BetterSewerKeysManager: Initialized");
            _isInitialized = true;
        }

        /// <summary>
        /// Discover all SewerDoorController instances in the scene and assign IDs
        /// </summary>
        public void DiscoverEntrances()
        {
            if (!_isInitialized || _saveData == null)
            {
                Utils.ModLogger.Warning("BetterSewerKeysManager: Cannot discover entrances - not initialized");
                return;
            }

            // Find all SewerDoorController instances, including inactive ones
            var allDoors = Object.FindObjectsOfType<SewerDoorController>(includeInactive: true);
            Utils.ModLogger.Info($"BetterSewerKeysManager: Found {allDoors.Length} sewer door controllers");

            // Use a HashSet to track which doors we've already processed by their instance ID
            // This ensures we don't process the same door twice even if FindObjectsOfType returns duplicates
            var processedDoors = new HashSet<SewerDoorController>();
            
            _entranceMap.Clear();
            _doorToEntranceMap.Clear();
            _nextEntranceID = 0;

            foreach (var door in allDoors)
            {
                if (door == null)
                    continue;
                
                // Skip if we've already processed this door instance
                if (processedDoors.Contains(door))
                {
                    Utils.ModLogger.Debug($"BetterSewerKeysManager: Skipping duplicate door instance: {door.gameObject.name} at {door.transform.position}");
                    continue;
                }
                
                processedDoors.Add(door);

                int entranceID = _nextEntranceID++;
                _entranceMap[entranceID] = door;
                _doorToEntranceMap[door] = entranceID;

                // Initialize save data for this entrance if not present
                if (!_saveData.UnlockedEntrances.ContainsKey(entranceID))
                {
                    _saveData.UnlockedEntrances[entranceID] = false;
                }
                
                // Initialize other dictionaries if not present
                if (!_saveData.KeyLocationIndices.ContainsKey(entranceID))
                {
                    _saveData.KeyLocationIndices[entranceID] = -1;
                }
                
                if (!_saveData.KeyPossessorIndices.ContainsKey(entranceID))
                {
                    _saveData.KeyPossessorIndices[entranceID] = -1;
                }
                
                if (!_saveData.IsRandomWorldKeyCollected.ContainsKey(entranceID))
                {
                    _saveData.IsRandomWorldKeyCollected[entranceID] = false;
                }

                Utils.ModLogger.Debug(
                    $"BetterSewerKeysManager: Registered entrance {entranceID} for door {door.gameObject.name} at position {door.transform.position}");
            }

            Utils.ModLogger.Info($"BetterSewerKeysManager: Discovered {_entranceMap.Count} entrances");
            
            // Trigger save after discovering all entrances to ensure dictionaries are initialized
            // Only save if we actually discovered new entrances
            if (_saveData != null && _entranceMap.Count > 0)
            {
                Utils.ModLogger.Debug($"BetterSewerKeysManager: Triggering save after discovering {_entranceMap.Count} entrances");
                BetterSewerKeysSave.RequestGameSave();
            }
        }

        /// <summary>
        /// Register a door and get its entrance ID
        /// </summary>
        public int RegisterDoor(SewerDoorController door)
        {
            if (_doorToEntranceMap.TryGetValue(door, out int existingID))
            {
                return existingID;
            }

            int entranceID = _nextEntranceID++;
            _entranceMap[entranceID] = door;
            _doorToEntranceMap[door] = entranceID;

            // Initialize save data for this entrance if not present
            if (_saveData != null && !_saveData.UnlockedEntrances.ContainsKey(entranceID))
            {
                _saveData.UnlockedEntrances[entranceID] = false;
            }

            Utils.ModLogger.Debug(
                $"BetterSewerKeysManager: Registered new entrance {entranceID} for door {door.gameObject.name}");
            return entranceID;
        }

        /// <summary>
        /// Get the entrance ID for a door
        /// </summary>
        public int GetEntranceID(SewerDoorController door)
        {
            return _doorToEntranceMap.TryGetValue(door, out int id) ? id : -1;
        }

        /// <summary>
        /// Check if an entrance is unlocked
        /// </summary>
        public bool IsEntranceUnlocked(int entranceID)
        {
            if (_saveData == null)
                return false;

            return _saveData.IsEntranceUnlocked(entranceID);
        }

        /// <summary>
        /// Unlock a specific entrance
        /// </summary>
        public void UnlockEntrance(int entranceID)
        {
            if (_saveData == null)
            {
                Utils.ModLogger.Warning(
                    $"BetterSewerKeysManager: Cannot unlock entrance {entranceID} - save data not initialized");
                return;
            }

            _saveData.SetEntranceUnlocked(entranceID, true);
            Utils.ModLogger.Info($"BetterSewerKeysManager: Unlocked entrance {entranceID}");
        }

        /// <summary>
        /// Get all entrance IDs
        /// </summary>
        public List<int> GetAllEntranceIDs()
        {
            return _entranceMap.Keys.ToList();
        }

        /// <summary>
        /// Get the first locked entrance ID, or -1 if all are unlocked
        /// </summary>
        public int GetFirstLockedEntranceID()
        {
            if (_saveData == null)
                return -1;

            foreach (var entranceID in _entranceMap.Keys.OrderBy(id => id))
            {
                if (!_saveData.IsEntranceUnlocked(entranceID))
                {
                    return entranceID;
                }
            }

            return -1;
        }

        /// <summary>
        /// Check if all entrances are unlocked
        /// </summary>
        public bool AreAllEntrancesUnlocked()
        {
            if (_saveData == null || _entranceMap.Count == 0)
                return false;

            return _entranceMap.Keys.All(id => _saveData.IsEntranceUnlocked(id));
        }

        /// <summary>
        /// Apply save data to this manager (called after loading)
        /// </summary>
        public void ApplySaveData(BetterSewerKeysSave saveData)
        {
            _saveData = saveData;
            Utils.ModLogger.Info(
                $"BetterSewerKeysManager: Applied save data with {saveData.UnlockedEntrances.Count} entrances");
        }

        /// <summary>
        /// Get the save data reference
        /// </summary>
        public BetterSewerKeysSave? GetSaveData()
        {
            return _saveData;
        }

        /// <summary>
        /// Assign random key locations and possessors per entrance
        /// </summary>
        public void AssignKeyDistribution(SewerManager sewerManager)
        {
            if (!_isInitialized || _saveData == null || sewerManager == null)
            {
                Utils.ModLogger.Warning(
                    "BetterSewerKeysManager: Cannot assign key distribution - not initialized or sewer manager missing");
                return;
            }

            int entranceCount = _entranceMap.Count;
            if (entranceCount == 0)
            {
                Utils.ModLogger.Warning(
                    "BetterSewerKeysManager: No entrances discovered, cannot assign key distribution");
                return;
            }

            // Shuffle available locations and possessors
            List<int> availableLocations = new List<int>();
            List<int> availablePossessors = new List<int>();

            if (sewerManager.RandomSewerKeyLocations != null && sewerManager.RandomSewerKeyLocations.Length > 0)
            {
                for (int i = 0; i < sewerManager.RandomSewerKeyLocations.Length; i++)
                {
                    availableLocations.Add(i);
                }
            }

            if (sewerManager.SewerKeyPossessors != null && sewerManager.SewerKeyPossessors.Length > 0)
            {
                for (int i = 0; i < sewerManager.SewerKeyPossessors.Length; i++)
                {
                    availablePossessors.Add(i);
                }
            }

            // Shuffle lists
            ShuffleList(availableLocations);
            ShuffleList(availablePossessors);

            // Assign one location and one possessor per entrance
            int locationIndex = 0;
            int possessorIndex = 0;

            foreach (var entranceID in _entranceMap.Keys.OrderBy(id => id))
            {
                // Assign location if available
                if (locationIndex < availableLocations.Count && !_saveData.KeyLocationIndices.ContainsKey(entranceID))
                {
                    int location = availableLocations[locationIndex++];
                    _saveData.SetKeyLocationIndex(entranceID, location);
                    Utils.ModLogger.Debug($"Assigned key location {location} to entrance {entranceID}");
                }

                // Assign possessor if available
                if (possessorIndex < availablePossessors.Count &&
                    !_saveData.KeyPossessorIndices.ContainsKey(entranceID))
                {
                    int possessor = availablePossessors[possessorIndex++];
                    _saveData.SetKeyPossessorIndex(entranceID, possessor);
                    Utils.ModLogger.Debug($"Assigned key possessor {possessor} to entrance {entranceID}");
                }
            }

            Utils.ModLogger.Info(
                $"BetterSewerKeysManager: Assigned key distribution for {_entranceMap.Count} entrances");
            
            // Trigger save after assigning key distribution
            if (_saveData != null)
            {
                BetterSewerKeysSave.RequestGameSave();
            }
        }

        private void ShuffleList<T>(List<T> list)
        {
            System.Random rng = new System.Random();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
