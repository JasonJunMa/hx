using System;

namespace ET
{
    /// <summary>
    /// 华夏 DataLink.dll StreamEncrypter 算法还原
    /// 逆向自 PE32 x86 DLL (深圳网域 2006)
    ///
    /// 不对称流加密 (非 RC4):
    ///   4 字节块:  Encrypt = ROR(block, rot) XOR key
    ///              Decrypt = ROL(cipher XOR key, rot)
    ///   尾部逐字节: Encrypt = ROR(byte, rot) XOR rot
    ///              Decrypt = ROL(byte XOR rot, rot)
    ///   rot 来自种子后 4 字节循环
    ///
    /// ⚠️ x86 ror/rol 硬件自动 mod 32/8, C# 必须手动 mod
    /// </summary>
    [EnableClass]
    public class HX_StreamEnc
    {
        public const uint DefaultKey = 0x00013168;

        private readonly uint _key;
        private readonly byte[] _seed;
        private int _blockIndex;

        public HX_StreamEnc(uint key, byte[] seed)
        {
            _key = key;
            _seed = seed ?? throw new ArgumentNullException(nameof(seed));
            if (_seed.Length < 4) throw new ArgumentException("种子至少 4 字节");
            _blockIndex = 0;
        }

        private static uint Ror32(uint v, byte r) { r &= 31; return (v >> r) | (v << (32 - r)); }
        private static uint Rol32(uint v, byte r) { r &= 31; return (v << r) | (v >> (32 - r)); }
        private static byte Ror8(byte v, byte r) { r &= 7;  return (byte)((v >> r) | (v << (8 - r))); }
        private static byte Rol8(byte v, byte r) { r &= 7;  return (byte)((v << r) | (v >> (8 - r))); }

        public byte[] Encrypt(byte[] data)
        {
            byte[] output = new byte[data.Length];
            _blockIndex = 0;
            int i = 0;
            while (i <= data.Length - 4)
            {
                uint block = BitConverter.ToUInt32(data, i);
                byte rot = _seed[_blockIndex % 4];
                BitConverter.GetBytes(Ror32(block, rot) ^ _key).CopyTo(output, i);
                _blockIndex = (_blockIndex + 1) & 0x7FFFFFFF;
                i += 4;
            }
            while (i < data.Length)
            {
                byte rot = _seed[_blockIndex % 4];
                output[i] = (byte)(Ror8(data[i], rot) ^ rot);
                i++;
            }
            return output;
        }

        public byte[] Decrypt(byte[] data)
        {
            byte[] output = new byte[data.Length];
            _blockIndex = 0;
            int i = 0;
            while (i <= data.Length - 4)
            {
                uint cipher = BitConverter.ToUInt32(data, i);
                byte rot = _seed[_blockIndex % 4];
                BitConverter.GetBytes(Rol32(cipher ^ _key, rot)).CopyTo(output, i);
                _blockIndex = (_blockIndex + 1) & 0x7FFFFFFF;
                i += 4;
            }
            while (i < data.Length)
            {
                byte rot = _seed[_blockIndex % 4];
                output[i] = Rol8((byte)(data[i] ^ rot), rot);
                i++;
            }
            return output;
        }

        public void Reset() => _blockIndex = 0;
    }
}
