using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DirectX.Capture;
using DShowNET;

namespace WebCam
{
    /// <summary>
    /// The WebCam class gives easy access to the WebCam and video files for use as an input to deep learning platforms such as the MyCaffe AI Platform.
    /// @see [MyCaffe: A Complete C# Re-Write of Caffe with Reinforcement Learning](https://arxiv.org/abs/1810.02272) by David W. Brown, arXiv:1810.02272, 2018
    /// </summary>
    public class WebCam : ISampleGrabberCB, IDisposable
    {
        IBaseFilter m_camFilter;
        IBaseFilter m_videoFilter;
        IFilterGraph2 m_graphBuilder;
        IAMCameraControl m_camControl;
        ICaptureGraphBuilder2 m_captureGraphBuilder;      
        ISampleGrabber m_sampleGrabber;
        IMediaControl m_mediaControl;
        IMediaEventEx m_mediaEventEx;
        IVideoWindow m_videoWindow;
        IVideoFrameStep m_videoFrameStep;
        IBaseFilter m_baseGrabFilter;
        IMediaSeeking m_mediaSeek;
        IBaseFilter m_nullRenderer = null;
        VideoInfoHeader m_videoInfoHeader;
        Filters m_filters = new Filters();
        Filter m_selectedFilter;
        byte[] m_rgBuffer = null;
        AutoResetEvent m_evtImageSnapped = new AutoResetEvent(false);
        bool m_bSnapEnabled = false;
        bool m_bInvertImage = false;
        bool m_bRunning = false;
        bool m_bConnected = false;
        long m_lDuration = 0;
        bool m_bAutoResize = true;

        const int WS_CHILD = 0x40000000;
        const int WS_CLIPCHILDREN = 0x02000000;
        const int WS_CLIPSIBLINGS = 0x04000000;
        const int WM_GRAPHNOTIFY = 0x8000 + 1;

        /// <summary>
        /// The OnSnapshot event fires when calling the GetImage method.
        /// </summary>
        public event EventHandler<ImageArgs> OnSnapshot;

        /// <summary>
        /// The constructor.
        /// </summary>
        public WebCam()
        {
        }

        /// <summary>
        /// Cleanup all resources used by closing the video feed.
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        /// <summary>
        /// Get/set whether or not to subscribe to the target picturebox size changed event and automatically resize the video window.
        /// </summary>
        public bool AutoResize
        {
            get { return m_bAutoResize; }
            set { m_bAutoResize = value; }
        }

        /// <summary>
        /// Get/set whether or not to invert the images received.
        /// </summary>
        public bool InvertImage
        {
            get { return m_bInvertImage; }
            set { m_bInvertImage = value; }
        }

        /// <summary>
        /// Return the video compressors found.
        /// </summary>
        public FilterCollection VideoCompressors
        {
            get { return m_filters.VideoCompressors; }
        }

        /// <summary>
        /// Return the video filters found.
        /// </summary>
        public FilterCollection VideoInputDevices
        {
            get { return m_filters.VideoInputDevices; }
        }

        /// <summary>
        /// Return whether or not the video feed is open or not.
        /// </summary>
        public bool IsConnected
        {
            get { return m_bConnected;  }
        }

        /// <summary>
        /// Open a new video feed (either web-cam or video file).
        /// </summary>
        /// <param name="filter">Specifies the web-cam filter to use, or <i>null</i> when opening a video file.</param>
        /// <param name="pb">Specifies the output window, or <i>null</i> when running headless and only receiving snapshots.</param>
        /// <param name="strFile">Specifies the video file to use, or <i>null</i> when opening a web-cam feed.</param>
        /// <returns></returns>
        public long Open(Filter filter, PictureBox pb, string strFile)
        {
            int hr;

            if (filter != null && strFile != null)
                throw new ArgumentException("Both the filter and file are non NULL - only one of these can be used at a time; The filter is used with the web-cam and the file is used with a video file.");

            m_selectedFilter = filter;
            m_graphBuilder = (IFilterGraph2)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.FilterGraph, true));

            // When using a web-cam, create the moniker for the filter and add the filter to the graph.
            if (strFile == null)
            {
                IMoniker moniker = m_selectedFilter.CreateMoniker();
                m_graphBuilder.AddSourceFilterForMoniker(moniker, null, m_selectedFilter.Name, out m_camFilter);
                Marshal.ReleaseComObject(moniker);
                m_camControl = m_camFilter as IAMCameraControl;

                // Create the capture builder used to build the web-cam filter graph.
                m_captureGraphBuilder = (ICaptureGraphBuilder2)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.CaptureGraphBuilder2, true));
                hr = m_captureGraphBuilder.SetFiltergraph(m_graphBuilder as IGraphBuilder);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Add the web-cam filter to the graph.
                hr = m_graphBuilder.AddFilter(m_camFilter, m_selectedFilter.Name);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            else
            {
                // Build the graph with the video file.
                hr = m_graphBuilder.RenderFile(strFile, null);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                m_mediaSeek = m_graphBuilder as IMediaSeeking;

                if (pb != null)
                    m_videoFrameStep = m_graphBuilder as IVideoFrameStep;
            }

            // Create the sample grabber used to get snapshots.
            m_sampleGrabber = (ISampleGrabber)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.SampleGrabber, true));
            m_baseGrabFilter = m_sampleGrabber as IBaseFilter;
            m_mediaControl = m_graphBuilder as IMediaControl;

            // When using a target window, get the video window used with the target output window
            if (pb != null)
            {
                m_mediaEventEx = m_graphBuilder as IMediaEventEx;
                m_videoWindow = m_graphBuilder as IVideoWindow;
            }
            // Otherwise create the null renderer for no video output is needed (only snapshots).
            else
            {
                m_nullRenderer = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.NullRenderer, true));
            }

            // Add the sample grabber to the filter graph.
            hr = m_graphBuilder.AddFilter(m_baseGrabFilter, "Ds.Lib Grabber");
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Turn off the sample grabber buffers.
            hr = m_sampleGrabber.SetBufferSamples(false);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Turn off the sample grabber one-shot.
            hr = m_sampleGrabber.SetOneShot(false);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Turn ON the sample grabber callback where video data is to be received.
            hr = m_sampleGrabber.SetCallback(this, 1);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Set the media format used by the sample grabber.
            AMMediaType media = new AMMediaType();
            media.majorType = MediaType.Video;
            media.subType = MediaSubType.RGB24;
            media.formatType = FormatType.VideoInfo;

            hr = m_sampleGrabber.SetMediaType(media);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Connect the WebCam Filters and Frame Grabber.
            if (m_selectedFilter != null)
            {
                Guid cat;
                Guid med;

                cat = PinCategory.Preview;
                med = MediaType.Video;
                hr = m_captureGraphBuilder.RenderStream(ref cat, ref med, m_camFilter, null, null);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                cat = PinCategory.Capture;
                med = MediaType.Video;
                hr = m_captureGraphBuilder.RenderStream(ref cat, ref med, m_camFilter, null, m_baseGrabFilter);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            // Connect the Frame Grabber and (optionally the Null Renderer)
            else
            {
                // Get the video decoder and its pins.
                m_videoFilter = Utility.GetFilter(m_graphBuilder as IGraphBuilder, "Video Decoder", false);

                IPin pOutput;
                hr = Utility.GetPin(m_videoFilter, PinDirection.Output, out pOutput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                IPin pInput;
                hr = pOutput.ConnectedTo(out pInput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                PinInfo pinInfo;
                hr = pInput.QueryPinInfo(out pinInfo);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Get the sample grabber pins.
                IPin pGrabInput;
                hr = Utility.GetPin(m_baseGrabFilter, PinDirection.Input, out pGrabInput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                IPin pGrabOutput;
                hr = Utility.GetPin(m_baseGrabFilter, PinDirection.Output, out pGrabOutput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Disconnect the source filter output and the input it is connected to.
                hr = pOutput.Disconnect();
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                hr = pInput.Disconnect();
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Connect the source output to the Grabber input.
                hr = m_graphBuilder.Connect(pOutput, pGrabInput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // When rendering video output, connect the Grabber output to the original downstream input that the source was connected to.
                if (m_nullRenderer == null)
                {
                    hr = m_graphBuilder.Connect(pGrabOutput, pInput);
                    if (hr < 0)
                        Marshal.ThrowExceptionForHR(hr);
                }

                Marshal.ReleaseComObject(pOutput);
                Marshal.ReleaseComObject(pInput);
                Marshal.ReleaseComObject(pGrabInput);
                Marshal.ReleaseComObject(pGrabOutput);
            }

            // Remove sound filters.
            IBaseFilter soundFilter = Utility.GetFilter(m_graphBuilder as IGraphBuilder, "Audio Decoder", false);
            if (soundFilter != null)
            {
                hr = m_graphBuilder.RemoveFilter(soundFilter);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                Marshal.ReleaseComObject(soundFilter);
            }

            soundFilter = Utility.GetFilter(m_graphBuilder as IGraphBuilder, "Sound", false);
            if (soundFilter != null)
            {
                hr = m_graphBuilder.RemoveFilter(soundFilter);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                Marshal.ReleaseComObject(soundFilter);
            }

            // When using a headless (no video rendering) setup, connect the null renderer to the Sample Grabber.
            if (m_nullRenderer != null)
            {
                // Add the null renderer.
                hr = m_graphBuilder.AddFilter(m_nullRenderer, "Null Renderer");
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Get the sample grabber output pin.
                IPin pGrabOutput;
                hr = Utility.GetPin(m_baseGrabFilter, PinDirection.Output, out pGrabOutput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Get the null renderer input pin.
                IPin pInput;
                hr = Utility.GetPin(m_nullRenderer, PinDirection.Input, out pInput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Disconnect the sample grabber pin.
                hr = pGrabOutput.Disconnect();
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Connect the Grabber output to the null renderer.
                hr = m_graphBuilder.Connect(pGrabOutput, pInput);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                Marshal.ReleaseComObject(pInput);
                Marshal.ReleaseComObject(pGrabOutput);

                // Remove the Video Renderer for it is no longer needed.
                IBaseFilter ivideorender = Utility.GetFilter(m_graphBuilder as IGraphBuilder, "Video Renderer");
                if (ivideorender != null)
                {
                    m_graphBuilder.RemoveFilter(ivideorender);
                    Marshal.ReleaseComObject(ivideorender);
                }
            }

            // Get the sample grabber media settings and video header.
            media = new AMMediaType();
            hr = m_sampleGrabber.GetConnectedMediaType(media);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            if ((media.formatType != FormatType.VideoInfo && 
                 media.formatType != FormatType.WaveEx && 
                 media.formatType != FormatType.MpegVideo) ||
                media.formatPtr == IntPtr.Zero)
                throw new Exception("Media grabber format is unknown.");

            // Get the video header with frame sizing information.
            m_videoInfoHeader = Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader)) as VideoInfoHeader;
            Marshal.FreeCoTaskMem(media.formatPtr);
            media.formatPtr = IntPtr.Zero;
          
            // If we are rendering video output, setup the video window (which requires a message pump).
            if (m_videoWindow != null)
            {
                // setup the video window
                hr = m_videoWindow.put_Owner(pb.Handle);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                hr = m_videoWindow.put_WindowStyle(WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);


                // resize the window
                hr = m_videoWindow.SetWindowPosition(0, 0, pb.Width, pb.Height);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                hr = m_videoWindow.put_Visible(DsHlp.OATRUE);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Subscribe to the picturebox size changed event.
                pb.SizeChanged += Pb_SizeChanged;
            }


            // start the capturing
            hr = m_mediaControl.Run();
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // When using a video file, immediately stop at the start.
            if (strFile != null)
            {
                hr = m_mediaControl.Pause();
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }

            // When using a media file, we need to save the video file's duration.
            if (m_mediaSeek != null)
            {
                hr = m_mediaSeek.GetDuration(out m_lDuration);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }

            m_bConnected = true;

            return m_lDuration;
        }

        /// <summary>
        /// Event handler to resize video frame when the picturebox size changes (if required to do so).
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Pb_SizeChanged(object sender, EventArgs e)
        {
            if (m_videoWindow == null)
                return;

            if (m_bAutoResize)
            {
                int hr;

                PictureBox pb = sender as PictureBox;
                if (pb == null)
                    return;

                hr = m_videoWindow.put_Width(pb.Width);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                hr = m_videoWindow.put_Height(pb.Height);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
        }

        /// <summary>
        /// Close the currently open video feed.
        /// </summary>
        public void Close()
        {
            m_bConnected = false;
            m_lDuration = 0;

            if (m_mediaControl != null)
                m_mediaControl.Stop();

            if (m_mediaEventEx != null)
                m_mediaEventEx.SetNotifyWindow(IntPtr.Zero, WM_GRAPHNOTIFY, IntPtr.Zero);

            if (m_videoWindow != null)
            {
                m_videoWindow.put_Visible(DsHlp.OAFALSE);
                m_videoWindow.put_Owner(IntPtr.Zero);
            }

            // Release all interfaces.

            if (m_graphBuilder != null)
            {
                Marshal.ReleaseComObject(m_graphBuilder);
                m_graphBuilder = null;
            }

            if (m_camControl != null)
            {
                Marshal.ReleaseComObject(m_camControl);
                m_camControl = null;
            }

            if (m_captureGraphBuilder != null)
            {
                Marshal.ReleaseComObject(m_captureGraphBuilder);
                m_captureGraphBuilder = null;
            }

            if (m_mediaSeek != null)
            {
                Marshal.ReleaseComObject(m_mediaSeek);
                m_mediaSeek = null;
            }

            if (m_videoFrameStep != null)
            {
                Marshal.ReleaseComObject(m_videoFrameStep);
                m_videoFrameStep = null;
            }

            if (m_sampleGrabber != null)
            {
                Marshal.ReleaseComObject(m_sampleGrabber);
                m_sampleGrabber = null;
            }

            if (m_baseGrabFilter != null)
            {
                Marshal.ReleaseComObject(m_baseGrabFilter);
                m_baseGrabFilter = null;
            }

            if (m_mediaControl != null)
            {
                Marshal.ReleaseComObject(m_mediaControl);
                m_mediaControl = null;
            }

            if (m_mediaEventEx != null)
            {
                Marshal.ReleaseComObject(m_mediaEventEx);
                m_mediaEventEx = null;
            }

            if (m_videoWindow != null)
            {
                Marshal.ReleaseComObject(m_videoWindow);
                m_videoWindow = null;
            }

            if (m_nullRenderer != null)
            {
                Marshal.ReleaseComObject(m_nullRenderer);
                m_nullRenderer = null;
            }

            if (m_videoFilter != null)
            {
                Marshal.ReleaseComObject(m_videoFilter);
                m_videoFilter = null;
            }
        }

        /// <summary>
        /// Step a specified number of frames in the feed (this function only applies to a videw file feed).
        /// </summary>
        /// <param name="nFrames">Specifies the number of frames to step.</param>
        /// <returns>After a successful step <i>true</i> is returned, otherwise when ignored or not running <i>false</i> is returned.</returns>
        public bool Step(int nFrames)
        {
            if (m_mediaSeek == null)
                return false;

            if (m_bRunning)
                return false;

            if (m_videoFrameStep != null)
            {
                int hr = m_videoFrameStep.Step(nFrames, null);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            else
            {
                long lPosition;
                int hr = m_mediaSeek.GetCurrentPosition(out lPosition);

                long lStep = m_videoInfoHeader.AvgTimePerFrame * nFrames;
                long lNewPosition = lPosition + lStep;

                if (lNewPosition > Duration)
                    lNewPosition = Duration;

                SetPosition(lNewPosition);
            }

            return true;
        }

        /// <summary>
        /// Play a video file (does not apply to a web-cam feed).
        /// </summary>
        /// <returns>After a successful initiated play <i>true</i> is returned, otherwise when ignored or not running <i>false</i> is returned.</returns>
        public bool Play()
        {
            if (m_mediaControl == null)
                return false;

            int hr = m_mediaControl.Run();
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            m_bRunning = true;

            return true;
        }

        /// <summary>
        /// Stop a video file from playing. (does not apply to a web-cam feed).
        /// </summary>
        /// <returns>After a successful stop <i>true</i> is returned, otherwise when ignored or not running <i>false</i> is returned.</returns>
        public bool Stop()
        {
            if (m_mediaControl == null)
                return false;

            int hr = m_mediaControl.Stop();
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            m_bRunning = false;

            return true;
        }

        /// <summary>
        /// Returns whether or not a video file is at its end.  When using a web-cam, this function always returns <i>false</i>.
        /// </summary>
        public bool IsAtEnd
        {
            get
            {
                if (m_mediaSeek == null)
                    return false;

                long lCurrent = CurrentPosition;
                if (lCurrent == m_lDuration)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Returns the percentage of video that has already been played.  When using a web-cam, this function always returns 0.
        /// </summary>
        public double CompletionPercent
        {
            get
            {
                if (m_mediaSeek == null)
                    return 0;

                long lCurrent = CurrentPosition;
                double dfPct = (Duration == 0) ? 0 : (double)lCurrent / (double)Duration;
                return dfPct;
            }
        }

        /// <summary>
        /// Returns the duration of a video file.  When using a web-cam, this function always returns 0.
        /// </summary>
        public long Duration
        {
            get { return m_lDuration; }
        }

        /// <summary>
        /// Returns the current position of a video file.  When using a web-cam, this function always returns 0.
        /// </summary>
        public long CurrentPosition
        {
            get
            {
                if (m_mediaSeek == null)
                    return 0;

                long lPosition;
                int hr = m_mediaSeek.GetCurrentPosition(out lPosition);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                return lPosition;
            }
        }

        /// <summary>
        /// Set the current position within a video file to the specified position.  This function is ignored when using a web-cam.
        /// </summary>
        /// <param name="lPosition">Specifies the new position to set.</param>
        public void SetPosition(long lPosition)
        {
            if (m_mediaSeek == null)
                return;

            long lDuration;
            int hr = m_mediaSeek.GetDuration(out lDuration);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            if (lPosition < 0 || lPosition > lDuration)
                throw new Exception("The postion specified is outside of the video duration range [0," + lDuration.ToString() + "].  Please specify a valid position.");

            DsOptInt64 pos = new DsOptInt64(lPosition);
            DsOptInt64 stop = new DsOptInt64(lDuration);
            hr = m_mediaSeek.SetPositions(pos, SeekingFlags.AbsolutePositioning, stop, SeekingFlags.AbsolutePositioning);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        /// <summary>
        /// Set the web-cam to a given focus.  This function is ignored when using a video file.
        /// </summary>
        /// <param name="nVal">Specifies the focus value.</param>
        public void SetFocus(int nVal)
        {
            if (m_camControl != null)
                m_camControl.Set(CameraControlProperty.Focus, nVal, CameraControlFlags.Manual);
        }

        /// <summary>
        /// Get the focus value of the web-cam.  This function always returns -1 when using a video file.
        /// </summary>
        /// <returns>The focus value is returned.</returns>
        public int GetFocus()
        {
            int nVal = -1;
            CameraControlFlags flags;

            if (m_camControl != null)
                return m_camControl.Get(CameraControlProperty.Focus, out nVal, out flags);

            return nVal;
        }

        /// <summary>
        /// Get a snapshot of the video or webcam.
        /// </summary>
        public void GetImage()
        {
            int nSize = m_videoInfoHeader.BmiHeader.ImageSize;
            if (m_rgBuffer == null || m_rgBuffer.Length != nSize + 63999)
                m_rgBuffer = new byte[nSize + 63999];

            m_bSnapEnabled = true;
            Step(1);

            while (!m_evtImageSnapped.WaitOne(100))
            {
                Application.DoEvents();
            }

            return;
        }

        /// <summary>
        /// The SampleCB is the call back upon receiving each sample.
        /// </summary>
        /// <param name="SampleTime">Specifies the sample time.</param>
        /// <param name="pSample">Specifies the interface used to retrieve a sample.</param>
        /// <returns>Always retunrs 0 for this function is not used.</returns>
        public int SampleCB(double SampleTime, IMediaSample pSample)
        {
            Marshal.ReleaseComObject(pSample);
            return 0;
        }

        /// <summary>
        /// The BufferCB is the callback used to receive buffered video data from the SampleGrabber.
        /// </summary>
        /// <param name="SampleTime">Specifies the sample time.</param>
        /// <param name="pBuffer">Specifies the buffered data.</param>
        /// <param name="BufferLen">Specifies the buffered data length.</param>
        /// <returns>This function returns 0.</returns>
        /// <remarks>
        /// Upon successfully receiving video data and converting it to a Bitmap, the bitmap is then sent
        /// on the the OnSnapshot event handler.
        /// </remarks>
        public int BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen)
        {
            if (pBuffer == IntPtr.Zero || m_rgBuffer == null || BufferLen >= m_rgBuffer.Length || !m_bSnapEnabled)
                return 0;

            if (OnSnapshot != null)
            {
                Marshal.Copy(pBuffer, m_rgBuffer, 0, BufferLen);

                int nWid = m_videoInfoHeader.BmiHeader.Width;
                int nHt = m_videoInfoHeader.BmiHeader.Height;
                int nStride = nWid * 3;

                GCHandle handle = GCHandle.Alloc(m_rgBuffer, GCHandleType.Pinned);
                long nScan0 = handle.AddrOfPinnedObject().ToInt64();
                nScan0 += (nHt - 1) * nStride;

                Bitmap bmp = new Bitmap(nWid, nHt, -nStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(nScan0));

                handle.Free();

                if (m_bInvertImage)
                    bmp = invertImage(bmp);

                OnSnapshot(this, new ImageArgs(bmp, m_bInvertImage));
            }

            m_bSnapEnabled = false;
            m_evtImageSnapped.Set();

            return 0;
        }

        private Bitmap invertImage(Bitmap bmp)
        {
            Bitmap bmpNew = new Bitmap(bmp.Width, bmp.Height);
            ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {-1, 0, 0, 0, 0},
                    new float[] {0, -1, 0, 0, 0},
                    new float[] {0, 0, -1, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {1, 1, 1, 0, 1}
                });
            ImageAttributes attributes = new ImageAttributes();

            attributes.SetColorMatrix(colorMatrix);

            using (Graphics g = Graphics.FromImage(bmpNew))
            {              
                g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);
            }

            bmp.Dispose();

            return bmpNew;
        }
    }

    /// <summary>
    /// The ImageArgs provides the arguments sent to the OnSnapshot event.
    /// </summary>
    public class ImageArgs : EventArgs
    {
        Bitmap m_bmp;
        bool m_bInverted = false;

        /// <summary>
        /// The constructor.
        /// </summary>
        /// <param name="bmp">Specifies the bitmap of the image snapshot.</param>
        /// <param name="bInverted">Specifies whether or not the image was inverted (colorwise).</param>
        public ImageArgs(Bitmap bmp, bool bInverted)
        {
            m_bmp = bmp;
            m_bInverted = bInverted;
        }

        /// <summary>
        /// Returns whether or not the colors of the image have been inverted.
        /// </summary>
        public bool Inverted
        {
            get { return m_bInverted; }
        }

        /// <summary>
        /// Returns the bitmap of the snapshot.
        /// </summary>
        public Bitmap Image
        {
            get { return m_bmp; }
        }
    }

}
