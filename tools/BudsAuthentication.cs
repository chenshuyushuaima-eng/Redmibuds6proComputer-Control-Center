// SPDX-License-Identifier: AGPL-3.0-or-later
// Authentication algorithm ported from RedmiBudsBar Authentication.swift and
// Gadgetbridge Authentication.java. Copyright their respective contributors.
// Sources and license details are listed in THIRD_PARTY_NOTICES.md.
using System;

public static class BudsAuthentication
{
    private static readonly byte[] Plaintext = {
        0x11,0x22,0x33,0x33,0x22,0x11,0x11,0x22,0x33,0x33,0x22,0x11,0x11,0x22,0x33,0x33
    };
    private static readonly int[][] Matrix = {
        new[]{2,1,1,1,4,2,1,1,2,2,4,2,4,4,16,8},
        new[]{2,1,1,1,4,2,1,1,1,1,2,1,2,2,8,4},
        new[]{1,1,4,2,2,2,4,2,16,8,4,4,2,1,1,1},
        new[]{1,1,4,2,1,1,2,1,8,4,2,2,2,1,1,1},
        new[]{16,8,2,2,4,2,4,4,1,1,4,2,1,1,2,1},
        new[]{8,4,1,1,2,1,2,2,1,1,4,2,1,1,2,1},
        new[]{2,2,4,2,4,4,16,8,2,1,1,1,4,2,1,1},
        new[]{1,1,2,1,2,2,8,4,2,1,1,1,4,2,1,1},
        new[]{4,2,4,4,16,8,2,2,1,1,2,1,1,1,4,2},
        new[]{2,1,2,2,8,4,1,1,1,1,2,1,1,1,4,2},
        new[]{4,4,16,8,1,1,2,1,4,2,1,1,4,2,2,2},
        new[]{2,2,8,4,1,1,2,1,4,2,1,1,2,1,1,1},
        new[]{1,1,2,1,1,1,4,2,4,4,16,8,2,2,4,2},
        new[]{1,1,2,1,1,1,4,2,2,2,8,4,1,1,2,1},
        new[]{4,2,1,1,2,1,1,1,4,2,2,2,16,8,4,4},
        new[]{4,2,1,1,2,1,1,1,2,1,1,1,8,4,2,2}
    };

    private static int Pow(int exponent)
    {
        int result = 1, value = 45;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result = result * value % 257;
            value = value * value % 257;
            exponent >>= 1;
        }
        return result;
    }

    private static bool Xor(int index) { return ((1 << index) & 0x9999) != 0; }

    public static byte[] Response(byte[] challenge)
    {
        if (challenge == null || challenge.Length != 16) throw new ArgumentException("Challenge must contain 16 bytes.");
        unchecked
        {
            byte[] exp = new byte[256], log = new byte[256];
            for (int i = 0; i < 256; i++) { exp[i] = (byte)Pow(i); log[exp[i]] = (byte)i; }
            byte[][] keys = new byte[17][];
            keys[0] = (byte[])challenge.Clone();
            keys[0][15] ^= 6;
            byte[] register = new byte[17];
            Array.Copy(keys[0], register, 16);
            for (int i = 0; i < 16; i++) register[16] ^= register[i];
            for (int k = 1; k <= 16; k++)
            {
                for (int i = 0; i < 17; i++) register[i] = (byte)((register[i] >> 5) | (register[i] << 3));
                keys[k] = new byte[16];
                for (int i = 0; i < 16; i++) keys[k][i] = (byte)(register[(k + i) % 17] + (byte)Pow(Pow(17 * (k + 1) + i + 1)));
            }
            byte[] block = (byte[])Plaintext.Clone();
            for (int round = 0; round < 8; round++)
            {
                for (int i = 0; i < 16; i++)
                {
                    if (round == 2) block[i] = Xor(i) ? (byte)(block[i] ^ Plaintext[i]) : (byte)(block[i] + Plaintext[i]);
                    block[i] = Xor(i) ? (byte)(block[i] ^ keys[round * 2][i]) : (byte)(block[i] + keys[round * 2][i]);
                    block[i] = Xor(i) ? exp[block[i]] : log[block[i]];
                    block[i] = Xor(i) ? (byte)(block[i] + keys[round * 2 + 1][i]) : (byte)(block[i] ^ keys[round * 2 + 1][i]);
                }
                byte[] copy = (byte[])block.Clone();
                for (int i = 0; i < 16; i++)
                {
                    int sum = 0;
                    for (int j = 0; j < 16; j++) sum += Matrix[i][j] * copy[j];
                    block[i] = (byte)sum;
                }
            }
            for (int i = 0; i < 16; i++) block[i] = Xor(i) ? (byte)(block[i] ^ keys[16][i]) : (byte)(block[i] + keys[16][i]);
            return block;
        }
    }
}
