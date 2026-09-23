namespace FischMacroCS.Core;

/// <summary>A safety stop requires the user to check the fishing position before restarting.</summary>
public sealed class FishingSafetyException(string message) : Exception(message);
