using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DirectX.Capture;
using DShowNET;

namespace WebCam
{
    public class WebCam : ISampleGrabberCB, IDisposable
    {
        IBaseFilter m_camFilter;
        IFilterGraph2 m_graphBuilder;
        IAMCameraControl m_camControl;
        ICaptureGraphBuilder2 m_captureGraphBuilder;
        ISampleGrabber m_sampleGrabber;
        IMediaControl m_mediaControl;
        IMediaEventEx m_mediaEventEx;
        IVideoWindow m_videoWindow;
        IVideoFrameStep m_videoFrameStep;
        IBaseFilter m_baseGrabFilter;
        IBasicVideo2 m_basicVideo;
        IMediaSeeking m_mediaSeek;
        VideoInfoHeader m_videoInfoHeader;
        Filters m_filters = new Filters();
        Filter m_selectedFilter;
        byte[] m_rgBuffer = null;
        AutoResetEvent m_evtImageSnapped = new AutoResetEvent(false);
        PictureBox m_pb;
        bool m_bSnapEnabled = false;
        bool m_bInvertImage = false;
        IntPtr m_tmpBuffer = IntPtr.Zero;
        int m_nTmpBufferSize = 0;

        const int WS_CHILD = 0x40000000;
        const int WS_CLIPCHILDREN = 0x02000000;
        const int WS_CLIPSIBLINGS = 0x04000000;
        const int WM_GRAPHNOTIFY = 0x8000 + 1;

        public event EventHandler<ImageArgs> OnSnapshot;

        public WebCam()
        {
        }

        public bool InvertImage
        {
            get { return m_bInvertImage; }
            set { m_bInvertImage = value; }
        }

        public void Dispose()
        {
            Close();

            if (m_tmpBuffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(m_tmpBuffer);
                m_tmpBuffer = IntPtr.Zero;
                m_nTmpBufferSize = 0;
            }
        }

        public FilterCollection VideoCompressors
        {
            get { return m_filters.VideoCompressors; }
        }

        public FilterCollection VideoInputDevices
        {
            get { return m_filters.VideoInputDevices; }
        }

        public bool IsConnected
        {
            get
            {
                if (m_videoWindow != null)
                    return true;

                return false;
            }
        }

        private IBaseFilter getFilter(IGraphBuilder ibuilder, string strName)
        {
            IEnumFilters ienum;
            int hr = ibuilder.EnumFilters(out ienum);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            IBaseFilter ifilter;
            uint nFetched;
            while (ienum.Next(1, out ifilter, out nFetched) == 0)
            {
                FilterInfo fi = new FilterInfo();
                hr = ifilter.QueryFilterInfo(fi);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                Trace.WriteLine(fi.achName);

                if (fi.achName == strName)
                {
                    Marshal.ReleaseComObject(ienum);
                    return ifilter;
                }
            }

            Marshal.ReleaseComObject(ienum);
            return null;
        }

        public void Open(Filter filter, PictureBox pb, string strFile)
        {
            int hr;

            m_selectedFilter = filter;
            m_graphBuilder = (IFilterGraph2)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.FilterGraph, true));

            if (strFile == null)
            {
                UCOMIMoniker moniker = m_selectedFilter.CreateMoniker();
                m_graphBuilder.AddSourceFilterForMoniker(moniker, null, m_selectedFilter.Name, out m_camFilter);
                Marshal.ReleaseComObject(moniker);
                m_camControl = m_camFilter as IAMCameraControl;

                m_captureGraphBuilder = (ICaptureGraphBuilder2)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.CaptureGraphBuilder2, true));
                hr = m_captureGraphBuilder.SetFiltergraph(m_graphBuilder as IGraphBuilder);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }

            m_pb = pb;
            m_sampleGrabber = (ISampleGrabber)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.SampleGrabber, true));
            m_mediaControl = m_graphBuilder as IMediaControl;
            m_videoWindow = m_graphBuilder as IVideoWindow;
            m_mediaEventEx = m_graphBuilder as IMediaEventEx;
            m_baseGrabFilter = m_sampleGrabber as IBaseFilter;
            m_videoFrameStep = m_graphBuilder as IVideoFrameStep;

            hr = m_graphBuilder.AddFilter(m_baseGrabFilter, "Ds.Lib Grabber");
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            hr = m_sampleGrabber.SetBufferSamples(false);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            hr = m_sampleGrabber.SetOneShot(false);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            if (m_selectedFilter != null)
            {
                hr = m_graphBuilder.AddFilter(m_camFilter, m_selectedFilter.Name);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                hr = m_sampleGrabber.SetCallback(this, 1);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            else
            {
                hr = m_graphBuilder.RenderFile(strFile, strFile);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                m_basicVideo = m_graphBuilder as IBasicVideo2;
                m_mediaSeek = m_graphBuilder as IMediaSeeking;
            }

            AMMediaType media = new AMMediaType();            
            media.majorType = MediaType.Video;
            media.subType = MediaSubType.RGB24;
            media.formatType = FormatType.VideoInfo;
            hr = m_sampleGrabber.SetMediaType(media);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            Guid cat;
            Guid med;
            if (m_selectedFilter != null)
            {
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

            media = new AMMediaType();
            hr = m_sampleGrabber.GetConnectedMediaType(media);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            if ((media.formatType != FormatType.VideoInfo && 
                 media.formatType != FormatType.WaveEx && 
                 media.formatType != FormatType.MpegVideo) ||
                media.formatPtr == IntPtr.Zero)
                throw new Exception("Media grabber format is unknown.");

            m_videoInfoHeader = Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader)) as VideoInfoHeader;
            Marshal.FreeCoTaskMem(media.formatPtr);
            media.formatPtr = IntPtr.Zero;


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


            // start the capturing

            hr = m_mediaControl.Run();
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            if (strFile != null)
            {
                hr = m_mediaControl.Pause();
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
        }

        public bool Step(int nFrames)
        {
            if (m_videoFrameStep == null)
                return false;

            m_videoFrameStep.Step(nFrames, null);
            return true;
        }

        public bool Play()
        {
            if (m_mediaControl == null)
                return false;

            m_mediaControl.Run();

            return true;
        }

        public bool Stop()
        {
            if (m_mediaControl == null)
                return false;

            m_mediaControl.Stop();

            return true;
        }

        public bool IsAtEnd
        {
            get
            {
                if (m_mediaSeek == null)
                    return false;

                long lDuration;
                int hr = m_mediaSeek.GetDuration(out lDuration);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                long lCurrent;
                hr = m_mediaSeek.GetCurrentPosition(out lCurrent);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                if (lCurrent == lDuration)
                    return true;

                return false;
            }
        }

        public double CompletionPercent
        {
            get
            {
                if (m_mediaSeek == null)
                    return 0;

                long lDuration;
                int hr = m_mediaSeek.GetDuration(out lDuration);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                long lCurrent;
                hr = m_mediaSeek.GetCurrentPosition(out lCurrent);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                double dfPct = (lDuration == 0) ? 0 : (double)lCurrent / (double)lDuration;
                return dfPct;
            }
        }

        public void SetFocus(int nVal)
        {
            if (m_camControl != null)
                m_camControl.Set(CameraControlProperty.Focus, nVal, CameraControlFlags.Manual);
        }

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
        /// <remarks>
        /// For more information on getting the snapshot from the video with the IBasicVideo interface, 
        /// @see https://stackoverflow.com/questions/1354165/directshow-net-bitmap-shows-stripe-from-right-on-left-side-of-image
        /// </remarks>
        public void GetImage()
        {
            if (m_basicVideo != null)
            {
                int nSize = 0;
                int hr = m_basicVideo.GetCurrentImage(ref nSize, IntPtr.Zero);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                if (m_tmpBuffer == IntPtr.Zero || m_nTmpBufferSize != nSize)
                {
                    if (m_tmpBuffer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(m_tmpBuffer);

                    m_tmpBuffer = Marshal.AllocCoTaskMem(nSize);
                    m_nTmpBufferSize = nSize;
                }

                if (m_rgBuffer == null || m_rgBuffer.Length != nSize)
                    m_rgBuffer = new byte[nSize];

                // Get the pixel buffer of the image.
                hr = m_basicVideo.GetCurrentImage(ref nSize, m_tmpBuffer);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                // Get the bitmap header, copy the data making sure to offset for the header size.
                BitmapInfoHeader bmpHeader = (BitmapInfoHeader)Marshal.PtrToStructure(m_tmpBuffer, typeof(BitmapInfoHeader));
                Marshal.Copy(m_tmpBuffer, m_rgBuffer, bmpHeader.Size, nSize - bmpHeader.Size);

                // This step uses more memory but avoids the 'unsafe code' requirement in the example noted in the remarks.
                GCHandle handle = GCHandle.Alloc(m_rgBuffer, GCHandleType.Pinned);
                long nScan0 = handle.AddrOfPinnedObject().ToInt64();

                // Change the format type.
                PixelFormat pixelFmt = PixelFormat.Format32bppRgb;
                int nWidth = bmpHeader.Width;
                int nHeight = bmpHeader.Height;
                int nBitsPerPixel = ((int)pixelFmt & 0xff00) >> 8;
                int nBytesPerPixel = (nBitsPerPixel + 7) / 8;
                int nStride = 4 * ((nWidth * nBytesPerPixel + 3) / 4);

                // Create and rote the image.
                Bitmap bmp = new Bitmap(nWidth, nHeight, nStride, pixelFmt, new IntPtr(nScan0));
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
                handle.Free();

                if (m_bInvertImage)
                    bmp = invertImage(bmp);

                OnSnapshot(this, new ImageArgs(bmp, m_bInvertImage));
            }
            else
            {
                int nSize = m_videoInfoHeader.BmiHeader.ImageSize;
                if (m_rgBuffer == null || m_rgBuffer.Length != nSize + 63999)
                    m_rgBuffer = new byte[nSize + 63999];

                m_bSnapEnabled = true;
                m_evtImageSnapped.WaitOne();
            }

            return;
        }

        public void Close()
        {
            if (m_mediaControl != null)
                m_mediaControl.StopWhenReady();

            if (m_mediaEventEx != null)
                m_mediaEventEx.SetNotifyWindow(IntPtr.Zero, WM_GRAPHNOTIFY, IntPtr.Zero);

            if (m_videoWindow != null)
            {
                m_videoWindow.put_Visible(DsHlp.OAFALSE);
                m_videoWindow.put_Owner(IntPtr.Zero);
            }

            m_mediaControl = null;
            m_mediaEventEx = null;
            m_videoWindow = null;
            m_videoFrameStep = null;
            m_baseGrabFilter = null;
            m_camControl = null;
            m_basicVideo = null;

            if (m_sampleGrabber != null)
            {
                Marshal.ReleaseComObject(m_sampleGrabber);
                m_sampleGrabber = null;
            }

            if (m_captureGraphBuilder != null)
            {
                Marshal.ReleaseComObject(m_captureGraphBuilder);
                m_captureGraphBuilder = null;
            }

            if (m_graphBuilder != null)
            {
                Marshal.ReleaseComObject(m_graphBuilder);
                m_graphBuilder = null;
            }

            if (m_camFilter != null)
            {
                Marshal.ReleaseComObject(m_camFilter);
                m_camFilter = null;
            }
        }

        public int SampleCB(double SampleTime, IMediaSample pSample)
        {
            Marshal.ReleaseComObject(pSample);
            return 0;
        }

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

    public class ImageArgs : EventArgs
    {
        Bitmap m_bmp;
        bool m_bInverted = false;

        public ImageArgs(Bitmap bmp, bool bInverted)
        {
            m_bmp = bmp;
            m_bInverted = bInverted;
        }

        public bool Inverted
        {
            get { return m_bInverted; }
        }

        public Bitmap Image
        {
            get { return m_bmp; }
        }
    }

}
