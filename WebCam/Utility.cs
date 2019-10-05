using DShowNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WebCam
{
    /// <summary>
    /// The Utility class provides a set of helper methods that are useful when working with the filter graph.
    /// </summary>
    public class Utility
    {
        /// <summary>
        /// Using a GraphBuilder, connect two filters.
        /// </summary>
        /// <param name="pGraph">Specifies the graph builder.</param>
        /// <param name="pSrc">Specifies the source filter.</param>
        /// <param name="pDst">Specifies the destination filter.</param>
        /// <returns>A value of 0 is returned on success, or an error value on failure.</returns>
        public static int ConnectFilters(IGraphBuilder pGraph, IBaseFilter pSrc, IBaseFilter pDst)
        {
            IPin pOut = null;

            int hr = FindUnconnectedPin(pSrc, PinDirection.Output, out pOut);
            if (hr == 0)
            {
                hr = ConnectFilters(pGraph, pOut, pDst);
                Marshal.ReleaseComObject(pOut);
            }

            return hr;
        }

        /// <summary>
        /// Using a GraphBuilder, connect an output pin to the destination filter.
        /// </summary>
        /// <param name="pGraph">Specifies the graph builder.</param>
        /// <param name="pOut">Specifies the output pin to connect.</param>
        /// <param name="pDst">Specifies the destination filter.</param>
        /// <returns>A value of 0 is returned on success, or an error value on failure.</returns>
        public static int ConnectFilters(IGraphBuilder pGraph, IPin pOut, IBaseFilter pDst)
        {
            IPin pIn = null;

            int hr = FindUnconnectedPin(pDst, PinDirection.Input, out pIn);
            if (hr == 0)
            {
                hr = pGraph.Connect(pOut, pIn);
                Marshal.ReleaseComObject(pIn);
            }

            return hr;
        }

        /// <summary>
        /// The GetFilter method returns a given filter with a given name or portion of a name.
        /// </summary>
        /// <param name="ibuilder">Specifies the Graph Builder used to find the filter.</param>
        /// <param name="strName">Specifies the filter name to look for.</param>
        /// <param name="bExact">Optionally, specifies whether or not to look for the exact name, or just a portion of the name.</param>
        /// <returns>If found, the filter is returned, otherwise <i>null</i> is returned.</returns>
        public static IBaseFilter GetFilter(IGraphBuilder ibuilder, string strName, bool bExact = true)
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

#if DEBUG
                Trace.WriteLine(fi.achName);
#endif

                if ((bExact && fi.achName == strName) || (!bExact && fi.achName.Contains(strName)))
                {
                    Marshal.ReleaseComObject(ienum);
                    return ifilter;
                }

                Marshal.ReleaseComObject(ifilter);
            }

            Marshal.ReleaseComObject(ienum);
            return null;
        }


        /// <summary>
        /// Return the first unconnected input or output pin.
        /// </summary>
        /// <param name="pFilter">Specifies the filter to check.</param>
        /// <param name="pinDir">Specifies the pin direction to look for.</param>
        /// <param name="ppPin">Returns the first unconnected pin in the direction specified.</param>
        /// <returns>An error value < 0 is returned or 0 for success.</returns>
        public static int FindUnconnectedPin(IBaseFilter pFilter, PinDirection pinDir, out IPin ppPin)
        {
            uint VFW_E_NOT_FOUND = 0x80040216;
            IEnumPins pEnum = null;
            bool bFound = false;

            ppPin = null;

            int hr = pFilter.EnumPins(out pEnum);
            if (hr < 0)
                return hr;

            IPin[] rgPin = new IPin[1];
            int nFetched;
            while (pEnum.Next(1, rgPin, out nFetched) == 0)
            {
                hr = MatchPin(rgPin[0], pinDir, false, out bFound);
                if (hr < 0)
                    return hr;

                if (bFound)
                {
                    ppPin = rgPin[0];
                    break;
                }

                if (rgPin[0] != null)
                {
                    Marshal.ReleaseComObject(rgPin[0]);
                    rgPin[0] = null;
                }
            }

            if (!bFound)
                hr = (int)VFW_E_NOT_FOUND;

            if (rgPin[0] != null && ppPin == null)
                Marshal.ReleaseComObject(rgPin[0]);

            if (pEnum != null)
                Marshal.ReleaseComObject(pEnum);

            return hr;
        }

        /// <summary>
        /// Match a pin by pin direction and connection state.
        /// </summary>
        /// <param name="pPin">Specifies the pin to check.</param>
        /// <param name="direction">Specifies the expected direction.</param>
        /// <param name="bShouldBeConnected">Specifies whether or not the pin should be connected or not.</param>
        /// <param name="bResult">Specifies the matching status.</param>
        /// <returns>An error value < 0 is returned or 0 for success.</returns>
        public static int MatchPin(IPin pPin, PinDirection direction, bool bShouldBeConnected, out bool bResult)
        {
            bool bMatch = false;
            bool bIsConnected = false;

            bResult = false;

            int hr = IsPinConnected(pPin, out bIsConnected);
            if (hr == 0)
            {
                if (bIsConnected == bShouldBeConnected)
                    hr = IsPinDirection(pPin, direction, out bMatch);
            }

            if (hr == 0)
                bResult = bMatch;

            return hr;
        }

        /// <summary>
        /// Query whether a pin is connected to another pin.
        /// </summary>
        /// <param name="pPin">Specifies the pin to check.</param>
        /// <param name="bResult">Specifies the connection status.</param>
        /// <returns>An error value < 0 is returned or 0 for success.</returns>
        public static int IsPinConnected(IPin pPin, out bool bResult)
        {
            uint VFW_E_NOT_CONNECTED = 0x80040209;
            IPin pTmp = null;

            bResult = false;

            int hr = pPin.ConnectedTo(out pTmp);
            if (hr == 0)
            {
                bResult = true;
            }
            else if (hr == (int)VFW_E_NOT_CONNECTED)
            {
                // The pin is not connected.  This is not an error for checking the connection status.
                bResult = false;
                hr = 0;
            }

            if (pTmp != null)
                Marshal.ReleaseComObject(pTmp);

            return hr;
        }

        /// <summary>
        /// Query whether a pin has a specified direction (input/output).
        /// </summary>
        /// <param name="pPin">Specifies the pin to check.</param>
        /// <param name="dir">Specifies the direction to match against the actual direction.</param>
        /// <param name="bResult">Specifies the direction matching status.</param>
        /// <returns>An error value < 0 is returned or 0 for success.</returns>
        public static int IsPinDirection(IPin pPin, PinDirection dir, out bool bResult)
        {
            PinDirection pinDir;

            bResult = false;

            int hr = pPin.QueryDirection(out pinDir);
            if (hr == 0)
                bResult = (pinDir == dir) ? true : false;

            return hr;
        }

        /// <summary>
        /// Get a pin of a certain direction.
        /// </summary>
        /// <param name="pFilter">Specifies the filter whos pin we want to get.</param>
        /// <param name="dir">Specifies the direction sought.</param>
        /// <param name="pin">Returns the pin found.</param>
        /// <param name="strName">Optionally, specifies a partial pin name to look.</param>
        /// <returns>On success, 0 is returned, otherwise an error is returned.</returns>
        public static int GetPin(IBaseFilter pFilter, PinDirection dir, out IPin pin, string strName = null)
        {
            uint VFW_E_NOT_FOUND = 0x80040216;
            IEnumPins iEnum = null;

            pin = null;

            int hr = pFilter.EnumPins(out iEnum);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            IPin[] rgPin = new IPin[1];
            int nFetched;

            while (iEnum.Next(1, rgPin, out nFetched) == 0)
            {
                PinInfo pinInfo;
                hr = rgPin[0].QueryPinInfo(out pinInfo);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                if (pinInfo.dir == dir && (strName == null || pinInfo.name.Contains(strName)))
                {
                    pin = rgPin[0];
                    break;
                }

                Marshal.ReleaseComObject(rgPin[0]);
            }

            Marshal.ReleaseComObject(iEnum);

            if (pin == null)
                return (int)VFW_E_NOT_FOUND;

            return 0;
        }

        /// <summary>
        /// Disconnect all output pins of the specified filter.
        /// </summary>
        /// <param name="pFilter">Specifies the filter whos output pins are to be disconnected.</param>
        /// <returns>On success, 0 is returned, otherwise an error is returned.</returns>
        public static int DisconnectAllOutputPins(IBaseFilter pFilter)
        {
            IEnumPins iEnum = null;

            int hr = pFilter.EnumPins(out iEnum);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            IPin[] rgPin = new IPin[1];
            int nFetched;

            while (iEnum.Next(1, rgPin, out nFetched) == 0)
            {
                PinInfo pinInfo;
                hr = rgPin[0].QueryPinInfo(out pinInfo);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                if (pinInfo.dir == PinDirection.Output)
                    rgPin[0].Disconnect();

                Marshal.ReleaseComObject(rgPin[0]);
            }

            return 0;
        }
    }
}
