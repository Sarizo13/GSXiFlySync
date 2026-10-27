using System.Runtime.InteropServices;

namespace GsxIflySync.SimConnect;

/// <summary>
/// Appels directs à l'API C de SimConnect (SimConnect.h du SDK MSFS 2024).
/// Choix : pas de dépendance à Microsoft.FlightSimulator.SimConnect.dll (managé),
/// pour que la solution compile sans le SDK. À l'exécution, SimConnect.dll
/// (natif, x64) doit se trouver à côté de l'exécutable.
/// </summary>
internal static class NativeMethods
{
    private const string Dll = "SimConnect.dll";

    public const uint ObjectIdUser = 0;
    public const uint UnusedDatumId = 0xFFFFFFFF;

    public const uint DataTypeFloat64 = 4;
    public const uint DataTypeString256 = 9;

    public const uint PeriodSecond = 4;

    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int SimConnect_Open(
        out IntPtr phSimConnect, string szName, IntPtr hWnd, uint userEventWin32, IntPtr hEventHandle, uint configIndex);

    [DllImport(Dll, ExactSpelling = true)]
    public static extern int SimConnect_Close(IntPtr hSimConnect);

    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int SimConnect_AddToDataDefinition(
        IntPtr hSimConnect, uint defineId, string datumName, string? unitsName, uint datumType, float epsilon, uint datumId);

    [DllImport(Dll, ExactSpelling = true)]
    public static extern int SimConnect_RequestDataOnSimObject(
        IntPtr hSimConnect, uint requestId, uint defineId, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit);

    [DllImport(Dll, ExactSpelling = true)]
    public static extern int SimConnect_SetDataOnSimObject(
        IntPtr hSimConnect, uint defineId, uint objectId, uint flags, uint arrayCount, uint unitSize, IntPtr dataSet);

    [DllImport(Dll, ExactSpelling = true)]
    public static extern int SimConnect_GetNextDispatch(IntPtr hSimConnect, out IntPtr ppData, out uint pcbData);

    [DllImport(Dll, ExactSpelling = true)]
    public static extern int SimConnect_GetLastSentPacketID(IntPtr hSimConnect, out uint pdwSendId);
}
