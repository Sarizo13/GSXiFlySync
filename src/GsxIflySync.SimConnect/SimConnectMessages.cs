using System.Buffers.Binary;
using System.Text;

namespace GsxIflySync.SimConnect;

public abstract record SimConnectMessage;

public sealed record OpenMessage : SimConnectMessage;

public sealed record QuitMessage : SimConnectMessage;

public sealed record ExceptionMessage(uint Exception, uint SendId, uint Index) : SimConnectMessage;

public sealed record DataMessage(uint RequestId, uint DefineId, byte[] Payload) : SimConnectMessage
{
    public double ReadFloat64() => BinaryPrimitives.ReadDoubleLittleEndian(Payload);

    public string ReadString()
    {
        var end = Array.IndexOf(Payload, (byte)0);
        return Encoding.UTF8.GetString(Payload, 0, end < 0 ? Payload.Length : end);
    }
}

public sealed record OtherMessage(uint Id) : SimConnectMessage;

/// <summary>
/// Décodage des structures SIMCONNECT_RECV_* (little-endian, DWORD = 4 octets).
/// En-tête commun : dwSize, dwVersion, dwID.
/// </summary>
public static class SimConnectMessageParser
{
    public const uint RecvException = 1;
    public const uint RecvOpen = 2;
    public const uint RecvQuit = 3;
    public const uint RecvSimObjectData = 8;

    /// <summary>SIMCONNECT_RECV_SIMOBJECT_DATA : en-tête (12) + 7 DWORD, dwData commence à 40.</summary>
    public const int DataOffset = 40;

    public static SimConnectMessage Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 12)
        {
            throw new InvalidDataException($"message SimConnect tronqué ({raw.Length} octets)");
        }

        var id = U32(raw, 8);
        switch (id)
        {
            case RecvOpen:
                return new OpenMessage();
            case RecvQuit:
                return new QuitMessage();
            case RecvException when raw.Length >= 24:
                return new ExceptionMessage(U32(raw, 12), U32(raw, 16), U32(raw, 20));
            case RecvSimObjectData when raw.Length >= DataOffset:
                return new DataMessage(U32(raw, 12), U32(raw, 20), raw[DataOffset..].ToArray());
            default:
                return new OtherMessage(id);
        }
    }

    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);

    /// <summary>Noms de SIMCONNECT_EXCEPTION (SimConnect.h), pour un journal lisible.</summary>
    public static string ExceptionName(uint code) => code switch
    {
        0 => "NONE",
        1 => "ERROR",
        2 => "SIZE_MISMATCH",
        3 => "UNRECOGNIZED_ID",
        4 => "UNOPENED",
        5 => "VERSION_MISMATCH",
        6 => "TOO_MANY_GROUPS",
        7 => "NAME_UNRECOGNIZED",
        8 => "TOO_MANY_EVENT_NAMES",
        9 => "EVENT_ID_DUPLICATE",
        10 => "TOO_MANY_MAPS",
        11 => "TOO_MANY_OBJECTS",
        12 => "TOO_MANY_REQUESTS",
        18 => "INVALID_DATA_TYPE",
        19 => "INVALID_DATA_SIZE",
        20 => "DATA_ERROR",
        21 => "INVALID_ARRAY",
        25 => "ILLEGAL_OPERATION",
        26 => "ALREADY_SUBSCRIBED",
        27 => "INVALID_ENUM",
        28 => "DEFINITION_ERROR",
        29 => "DUPLICATE_ID",
        30 => "DATUM_ID",
        31 => "OUT_OF_BOUNDS",
        _ => "code non répertorié",
    };
}
