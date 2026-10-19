namespace MyUniverseUplink.Runtime;

/// <summary>Log del plugin. Le implementazioni oscurano sempre i segreti registrati.</summary>
public interface IUplinkLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);

    /// <summary>Registra un valore segreto (token, header): da quel momento compare solo come [redacted].</summary>
    void RegisterSecret(string? secret);
}
