using System;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Exception for BitstreamReader errors
    /// </summary>
    public class BitstreamReaderException : Exception
    {
        public BitstreamReaderException(string message) :
            base(message)
        {
        }
    }


    /// <summary>
    /// Helper class for reading bit-packed structures
    /// </summary>
    public class BitstreamReader
    {
        int currentOffset;		// offset within data -- the offset of currentByte
        int currentBit;			// 0 is MSB, 7 is LSB
        byte currentByte;		// current byte in the reader, shifted as we read it
        byte [] data;

        public BitstreamReader()
        {
        }

        public BitstreamReader(byte [] data)
        {
            Init(data, 0);
        }

        public BitstreamReader(byte[] data, int offset)
        {
            Init(data, offset);
        }

        public void Init(byte[] data, int offset)
        {
            this.data = data;
            currentOffset = offset;
            currentBit = 0;
        }

        public int CurrentBitOffset
        {
            get 
            {
                return currentOffset * 8 + currentBit;
            }
        }

        public int RemainingBits
        {
            get
            {
                return data.Length * 8 - CurrentBitOffset;
            }
        }

        public int RemainingBytes
        {
            get
            {
                return RemainingBits / 8;
            }
        }

        public bool ReadFlag()
        {
            return ReadBits(1) != 0;
        }

        public void SkipBits(int n)
        {
            if (n == 0)
                return;
            if (n > RemainingBits)
                throw new BitstreamReaderException(String.Format("Attempt to skip {0} bits when {1} bits remain", n, RemainingBits));

            if (currentBit == 0 && ( ( n & 7) == 0) )
                currentOffset += (n / 8);
            else 
            {
                while (n > 0)
                {
                    int skipNow = Math.Min(n, 64);
                    ReadBitsULong(skipNow);
                    n -= skipNow;
                }
            }
        }

        public int ReadBits(int n)
        {
            if (n > 31)
                throw new ArgumentOutOfRangeException("n");
            return (int)ReadBitsULong(n);
        }
        public uint ReadUBits(int n)
        {
            if (n > 32)
                throw new ArgumentOutOfRangeException("n");
            return (uint)ReadBitsULong(n);
        }

        public ulong ReadBitsULong(int n)
        {
            if (n > 64)
                throw new ArgumentOutOfRangeException("n");
            if (n > RemainingBits)
                throw new BitstreamReaderException(String.Format("Attempt to read {0} bits when {1} bits remain", n, RemainingBits));
            ulong retval = 0;

            if (currentBit == 0 && n == 8)
            {
                retval = currentByte = data[currentOffset];
                currentOffset++;
                return retval;
            }

            // when count > 8, get bytes first
            int bitsLeft = 0;
            for (int iBit = 8; iBit <= n; iBit += 8)
            {
                retval |= ( (ulong)ReadByte() << (n - iBit) );
            }

            bitsLeft = n % 8;
            if (bitsLeft > 0)
            {
                // now read the bits left
                int nextBit = currentBit + bitsLeft;
                byte writeMask = (byte)(0xFF >> (8 - bitsLeft));

                if (nextBit > 8)
                {
                    // bits to be read within both the current and the next byte.
                    byte nextByte = data[currentOffset + 1];

                    // combin some bits from current byte and some from next bits.
                    retval |= (uint)(((data[currentOffset] << (nextBit - 8) | (nextByte >> (16 - nextBit))) & writeMask));
                    currentOffset++;
                }
                else
                {
                    // bits to be read only within current byte
                    retval |= (uint)((data[currentOffset] >> (8 - nextBit)) & writeMask);
                    if (nextBit == 8)
                    {
                        currentOffset++;
                    }
                }

                currentBit = nextBit & 0x7;
            }

            return retval;
        }

        public byte ReadByte()
        {
            if (currentBit != 0)
            {
                return (byte)(( data[currentOffset] << currentBit ) | ( data[++currentOffset] >> (8 - currentBit) ) );
            }
            else
            {
                return data[currentOffset++];
            }
        }
    }
}
