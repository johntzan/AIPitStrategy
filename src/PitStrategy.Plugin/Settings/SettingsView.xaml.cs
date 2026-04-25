using System.Windows.Controls;

namespace PitStrategy.Plugin.Settings
{
    public partial class SettingsView : UserControl
    {
        public SettingsView(PluginSettings settings)
        {
            InitializeComponent();
            DataContext = settings;
        }
    }
}
