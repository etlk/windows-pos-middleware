namespace WsWpfListener.Properties
{
    [global::System.Runtime.CompilerServices.CompilerGenerated()]
    [global::System.CodeDom.Compiler.GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "16.5.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase
    {
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        public static Settings Default { get { return defaultInstance; } }

        [global::System.Configuration.UserScopedSetting()]
        [global::System.Diagnostics.DebuggerNonUserCode()]
        public string WorkspaceUrl
        {
            get { return ((string)(this["WorkspaceUrl"])); }
            set { this["WorkspaceUrl"] = value; }
        }

         public string BaseUrl
        {
            get { return ((string)(this["BaseUrl"])); }
            set { this["BaseUrl"] = value; }
        }

        
        
    }
}
