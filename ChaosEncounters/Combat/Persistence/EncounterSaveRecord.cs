namespace ChaosEncounters.Combat.Persistence;

internal enum EncounterSaveLifecycle {
    PendingActivation = 1,
    Active = 2,
    NoCompatibleCandidate = 3,
    DisabledForCombat = 4,
    RuntimeFaulted = 5
}

internal sealed class EncounterSaveRecord {
    internal const int CurrentSchemaVersion = 1;

    public EncounterSaveRecord() {
    }

    public int SchemaVersion { get; set; }
    public EncounterSaveLifecycle Lifecycle { get; set; }
    public EncounterType EncounterType { get; set; }
    public string MechanicId { get; set; }
    public string LeaderId { get; set; }
    public List<string> InitialEnemyIds { get; set; }
    public List<string> PendingEnemyJoinIds { get; set; }
}
