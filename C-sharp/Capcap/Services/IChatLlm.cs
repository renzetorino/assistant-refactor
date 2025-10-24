namespace Capcap.Services
{
    public interface IChatLlm
    {
        Task<string> ChatAsync(string system, string user, CancellationToken ct);
        Task<string> ClassifyAsync(string system, string user, CancellationToken ct);
    }
}
