using System.Windows;

namespace Merkulov_Bp_Version_2.KatVrLogger
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            KatVrSdkInterop.LoadSdk(); 
        }
    }
}