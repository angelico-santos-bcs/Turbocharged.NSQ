﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Turbocharged.NSQ
{
    class IncomingMessageProcessor
    {
        const int FRAME_SIZE_LENGTH = 4;
        const int FRAME_TYPE_LENGTH = 4;
        const int FRAME_HEADER_TOTAL_LENGTH = FRAME_SIZE_LENGTH + FRAME_TYPE_LENGTH;

        const int MESSAGE_TIMESTAMP_LENGTH = 4;
        const int MESSAGE_ATTEMPTS_LENGTH = 4;
        const int MESSAGE_ID_LENGTH = 4;
        readonly NetworkStream _stream;

        public IncomingMessageProcessor(NetworkStream stream)
        {
            _stream = stream;
        }

        public async Task<Message> ReadMessageAsync()
        {
            // MESSAGE FRAME FORMAT:
            //   4 bytes - Int32, size of the frame, excluding this field
            //   4 bytes - Int32, frame type
            //   N bytes - data
            //      8 bytes - Int64, timestamp
            //      2 bytes - UInt16, attempts
            //     16 bytes - Hex-string encoded message ID
            //      N bytes - message body

            // Get the size of the incoming frame
            byte[] frameSizeBytes = await ReadBytesAsync(FRAME_SIZE_LENGTH).ConfigureAwait(false);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(frameSizeBytes);
            var frameLength = BitConverter.ToInt32(frameSizeBytes, 0);

            // Read the rest of the frame
            var frame = await ReadBytesAsync(frameLength).ConfigureAwait(false);

            // Get the frame type
            byte[] frameTypeBytes = new byte[FRAME_TYPE_LENGTH];
            Array.ConstrainedCopy(frame, 0, frameTypeBytes, 0, FRAME_TYPE_LENGTH);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(frameTypeBytes);
            var frameType = (MessageType)BitConverter.ToInt32(frameTypeBytes, 0);

            // Get the data portion of the frame
            var dataLength = frameLength - FRAME_TYPE_LENGTH;
            byte[] dataBuffer = new byte[dataLength];
            Array.ConstrainedCopy(frame, FRAME_TYPE_LENGTH, dataBuffer, 0, dataLength);

            var readableData = Encoding.ASCII.GetString(dataBuffer, 0, dataBuffer.Length);

            return new Message
            {
                MessageSize = frameLength,
                Type = frameType,

                Bytes = dataBuffer,
                Readable = readableData,
            };
        }

        async Task<byte[]> ReadBytesAsync(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            int bytesRead = 0;
            int bytesLeft = count;

            while ((bytesRead = await _stream.ReadAsync(buffer, offset, bytesLeft).ConfigureAwait(false)) > 0)
            {
                offset += bytesRead;
                bytesLeft -= bytesRead;
                if (offset > count) throw new InvalidOperationException("Read too many bytes");
                if (offset == count) break;
            }

            return buffer;
        }
    }
}
