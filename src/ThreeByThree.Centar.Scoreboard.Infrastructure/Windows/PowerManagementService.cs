using System.Runtime.InteropServices;
using ThreeByThree.Centar.Scoreboard.Application.Operations;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Windows;

public sealed partial class PowerManagementService : IPowerManagementService, IDisposable
{
    private bool executionRequestActive;
    private bool isDisposed;

    public void SetGameActive(bool isActive)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (executionRequestActive == isActive)
        {
            return;
        }

        var state = ExecutionState.Continuous;
        if (isActive)
        {
            state |= ExecutionState.SystemRequired | ExecutionState.DisplayRequired;
        }

        _ = SetThreadExecutionState(state);
        executionRequestActive = isActive;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        if (executionRequestActive)
        {
            _ = SetThreadExecutionState(ExecutionState.Continuous);
        }

        executionRequestActive = false;
        isDisposed = true;
        GC.SuppressFinalize(this);
    }

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000,
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial ExecutionState SetThreadExecutionState(ExecutionState executionState);
}
