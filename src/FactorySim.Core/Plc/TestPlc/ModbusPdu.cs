using System.Buffers.Binary;

namespace FactorySim.Core.Plc.TestPlc;

/// <summary>
/// One Modbus request (the PDU: function code and data) against the memory of the test PLC, with the same
/// checks and exception codes as <c>tools/fake_plc/fake_plc.py</c>. FC01–06, FC15 and FC16.
/// </summary>
public static class ModbusPdu
{
    public const byte IllegalFunction = 1;
    public const byte IllegalDataAddress = 2;
    public const byte IllegalDataValue = 3;

    /// <returns>The response PDU, or an exception response (function | 0x80, code).</returns>
    public static byte[] Execute(PlcMemory memory, ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length == 0)
        {
            return new byte[] { 0x80, IllegalFunction };
        }

        byte function = pdu[0];
        int size = memory.Size;
        switch (function)
        {
            case 1 or 2:
            {
                if (!TwoWords(pdu, out int address, out int count))
                {
                    return Error(function, IllegalDataValue);
                }

                if (count is < 1 or > 2000)
                {
                    return Error(function, IllegalDataValue);
                }

                if (address + count > size)
                {
                    return Error(function, IllegalDataAddress);
                }

                byte[] data = PackBits(function == 1 ? memory.ReadCoils(address, count) : memory.ReadInputs(address, count));
                return [function, (byte)data.Length, .. data];
            }

            case 5:
            {
                if (!TwoWords(pdu, out int address, out int value))
                {
                    return Error(function, IllegalDataValue);
                }

                if (value is not (0x0000 or 0xFF00))
                {
                    return Error(function, IllegalDataValue);
                }

                if (address >= size)
                {
                    return Error(function, IllegalDataAddress);
                }

                memory.WriteCoils(address, new[] { value == 0xFF00 });
                return pdu[..5].ToArray();
            }

            case 15:
            {
                if (!TwoWords(pdu, out int address, out int count) || pdu.Length < 6)
                {
                    return Error(function, IllegalDataValue);
                }

                int byteCount = pdu[5];
                if (count is < 1 or > 1968 || byteCount != (count + 7) / 8 || pdu.Length < 6 + byteCount)
                {
                    return Error(function, IllegalDataValue);
                }

                if (address + count > size)
                {
                    return Error(function, IllegalDataAddress);
                }

                memory.WriteCoils(address, UnpackBits(pdu.Slice(6, byteCount), count));
                return pdu[..5].ToArray();
            }

            case 3 or 4:
            {
                if (!TwoWords(pdu, out int address, out int count))
                {
                    return Error(function, IllegalDataValue);
                }

                if (count is < 1 or > 125)
                {
                    return Error(function, IllegalDataValue);
                }

                if (address + count > size)
                {
                    return Error(function, IllegalDataAddress);
                }

                ushort[] words = function == 3 ? memory.ReadHolding(address, count) : memory.ReadInputRegisters(address, count);
                var response = new byte[2 + 2 * count];
                response[0] = function;
                response[1] = (byte)(2 * count);
                for (int index = 0; index < count; index++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + 2 * index), words[index]);
                }

                return response;
            }

            case 6:
            {
                if (!TwoWords(pdu, out int address, out int value))
                {
                    return Error(function, IllegalDataValue);
                }

                if (address >= size)
                {
                    return Error(function, IllegalDataAddress);
                }

                memory.WriteHolding(address, new[] { (ushort)value });
                return pdu[..5].ToArray();
            }

            case 16:
            {
                if (!TwoWords(pdu, out int address, out int count) || pdu.Length < 6)
                {
                    return Error(function, IllegalDataValue);
                }

                int byteCount = pdu[5];
                if (count is < 1 or > 123 || byteCount != 2 * count || pdu.Length < 6 + byteCount)
                {
                    return Error(function, IllegalDataValue);
                }

                if (address + count > size)
                {
                    return Error(function, IllegalDataAddress);
                }

                var words = new ushort[count];
                for (int index = 0; index < count; index++)
                {
                    words[index] = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(6 + 2 * index, 2));
                }

                memory.WriteHolding(address, words);
                return pdu[..5].ToArray();
            }

            default:
                return Error(function, IllegalFunction);
        }
    }

    private static byte[] Error(byte function, byte code) => new[] { (byte)(function | 0x80), code };

    private static bool TwoWords(ReadOnlySpan<byte> pdu, out int first, out int second)
    {
        if (pdu.Length < 5)
        {
            first = second = 0;
            return false;
        }

        first = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        second = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        return true;
    }

    private static byte[] PackBits(IReadOnlyList<bool> bits)
    {
        var data = new byte[(bits.Count + 7) / 8];
        for (int index = 0; index < bits.Count; index++)
        {
            if (bits[index])
            {
                data[index / 8] |= (byte)(1 << (index % 8));
            }
        }

        return data;
    }

    private static bool[] UnpackBits(ReadOnlySpan<byte> data, int count)
    {
        var bits = new bool[count];
        for (int index = 0; index < count; index++)
        {
            bits[index] = ((data[index / 8] >> (index % 8)) & 1) == 1;
        }

        return bits;
    }
}
