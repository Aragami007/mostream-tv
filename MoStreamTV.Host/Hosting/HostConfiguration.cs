namespace MoStreamTV.Host.Hosting;

internal sealed record HostConfiguration(
    string RoomName,
    string HostName,
    string Password,
    int Port)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RoomName))
            throw new ArgumentException("Enter a room name.", nameof(RoomName));
        if (string.IsNullOrWhiteSpace(HostName))
            throw new ArgumentException("Enter your display name.", nameof(HostName));
        if (string.IsNullOrWhiteSpace(Password))
            throw new ArgumentException("Enter a server password.", nameof(Password));
        if (Port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), "Choose a port from 1024 to 65535.");
    }
}
