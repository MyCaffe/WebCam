using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DirectX.Capture;
using WebCam;

namespace WebCamSample
{
    public partial class WebCamDialog : Form
    {
        WebCam.WebCam m_webCam = new WebCam.WebCam();
        Bitmap m_bmp = null;
        AutoResetEvent m_evtBmpReady = new AutoResetEvent(false);
        long m_lDuration = 0;
        COMMAND m_cmd;
        ManualResetEvent m_evtCancel = new ManualResetEvent(false);
        AutoResetEvent m_evtCmdReady = new AutoResetEvent(false);
        AutoResetEvent m_evtCmdDone = new AutoResetEvent(false);
        Task m_taskCmd = null;
        AutoResetEvent m_evtCreateDone = new AutoResetEvent(false);
        Task m_taskCreate = null;
        string m_strDefaultFolder = null;
        string m_strDefaultFile = null;

        delegate void fnHandleSnap(Bitmap bmp);

        public enum COMMAND
        {
            SNAP,
            STEP,
            PLAY,
            STOP
        }

        public WebCamDialog(string strDefaultFolder = null, string strDefaultFile = null)
        {
            m_strDefaultFolder = strDefaultFolder;
            m_strDefaultFile = strDefaultFile;

            InitializeComponent();
            m_webCam.OnSnapshot += m_webCam_OnSnapshot;
        }

        private void m_webCam_OnSnapshot(object sender, ImageArgs e)
        {
            m_bmp = e.Image;
            m_evtBmpReady.Set();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            foreach (Filter filter in m_webCam.VideoInputDevices)
            {
                ListViewItem lvi = new ListViewItem(filter.Name);
                lvi.Tag = filter;
                lvi.Selected = true;

                listView1.Items.Add(lvi);
            }

            listView1.Items.Add(new ListViewItem("Video File"));
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            Filter filter = listView1.SelectedItems[0].Tag as Filter;
            string strFile = null;

            btnStep.Enabled = false;
            btnPlay.Enabled = false;
            btnStop.Enabled = false;
            lblEnd.Visible = false;

            if (filter == null)
            {
                if (!string.IsNullOrEmpty(m_strDefaultFolder))
                    openFileDialog1.InitialDirectory = m_strDefaultFolder;

                if (!string.IsNullOrEmpty(m_strDefaultFile))
                    openFileDialog1.FileName = m_strDefaultFile;

                if (openFileDialog1.ShowDialog() != DialogResult.OK)
                    return;

                btnStep.Enabled = true;
                btnPlay.Enabled = true;
                btnStop.Enabled = true;
                lblEnd.Visible = true;

                strFile = openFileDialog1.FileName;
            }

            m_evtCancel.Reset();
            bool bCreated = false;

            if (chkCreateOnSeparateThread.Checked)
            {
                m_taskCreate = Task.Factory.StartNew(new Action<object>(createThread), new Tuple<Filter, string>(filter, strFile));
                bCreated = true;
            }

            if (chkRunOnSeparateThread.Checked)
                m_taskCmd = Task.Factory.StartNew(new Action(testThread));

            if (!bCreated)
                m_webCam.Open(filter, pictureBox1, strFile);
        }

        private void timerUI_Tick(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
                btnConnect.Enabled = !m_webCam.IsConnected;
            else
                btnConnect.Enabled = false;

            btnDisconnect.Enabled = m_webCam.IsConnected;
            btnSnap.Enabled = m_webCam.IsConnected;

            if (m_evtBmpReady.WaitOne(0))
                pictureBox2.Image = m_bmp;

            if (m_webCam.IsAtEnd)
            {
                lblEnd.Visible = true;
                lblEnd.ForeColor = Color.Red;
                lblEnd.Text = "AT END";
            }
            else
            {
                double dfPct = m_webCam.CompletionPercent;
                if (dfPct > 0)
                {
                    lblEnd.Visible = true;
                    lblEnd.ForeColor = Color.Green;
                    lblEnd.Text = dfPct.ToString("P");
                }
                else
                {
                    lblEnd.Visible = false;
                }
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            bool bClosed = false;

            if (m_taskCmd != null || m_taskCreate != null)
            {
                m_evtCancel.Set();

                if (m_taskCmd != null)
                {
                    m_taskCmd.Wait();
                    m_taskCmd.Dispose();
                    m_taskCmd = null;
                }

                if (m_taskCreate != null)
                {
                    m_taskCreate.Wait();
                    m_taskCreate.Dispose();
                    m_taskCreate = null;
                    bClosed = true;
                }
            }

            if (!bClosed)
                m_webCam.Close();
        }

        private void btnSnap_Click(object sender, EventArgs e)
        {
            if (m_taskCmd != null)
            {
                m_cmd = COMMAND.SNAP;
                m_evtCmdReady.Set();
                m_evtCmdDone.WaitOne();
                return;
            }

            m_webCam.GetImage();
        }

        private void btnStep_Click(object sender, EventArgs e)
        {
            if (m_taskCmd != null)
            {
                m_cmd = COMMAND.STEP;
                m_evtCmdReady.Set();
                m_evtCmdDone.WaitOne();
                return;
            }

            m_webCam.Step(1);
        }

        private void btnPlay_Click(object sender, EventArgs e)
        {
            if (m_taskCmd != null)
            {
                m_cmd = COMMAND.PLAY;
                m_evtCmdReady.Set();
                m_evtCmdDone.WaitOne();
                return;
            }

            m_webCam.Play();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (m_taskCmd != null)
            {
                m_cmd = COMMAND.STOP;
                m_evtCmdReady.Set();
                m_evtCmdDone.WaitOne();
                return;
            }

            m_webCam.Stop();
        }

        private void createThread(object obj)
        {
            Tuple<Filter, string> args = obj as Tuple<Filter, string>;

            m_webCam = new WebCam.WebCam();
            m_webCam.OnSnapshot += m_webCam_OnSnapshot;
            m_webCam.Open(args.Item1, null, args.Item2);
            m_evtCreateDone.Set();

            m_evtCancel.WaitOne();
            m_webCam.Close();
        }

        private void testThread()
        {
            while (!m_evtCancel.WaitOne(0))
            {
                if (m_evtCmdReady.WaitOne(100))
                {
                    switch (m_cmd)
                    {
                        case COMMAND.SNAP:
                            m_webCam.GetImage();
                            break;

                        case COMMAND.STEP:
                            m_webCam.Step(1);
                            break;

                        case COMMAND.PLAY:
                            m_webCam.Play();
                            break;

                        case COMMAND.STOP:
                            m_webCam.Stop();
                            break;
                    }

                    m_evtCmdDone.Set();
                }
            }
        }
    }
}
