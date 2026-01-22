// ProcessExtensions.cs
using System.Diagnostics;
using System.Threading.Tasks;

namespace BMPLauncher.Core
{
    public static class ProcessExtensions
    {
        public static async Task WaitForExitAsync(this Process process)
        {
            await Task.Run(() => process.WaitForExit());
        }
    }
}