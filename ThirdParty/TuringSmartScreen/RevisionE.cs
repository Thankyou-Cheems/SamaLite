namespace TuringSmartScreenLib;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Ports;
using System.Reflection;
using System.Diagnostics;
using System.Text;

public sealed class TuringSmartScreenRevisionE : IDisposable
{
    private const int WriteSize = 250;
    private const int ReadSize = 1024;
    private const int PartialBlockSize = 80;

    private static readonly byte[] CommandHello = [0x01, 0xef, 0x69, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xc5, 0xd3];
    private static readonly byte[] CommandSetBrightness = [0x7b, 0xef, 0x69, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00];
    private static readonly byte[] CommandDisplayBitmapPrefix = [0xc8, 0xef, 0x69, 0x00];
    private static readonly byte[] CommandPreUpdateBitmap = [0x86, 0xef, 0x69, 0x00, 0x00, 0x00, 0x01];
    private static readonly byte[] CommandUpdateBitmap = [0xcc, 0xef, 0x69, 0x00, 0x00];
    private static readonly byte[] CommandQueryStatus = [0xcf, 0xef, 0x69, 0x00, 0x00, 0x00, 0x01];

    private static readonly byte[] CommandUpdateBitmapTerminate = [0xef, 0x69];

    private readonly byte[] commandDisplayBitmap;

    private readonly SerialPort port;

    private byte[] writeBuffer = [];

    private byte[] readBuffer = [];

    private int writeOffset;

    private int renderCount;

    private readonly Stopwatch operation = new();

    public long BytesSent { get; private set; }

    public long BytesReceived { get; private set; }

    public string LastStatus { get; private set; } = string.Empty;

    public bool UseBgrPartial { get; set; } = true;

    public string Identity { get; private set; } = string.Empty;

    public int Width { get; }

    public int Height { get; }

    // --------------------------------------------------------------------------------
    // Constructor
    // --------------------------------------------------------------------------------

    public TuringSmartScreenRevisionE(string name, int width = 480, int height = 1920)
    {
        Width = width;
        Height = height;

        var payloadSize = width * height * 4;
        commandDisplayBitmap = [
            .. CommandDisplayBitmapPrefix,
            (byte)((payloadSize >> 16) & 0xff),
            (byte)((payloadSize >> 8) & 0xff),
            (byte)(payloadSize & 0xff)
        ];

        port = new SerialPort(name)
        {
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            BaudRate = 115200,
            StopBits = StopBits.One,
            Parity = Parity.None
        };
    }

    public void Dispose()
    {
        Close();
        DisposePort();
        ReturnBuffers();
    }

    private void DisposePort()
    {
        try
        {
            port.Dispose();
        }
        catch (IOException)
        {
            // Ignore
        }
        catch (TargetInvocationException)
        {
            // Ignore
        }
    }

    private void RentBuffers()
    {
        if (writeBuffer.Length == 0)
        {
            writeBuffer = ArrayPool<byte>.Shared.Rent(WriteSize);
        }
        if (readBuffer.Length == 0)
        {
            readBuffer = ArrayPool<byte>.Shared.Rent(ReadSize);
        }
    }

    private void ReturnBuffers()
    {
        if (writeBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(writeBuffer);
            writeBuffer = [];
        }
        if (readBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            readBuffer = [];
        }
    }

    public void Close()
    {
        if (port.IsOpen)
        {
            try
            {
                port.Close();
            }
            catch (IOException)
            {
                // Ignore
            }
            catch (TargetInvocationException)
            {
                // Ignore
            }
        }
    }

    public void Open()
    {
        operation.Restart();
        RentBuffers();
        try
        {
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            Write(CommandHello);
            Flush();

            var response = ReadTo();
            Identity = System.Text.Encoding.ASCII.GetString(response);
            if (!response.StartsWith("chs_"u8))
            {
                throw new IOException($"Unknown response. response=[{Convert.ToHexString(response)}]");
            }
        }
        catch (Exception)
        {
            Close();
            ReturnBuffers();
            throw;
        }
    }

    // --------------------------------------------------------------------------------
    // Raw I/O
    // --------------------------------------------------------------------------------

    private ReadOnlySpan<byte> ReadResponse(int length = ReadSize)
    {
        var offset = 0;
        var deadline = Stopwatch.StartNew();
        try
        {
            while (offset < length && deadline.ElapsedMilliseconds < 1000)
            {
                port.ReadTimeout = Math.Max(1, 1000 - (int)deadline.ElapsedMilliseconds);
                var read = port.Read(readBuffer, offset, length - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }
        }
        catch (TimeoutException)
        {
            // Ignore
        }
        BytesReceived += offset;
        LastStatus = Encoding.ASCII.GetString(readBuffer, 0, offset).TrimEnd('\0');
        return readBuffer.AsSpan(0, offset);
    }

    private ReadOnlySpan<byte> ReadTo(byte terminator = 0x00)
    {
        var offset = 0;
        var deadline = Stopwatch.StartNew();
        try
        {
            while (offset < 128 && deadline.ElapsedMilliseconds < 1000)
            {
                port.ReadTimeout = Math.Max(1, 1000 - (int)deadline.ElapsedMilliseconds);
                var value = port.ReadByte();
                if (value == terminator)
                {
                    break;
                }

                readBuffer[offset] = (byte)value;
                offset++;
            }
        }
        catch (TimeoutException)
        {
            // Ignore
        }
        catch (IOException)
        {
            // Ignore
        }

        BytesReceived += offset;
        return readBuffer.AsSpan(0, offset);
    }

    private void Write(ReadOnlySpan<byte> values, byte pad = 0x00)
    {
        while (writeOffset + values.Length > WriteSize - 1)
        {
            var block = values[..(WriteSize - 1 - writeOffset)];
            block.CopyTo(writeBuffer.AsSpan(writeOffset, block.Length));
            writeOffset += block.Length;

            FlushInternal(pad);

            values = values[block.Length..];
        }

        if (values.Length > 0)
        {
            values.CopyTo(writeBuffer.AsSpan(writeOffset));
            writeOffset += values.Length;
        }
    }

    private void Write(byte value, byte pad = 0x00)
    {
        if (writeOffset + 1 > WriteSize - 1)
        {
            FlushInternal(pad);
        }

        writeBuffer[writeOffset] = value;
        writeOffset++;
    }

    private void Flush(byte pad = 0x00)
    {
        if (writeOffset > 0)
        {
            FlushInternal(pad);
        }
    }

    private void FlushInternal(byte pad)
    {
        if (operation.ElapsedMilliseconds > 8000) throw new TimeoutException("Display operation exceeded eight seconds.");
        writeBuffer.AsSpan(writeOffset, WriteSize - writeOffset).Fill(pad);
        port.Write(writeBuffer, 0, WriteSize);
        BytesSent += WriteSize;
        writeOffset = 0;
    }

    // --------------------------------------------------------------------------------
    // Command
    // --------------------------------------------------------------------------------

    public void SetBrightness(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 255);
        operation.Restart();
        Write(CommandSetBrightness);
        Write((byte)level);
        Flush();
    }

    public void Clear() => Clear(0, 0, 0);

    public void Clear(byte r, byte g, byte b)
    {
        // Start
        Write(0x2c);
        Flush(0x2c);

        // DisplayBitmap
        Write(commandDisplayBitmap);
        Flush();

        // Payload
        var pattern = (Span<byte>)stackalloc byte[4];
        pattern[0] = b;
        pattern[1] = g;
        pattern[2] = r;
        pattern[3] = 0xff;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                Write(pattern);
            }
        }
        Flush();

        // PreUpdateBitmap
        Write(CommandPreUpdateBitmap);
        Flush();
        ReadResponse();

        // QueryStatus
        Write(CommandQueryStatus);
        Flush();
        ReadResponse();
    }

    private bool IsFullBitmap(int width, int height, RotateOption option)
    {
        if (option is RotateOption.None or RotateOption.Rotate180)
        {
            return (width == Width) && (height == Height);
        }
        return (width == Height) && (height == Width);
    }

    public bool DisplayBitmap(int x, int y, byte[] bitmap, int width, int height, RotateOption option = RotateOption.None)
    {
        operation.Restart();
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || x + width > Width || y + height > Height || bitmap.Length < checked(width * height * 4))
            throw new ArgumentException("Bitmap must fit the screen and contain width*height*4 BGRA bytes.");
        if ((x == 0) && (y == 0) && IsFullBitmap(width, height, option))
        {
            DisplayFullBitmap(bitmap, option);
            renderCount = 0;
            return true;
        }

        for (var oy = 0; oy < height; oy += PartialBlockSize)
        {
            for (var ox = 0; ox < width; ox += PartialBlockSize)
            {
                if (!DisplayPartialBitmap(x + ox, y + oy, Math.Min(width - ox, PartialBlockSize), Math.Min(height - oy, PartialBlockSize), bitmap, ox, oy, width, option))
                {
                    return false;
                }
                renderCount++;
            }
        }

        return true;
    }

    private void DisplayFullBitmap(byte[] bitmap, RotateOption option)
    {
        // Start
        Write(0x2c);
        Flush(0x2c);

        // DisplayBitmap
        Write(commandDisplayBitmap);
        Flush();

        // Payload
        switch (option)
        {
            case RotateOption.Rotate90:
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        Write(bitmap.AsSpan((((Width - 1 - x) * Height) + y) * 4, 4));
                    }
                }
                break;
            case RotateOption.Rotate270:
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        Write(bitmap.AsSpan(((x * Height) + (Height - 1 - y)) * 4, 4));
                    }
                }
                break;
            case RotateOption.Rotate180:
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        Write(bitmap.AsSpan((((Height - 1 - y) * Width) + (Width - 1 - x)) * 4, 4));
                    }
                }
                break;
            default:
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        Write(bitmap.AsSpan(((y * Width) + x) * 4, 4));
                    }
                }
                break;
        }
        Flush();

        // PreUpdateBitmap
        Write(CommandPreUpdateBitmap);
        Flush();
        ReadResponse();

        // QueryStatus
        Write(CommandQueryStatus);
        Flush();
        ValidateStatus(ReadResponse());
    }

    private bool DisplayPartialBitmap(int x, int y, int w, int h, byte[] bitmap, int bitmapX, int bitmapY, int bitmapWidth, RotateOption option)
    {
        var (width, height, startX, startY) = option switch
        {
            RotateOption.Rotate90 => (h, w, Width - y - h, x),
            RotateOption.Rotate270 => (h, w, y, Height - x - w),
            RotateOption.Rotate180 => (w, h, Width - x - w, Height - y - h),
            _ => (w, h, x, y)
        };

        var header = (Span<byte>)stackalloc byte[5];
        header[3] = (byte)((width >> 8) & 0xff);
        header[4] = (byte)(width & 0xff);

        var bytesPerPixel = UseBgrPartial ? 3 : 4;
        var bitmapSize = (((width * bytesPerPixel) + header.Length) * height) + CommandUpdateBitmapTerminate.Length;
        var size = (Span<byte>)stackalloc byte[2];
        size[0] = (byte)((bitmapSize >> 8) & 0xff);
        size[1] = (byte)(bitmapSize & 0xff);

        Span<byte> countBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(countBytes, renderCount);

        // UpdateBitmap
        Write(CommandUpdateBitmap);
        Write(size);
        Write(new byte[3]);
        Write(countBytes);
        Flush();

        // Payload
        for (var oy = 0; oy < height; oy++)
        {
            var position = ((startY + oy) * Width) + startX;
            header[0] = (byte)((position >> 16) & 0xff);
            header[1] = (byte)((position >> 8) & 0xff);
            header[2] = (byte)(position & 0xff);
            Write(header);

            for (var ox = 0; ox < width; ox++)
            {
                int px;
                int py;
                switch (option)
                {
                    case RotateOption.Rotate90:
                        px = bitmapX + oy;
                        py = bitmapY + h - 1 - ox;
                        break;
                    case RotateOption.Rotate270:
                        px = bitmapX + w - 1 - oy;
                        py = bitmapY + ox;
                        break;
                    case RotateOption.Rotate180:
                        px = bitmapX + w - 1 - ox;
                        py = bitmapY + h - 1 - oy;
                        break;
                    default:
                        px = bitmapX + ox;
                        py = bitmapY + oy;
                        break;
                }
                Write(bitmap.AsSpan(((py * bitmapWidth) + px) * 4, bytesPerPixel));
            }
        }
        Write(CommandUpdateBitmapTerminate);
        Flush();

        // QueryStatus
        Write(CommandQueryStatus);
        Flush();

        var response = ReadResponse();
        // Succeed unless the device explicitly requests a resend.
        // Different panel models return different success responses,
        // but all use "needReSend:1" to indicate failure.
        return ValidateStatus(response);
    }

    private static bool ValidateStatus(ReadOnlySpan<byte> response)
    {
        if (response.Length == 0) throw new IOException("No display status received; compatibility is not confirmed.");
        if (response.StartsWith("needReSend:1"u8)) throw new IOException("Display requested a resend.");
        return true;
    }

    public void StopMedia()
    {
        operation.Restart();
        Write(new byte[] { 0x79, 0xef, 0x69, 0, 0, 0, 1 });
        Flush();
        Write(new byte[] { 0x96, 0xef, 0x69, 0, 0, 0, 1 });
        Flush();
        ReadResponse();
    }
}
