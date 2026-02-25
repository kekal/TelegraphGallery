using System.Diagnostics;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class ProcessLauncher : IProcessLauncher
    {
        public void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}
