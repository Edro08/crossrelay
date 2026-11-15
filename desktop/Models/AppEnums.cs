namespace CrossRelay.Desktop.Models;

public enum DeviceOperatingSystem
{
    Unknown = 0,
    Android = 1,
    Windows = 2,
    Linux = 3,
    Apple = 4
}

public enum TransferStatus
{
    Pending = 0,
    Negotiating = 1,
    InProgress = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}

public enum PairingStatus
{
    Unpaired = 0,
    AwaitingPin = 1,
    Paired = 2,
    Blocked = 3
}

public enum TransferPolicy
{
    AutoAccept = 0,
    AskConfirmation = 1
}

public enum BandwidthLimitMode
{
    Unlimited = 0,
    Rate100MBps = 1,
    Rate50MBps = 2,
    Rate10MBps = 3,
    Custom = 4
}

public enum NodeVisibilityMode
{
    VisibleToAll = 0,
    OnlyPaired = 1,
    Invisible = 2
}
