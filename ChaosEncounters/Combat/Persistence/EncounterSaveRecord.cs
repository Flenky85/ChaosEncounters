namespace ChaosEncounters.Combat.Persistence;

internal enum EncounterSaveLifecycle {
    PendingActivation = 1,
    Active = 2,
    NoCompatibleCandidate = 3,
    DisabledForCombat = 4,
    RuntimeFaulted = 5
}

internal sealed class EncounterSaveRecord {
    internal const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; }
    public EncounterSaveLifecycle Lifecycle { get; set; }
    public string MechanicId { get; set; }

    public PendingActivationSaveData PendingActivation { get; set; }
    public EncounterMechanicSaveData MechanicData { get; set; }
}

internal sealed class PendingActivationSaveData {
    public EncounterType EncounterType { get; set; }
    public string LeaderId { get; set; }
    public List<string> InitialEnemyIds { get; set; }
    public List<string> PendingEnemyJoinIds { get; set; }
}

internal sealed class EncounterMechanicSaveData {
    public ExecutionListSaveRecipe ExecutionList { get; set; }
    public RisingVengeanceSaveRecipe RisingVengeance { get; set; }
    public TyrantsAegisSaveRecipe TyrantsAegis { get; set; }
}

internal sealed class ExecutionListSaveRecipe {
    public List<string> OrderedEnemyIds { get; set; }
}

internal sealed class RisingVengeanceSaveRecipe {
    public List<RisingVengeanceMarkedEnemySaveData>
        MarkedEnemies { get; set; }
}

internal sealed class RisingVengeanceMarkedEnemySaveData {
    public string UnitId { get; set; }
    public int Marks { get; set; }
}

internal sealed class TyrantsAegisSaveRecipe {
    public string BossId { get; set; }
}
