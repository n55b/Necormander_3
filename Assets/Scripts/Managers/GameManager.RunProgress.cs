public partial class GameManager
{
    private const float DefaultCheckpointHP = 10f;
    private SaveData _loadedSaveData;
    private CharacterHealth _runPlayerHealth;

    private void InitializeRunGrowth()
    {
        bool hasSave = _loadedSaveData != null;
        var registry = dataManager != null ? dataManager.GET_GROWTH_REGISTRY() : null;

        inventoryManager?.Initialize(hasSave);
        playerSkillInventoryManager?.Initialize();
        if (hasSave)
        {
            inventoryManager?.LoadFromData(_loadedSaveData, registry);
            playerSkillInventoryManager?.LoadFromData(_loadedSaveData, registry);
        }

        itemPouch?.Initialize(hasSave, registry);
        if (hasSave) itemPouch?.LoadFromData(_loadedSaveData, registry);
    }

    private void RestoreRunHealth()
    {
        _runPlayerHealth = playerController.GetComponentInChildren<CharacterHealth>();
        // Equipment and pouch stat modifiers have already been applied by SpawnPlayer.
        if (_runPlayerHealth != null && _loadedSaveData != null)
            _runPlayerHealth.SetHP(_loadedSaveData.playerHP);
        _loadedSaveData = null;
    }

    public bool SaveCurrentState() => TrySaveRunAtFloor(currentFloor);

    private bool TrySaveRunAtFloor(int floor)
    {
        var data = new SaveData
        {
            currentFloor = floor,
            playerHP = _runPlayerHealth != null ? _runPlayerHealth.CurHP : DefaultCheckpointHP
        };

        inventoryManager?.SaveToData(data);
        playerSkillInventoryManager?.SaveToData(data);
        itemPouch?.SaveToData(data);
        return SaveSystem.Save(data);
    }
}
