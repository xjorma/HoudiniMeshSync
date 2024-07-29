using System;
using System.Linq;

#if HAS_NEWTONSOFT_JSON
using Newtonsoft.Json.Linq;
#endif

namespace LookingGlass.Toolkit {
    [Serializable]
    public struct DisplayInfo {
        public string hardwareVersion;

        /// <summary>
        /// The LKG device's hardware id.
        /// </summary>
        /// <remarks>In some contexts, this is also equivalent to the LKG device's "LKG name". Note that this is NOT the same as the LKG device's unique serial identifier.</remarks>
        public string hwid;

        /// <summary>
        /// The index of this display. This can be used to select a certain LKG display for certain operations.
        /// </summary>
        /// <remarks>
        /// This is also known as the head index in Looking Glass Bridge.
        /// </remarks>
        public int index;

        public string state;

        /// <summary>
        /// <para>
        /// Contains the xy screen coordinates of the top-left corner of the LKG display.
        /// This corresponds to the currently-running OS's display arrangement coordinates.
        /// </para>
        /// <para>
        /// See also:
        /// <list type="bullet">
        /// <item>On Windows: <seealso href="https://learn.microsoft.com/en-us/windows/win32/gdi/the-virtual-screen"/></item>
        /// </list>
        /// </para>
        /// </summary>
        /// <remarks>
        /// windowCoords[0] is the xpos,<br />
        /// windowCoords[1] is the ypos
        /// </remarks>
        public int[] windowCoords;

#if HAS_NEWTONSOFT_JSON
        public static DisplayInfo Parse(JObject obj) {
            DisplayInfo result = new();
            obj.TryGet<string>("hardwareVersion", "value", out result.hardwareVersion);
            obj.TryGet<string>("hwid", "value", out result.hwid);
            obj.TryGet<int>("index", "value", out result.index);
            obj.TryGet<string>("state", "value", out result.state);

            if (obj.TryGet("windowCoords", "value", out JObject jWindowCoords)) {
                result.windowCoords = new int[2];
                jWindowCoords.TryGet<int>("x", out result.windowCoords[0]);
                jWindowCoords.TryGet<int>("y", out result.windowCoords[1]);
            }
            return result;
        }
#endif

        public override int GetHashCode() => hwid?.GetHashCode() ?? 0;
        public override bool Equals(object obj) {
            if (obj == null || !(obj is DisplayInfo other))
                return false;
            return hardwareVersion == other.hardwareVersion &&
                hwid == other.hwid &&
                index == other.index &&
                state == other.state &&
                (((windowCoords == null) == (other.windowCoords == null)) || (windowCoords != null && windowCoords.SequenceEqual(other.windowCoords)));
        }
    }
}

