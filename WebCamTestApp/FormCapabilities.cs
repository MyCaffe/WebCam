using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebCam;

namespace WebCamTestApp
{
    public partial class FormCapabilities : Form
    {
        VideoCapabilityCollection m_colCap;
        VideoCapability m_selectedCap = null;

        public FormCapabilities(VideoCapabilityCollection colCap)
        {
            m_colCap = colCap;
            InitializeComponent();
        }

        public VideoCapability SelectedCapability
        {
            get { return m_selectedCap; }
        }

        private void FormCapabilities_Load(object sender, EventArgs e)
        {
            ListViewItem lviFirst = null;

            foreach (VideoCapability cap in m_colCap)
            {
                ListViewItem lvi = new ListViewItem(cap.ToString());
                lvi.Tag = cap;

                if (lviFirst == null)
                    lviFirst = lvi;

                lstItems.Items.Add(lvi);
            }

            if (lviFirst != null)
            {
                lviFirst.Selected = true;
                lviFirst.EnsureVisible();
            }
        }

        private void timerUI_Tick(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count > 0)
                btnOK.Enabled = true;
            else
                btnOK.Enabled = false;
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count > 0)
                m_selectedCap = lstItems.SelectedItems[0].Tag as VideoCapability;
        }
    }
}
