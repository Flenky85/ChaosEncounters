namespace ChaosEncounters.Combat.Persistence;

internal static class EncounterPersistenceValidation {
    internal const int MaximumEntityCount = 4096;
    internal const int MaximumEntityIdLength = 1024;

    internal static bool IsValidEntityId(string id) {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Length <= MaximumEntityIdLength;
    }
}
