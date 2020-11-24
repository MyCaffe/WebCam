using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebCamTestApp
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            WebCamSample.WebCamDialog dlg = new WebCamSample.WebCamDialog();
            dlg.OnGetVideoCapabilities += dlg_OnGetVideoCapabilities;


            Application.Run(dlg);
        }

        private static void dlg_OnGetVideoCapabilities(object sender, WebCamSample.GetVideoCapabilitiesArgs e)
        {
            if (e.VideoCapabilities.Count == 0)
            {
                MessageBox.Show("The selected device has no video capabilities to select from.", "No Video Capabilities", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FormCapabilities dlg = new FormCapabilities(e.VideoCapabilities);

            if (dlg.ShowDialog() == DialogResult.OK)
                e.SelectedVideoCapability = dlg.SelectedCapability;
        }
    }
}
